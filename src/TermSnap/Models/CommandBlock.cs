using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TermSnap.Services;

namespace TermSnap.Models;

/// <summary>
/// Warp 스타일 Command Block - 명령어와 출력을 하나의 단위로 그룹화
/// </summary>
public class CommandBlock : INotifyPropertyChanged
{
    private string _userInput = string.Empty;
    private string _generatedCommand = string.Empty;
    private string _explanation = string.Empty;
    private string _output = string.Empty;
    private string _error = string.Empty;
    private BlockStatus _status = BlockStatus.Pending;
    private bool _isExpanded = true;
    private bool _isFromCache = false;
    private double _similarity = 0;
    private string _searchMethod = string.Empty;
    private TimeSpan _duration;

    /// <summary>
    /// 블록 ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 블록 생성 시간
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// 사용자 입력 (자연어 요청)
    /// </summary>
    public string UserInput
    {
        get => _userInput;
        set { _userInput = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// AI가 생성한 명령어
    /// </summary>
    public string GeneratedCommand
    {
        get => _generatedCommand;
        set { _generatedCommand = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 명령어 설명
    /// </summary>
    public string Explanation
    {
        get => _explanation;
        set { _explanation = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasExplanation)); }
    }

    /// <summary>
    /// 명령어 실행 출력
    /// </summary>
    public string Output
    {
        get => _output;
        set { _output = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasOutput)); }
    }

    /// <summary>
    /// 에러 메시지
    /// </summary>
    public string Error
    {
        get => _error;
        set { _error = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }

    /// <summary>
    /// 블록 상태
    /// </summary>
    public BlockStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusIcon)); OnPropertyChanged(nameof(StatusColor)); }
    }

    /// <summary>
    /// 블록 확장/축소 상태
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 캐시에서 가져온 결과인지
    /// </summary>
    public bool IsFromCache
    {
        get => _isFromCache;
        set { _isFromCache = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 캐시 유사도 (RAG)
    /// </summary>
    public double Similarity
    {
        get => _similarity;
        set { _similarity = value; OnPropertyChanged(); OnPropertyChanged(nameof(SimilarityText)); }
    }

    /// <summary>
    /// 검색 방식 (fts5, embedding, none)
    /// </summary>
    public string SearchMethod
    {
        get => _searchMethod;
        set { _searchMethod = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 실행 시간
    /// </summary>
    public TimeSpan Duration
    {
        get => _duration;
        set { _duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); }
    }

    /// <summary>
    /// 현재 작업 디렉토리
    /// </summary>
    public string CurrentDirectory { get; set; } = "~";

    /// <summary>
    /// 서버 프로필 이름
    /// </summary>
    public string ServerProfile { get; set; } = string.Empty;

    /// <summary>
    /// 로컬 세션 여부 (true: 로컬 터미널, false: SSH 서버)
    /// </summary>
    public bool IsLocalSession { get; set; } = false;

    /// <summary>
    /// 엔트리 타입 (명령어, 시스템 메시지 등)
    /// </summary>
    public EntryType Type { get; set; } = EntryType.Command;

    #region Computed Properties

    /// <summary>
    /// 명령어 타입인지 (채팅 UI에서 오른쪽 표시용)
    /// </summary>
    public bool IsCommandEntry => Type == EntryType.Command;

    /// <summary>
    /// 시스템 메시지인지 (채팅 UI에서 왼쪽/중앙 표시용)
    /// </summary>
    public bool IsSystemEntry => Type != EntryType.Command;

    public bool HasExplanation => !string.IsNullOrWhiteSpace(Explanation);
    public bool HasOutput => !string.IsNullOrWhiteSpace(Output);
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public string StatusIcon => Status switch
    {
        BlockStatus.Pending => "⏳",
        BlockStatus.Generating => "🤖",
        BlockStatus.Confirming => "❓",
        BlockStatus.Executing => "⚡",
        BlockStatus.Success => "✓",
        BlockStatus.Failed => "✗",
        BlockStatus.Cancelled => "⊘",
        _ => "•"
    };

    public string StatusColor => Status switch
    {
        BlockStatus.Pending => "#9E9E9E",
        BlockStatus.Generating => "#2196F3",
        BlockStatus.Confirming => "#FF9800",
        BlockStatus.Executing => "#03A9F4",
        BlockStatus.Success => "#4CAF50",
        BlockStatus.Failed => "#F44336",
        BlockStatus.Cancelled => "#757575",
        _ => "#9E9E9E"
    };

    public string SimilarityText => IsFromCache ? $"{Similarity:P0}" : string.Empty;
    public string DurationText => Duration.TotalSeconds > 0 ? $"{Duration.TotalMilliseconds:N0}ms" : string.Empty;

    /// <summary>
    /// 응답 레이블 (로컬: "실행 결과", 서버: "서버 응답")
    /// </summary>
    public string ResponseLabel => IsLocalSession
        ? LocalizationService.Instance.GetString("CommandBlock.ExecutionResult")
        : LocalizationService.Instance.GetString("CommandBlock.ServerResponse");

    /// <summary>
    /// 응답 아이콘 (로컬: Console, 서버: ServerOutline)
    /// </summary>
    public string ResponseIconKind => IsLocalSession ? "Console" : "ServerOutline";

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// 블록 상태
/// </summary>
public enum BlockStatus
{
    /// <summary>대기 중</summary>
    Pending,

    /// <summary>AI가 명령어 생성 중</summary>
    Generating,

    /// <summary>사용자 확인 대기 중</summary>
    Confirming,

    /// <summary>명령어 실행 중</summary>
    Executing,

    /// <summary>실행 성공</summary>
    Success,

    /// <summary>실행 실패</summary>
    Failed,

    /// <summary>사용자가 취소</summary>
    Cancelled
}

/// <summary>
/// 엔트리 타입
/// </summary>
public enum EntryType
{
    /// <summary>명령어 실행 (사용자 -> 서버)</summary>
    Command,

    /// <summary>시스템 정보 메시지</summary>
    Info,

    /// <summary>환영 메시지</summary>
    Welcome,

    /// <summary>경고 메시지</summary>
    Warning,

    /// <summary>에러 메시지</summary>
    Error
}
