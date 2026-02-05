using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TermSnap.Services;

/// <summary>
/// 대화 검색 결과 항목
/// </summary>
public class ConversationSearchResult
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public DateTime Date { get; set; }
    public int LineNumber { get; set; }
    public string MatchedLine { get; set; } = "";
    public string Context { get; set; } = "";  // 주변 컨텍스트
    public List<string> Tags { get; set; } = new();
    public string Summary { get; set; } = "";

    // UI용 속성
    public string DateDisplay => Date.ToString("yyyy-MM-dd");
    public string TagsDisplay => Tags.Any() ? string.Join(", ", Tags.Take(5)) : "-";
}

/// <summary>
/// 대화 파일 인덱스 항목 (메타데이터만 저장)
/// </summary>
public class ConversationIndex
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public DateTime Date { get; set; }
    public List<string> Keywords { get; set; } = new();
    public string Summary { get; set; } = "";
    public long FileSize { get; set; }
}

/// <summary>
/// Lazy Loading 기반 대화 검색 서비스
/// - 태그 인덱스: frontmatter만 파싱하여 메모리 상주
/// - 전문 검색: findstr/grep 프로세스 호출 (메모리 사용 안함)
/// - 컨텍스트 캡처: 필요한 라인만 읽기
/// </summary>
public class ConversationSearchService : IDisposable
{
    private string? _workingDirectory;
    private string? _conversationsPath;
    private List<ConversationIndex> _index = new();
    private bool _disposed = false;
    private const int ContextLines = 5; // 검색 결과 전후 N줄

    public ConversationSearchService() { }

    public ConversationSearchService(string workingDirectory) : this()
    {
        SetWorkingDirectory(workingDirectory);
    }

    /// <summary>
    /// 작업 디렉토리 설정 및 인덱스 빌드
    /// </summary>
    public void SetWorkingDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        _workingDirectory = directory;
        _conversationsPath = Path.Combine(directory, ".claude", "conversations");

