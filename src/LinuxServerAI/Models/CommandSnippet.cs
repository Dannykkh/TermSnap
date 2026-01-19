using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nebula.Models;

/// <summary>
/// 자주 쓰는 명령어 스니펫
/// </summary>
public class CommandSnippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Category { get; set; } = "일반";
    public int UseCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; } = DateTime.MinValue;

    // 태그 (검색용)
    public List<string> Tags { get; set; } = new();
    
    /// <summary>
    /// 파라미터 정의 ({{paramName:defaultValue:description}} 형식에서 추출)
    /// </summary>
    public List<WorkflowParameter> Parameters { get; set; } = new();

    /// <summary>
    /// 명령어에 파라미터가 있는지 확인
    /// </summary>
    public bool HasParameters => Regex.IsMatch(Command, @"\{\{[^}]+\}\}");

    /// <summary>
    /// 사용 횟수 증가
    /// </summary>
    public void IncrementUseCount()
    {
        UseCount++;
        LastUsedAt = DateTime.Now;
    }

    /// <summary>
    /// 명령어에서 파라미터 추출
    /// 형식: {{paramName}}, {{paramName:defaultValue}}, {{paramName:defaultValue:description}}
    /// </summary>
    public List<WorkflowParameter> ExtractParameters()
    {
        var parameters = new List<WorkflowParameter>();
        var pattern = @"\{\{([^:}]+)(?::([^:}]*))?(?::([^}]*))?\}\}";
        var matches = Regex.Matches(Command, pattern);

        foreach (Match match in matches)
        {
            var paramName = match.Groups[1].Value.Trim();
            var defaultValue = match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;
            var description = match.Groups[3].Success ? match.Groups[3].Value.Trim() : string.Empty;

            // 중복 제거
            if (!parameters.Any(p => p.Name == paramName))
            {
                parameters.Add(new WorkflowParameter
                {
                    Name = paramName,
                    DefaultValue = defaultValue,
                    Description = description,
                    Value = defaultValue // 초기값으로 기본값 설정
                });
            }
        }

        return parameters;
    }

    /// <summary>
    /// 파라미터 값을 적용한 최종 명령어 생성
    /// </summary>
    public string ResolveCommand(Dictionary<string, string>? parameterValues = null)
    {
        var resolvedCommand = Command;
        var pattern = @"\{\{([^:}]+)(?::[^}]*)?\}\}";
        
        resolvedCommand = Regex.Replace(resolvedCommand, pattern, match =>
        {
            var paramName = match.Groups[1].Value.Trim();
            
            // 제공된 값이 있으면 사용, 아니면 기본값 또는 빈 문자열
            if (parameterValues?.TryGetValue(paramName, out var value) == true)
            {
                return value;
            }
            
            // Parameters 컬렉션에서 기본값 찾기
            var param = Parameters.FirstOrDefault(p => p.Name == paramName);
            return param?.Value ?? param?.DefaultValue ?? string.Empty;
        });

        return resolvedCommand;
    }

    public override string ToString()
    {
        return $"{Name}: {Command}";
    }
}

/// <summary>
/// 워크플로우 파라미터
/// </summary>
public class WorkflowParameter
{
    /// <summary>
    /// 파라미터 이름
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 기본값
    /// </summary>
    public string DefaultValue { get; set; } = string.Empty;

    /// <summary>
    /// 파라미터 설명
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 현재 값 (사용자 입력)
    /// </summary>
    public string Value { get; set; } = string.Empty;
    
    /// <summary>
    /// 파라미터 타입 (향후 확장용)
    /// </summary>
    public WorkflowParameterType Type { get; set; } = WorkflowParameterType.Text;
}

/// <summary>
/// 파라미터 타입
/// </summary>
public enum WorkflowParameterType
{
    Text,       // 일반 텍스트
    Number,     // 숫자
    FilePath,   // 파일 경로
    Directory,  // 디렉토리 경로
    Choice,     // 선택 목록
    Boolean     // 예/아니오
}

/// <summary>
/// 명령어 스니펫 컬렉션
/// </summary>
public class CommandSnippetCollection
{
    public List<CommandSnippet> Snippets { get; set; } = new();

    /// <summary>
    /// 스니펫 추가
    /// </summary>
    public void Add(CommandSnippet snippet)
    {
        Snippets.Add(snippet);
    }

    /// <summary>
    /// 스니펫 삭제
    /// </summary>
    public bool Remove(string id)
    {
        var snippet = Snippets.FirstOrDefault(s => s.Id == id);
        if (snippet != null)
        {
            return Snippets.Remove(snippet);
        }
        return false;
    }

    /// <summary>
    /// 검색 (이름, 설명, 명령어, 태그에서)
    /// </summary>
    public List<CommandSnippet> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Snippets;

        query = query.ToLower();

