using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MaterialDesignThemes.Wpf;
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

    // AI JSON 응답 관련 필드
    private double _confidence = 1.0;
    private string? _warning;
    private List<string>? _alternatives;
    private bool _requiresSudo;
    private bool _isDangerous;
    private string? _category;
    private int? _estimatedDuration;
    private CommandRiskLevel _riskLevel = CommandRiskLevel.Low;

    // 오류 분석 관련 필드
    private string? _errorCause;
    private string? _errorSolution;
    private string? _requiredAction;

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

    #region AI JSON 응답 속성

    /// <summary>
    /// AI 신뢰도 (0.0 ~ 1.0)
    /// </summary>
    public double Confidence
    {
        get => _confidence;
        set { _confidence = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConfidencePercent)); OnPropertyChanged(nameof(ConfidenceColor)); }
    }

    /// <summary>
    /// 경고 메시지
    /// </summary>
    public string? Warning
    {
        get => _warning;
        set { _warning = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasWarning)); }
    }

    /// <summary>
    /// 대체 명령어 목록
    /// </summary>
    public List<string>? Alternatives
    {
        get => _alternatives;
        set { _alternatives = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAlternatives)); OnPropertyChanged(nameof(AlternativesText)); }
    }

    /// <summary>
    /// sudo 필요 여부
    /// </summary>
    public bool RequiresSudo
    {
        get => _requiresSudo;
        set { _requiresSudo = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 위험한 명령어 여부
    /// </summary>
    public bool IsDangerous
    {
        get => _isDangerous;
        set { _isDangerous = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 명령어 카테고리 (파일, 네트워크, 프로세스, 시스템, 패키지)
    /// </summary>
    public string? Category
    {
        get => _category;
        set { _category = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCategory)); OnPropertyChanged(nameof(CategoryIcon)); }
    }

    /// <summary>
    /// 예상 실행 시간 (초)
    /// </summary>
    public int? EstimatedDuration
    {
        get => _estimatedDuration;
        set { _estimatedDuration = value; OnPropertyChanged(); OnPropertyChanged(nameof(EstimatedDurationText)); }
    }

    /// <summary>
    /// 명령어 위험도 레벨
    /// </summary>
    public CommandRiskLevel RiskLevel
    {
        get => _riskLevel;
        set { _riskLevel = value; OnPropertyChanged(); OnPropertyChanged(nameof(RiskLevelText)); OnPropertyChanged(nameof(RiskLevelColor)); OnPropertyChanged(nameof(RiskLevelIcon)); }
    }

    #endregion

    #region 오류 분석 속성

    /// <summary>
    /// 오류 원인 (AI 분석)
    /// </summary>
    public string? ErrorCause
    {
        get => _errorCause;
        set { _errorCause = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasErrorAnalysis)); }
    }

    /// <summary>
    /// 오류 해결 방법 (AI 분석)
    /// </summary>
    public string? ErrorSolution
    {
        get => _errorSolution;
        set { _errorSolution = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 필요한 조치 (예: 패키지 설치)
    /// </summary>
    public string? RequiredAction
    {
        get => _requiredAction;
        set { _requiredAction = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRequiredAction)); }
    }

    #endregion

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

    // AI JSON 응답 Computed Properties
    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);
    public bool HasAlternatives => Alternatives != null && Alternatives.Count > 0;
    public bool HasCategory => !string.IsNullOrWhiteSpace(Category);
    public string ConfidencePercent => $"{Confidence * 100:0}%";
    public string AlternativesText => HasAlternatives ? string.Join(" / ", Alternatives!) : string.Empty;
    public string EstimatedDurationText => EstimatedDuration.HasValue ? $"~{EstimatedDuration}초" : string.Empty;

    // 오류 분석 Computed Properties
    public bool HasErrorAnalysis => !string.IsNullOrWhiteSpace(ErrorCause);
    public bool HasRequiredAction => !string.IsNullOrWhiteSpace(RequiredAction);

    /// <summary>
    /// 위험도 레벨 텍스트
    /// </summary>
    public string RiskLevelText => RiskLevel switch
    {
        CommandRiskLevel.Low => "안전",
        CommandRiskLevel.Medium => "주의",
        CommandRiskLevel.High => "위험",
        CommandRiskLevel.Critical => "치명적",
        _ => "알 수 없음"
    };

    /// <summary>
    /// 위험도 레벨 색상
    /// </summary>
    public string RiskLevelColor => RiskLevel switch
    {
        CommandRiskLevel.Low => "#4CAF50",      // 녹색
        CommandRiskLevel.Medium => "#FF9800",   // 주황
        CommandRiskLevel.High => "#FF5722",     // 진한 주황
        CommandRiskLevel.Critical => "#F44336", // 빨강
        _ => "#9E9E9E"
    };

    /// <summary>
    /// 위험도 레벨 아이콘
    /// </summary>
    public PackIconKind RiskLevelIcon => RiskLevel switch
    {
        CommandRiskLevel.Low => PackIconKind.CheckCircleOutline,
        CommandRiskLevel.Medium => PackIconKind.AlertCircleOutline,
        CommandRiskLevel.High => PackIconKind.AlertOutline,
        CommandRiskLevel.Critical => PackIconKind.SkullOutline,
        _ => PackIconKind.HelpCircleOutline
    };

    /// <summary>
    /// 신뢰도에 따른 색상
    /// </summary>
    public string ConfidenceColor => Confidence switch
    {
        >= 0.9 => "#4CAF50",  // 녹색 (높음)
        >= 0.7 => "#FF9800",  // 주황색 (중간)
        _ => "#F44336"        // 빨간색 (낮음)
    };

    /// <summary>
    /// 카테고리 아이콘
    /// </summary>
    public PackIconKind CategoryIcon => Category?.ToLower() switch
    {
        "파일" => PackIconKind.FileOutline,
        "네트워크" => PackIconKind.Web,
        "프로세스" => PackIconKind.Memory,
        "시스템" => PackIconKind.Cog,
        "패키지" => PackIconKind.Package,
        _ => PackIconKind.Console
    };

    /// <summary>
    /// 응답 레이블 (로컬: "실행 결과", 서버: "서버 응답")
    /// </summary>
    public string ResponseLabel => IsLocalSession
        ? LocalizationService.Instance.GetString("CommandBlock.ExecutionResult")
        : LocalizationService.Instance.GetString("CommandBlock.ServerResponse");

    /// <summary>
    /// 응답 아이콘 (로컬: Console, 서버: ServerOutline)
    /// </summary>
    public PackIconKind ResponseIconKind => IsLocalSession ? PackIconKind.Console : PackIconKind.ServerOutline;

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
