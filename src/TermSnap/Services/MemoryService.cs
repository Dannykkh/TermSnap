using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// AI 장기기억 서비스 (MEMORY.md 기반)
/// - MEMORY.md 파일로 저장 (claude CLI가 직접 읽음)
/// - CLAUDE.md에서 참조하도록 설정
/// - 탭별로 인스턴스를 생성하여 사용 (각 프로젝트별 독립 관리)
/// </summary>
public class MemoryService : IDisposable
{
    private const string MemoryFileName = "MEMORY.md";
    private string? _currentDirectory;
    private List<MemoryEntry> _memories = new();
    private bool _disposed = false;

    /// <summary>
    /// 새 MemoryService 인스턴스 생성
    /// 각 탭/패널에서 독립적으로 인스턴스를 생성하여 사용
    /// </summary>
    public MemoryService() { }

    /// <summary>
    /// 작업 디렉토리와 함께 초기화
    /// </summary>
    public MemoryService(string workingDirectory) : this()
    {
        SetWorkingDirectory(workingDirectory);
    }

    /// <summary>
    /// 현재 작업 디렉토리의 MEMORY.md 경로
    /// </summary>
    public string? MemoryFilePath => _currentDirectory != null
        ? Path.Combine(_currentDirectory, MemoryFileName)
        : null;