        return Snippets.Where(s =>
            s.Name.ToLower().Contains(query) ||
            s.Description.ToLower().Contains(query) ||
            s.Command.ToLower().Contains(query) ||
            s.Tags.Any(t => t.ToLower().Contains(query))
        ).ToList();
    }

    /// <summary>
    /// 카테고리별 조회
    /// </summary>
    public List<CommandSnippet> GetByCategory(string category)
    {
        return Snippets.Where(s => s.Category == category).ToList();
    }

    /// <summary>
    /// 자주 사용하는 순서로 정렬
    /// </summary>
    public List<CommandSnippet> GetMostUsed(int count = 10)
    {
        return Snippets
            .OrderByDescending(s => s.UseCount)
            .ThenByDescending(s => s.LastUsedAt)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// 최근 사용한 순서로 정렬
    /// </summary>
    public List<CommandSnippet> GetRecentlyUsed(int count = 10)
    {
        return Snippets
            .Where(s => s.LastUsedAt != DateTime.MinValue)
            .OrderByDescending(s => s.LastUsedAt)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// 모든 카테고리 목록
    /// </summary>
    public List<string> GetAllCategories()
    {
        return Snippets.Select(s => s.Category).Distinct().OrderBy(c => c).ToList();
    }

    /// <summary>
    /// 기본 스니펫 생성
    /// </summary>
    public static CommandSnippetCollection CreateDefault()
    {
        var collection = new CommandSnippetCollection();

        // 시스템 정보
        collection.Add(new CommandSnippet
        {
            Name = "시스템 정보",
            Description = "전체 시스템 정보 확인 (OS, 커널, 메모리, CPU)",
            Command = "uname -a && cat /etc/os-release && free -h && lscpu | grep 'Model name'",
            Category = "시스템",
            Tags = new List<string> { "시스템", "정보", "상태" }
        });

        collection.Add(new CommandSnippet
        {
            Name = "디스크 사용량",
            Description = "디스크 파티션별 사용량 확인",
            Command = "df -h",
            Category = "시스템",
            Tags = new List<string> { "디스크", "용량", "저장소" }
        });

        collection.Add(new CommandSnippet
        {
            Name = "메모리 사용량",
            Description = "메모리 및 스왑 사용 현황",
            Command = "free -h",
            Category = "시스템",
            Tags = new List<string> { "메모리", "RAM", "스왑" }
        });

        // 프로세스 관리
        collection.Add(new CommandSnippet
        {
            Name = "CPU 많이 쓰는 프로세스",
            Description = "CPU 사용률 높은 프로세스 상위 10개",
            Command = "ps aux --sort=-%cpu | head -n 11",
            Category = "프로세스",
            Tags = new List<string> { "프로세스", "CPU", "성능" }
        });

        collection.Add(new CommandSnippet
        {
            Name = "메모리 많이 쓰는 프로세스",
            Description = "메모리 사용량 높은 프로세스 상위 10개",
            Command = "ps aux --sort=-%mem | head -n 11",
            Category = "프로세스",
            Tags = new List<string> { "프로세스", "메모리", "성능" }
        });

        // 네트워크
        collection.Add(new CommandSnippet
        {
            Name = "열린 포트 확인",
            Description = "현재 리스닝 중인 포트 목록",
            Command = "sudo netstat -tlnp",
            Category = "네트워크",
            Tags = new List<string> { "네트워크", "포트", "리스닝" }
        });

        collection.Add(new CommandSnippet
        {
            Name = "외부 IP 확인",
            Description = "서버의 공인 IP 주소 확인",
            Command = "curl -s ifconfig.me",
            Category = "네트워크",
            Tags = new List<string> { "네트워크", "IP", "공인IP" }
        });

        // 로그
        collection.Add(new CommandSnippet
        {
            Name = "시스템 로그 (최근)",
            Description = "시스템 로그 마지막 50줄",
            Command = "sudo journalctl -n 50 --no-pager",
            Category = "로그",
            Tags = new List<string> { "로그", "시스템", "journalctl" }
        });

        collection.Add(new CommandSnippet
        {
            Name = "인증 실패 로그",
            Description = "실패한 로그인 시도 확인",
            Command = "sudo grep 'Failed password' /var/log/auth.log | tail -n 20",
            Category = "로그",
            Tags = new List<string> { "로그", "보안", "인증" }
        });

        // Docker
        collection.Add(new CommandSnippet
        {
            Name = "Docker 컨테이너 목록",
            Description = "실행 중인 Docker 컨테이너 확인",
            Command = "docker ps",
            Category = "Docker",
            Tags = new List<string> { "docker", "컨테이너" }
        });

        collection.Add(new CommandSnippet
        {
            Name = "Docker 이미지 목록",
            Description = "저장된 Docker 이미지 확인",
            Command = "docker images",
            Category = "Docker",
            Tags = new List<string> { "docker", "이미지" }
        });
        
        // 워크플로우 예제 (파라미터 포함)
        collection.Add(new CommandSnippet
        {
            Name = "파일 검색",
            Description = "특정 이름의 파일 검색 (파라미터 입력)",
            Command = "find {{directory:/::검색할 디렉토리}} -name '{{pattern:*::파일 패턴}}'",
            Category = "워크플로우",
            Tags = new List<string> { "find", "검색", "파라미터" }
        });

        collection.Add(new CommandSnippet
        {
            Name = "서비스 상태 확인",
            Description = "특정 서비스의 상태 확인 (파라미터 입력)",
            Command = "systemctl status {{service:nginx::서비스 이름}}",
            Category = "워크플로우",
            Tags = new List<string> { "systemctl", "서비스", "파라미터" }
        });

        collection.Add(new CommandSnippet
        {
            Name = "로그 검색",
            Description = "특정 키워드로 로그 파일 검색",
            Command = "grep -i '{{keyword:error::검색할 키워드}}' {{logfile:/var/log/syslog::로그 파일 경로}} | tail -n {{lines:50::표시할 줄 수}}",
            Category = "워크플로우",
            Tags = new List<string> { "grep", "로그", "파라미터" }
        });

        return collection;
    }
}