        // 인덱스 빌드 (frontmatter만)
        BuildIndex();
    }

    /// <summary>
    /// 태그 인덱스 빌드 (frontmatter만 파싱 - 빠름)
    /// </summary>
    public void BuildIndex()
    {
        _index.Clear();

        if (string.IsNullOrEmpty(_conversationsPath) || !Directory.Exists(_conversationsPath))
            return;

        try
        {
            var files = Directory.GetFiles(_conversationsPath, "*.md")
                .OrderByDescending(f => f);

            foreach (var file in files)
            {
                var indexItem = ParseFrontmatterOnly(file);
                if (indexItem != null)
                {
                    _index.Add(indexItem);
                }
            }

            Debug.WriteLine($"[ConversationSearch] 인덱스 빌드: {_index.Count}개 파일");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConversationSearch] 인덱스 빌드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// frontmatter만 파싱 (파일 전체 읽지 않음)
    /// </summary>
    private ConversationIndex? ParseFrontmatterOnly(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var index = new ConversationIndex
            {
                FilePath = filePath,
                FileName = Path.GetFileNameWithoutExtension(filePath),
                FileSize = fileInfo.Length
            };

            // frontmatter만 읽기 (최대 50줄)
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            var inFrontmatter = false;
            var lineCount = 0;

            while (!reader.EndOfStream && lineCount < 50)
            {
                var line = reader.ReadLine();
                lineCount++;

                if (line == null) break;

                if (line.Trim() == "---")
                {
                    if (!inFrontmatter)
                    {
                        inFrontmatter = true;
                        continue;
                    }
                    else
                    {
                        break; // frontmatter 끝
                    }
                }

                if (inFrontmatter)
                {
                    // date: 2026-02-05
                    if (line.StartsWith("date:"))
                    {
                        var dateStr = line.Substring(5).Trim();
                        if (DateTime.TryParse(dateStr, out var date))
                            index.Date = date;
                    }
                    // keywords: [tag1, tag2]
                    else if (line.StartsWith("keywords:"))
                    {
                        var keywordsStr = line.Substring(9).Trim();
                        index.Keywords = ParseKeywords(keywordsStr);
                    }
                    // summary: "..."
                    else if (line.StartsWith("summary:"))
                    {
                        index.Summary = line.Substring(8).Trim().Trim('"');
                    }
                }
            }

            // 날짜가 없으면 파일명에서 추출
            if (index.Date == default)
            {
                if (DateTime.TryParse(index.FileName, out var date))
                    index.Date = date;
            }

            return index;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// keywords 배열 파싱 ([tag1, tag2] 형식)
    /// </summary>
    private List<string> ParseKeywords(string keywordsStr)
    {
        var result = new List<string>();

        // [] 제거
        keywordsStr = keywordsStr.Trim('[', ']');
        if (string.IsNullOrWhiteSpace(keywordsStr))
            return result;

        // 쉼표로 분리
        var parts = keywordsStr.Split(',');
        foreach (var part in parts)
        {
            var keyword = part.Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(keyword))
                result.Add(keyword);
        }

        return result;
    }

    /// <summary>
    /// 태그로 검색 (인덱스 사용 - 빠름)
    /// </summary>
    public List<ConversationIndex> SearchByTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return _index.ToList();

        return _index
            .Where(i => i.Keywords.Any(k =>
                k.Contains(tag, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(i => i.Date)
            .ToList();
    }

    /// <summary>
    /// 모든 태그 목록 (자동완성용)
    /// </summary>
    public List<string> GetAllTags()
    {
        return _index
            .SelectMany(i => i.Keywords)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();
    }

    /// <summary>
    /// 전문 검색 (findstr 사용 - 메모리 효율적)
    /// </summary>
    public async Task<List<ConversationSearchResult>> SearchFullTextAsync(string query)
    {
        var results = new List<ConversationSearchResult>();

        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(_conversationsPath))
            return results;

        try
        {
            // findstr 또는 rg 사용
            var searchResults = await RunFindStrAsync(query);

            foreach (var (filePath, lineNumber, matchedLine) in searchResults)
            {
                var indexItem = _index.FirstOrDefault(i => i.FilePath == filePath);

                results.Add(new ConversationSearchResult
                {
                    FilePath = filePath,
                    FileName = Path.GetFileNameWithoutExtension(filePath),
                    Date = indexItem?.Date ?? DateTime.Now,
                    LineNumber = lineNumber,
                    MatchedLine = matchedLine.Trim(),
                    Tags = indexItem?.Keywords ?? new List<string>(),
                    Summary = indexItem?.Summary ?? ""
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConversationSearch] 검색 실패: {ex.Message}");
        }

        return results.OrderByDescending(r => r.Date).ThenBy(r => r.LineNumber).ToList();
    }

    /// <summary>
    /// findstr 실행 (Windows)
    /// </summary>
    private async Task<List<(string FilePath, int LineNumber, string Line)>> RunFindStrAsync(string query)
    {
        var results = new List<(string, int, string)>();

        if (string.IsNullOrEmpty(_conversationsPath))
            return results;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "findstr",
                Arguments = $"/S /I /N \"{query}\" \"{Path.Combine(_conversationsPath, "*.md")}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // 결과 파싱: 파일경로:라인번호:내용
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"^(.+?):(\d+):(.*)$");
                if (match.Success)
                {
                    var filePath = match.Groups[1].Value;
                    if (int.TryParse(match.Groups[2].Value, out var lineNum))
                    {
                        results.Add((filePath, lineNum, match.Groups[3].Value));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConversationSearch] findstr 실행 실패: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// 특정 라인 주변 컨텍스트 가져오기 (Lazy Loading)
    /// </summary>
    public string GetContext(string filePath, int lineNumber, int contextLines = 5)
    {
        if (!File.Exists(filePath))
            return "";

        try
        {
            var startLine = Math.Max(1, lineNumber - contextLines);
            var endLine = lineNumber + contextLines;
            var sb = new StringBuilder();
            var currentLine = 0;

            using var reader = new StreamReader(filePath, Encoding.UTF8);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                currentLine++;

                if (currentLine < startLine) continue;
                if (currentLine > endLine) break;

                // 현재 라인 하이라이트 표시
                if (currentLine == lineNumber)
                    sb.AppendLine($">>> {line}");
                else
                    sb.AppendLine($"    {line}");
            }

            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 특정 날짜의 대화 전체 내용 (필요할 때만 로드)
    /// </summary>
    public string GetFullContent(string filePath)
    {
        if (!File.Exists(filePath))
            return "";

        try
        {
            return File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 인덱스 목록 (UI용)
    /// </summary>
    public List<ConversationIndex> GetAllIndexes()
    {
        return _index.OrderByDescending(i => i.Date).ToList();
    }

    /// <summary>
    /// 날짜 범위로 필터
    /// </summary>
    public List<ConversationIndex> GetByDateRange(DateTime start, DateTime end)
    {
        return _index
            .Where(i => i.Date >= start && i.Date <= end)
            .OrderByDescending(i => i.Date)
            .ToList();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _index.Clear();
    }
}
