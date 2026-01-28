using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TermSnap.Models;

/// <summary>
/// Ralph Loop 설정 모델
/// PRD 기반 자동 루프 실행을 위한 구성
/// </summary>
public class RalphLoopConfig : INotifyPropertyChanged
{
    private string _prd = string.Empty;
    private string _workingDirectory = string.Empty;
    private int _maxIterations = 100;
    private int _currentIteration = 0;
    private RalphLoopState _state = RalphLoopState.Idle;
    private string _currentTask = string.Empty;
    private DateTime? _startTime;
    private List<string> _completedTasks = new();
    private string _aiCommand = "claude";  // 기본 AI CLI 명령어

    /// <summary>
    /// PRD (Product Requirements Document) 내용
    /// </summary>
    public string PRD
    {
        get => _prd;
        set { _prd = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 작업 디렉토리
    /// </summary>
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set { _workingDirectory = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 최대 반복 횟수 (무한 루프 방지)
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
    /// 현재 상태
    /// </summary>
    public RalphLoopState State
    {
        get => _state;
        set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(StateText)); }
    }

    /// <summary>
    /// 실행 중 여부
    /// </summary>
    public bool IsRunning => _state == RalphLoopState.Running || _state == RalphLoopState.WaitingForResponse;

    /// <summary>
    /// 상태 텍스트
    /// </summary>
    public string StateText => _state switch
    {
        RalphLoopState.Idle => "대기 중",
        RalphLoopState.Running => "실행 중",
        RalphLoopState.WaitingForResponse => "응답 대기",
        RalphLoopState.Paused => "일시 정지",
        RalphLoopState.Completed => "완료",
        RalphLoopState.Error => "오류",
        _ => "알 수 없음"
    };

    /// <summary>
    /// 현재 작업 중인 태스크
    /// </summary>
    public string CurrentTask
    {
        get => _currentTask;
        set { _currentTask = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 시작 시간
    /// </summary>
    public DateTime? StartTime
    {
        get => _startTime;
        set { _startTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(ElapsedTime)); }
    }

    /// <summary>
    /// 경과 시간
    /// </summary>
    public TimeSpan ElapsedTime => _startTime.HasValue ? DateTime.Now - _startTime.Value : TimeSpan.Zero;

    /// <summary>
    /// 완료된 태스크 목록
    /// </summary>
    public List<string> CompletedTasks
    {
        get => _completedTasks;
        set { _completedTasks = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// AI CLI 명령어 (claude, aider, codex 등)
    /// </summary>
    public string AICommand
    {
        get => _aiCommand;
        set { _aiCommand = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 설정 초기화
    /// </summary>
    public void Reset()
    {
        CurrentIteration = 0;
        State = RalphLoopState.Idle;
        CurrentTask = string.Empty;
        StartTime = null;
        CompletedTasks.Clear();
        OnPropertyChanged(nameof(CompletedTasks));
    }
}

/// <summary>
/// Ralph Loop 상태
/// </summary>
public enum RalphLoopState
{
    Idle,              // 대기 중
    Running,           // 실행 중
    WaitingForResponse,// AI 응답 대기
    Paused,            // 일시 정지
    Completed,         // 완료
    Error              // 오류
}
