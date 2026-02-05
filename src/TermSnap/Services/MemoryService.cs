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
    /// MEMORY.md 내용 생성 (컨텍스트 트리 구조)
    /// </summary>
    private string GenerateMemoryFile()
    {
        var sb = new StringBuilder();
        var dateStr = DateTime.Now.ToString("yyyy-MM-dd");
        var projectName = _currentDirectory != null ? Path.GetFileName(_currentDirectory) : "Unknown";

        sb.AppendLine("# MEMORY.md - 프로젝트 장기기억");
        sb.AppendLine();

        // 프로젝트 목표
        sb.AppendLine("## 프로젝트 목표");
        sb.AppendLine();
        sb.AppendLine("| 목표 | 상태 |");
        sb.AppendLine("|------|------|");
        var goals = _memories.Where(m => m.IsActive && m.Type == MemoryType.Goal).ToList();
        if (goals.Any())
        {
            foreach (var goal in goals)
            {
                sb.AppendLine($"| {goal.Content} | 🔄 진행중 |");
            }
        }
        else
        {
            sb.AppendLine("| (목표 추가) | 🔄 진행중 |");
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // 키워드 인덱스
        sb.AppendLine("## 키워드 인덱스");
        sb.AppendLine();
        sb.AppendLine("| 키워드 | 섹션 |");
        sb.AppendLine("|--------|------|");
        // 각 메모리의 Context에서 키워드 추출하여 인덱스 생성
        var keywordIndex = _memories
            .Where(m => m.IsActive && !string.IsNullOrEmpty(m.Context))
            .GroupBy(m => m.Context)
            .Take(10);
        foreach (var kw in keywordIndex)
        {
            var section = kw.First().Type switch
            {
                MemoryType.Architecture => "#architecture",
                MemoryType.Pattern => "#patterns",
                MemoryType.Tool => "#tools",
                MemoryType.Gotcha => "#gotchas",
                _ => "#meta"
            };
            sb.AppendLine($"| {kw.Key} | {section} |");
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // architecture/
        sb.AppendLine("## architecture/");
        sb.AppendLine();
        var archItems = _memories.Where(m => m.IsActive && m.Type == MemoryType.Architecture).ToList();
        foreach (var item in archItems)
        {
            sb.AppendLine($"### {item.Context ?? "항목"}");
            sb.AppendLine($"`tags: {item.Context ?? "architecture"}`");
            sb.AppendLine($"`date: {item.CreatedAt:yyyy-MM-dd}`");
            sb.AppendLine();
            sb.AppendLine($"- {item.Content}");
            sb.AppendLine();
        }

        // patterns/
        sb.AppendLine("## patterns/");
        sb.AppendLine();
        var patternItems = _memories.Where(m => m.IsActive && m.Type == MemoryType.Pattern).ToList();
        foreach (var item in patternItems)
        {
            sb.AppendLine($"### {item.Context ?? "항목"}");
            sb.AppendLine($"`tags: {item.Context ?? "pattern"}`");
            sb.AppendLine($"`date: {item.CreatedAt:yyyy-MM-dd}`");
            sb.AppendLine();
            sb.AppendLine($"- {item.Content}");
            sb.AppendLine();
        }

        // tools/
        sb.AppendLine("## tools/");
        sb.AppendLine();
        var toolItems = _memories.Where(m => m.IsActive && m.Type == MemoryType.Tool).ToList();
        foreach (var item in toolItems)
        {
            sb.AppendLine($"### {item.Context ?? "항목"}");
            sb.AppendLine($"`tags: {item.Context ?? "tool"}`");
            sb.AppendLine($"`date: {item.CreatedAt:yyyy-MM-dd}`");
            sb.AppendLine();
            sb.AppendLine($"- {item.Content}");
            sb.AppendLine();
        }

        // gotchas/
        sb.AppendLine("## gotchas/");
        sb.AppendLine();
        var gotchaItems = _memories.Where(m => m.IsActive && m.Type == MemoryType.Gotcha).ToList();
        foreach (var item in gotchaItems)
        {
            sb.AppendLine($"### {item.Context ?? "항목"}");
            sb.AppendLine($"`tags: {item.Context ?? "gotcha"}`");
            sb.AppendLine($"`date: {item.CreatedAt:yyyy-MM-dd}`");
            sb.AppendLine();
            sb.AppendLine($"- {item.Content}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();

        // meta/
        sb.AppendLine("## meta/");
        sb.AppendLine($"- **프로젝트**: {projectName}");
        sb.AppendLine($"- **생성일**: {dateStr}");
        sb.AppendLine($"- **마지막 업데이트**: {dateStr}");

        return sb.ToString();
    }

    /// <summary>
    /// MEMORY.md 파일 파싱 (컨텍스트 트리 구조)
    /// </summary>
    private List<MemoryEntry> ParseMemoryFile(string content)
    {
        var memories = new List<MemoryEntry>();
        var lines = content.Split('\n');

        MemoryType currentType = MemoryType.Architecture;
        string? currentContext = null;
        string? currentTags = null;
        string? currentDate = null;
        int id = 1;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // 빈 줄이나 구분선 무시
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("---") || line.StartsWith("*"))
                continue;

            // 테이블 헤더/구분선 무시
            if (line.StartsWith("|") && (line.Contains("---") || line.Contains("목표") || line.Contains("키워드") || line.Contains("섹션")))
                continue;

            // 프로젝트 목표 테이블 행 파싱
            if (line.StartsWith("|") && currentType == MemoryType.Goal)
            {
                var parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1)
                {
                    var goalContent = parts[0].Trim();
                    if (!string.IsNullOrWhiteSpace(goalContent) && goalContent != "(목표 추가)")
                    {
                        memories.Add(new MemoryEntry
                        {
                            Id = id++,
                            Content = goalContent,
                            Type = MemoryType.Goal,
                            IsActive = true,
                            CreatedAt = DateTime.Now
                        });
                    }
                }
                continue;
            }

            // 섹션 헤더 (## 섹션명)
            if (line.StartsWith("## "))
            {
                var sectionName = line.TrimStart('#', ' ').Trim();
                currentType = ParseSectionName(sectionName);
                currentContext = null;
                currentTags = null;
                currentDate = null;
                continue;
            }

            // 항목 헤더 (### 항목명)
            if (line.StartsWith("### "))
            {
                currentContext = line.TrimStart('#', ' ').Trim();
                continue;
            }

            // 제목 줄 무시 (# 로 시작)
            if (line.StartsWith("#"))
                continue;

            // 태그 줄 (`tags: ...`)
            if (line.StartsWith("`tags:"))
            {
                currentTags = line.Trim('`').Replace("tags:", "").Trim();
                continue;
            }

            // 날짜 줄 (`date: ...`)
            if (line.StartsWith("`date:"))
            {
                currentDate = line.Trim('`').Replace("date:", "").Trim();
                continue;
            }

            // meta/ 섹션의 **키**: 값 형식
            if (line.StartsWith("- **") && currentType == MemoryType.Meta)
            {
                var match = Regex.Match(line, @"\*\*(.+?)\*\*:\s*(.+)");
                if (match.Success)
                {
                    memories.Add(new MemoryEntry
                    {
                        Id = id++,
                        Content = $"{match.Groups[1].Value}: {match.Groups[2].Value}",
                        Type = MemoryType.Meta,
                        Context = match.Groups[1].Value,
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    });
                }
                continue;
            }

            // 메모리 항목 (- 또는 * 로 시작하는 리스트)
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                var itemContent = line.Substring(2).Trim();
                if (!string.IsNullOrWhiteSpace(itemContent) && itemContent.Length > 2)
                {
                    // 템플릿 플레이스홀더 무시
                    if (itemContent == "(없음)" || itemContent == "..." || itemContent == "(none)" || itemContent == "(목표 추가)")
                        continue;

                    memories.Add(new MemoryEntry
                    {
                        Id = id++,
                        Content = itemContent,
                        Type = currentType,
                        Context = currentTags ?? currentContext,
                        IsActive = true,
                        CreatedAt = DateTime.TryParse(currentDate, out var dt) ? dt : DateTime.Now
                    });
                }
            }
        }

        return memories;
    }

    private static string GetTypeSectionName(MemoryType type) => type switch
    {
        MemoryType.Architecture => "architecture/",
        MemoryType.Pattern => "patterns/",
        MemoryType.Tool => "tools/",
        MemoryType.Gotcha => "gotchas/",
        MemoryType.Goal => "프로젝트 목표",
        MemoryType.Meta => "meta/",
        _ => "기타"
    };

    private static MemoryType ParseSectionName(string name)
    {
        var lowerName = name.ToLowerInvariant();

        // 컨텍스트 트리 섹션 매칭
        if (lowerName.Contains("architecture") || name.Contains("아키텍처") || name.Contains("설계"))
            return MemoryType.Architecture;
        if (lowerName.Contains("pattern") || name.Contains("패턴") || name.Contains("워크플로우"))
            return MemoryType.Pattern;
        if (lowerName.Contains("tool") || name.Contains("도구") || name.Contains("mcp"))
            return MemoryType.Tool;
        if (lowerName.Contains("gotcha") || name.Contains("주의") || name.Contains("함정"))
            return MemoryType.Gotcha;
        if (lowerName.Contains("goal") || name.Contains("목표"))
            return MemoryType.Goal;
        if (lowerName.Contains("meta") || name.Contains("메타") || name.Contains("프로젝트"))
            return MemoryType.Meta;

        // 키워드 인덱스는 건너뛰기
        if (lowerName.Contains("키워드") || lowerName.Contains("keyword") || lowerName.Contains("index"))
            return MemoryType.Meta;

        return MemoryType.Architecture;
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

    // 메모리 추출 패턴 (컨텍스트 트리 구조)
    private static readonly (Regex Pattern, MemoryType Type, double Importance)[] ExtractionPatterns = new[]
    {
        // 아키텍처 결정
        (new Regex(@"(?:선택|결정|도입)(?:했|함|하기로).*?(.+?)(?:을|를)?", RegexOptions.IgnoreCase), MemoryType.Architecture, 0.9),
        (new Regex(@"(.+?)(?:패턴|아키텍처|구조)(?:을|를)?\s*(?:사용|적용)", RegexOptions.IgnoreCase), MemoryType.Architecture, 0.8),

        // 작업 패턴
        (new Regex(@"(?:주로|항상|매번)\s*(.+?)(?:을|를)?\s*(?:함|해|사용)", RegexOptions.IgnoreCase), MemoryType.Pattern, 0.7),

        // 도구
        (new Regex(@"(.+?)(?:도구|툴|서버)(?:을|를)?\s*(?:사용|설치)", RegexOptions.IgnoreCase), MemoryType.Tool, 0.8),

        // 주의사항 (gotchas)
        (new Regex(@"(?:주의|조심|피해야|안됨).*?(.+?)(?:을|를)?", RegexOptions.IgnoreCase), MemoryType.Gotcha, 0.9),
        (new Regex(@"(.+?)(?:문제|버그|오류).*?(?:발생|생김|있음)", RegexOptions.IgnoreCase), MemoryType.Gotcha, 0.8),
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
