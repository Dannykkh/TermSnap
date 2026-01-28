using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace TermSnap.Models;

/// <summary>
/// 에이전트 작업 상태
/// </summary>
public enum AgentTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// 에이전트 작업
/// </summary>
public class AgentTask : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N")[..8];
    private string _description = string.Empty;
    private TaskComplexity _complexity = TaskComplexity.Medium;
    private AgentTaskStatus _status = AgentTaskStatus.Pending;
    private string? _result;
    private string? _error;
    private DateTime _createdAt = DateTime.Now;
    private DateTime? _startedAt;
    private DateTime? _completedAt;
    private string? _assignedAgent;
    private ModelTier? _assignedTier;
    private int _priority;
    private List<string> _dependencies = new();

    /// <summary>
    /// 작업 ID
    /// </summary>
    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 작업 설명
    /// </summary>
    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 작업 복잡도
    /// </summary>
    public TaskComplexity Complexity
    {
        get => _complexity;
        set { _complexity = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 작업 상태
    /// </summary>
    public AgentTaskStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 실행 결과
    /// </summary>
    public string? Result
    {
        get => _result;
        set { _result = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 오류 메시지
    /// </summary>
    public string? Error
    {
        get => _error;
        set { _error = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedAt
    {
        get => _createdAt;
        set { _createdAt = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 시작 시간
    /// </summary>
    public DateTime? StartedAt
    {
        get => _startedAt;
        set { _startedAt = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 완료 시간
    /// </summary>
    public DateTime? CompletedAt
    {
        get => _completedAt;
        set { _completedAt = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 할당된 에이전트 이름
    /// </summary>
    public string? AssignedAgent
    {
        get => _assignedAgent;
        set { _assignedAgent = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 할당된 모델 티어
    /// </summary>
    public ModelTier? AssignedTier
    {
        get => _assignedTier;
        set { _assignedTier = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 우선순위 (높을수록 먼저 실행)
    /// </summary>
    public int Priority
    {
        get => _priority;
        set { _priority = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 의존하는 작업 ID 목록
    /// </summary>
    public List<string> Dependencies
    {
        get => _dependencies;
        set { _dependencies = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 실행 시간 (초)
    /// </summary>
    public double? ExecutionTimeSeconds =>
        _startedAt.HasValue && _completedAt.HasValue
            ? (_completedAt.Value - _startedAt.Value).TotalSeconds
            : null;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// 에이전트 응답
/// </summary>
public class AgentResponse
{
    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 응답 내용
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 오류 메시지
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// 사용된 모델
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 토큰 사용량
    /// </summary>
    public int? TokensUsed { get; set; }

    /// <summary>
    /// 실행 시간 (ms)
    /// </summary>
    public long? ExecutionTimeMs { get; set; }

    /// <summary>
    /// 추가 메타데이터
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    public static AgentResponse Ok(string content) => new() { Success = true, Content = content };
    public static AgentResponse Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// 에이전트 컨텍스트 (작업 실행 시 전달)
/// </summary>
public class AgentContext
{
    /// <summary>
    /// 작업 디렉토리
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 프로젝트 컨텍스트 (PRD, 코드 구조 등)
    /// </summary>
    public string? ProjectContext { get; set; }

    /// <summary>
    /// 이전 대화 기록
    /// </summary>
    public List<string>? ConversationHistory { get; set; }

    /// <summary>
    /// 관련 파일 경로들
    /// </summary>
    public List<string>? RelevantFiles { get; set; }

    /// <summary>
    /// 추가 파라미터
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }
}
