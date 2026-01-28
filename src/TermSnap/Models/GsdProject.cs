using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TermSnap.Models;

/// <summary>
/// GSD 프로젝트 설정
/// </summary>
public class GsdConfig
{
    /// <summary>
    /// 프로젝트 이름
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// 생성 일시
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 실행 모드 (yolo: 자동, interactive: 대화형)
    /// </summary>
    public string ExecutionMode { get; set; } = "interactive";

    /// <summary>
    /// 계획 깊이 (quick, standard, detailed)
    /// </summary>
    public string PlanningDepth { get; set; } = "standard";

    /// <summary>
    /// Git 추적 여부
    /// </summary>
    public bool GitTracking { get; set; } = true;

    /// <summary>
    /// AI 모델 프로필
    /// </summary>
    public string ModelProfile { get; set; } = "balanced";

    /// <summary>
    /// 현재 페이즈 번호
    /// </summary>
    public int CurrentPhase { get; set; } = 0;

    /// <summary>
    /// 현재 단계 (discuss, plan, execute, verify)
    /// </summary>
    public string CurrentStep { get; set; } = "init";
}

/// <summary>
/// GSD 페이즈 정보
/// </summary>
public class GsdPhase
{
    /// <summary>
    /// 페이즈 번호
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// 페이즈 이름
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 페이즈 설명
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 상태 (pending, in_progress, completed)
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// 완료된 단계들
    /// </summary>
    public List<string> CompletedSteps { get; set; } = new();

    /// <summary>
    /// 성공 기준
    /// </summary>
    public List<string> SuccessCriteria { get; set; } = new();
}

/// <summary>
/// GSD 프로젝트 상태
/// </summary>
public class GsdState
{
    /// <summary>
    /// 현재 페이즈
    /// </summary>
    public int CurrentPhase { get; set; } = 1;

    /// <summary>
    /// 현재 단계
    /// </summary>
    public string CurrentStep { get; set; } = "discuss";

    /// <summary>
    /// 마지막 업데이트
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>
    /// 완료된 작업들
    /// </summary>
    public List<string> CompletedTasks { get; set; } = new();

    /// <summary>
    /// 보류 중인 작업들
    /// </summary>
    public List<string> PendingTasks { get; set; } = new();

    /// <summary>
    /// 메모/노트
    /// </summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// GSD 워크플로우 단계 (열거형)
/// </summary>
public enum GsdStep
{
    Discuss = 0,
    Plan = 1,
    Execute = 2,
    Verify = 3
}

/// <summary>
/// GSD 실행 모드 (에이전트 시스템 확장)
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// Interactive - 각 단계마다 사용자 확인 (기본값)
    /// </summary>
    Interactive,

    /// <summary>
    /// Yolo - 자동 실행 (확인 없이)
    /// </summary>
    Yolo,

    /// <summary>
    /// EcoMode - 저비용 모델 우선 사용
    /// </summary>
    EcoMode,

    /// <summary>
    /// Pipeline - 순차 실행 (결과를 다음 단계로 전달)
    /// </summary>
    Pipeline,

    /// <summary>
    /// Swarm - 병렬 실행 (3-5개 에이전트 동시)
    /// </summary>
    Swarm,

    /// <summary>
    /// Expert - 전문 에이전트 자동 선택
    /// </summary>
    Expert
}

/// <summary>
/// GSD 워크플로우 설정 (UI 바인딩용)
/// </summary>
public class GsdWorkflowConfig : INotifyPropertyChanged
{
    private int _currentPhase = 1;
    private GsdStep _currentStep = GsdStep.Discuss;
    private bool _isExecuting;
    private int _maxIterations = 100;
    private int _currentIteration;
    private DateTime? _executeStartTime;
    private string _workingDirectory = string.Empty;
    private string _prdContent = string.Empty;
    private string _currentTask = string.Empty;
    private string _aiCommand = "claude";

    /// <summary>
    /// 현재 Phase 번호
    /// </summary>
    public int CurrentPhase
    {
        get => _currentPhase;
        set { _currentPhase = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 현재 단계
    /// </summary>
    public GsdStep CurrentStep
    {
        get => _currentStep;
        set { _currentStep = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentStepText)); }
    }

    /// <summary>
    /// 현재 단계 텍스트
    /// </summary>
    public string CurrentStepText => _currentStep switch
    {
        GsdStep.Discuss => "Discuss",
        GsdStep.Plan => "Plan",
        GsdStep.Execute => "Execute",
        GsdStep.Verify => "Verify",
        _ => "Unknown"
    };

    /// <summary>
    /// Execute 실행 중 여부
    /// </summary>
    public bool IsExecuting
    {
        get => _isExecuting;
        set { _isExecuting = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 최대 반복 횟수
    /// </summary>
    public int MaxIterations
    {
        get => _maxIterations;
        set { _maxIterations = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 현재 반복 횟수
    /// </summary>
    public int CurrentIteration
    {
        get => _currentIteration;
        set { _currentIteration = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); }
    }

    /// <summary>
    /// 진행률 (0-100)
    /// </summary>
    public int Progress => _maxIterations > 0 ? (int)((double)_currentIteration / _maxIterations * 100) : 0;

    /// <summary>
    /// Execute 시작 시간
    /// </summary>
    public DateTime? ExecuteStartTime
    {
        get => _executeStartTime;
        set { _executeStartTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(ElapsedTime)); }
    }

    /// <summary>
    /// 경과 시간
    /// </summary>
    public TimeSpan ElapsedTime => _executeStartTime.HasValue ? DateTime.Now - _executeStartTime.Value : TimeSpan.Zero;

    /// <summary>
    /// 작업 디렉토리
    /// </summary>
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set { _workingDirectory = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// PRD 내용 (Execute 단계에서 Plan에서 로드)
    /// </summary>
    public string PrdContent
    {
        get => _prdContent;
        set { _prdContent = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 현재 작업 중인 태스크
    /// </summary>
    public string CurrentTask
    {
        get => _currentTask;
        set { _currentTask = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// AI CLI 명령어
    /// </summary>
    public string AICommand
    {
        get => _aiCommand;
        set { _aiCommand = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Execute 상태 초기화
    /// </summary>
    public void ResetExecute()
    {
        CurrentIteration = 0;
        IsExecuting = false;
        ExecuteStartTime = null;
        CurrentTask = string.Empty;
    }
}

/// <summary>
/// GSD 태스크 (Plan에서 추출)
/// </summary>
public class GsdTask : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private bool _isCompleted;

    /// <summary>
    /// 태스크 내용
    /// </summary>
    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 완료 여부
    /// </summary>
    public bool IsCompleted
    {
        get => _isCompleted;
        set { _isCompleted = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