    /// <summary>
    /// 작업 디렉토리 설정 및 MEMORY.md 로드
    /// </summary>
    public void SetWorkingDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        _currentDirectory = directory;
        LoadFromFile();
    }

    /// <summary>
    /// MEMORY.md 파일에서 로드
    /// </summary>
    public void LoadFromFile()
    {
        _memories.Clear();

        if (MemoryFilePath == null || !File.Exists(MemoryFilePath))
            return;

        try
        {
            var content = File.ReadAllText(MemoryFilePath, Encoding.UTF8);
            _memories = ParseMemoryFile(content);
            System.Diagnostics.Debug.WriteLine($"[Memory] 로드됨: {_memories.Count}개 ({MemoryFilePath})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Memory] 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// MEMORY.md 파일로 저장
    /// </summary>
    public void SaveToFile()
    {
        if (MemoryFilePath == null || _currentDirectory == null)
            return;

        try
        {
            var content = GenerateMemoryFile();
            File.WriteAllText(MemoryFilePath, content, Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine($"[Memory] 저장됨: {_memories.Count}개");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Memory] 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// MEMORY.md 내용 생성
    /// </summary>
    private string GenerateMemoryFile()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AI 장기기억 (MEMORY.md)");
        sb.AppendLine();
        sb.AppendLine("> 이 파일은 AI가 참조하는 장기기억입니다. CLAUDE.md에서 이 파일을 참조합니다.");
        sb.AppendLine("> TermSnap에서 자동 관리되며, 직접 편집해도 됩니다.");
        sb.AppendLine();

        // 타입별로 그룹화
        var groups = _memories
            .Where(m => m.IsActive)
            .GroupBy(m => m.Type)
            .OrderBy(g => (int)g.Key);

        foreach (var group in groups)
        {
            var typeName = GetTypeSectionName(group.Key);
            sb.AppendLine($"## {typeName}");
            sb.AppendLine();

            foreach (var memory in group.OrderByDescending(m => m.Importance))
            {
                sb.AppendLine($"- {memory.Content}");
            }
            sb.AppendLine();
        }

        // 마지막 업데이트 시간
        sb.AppendLine("---");
        sb.AppendLine($"*마지막 업데이트: {DateTime.Now:yyyy-MM-dd HH:mm}*");

        return sb.ToString();
    }

    /// <summary>
    /// MEMORY.md 파일 파싱
    /// </summary>
    private List<MemoryEntry> ParseMemoryFile(string content)
    {
        var memories = new List<MemoryEntry>();
        var lines = content.Split('\n');

        MemoryType currentType = MemoryType.Fact;
        int id = 1;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // 빈 줄이나 구분선 무시
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("---") || line.StartsWith("*"))
                continue;

            // 섹션 헤더 (## 또는 ### 타입명)
            if (line.StartsWith("## ") || line.StartsWith("### "))
            {
                var sectionName = line.TrimStart('#', ' ').Trim();
                currentType = ParseSectionName(sectionName);
                continue;
            }

            // 제목 줄 무시 (# 로 시작)
            if (line.StartsWith("#"))
                continue;

            // 메모리 항목 (- 또는 * 로 시작하는 리스트)
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                var itemContent = line.Substring(2).Trim();
                if (!string.IsNullOrWhiteSpace(itemContent) && itemContent.Length > 2)
                {
                    // 템플릿 플레이스홀더 무시 (예: "- (없음)", "- ...")
                    if (itemContent == "(없음)" || itemContent == "..." || itemContent == "(none)")
                        continue;

                    memories.Add(new MemoryEntry
                    {
                        Id = id++,
                        Content = itemContent,
                        Type = currentType,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    });
                }
            }
        }

        return memories;
    }

    private static string GetTypeSectionName(MemoryType type) => type switch
    {
        MemoryType.Fact => "사실",
        MemoryType.Preference => "선호도",
        MemoryType.TechStack => "기술 스택",
        MemoryType.Project => "프로젝트",
        MemoryType.Experience => "경험",
        MemoryType.WorkPattern => "작업 패턴",
        MemoryType.Instruction => "지침",
        MemoryType.Lesson => "학습된 교훈",
        _ => "기타"
    };

    private static MemoryType ParseSectionName(string name)
    {
        var lowerName = name.ToLowerInvariant();

        // 한국어 매칭
        if (name.Contains("사실") || lowerName.Contains("fact"))
            return MemoryType.Fact;
        if (name.Contains("선호") || lowerName.Contains("preference"))
            return MemoryType.Preference;
        if (name.Contains("기술") || lowerName.Contains("tech") || lowerName.Contains("stack"))
            return MemoryType.TechStack;
        if (name.Contains("프로젝트") || lowerName.Contains("project") || lowerName.Contains("context"))
            return MemoryType.Project;
        if (name.Contains("경험") || lowerName.Contains("experience"))
            return MemoryType.Experience;
        if (name.Contains("패턴") || lowerName.Contains("pattern") || lowerName.Contains("work"))
            return MemoryType.WorkPattern;
        if (name.Contains("지침") || lowerName.Contains("instruction") || lowerName.Contains("caution") || lowerName.Contains("주의"))
            return MemoryType.Instruction;
        if (name.Contains("교훈") || lowerName.Contains("lesson") || lowerName.Contains("learn"))
            return MemoryType.Lesson;
        if (name.Contains("결정") || lowerName.Contains("decision"))
            return MemoryType.Project;

        return MemoryType.Fact;
    }

    #region CRUD Operations

    /// <summary>
    /// 메모리 추가
    /// </summary>
    public Task<int> AddMemory(MemoryEntry entry)
    {
        // 중복 체크
        var existing = _memories.FirstOrDefault(m =>
            m.IsActive && m.Content.Equals(entry.Content, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            return Task.FromResult(existing.Id);
        }

        entry.Id = _memories.Count > 0 ? _memories.Max(m => m.Id) + 1 : 1;
        entry.CreatedAt = DateTime.Now;
        entry.IsActive = true;
        _memories.Add(entry);

        SaveToFile();
        return Task.FromResult(entry.Id);
    }

    /// <summary>
    /// 메모리 수정
    /// </summary>
    public Task UpdateMemory(MemoryEntry entry)
    {
        var existing = _memories.FirstOrDefault(m => m.Id == entry.Id);
        if (existing != null)
        {
            existing.Content = entry.Content;
            existing.Type = entry.Type;
            existing.Importance = entry.Importance;
            SaveToFile();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 메모리 삭제
    /// </summary>
    public void DeleteMemory(int id)
    {
        var entry = _memories.FirstOrDefault(m => m.Id == id);
        if (entry != null)
        {
            entry.IsActive = false;
            SaveToFile();
        }
    }

    /// <summary>
    /// 모든 메모리 조회
    /// </summary>
    public List<MemoryEntry> GetAllMemories(bool includeInactive = false)
    {
        return includeInactive
            ? _memories.ToList()
            : _memories.Where(m => m.IsActive).ToList();
    }

    /// <summary>
    /// 타입별 메모리 조회
    /// </summary>
    public List<MemoryEntry> GetMemoriesByType(MemoryType type)
    {
        return _memories.Where(m => m.IsActive && m.Type == type).ToList();
    }

    #endregion

    #region Auto Extraction

    // 메모리 추출 패턴
    private static readonly (Regex Pattern, MemoryType Type, double Importance)[] ExtractionPatterns = new[]
    {
        // 사실
        (new Regex(@"제\s*이름은?\s*(.+?)(?:입니다|이에요|예요|야|임)", RegexOptions.IgnoreCase), MemoryType.Fact, 0.9),
        (new Regex(@"나는?\s*(.+?)(?:입니다|이에요|예요|야|임)", RegexOptions.IgnoreCase), MemoryType.Fact, 0.7),

        // 기술 스택
        (new Regex(@"(?:주로|주언어|메인)\s*(.+?)(?:을|를)?\s*(?:씀|사용|이용)", RegexOptions.IgnoreCase), MemoryType.TechStack, 0.8),

        // 선호도
        (new Regex(@"(.+?)(?:을|를)\s*(?:선호|좋아)", RegexOptions.IgnoreCase), MemoryType.Preference, 0.7),

        // 지침
        (new Regex(@"(?:항상|반드시|꼭)\s*(.+?)(?:해줘|하세요|해)", RegexOptions.IgnoreCase), MemoryType.Instruction, 0.9),
    };

    /// <summary>
    /// 대화에서 메모리 자동 추출
    /// </summary>
    public async Task<List<MemoryEntry>> ExtractMemoriesFromConversation(string userMessage, string? aiResponse = null, string? sessionId = null)
    {
        var extracted = new List<MemoryEntry>();

        foreach (var (pattern, type, importance) in ExtractionPatterns)
        {
            var matches = pattern.Matches(userMessage);
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var content = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(content) && content.Length > 2 && content.Length < 200)
                    {
                        var memory = new MemoryEntry
                        {
                            Content = content,
                            Type = type,
                            Source = userMessage,
                            Importance = importance,
                            IsAutoGenerated = true
                        };
                        extracted.Add(memory);
                    }
                }
            }
        }

        // 저장
        foreach (var memory in extracted)
        {
            await AddMemory(memory);
        }

        return extracted;
    }

    #endregion

    #region Statistics

    /// <summary>
    /// 통계 조회
    /// </summary>
    public (int Total, int WithEmbedding, int AutoGenerated, Dictionary<MemoryType, int> ByType) GetStatistics()
    {
        var active = _memories.Where(m => m.IsActive).ToList();
        var byType = active.GroupBy(m => m.Type).ToDictionary(g => g.Key, g => g.Count());
        var autoGenerated = active.Count(m => m.IsAutoGenerated);

        return (active.Count, 0, autoGenerated, byType);
    }

    /// <summary>
    /// MEMORY.md 파일 존재 여부
    /// </summary>
    public bool HasMemoryFile => MemoryFilePath != null && File.Exists(MemoryFilePath);

    /// <summary>
    /// MEMORY.md 생성 (없으면)
    /// </summary>
    public void CreateMemoryFileIfNotExists()
    {
        if (MemoryFilePath == null || File.Exists(MemoryFilePath))
            return;

        // 빈 파일 생성
        SaveToFile();
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
