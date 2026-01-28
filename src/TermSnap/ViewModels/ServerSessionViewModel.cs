using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TermSnap.Core;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.ViewModels;

/// <summary>
/// 단일 서버 세션 뷰모델
/// Ring Buffer로 메모리 누수 방지
/// </summary>
public class ServerSessionViewModel : INotifyPropertyChanged, ISessionViewModel
{
    private readonly AppConfig _config;
    private SshService? _sshService;
    private ErrorHandler? _errorHandler;
    private ServerConfig? _serverProfile;

    private string _userInput = string.Empty;
    private bool _isConnected = false;
    private bool _isBusy = false;
    private string _statusMessage = LocalizationService.Instance.GetString("ViewModel.NotConnected");
    private string _tabHeader = LocalizationService.Instance.GetString("ViewModel.NewSession");
    private string _currentDirectory = "~";
    private bool _useShellStream = false; // ShellStream 모드 사용 여부 (false = CreateCommand 사용, pm2 등 호환성 향상)
    private ObservableCollection<FrequentCommand> _frequentCommands = new();
    private bool _useBlockUI = true; // Block UI 사용 여부
    private bool _useAISuggestion = true; // AI 추천/변환 사용 여부
    private bool _isFileTreeVisible = false; // 파일 트리 패널 표시 여부
    private string? _fileTreeCurrentPath = null; // 파일 트리 현재 경로
    private bool _showSnippetPanel = false; // 스니펫 패널 표시 여부 (서버 세션에서는 사용 안 함)

    // Port Forwarding
    private ObservableCollection<PortForwardingConfig> _portForwardings = new();

    // Spinner for data receiving indicator
    private static readonly string[] SpinnerFrames = { "/", "-", "\\", "|" };
    private int _spinnerFrameIndex = 0;
    private string _spinnerText = string.Empty;
    private System.Windows.Threading.DispatcherTimer? _spinnerTimer;
    private System.Windows.Threading.DispatcherTimer? _dataReceivedTimer;
    private DateTime _lastDataReceivedTime = DateTime.MinValue;

    // Scroll position for tab switching
    private double _savedScrollVerticalOffset = 0;
    private double _savedTerminalScrollVerticalOffset = 0;

    // Command history navigation
    private List<string> _commandHistoryList = new();
    private int _commandHistoryIndex = -1;
    private string _currentEditingCommand = string.Empty;

    // CommandBlock search/filter
    private string _searchText = string.Empty;
    private BlockStatus? _statusFilter = null;

    // Real-time output streaming
    private CommandBlock? _currentExecutingBlock = null;

    /// <summary>
    /// Block UI 스크롤 위치 (탭 전환 시 유지)
    /// </summary>
    public double SavedScrollVerticalOffset
    {
        get => _savedScrollVerticalOffset;
        set { _savedScrollVerticalOffset = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Terminal (Messages) 스크롤 위치 (탭 전환 시 유지)
    /// </summary>
    public double SavedTerminalScrollVerticalOffset
    {
        get => _savedTerminalScrollVerticalOffset;
        set { _savedTerminalScrollVerticalOffset = value; OnPropertyChanged(); }
    }

    // Ring Buffer 설정 - 메모리 누수 방지
    private const int MaxMessages = 500;        // 최대 메시지 수
    private const int MaxCommandBlocks = 200;   // 최대 명령 블록 수
    private const int TrimCount = 50;           // 한 번에 삭제할 개수

    /// <summary>
    /// 기존 채팅 메시지 (Ring Buffer 적용 - 최대 500개)
    /// </summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    /// <summary>
    /// Command Block 목록 (Ring Buffer 적용 - 최대 200개)
    /// </summary>
    public ObservableCollection<CommandBlock> CommandBlocks { get; } = new();

    /// <summary>
    /// 필터링된 Command Block 목록 (검색어 적용)
    /// </summary>
    public IEnumerable<CommandBlock> FilteredCommandBlocks
    {
        get
        {
            var blocks = CommandBlocks.AsEnumerable();

            // 검색어 필터
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var search = _searchText.ToLower();
                blocks = blocks.Where(b =>
                    b.UserInput?.ToLower().Contains(search) == true ||
                    b.GeneratedCommand?.ToLower().Contains(search) == true ||
                    b.Output?.ToLower().Contains(search) == true ||
                    b.Error?.ToLower().Contains(search) == true
                );
            }

            // 상태 필터
            if (_statusFilter.HasValue)
            {
                blocks = blocks.Where(b => b.Status == _statusFilter.Value);
            }

            return blocks;
        }
    }

    /// <summary>
    /// CommandBlock 검색어
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredCommandBlocks));
        }
    }

    /// <summary>
    /// CommandBlock 상태 필터
    /// </summary>
    public BlockStatus? StatusFilter
    {
        get => _statusFilter;
        set
        {
            _statusFilter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredCommandBlocks));
        }
    }

    /// <summary>
    /// Block UI 사용 여부
    /// </summary>
    public bool UseBlockUI
    {
        get => _useBlockUI;
        set
        {
            _useBlockUI = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// AI 추천/변환 사용 여부 (OFF면 입력을 바로 서버에 전송)
    /// </summary>
    public bool UseAISuggestion
    {
        get => _useAISuggestion;
        set
        {
            _useAISuggestion = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 파일 트리 패널 표시 여부
    /// </summary>
    public bool IsFileTreeVisible
    {
        get => _isFileTreeVisible;
        set { _isFileTreeVisible = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 파일 트리 현재 경로 (각 탭마다 독립적)
    /// </summary>
    public string? FileTreeCurrentPath
    {
        get => _fileTreeCurrentPath;
        set { _fileTreeCurrentPath = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 스니펫 패널 표시 여부 (서버 세션에서는 사용 안 함)
    /// </summary>
    public bool ShowSnippetPanel
    {
        get => _showSnippetPanel;
        set { _showSnippetPanel = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 데이터 수신 중 스피너 텍스트
    /// </summary>
    public string SpinnerText
    {
        get => _spinnerText;
        private set
        {
            _spinnerText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 활성 포트 포워딩 개수
    /// </summary>
    public int ActivePortForwardingCount => PortForwardings?.Count(pf => pf.Status == PortForwardingStatus.Running) ?? 0;

    /// <summary>
    /// 포트 포워딩 상태 텍스트 (탭 헤더용)
    /// </summary>
    public string PortForwardingStatusText
    {
        get
        {
            int count = ActivePortForwardingCount;
            return count > 0 ? $"🔌{count}" : string.Empty;
        }
    }

    /// <summary>
    /// 자주 사용하는 명령어 목록
    /// </summary>
    public ObservableCollection<FrequentCommand> FrequentCommands
    {
        get => _frequentCommands;
        private set
        {
            _frequentCommands = value;
            OnPropertyChanged();
        }
    }

    public string TabHeader
    {
        get => _tabHeader;
        set
        {
            _tabHeader = value;
            OnPropertyChanged();
        }
    }

    public string UserInput
    {
        get => _userInput;
        set
        {
            _userInput = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSendMessage));
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSendMessage));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSendMessage));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool CanSendMessage => IsConnected && !IsBusy && !string.IsNullOrWhiteSpace(UserInput);

    public ServerConfig? ServerProfile => _serverProfile;

    /// <summary>
    /// 세션 타입 (ISessionViewModel 구현)
    /// </summary>
    public SessionType Type => SessionType.SSH;

    /// <summary>
    /// 현재 작업 디렉토리
    /// </summary>
    public string CurrentDirectory
    {
        get => _currentDirectory;
        private set
        {
            _currentDirectory = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// ShellStream 모드 사용 여부
    /// </summary>
    public bool UseShellStream
    {
        get => _useShellStream;
        set
        {
            _useShellStream = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// SshService 인스턴스 (외부 접근용)
    /// </summary>
    public SshService? SshService => _sshService;

    public ICommand SendMessageCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand UseSnippetCommand { get; }
    public ICommand ChatModeCommand { get; }
    public ICommand OpenFileTransferCommand { get; }
    public ICommand OpenMonitorCommand { get; }
    public ICommand OpenLogViewerCommand { get; }
    public ICommand UseFrequentCommandCmd { get; }
    public ICommand ShowCommandDetailCmd { get; }
    public ICommand ToggleFileTreeCommand { get; }
    public ICommand OpenPortForwardingManagerCommand { get; }

    /// <summary>
    /// Port Forwarding 설정 목록
    /// </summary>
    public ObservableCollection<PortForwardingConfig> PortForwardings
    {
        get => _portForwardings;
        set
        {
            _portForwardings = value;
            OnPropertyChanged();
        }
    }

    public ServerSessionViewModel(AppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        SendMessageCommand = new RelayCommand(async () => await SendMessage(), () => CanSendMessage);
        DisconnectCommand = new RelayCommand(() => Disconnect(), () => IsConnected);
        UseSnippetCommand = new RelayCommand<CommandSnippet>(snippet => UseSnippet(snippet));
        ChatModeCommand = new RelayCommand(async () => await ChatMode(), () => IsConnected && !IsBusy);
        OpenFileTransferCommand = new RelayCommand(() => OpenFileTransfer(), () => IsConnected);
        OpenMonitorCommand = new RelayCommand(() => OpenMonitor(), () => IsConnected);
        OpenLogViewerCommand = new RelayCommand(() => OpenLogViewer(), () => IsConnected);
        UseFrequentCommandCmd = new RelayCommand<FrequentCommand>(cmd => { if (cmd != null) UseFrequentCommand(cmd); });
        ShowCommandDetailCmd = new RelayCommand<FrequentCommand>(cmd => { if (cmd != null) ShowCommandDetail(cmd); });
        ToggleFileTreeCommand = new RelayCommand(() => IsFileTreeVisible = !IsFileTreeVisible);
        OpenPortForwardingManagerCommand = new RelayCommand(() => OpenPortForwardingManager(), () => IsConnected);

        AddMessage("세션이 준비되었습니다. 서버에 연결해주세요.", false, MessageType.Info);
    }

    /// <summary>
    /// 서버에 연결
    /// </summary>
    public async Task ConnectAsync(ServerConfig profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        // AI Provider 초기화 (AIProviderManager 싱글톤 사용)
        // API 키가 없어도 SSH 연결은 허용 - AI 기능만 비활성화
        if (!AIProviderManager.Instance.IsInitialized)
        {
            var configuredModel = _config.GetConfiguredModels().FirstOrDefault();
            if (configuredModel == null)
            {
                // 하위 호환성: Gemini API 키 확인
                var geminiApiKey = ConfigService.GetGeminiApiKey(_config);
                if (!string.IsNullOrWhiteSpace(geminiApiKey))
                {
                    configuredModel = new AIModelConfig
                    {
                        Provider = Models.AIProviderType.Gemini,
                        ModelId = "gemini-2.0-flash",
                        ApiKey = geminiApiKey
                    };
                }
                // API 키가 없으면 AI Provider 없이 진행 (직접 명령어 입력 가능)
            }

            if (configuredModel != null)
            {
                AIProviderManager.Instance.SetCurrentProvider(configuredModel);
            }
        }

        IsBusy = true;
        StatusMessage = "연결 중...";
        AddMessage($"{profile.ProfileName}에 연결하는 중...", false, MessageType.Info);

        try
        {
            // SSH 서비스 초기화
            _sshService = new SshService(profile);

            // ErrorHandler에 현재 Provider 전달 (AI Provider가 없으면 ErrorHandler도 null)
            var currentProvider = AIProviderManager.Instance.CurrentProvider;
            if (currentProvider != null)
            {
                _errorHandler = new ErrorHandler(currentProvider, _sshService, _config.MaxRetryAttempts);
            }
            else
            {
                _errorHandler = null;
                // AI 없이 연결 - 직접 명령어 입력만 가능
            }

            // SSH 연결
            await _sshService.ConnectAsync();

            // ShellStream 초기화 (세션 상태 유지를 위해)
            if (_useShellStream)
            {
                try
                {
                    StatusMessage = "세션 초기화 중...";
                    await _sshService.InitializeShellStreamAsync();
                    CurrentDirectory = _sshService.CurrentDirectory;
                    AddMessage($"📁 현재 디렉토리: {CurrentDirectory}", false, MessageType.Info);

                    // 실시간 출력 스트리밍을 위한 이벤트 구독
                    _sshService.OutputReceived += OnShellOutputReceived;
                }
                catch (Exception ex)
                {
                    AddMessage($"⚠️ ShellStream 초기화 실패: {ex.Message}", false, MessageType.Warning);
                    AddMessage("기본 명령어 모드로 전환합니다.", false, MessageType.Info);
                    _useShellStream = false;
                }
            }

            _serverProfile = profile;
            IsConnected = true;
            StatusMessage = $"연결됨 ({profile.ProfileName})";
            TabHeader = profile.ProfileName;
            AddMessage($"✓ {profile.ProfileName}에 연결되었습니다.", false, MessageType.Success);

            // Port Forwarding 설정 로드
            LoadPortForwardingsFromProfile(profile);

            // 서버 정보 가져와서 환영 메시지 표시
            await ShowServerWelcomeMessage();

            // Port Forwarding 복구 (재연결 시) 또는 AutoStart 시작 (신규 연결 시)
            try
            {
                // 먼저 자동 복구 시도
                await _sshService.RecoverPortForwardingsAsync();

                // 복구되지 않은 AutoStart 항목 시작
                foreach (var pf in PortForwardings.Where(p => p.AutoStart && p.Status != PortForwardingStatus.Running))
                {
                    _ = StartPortForwardingAsync(pf);
                }
            }
            catch (Exception ex)
            {
                AddMessage($"Port Forwarding 복구 실패: {ex.Message}", false, MessageType.Warning);
            }

            // 자주 사용하는 명령어 로드
            RefreshFrequentCommands();
            if (FrequentCommands.Count > 0)
            {
                AddMessage($"📌 자주 사용하는 명령어 {FrequentCommands.Count}개를 로드했습니다.", false, MessageType.Info);
            }

            // 마지막 사용 프로필 업데이트
            _config.LastUsedProfile = profile.ProfileName;
            ConfigService.Save(_config);
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = "연결 실패";
            AddMessage($"연결 실패: {ex.Message}", false, MessageType.Error);
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 현재 입력창의 내용을 실행 (외부에서 호출 가능)
    /// </summary>
    public async Task ExecuteCurrentInputAsync()
    {
        await SendMessage();
    }

    private async Task SendMessage()
    {
        var aiProvider = AIProviderManager.Instance.CurrentProvider;
        var ragService = RAGService.Instance;

        if (string.IsNullOrWhiteSpace(UserInput) || _sshService == null)
            return;

        // AI가 없거나 AI 추천이 꺼져 있으면 직접 명령어 실행 모드
        bool directMode = aiProvider == null || !_useAISuggestion;

        var userMessage = UserInput.Trim();
        UserInput = string.Empty;

        // 명령어 히스토리에 추가
        AddToCommandHistory(userMessage);

        // 탭 제목을 질문 내용으로 변경 (최대 30자)
        var tabTitle = userMessage.Length > 30 ? userMessage[..30] + "..." : userMessage;
        TabHeader = tabTitle;

        // 두 뷰 동기화: CommandBlock과 Messages 둘 다 추가
        var block = new CommandBlock
        {
            UserInput = userMessage,
            Status = BlockStatus.Generating,
            CurrentDirectory = CurrentDirectory,
            ServerProfile = _serverProfile?.ProfileName ?? ""
        };
        Application.Current.Dispatcher.Invoke(() => CommandBlocks.AddWithLimit(block, MaxCommandBlocks, TrimCount));
        AddMessage(userMessage, true); // 터미널 뷰용

        IsBusy = true;

        CommandHistory? history = null;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            string command;
            string explanation = "";

            if (directMode)
            {
                // AI 없음 - 직접 명령어 실행 모드
                StatusMessage = "명령어 실행 중...";
                command = userMessage; // 입력을 그대로 명령어로 사용

                // 위험한 명령어 체크
                if (ErrorHandler.IsDangerousCommand(command))
                {
                    block.GeneratedCommand = command;
                    block.Error = "위험한 명령어가 감지되어 실행이 차단되었습니다.";
                    block.Status = BlockStatus.Failed;
                    AddMessage($"⚠️ 위험한 명령어가 감지되었습니다: {command}", false, MessageType.Error);
                    AddMessage("안전을 위해 실행이 차단되었습니다.", false, MessageType.Warning);
                    return;
                }

                AddMessage($"실행 명령어: {command}", false, MessageType.Command);
            }
            else
            {
                // AI 모드 - RAG 검색 및 AI 변환
                StatusMessage = "🔍 유사한 이전 질문 검색 중...";

                // RAG: 먼저 캐시된 답변 검색
                var ragResult = await ragService.SearchOrGenerateCommand(userMessage, _serverProfile?.ProfileName);

                if (ragResult.IsFromCache && ragResult.CachedCommand != null)
                {
                    command = ragResult.CachedCommand;
                    explanation = ragResult.CachedExplanation ?? "";

                    block.IsFromCache = true;
                    block.Similarity = ragResult.Similarity;
                    block.SearchMethod = ragResult.SearchMethod;

                    AddMessage($"📚 캐시에서 찾음 (유사도: {ragResult.Similarity:P0}, 방식: {ragResult.SearchMethod})", false, MessageType.Info);
                    AddMessage($"생성된 명령어: {command}", false, MessageType.Command);
                    if (!string.IsNullOrWhiteSpace(explanation))
                        AddMessage($"💡 {explanation}", false, MessageType.Info);
                }
                else
                {
                    StatusMessage = "🤖 AI가 명령어를 생성하는 중...";
                    var aiResponse = await aiProvider!.ConvertToLinuxCommandAsync(userMessage);
                    command = aiResponse.Command;

                    // AI 응답의 JSON 데이터를 CommandBlock에 매핑
                    block.Confidence = aiResponse.Confidence;
                    block.Warning = aiResponse.Warning;
                    block.Alternatives = aiResponse.Alternatives;
                    block.RequiresSudo = aiResponse.RequiresSudo;
                    block.IsDangerous = aiResponse.IsDangerous;
                    block.Category = aiResponse.Category;
                    block.EstimatedDuration = aiResponse.EstimatedDuration;

                    // AI가 설명을 제공했으면 사용
                    if (!string.IsNullOrWhiteSpace(aiResponse.Explanation))
                    {
                        explanation = aiResponse.Explanation;
                    }

                    // AI가 위험하다고 판단하거나 ErrorHandler가 위험하다고 판단
                    if (aiResponse.IsDangerous || ErrorHandler.IsDangerousCommand(command))
                    {
                        block.GeneratedCommand = command;
                        block.IsDangerous = true;
                        block.Error = aiResponse.Warning ?? "위험한 명령어가 감지되어 실행이 차단되었습니다.";
                        block.Status = BlockStatus.Failed;
                        AddMessage($"⚠️ 위험한 명령어가 감지되었습니다: {command}", false, MessageType.Error);
                        AddMessage("안전을 위해 실행이 차단되었습니다.", false, MessageType.Warning);
                        return;
                    }

                    // 설명이 없으면 별도로 생성
                    if (string.IsNullOrWhiteSpace(explanation))
                    {
                        StatusMessage = "명령어 설명 생성 중...";
                        try
                        {
                            var explainResponse = await aiProvider.ExplainCommandAsync(command);
                            explanation = explainResponse.Summary;
                            if (!string.IsNullOrWhiteSpace(explainResponse.Details))
                                explanation += "\n" + explainResponse.Details;
                        }
                        catch { }
                    }

                    AddMessage($"생성된 명령어: {command}", false, MessageType.Command);
                    if (!string.IsNullOrWhiteSpace(explanation))
                        AddMessage($"💡 {explanation}", false, MessageType.Info);
                    if (aiResponse.HasWarning)
                        AddMessage($"⚠️ {aiResponse.Warning}", false, MessageType.Warning);
                }
            }

            // Block 업데이트
            block.GeneratedCommand = command;
            block.Explanation = explanation;
            block.Status = directMode ? BlockStatus.Executing : BlockStatus.Confirming;

            history = new CommandHistory(userMessage, command, _serverProfile?.ProfileName ?? "Unknown")
            {
                Explanation = explanation,
                // AI JSON 응답 필드 복사
                Confidence = block.Confidence,
                Warning = block.Warning,
                Alternatives = block.Alternatives,
                RequiresSudo = block.RequiresSudo,
                IsDangerous = block.IsDangerous,
                Category = block.Category,
                EstimatedDuration = block.EstimatedDuration
            };

            string finalCommand = command;

            // 직접 모드가 아닐 때만 확인 대화상자 표시
            if (!directMode)
            {
                var dialog = new Views.CommandConfirmDialog(command, explanation);
                var dialogResult = dialog.ShowDialog();

                if (dialogResult == null || !dialogResult.Value)
                {
                    block.Status = BlockStatus.Cancelled;
                    AddMessage("사용자가 명령어 실행을 취소했습니다.", false, MessageType.Info);
                    return;
                }

                finalCommand = dialog.EditedCommand;
                if (finalCommand != command)
                {
                    history.WasEdited = true;
                    history.OriginalCommand = command;
                    history.GeneratedCommand = finalCommand;
                    block.GeneratedCommand = finalCommand;
                    AddMessage($"편집된 명령어: {finalCommand}", false, MessageType.Command);
                }
            }

            StatusMessage = "명령어 실행 중...";
            block.Status = BlockStatus.Executing;

            // 실시간 출력 스트리밍을 위해 현재 실행 중인 블록 설정
            _currentExecutingBlock = block;

            // 스피너 시작
            StartDataReceivingSpinner();

            bool success;
            string output = "";
            string? error = null;

            if (_useShellStream && _sshService?.HasActiveShellStream == true)
            {
                var shellResult = await _sshService.ExecuteShellCommandAsync(finalCommand);
                success = shellResult.IsSuccess;
                output = shellResult.Output;
                error = shellResult.Error;

                if (success)
                    CurrentDirectory = shellResult.CurrentDirectory;

                if (shellResult.IsTimeout)
                    AddMessage("⏱️ 명령어 실행 시간 초과", false, MessageType.Warning);
            }
            else
            {
                // ErrorHandler가 있으면 재시도 로직 사용, 없으면 직접 실행
                if (_errorHandler != null)
                {
                    var (retrySuccess, cmdResult, attempts) = await _errorHandler.ExecuteWithRetry(
                        finalCommand,
                        msg => AddMessage(msg, false, MessageType.Info)
                    );
                    success = retrySuccess;
                    output = cmdResult.Output;
                    error = cmdResult.Error;

                    // 현재 디렉토리 업데이트
                    if (!string.IsNullOrEmpty(cmdResult.CurrentDirectory))
                        CurrentDirectory = cmdResult.CurrentDirectory;

                    if (!success)
                        AddMessage($"시도 횟수: {attempts.Length}", false, MessageType.Info);
                }
                else
                {
                    // AI 없이 직접 명령어 실행
                    var result = await _sshService!.ExecuteCommandAsync(finalCommand);
                    success = result.ExitCode == 0;
                    output = result.Output;
                    error = result.Error;

                    // 현재 디렉토리 업데이트
                    if (!string.IsNullOrEmpty(result.CurrentDirectory))
                        CurrentDirectory = result.CurrentDirectory;
                }
            }

            stopwatch.Stop();

            // 스피너 중지
            StopDataReceivingSpinner();

            history.IsSuccess = success;
            history.Output = output;
            history.Error = error;

            // Block 결과 업데이트
            block.Output = output;
            block.Error = error ?? "";
            block.Status = success ? BlockStatus.Success : BlockStatus.Failed;
            block.Duration = stopwatch.Elapsed;
            block.CurrentDirectory = CurrentDirectory;

            // 명령어 실행 통계 기록
            UsageStatisticsService.Instance.RecordCommandExecution(success, block.Category);

            // 터미널 뷰 결과 업데이트
            if (success)
            {
                AddMessage($"✓ 성공", false, MessageType.Success);
                if (!string.IsNullOrWhiteSpace(output))
                    AddMessage(output, false, MessageType.Normal);
            }
            else
            {
                AddMessage($"✗ 실패", false, MessageType.Error);
                if (!string.IsNullOrWhiteSpace(error))
                    AddMessage($"오류: {error}", false, MessageType.Error);
            }

            if (_useShellStream)
                StatusMessage = $"연결됨 ({_serverProfile?.ProfileName}) - {CurrentDirectory}";

            // DB 저장
            try
            {
                string? embeddingVector = null;
                var embeddingService = AIProviderManager.Instance.CurrentEmbeddingService;
                if (embeddingService != null)
                {
                    var embedding = await embeddingService.GetEmbeddingAsync(history.UserInput);
                    embeddingVector = IEmbeddingService.SerializeVector(embedding);
                }
                history.Id = HistoryDatabaseService.Instance.AddHistory(history, embeddingVector);
                block.Id = history.Id;
                RefreshFrequentCommands();
            }
            catch (Exception dbEx)
            {
                System.Diagnostics.Debug.WriteLine($"DB 저장 실패: {dbEx.Message}");
                _config.CommandHistory.Add(history);
                ConfigService.Save(_config);
            }
        }
        catch (Exception ex)
        {
            // 스피너 중지
            StopDataReceivingSpinner();

            block.Error = ex.Message;
            block.Status = BlockStatus.Failed;
            AddMessage($"오류 발생: {ex.Message}", false, MessageType.Error);

            if (history != null)
            {
                history.IsSuccess = false;
                history.Error = ex.Message;
                try { HistoryDatabaseService.Instance.AddHistory(history); }
                catch { _config.CommandHistory.Add(history); ConfigService.Save(_config); }
            }
        }
        finally
        {
            // 실시간 출력 스트리밍 종료
            _currentExecutingBlock = null;

            IsBusy = false;
            StatusMessage = IsConnected ? $"연결됨 ({_serverProfile?.ProfileName})" : "연결되지 않음";
        }
    }

    private void Disconnect()
    {
        _sshService?.Disconnect();
        _sshService?.Dispose();
        _sshService = null;
        _errorHandler = null;

        IsConnected = false;
        StatusMessage = "연결 해제됨";
        TabHeader = $"{_serverProfile?.ProfileName} (연결 해제됨)";
        AddMessage("SSH 연결이 해제되었습니다.", false, MessageType.Info);
    }

    /// <summary>
    /// 서버 환영 메시지 표시 - 서버 정보 수집 및 표시
    /// </summary>
    private async Task ShowServerWelcomeMessage()
    {
        if (_sshService == null) return;

        try
        {
            // 서버 정보 수집 (병렬로 실행)
            var hostnameTask = _sshService.ExecuteCommandAsync("hostname");
            var userTask = _sshService.ExecuteCommandAsync("whoami");
            var osTask = _sshService.ExecuteCommandAsync("cat /etc/os-release 2>/dev/null | grep PRETTY_NAME | cut -d'\"' -f2 || uname -s");
            var kernelTask = _sshService.ExecuteCommandAsync("uname -r");
            var uptimeTask = _sshService.ExecuteCommandAsync("uptime -p 2>/dev/null || uptime");
            var pwdTask = _sshService.ExecuteCommandAsync("pwd");

            await Task.WhenAll(hostnameTask, userTask, osTask, kernelTask, uptimeTask, pwdTask);

            var hostname = hostnameTask.Result?.Output?.Trim() ?? "unknown";
            var user = userTask.Result?.Output?.Trim() ?? "unknown";
            var os = osTask.Result?.Output?.Trim() ?? "Linux";
            var kernel = kernelTask.Result?.Output?.Trim() ?? "";
            var uptime = uptimeTask.Result?.Output?.Trim() ?? "";
            var pwd = pwdTask.Result?.Output?.Trim() ?? "~";

            // 현재 디렉토리 업데이트
            CurrentDirectory = pwd;

            // 환영 메시지 구성
            var welcomeLines = new System.Text.StringBuilder();
            welcomeLines.AppendLine($"═══════════════════════════════════════════════════════");
            welcomeLines.AppendLine($"  {user}@{hostname}");
            welcomeLines.AppendLine($"───────────────────────────────────────────────────────");
            welcomeLines.AppendLine($"  OS: {os}");
            if (!string.IsNullOrEmpty(kernel))
                welcomeLines.AppendLine($"  Kernel: {kernel}");
            if (!string.IsNullOrEmpty(uptime))
                welcomeLines.AppendLine($"  Uptime: {uptime.Replace("up ", "")}");
            welcomeLines.AppendLine($"  현재 디렉토리: {pwd}");
            welcomeLines.AppendLine($"═══════════════════════════════════════════════════════");

            // 블록 UI 모드일 때도 환영 블록 추가
            if (_useBlockUI)
            {
                var welcomeBlock = new CommandBlock
                {
                    UserInput = "서버 연결",
                    GeneratedCommand = $"ssh {user}@{hostname}",
                    Output = welcomeLines.ToString(),
                    Status = BlockStatus.Success,
                    CurrentDirectory = pwd,
                    ServerProfile = _serverProfile?.ProfileName ?? ""
                };
                Application.Current.Dispatcher.Invoke(() => CommandBlocks.AddWithLimit(welcomeBlock, MaxCommandBlocks, TrimCount));
            }

            // 기존 메시지 목록에도 추가 (터미널 뷰용)
            AddMessage(welcomeLines.ToString(), false, MessageType.Info);

            // 상태 메시지 업데이트
            StatusMessage = $"연결됨 ({_serverProfile?.ProfileName}) - {pwd}";
        }
        catch (Exception ex)
        {
            // 서버 정보 수집 실패 시 기본 메시지만 표시
            AddMessage($"서버 정보 로드 중 오류: {ex.Message}", false, MessageType.Warning);
            AddMessage("이제 원하는 작업을 입력해주세요!", false, MessageType.Info);
        }
    }

    private void UseSnippet(CommandSnippet? snippet)
    {
        if (snippet != null && IsConnected)
        {
            UserInput = snippet.Command;
            snippet.IncrementUseCount();
            ConfigService.Save(_config);

            AddMessage($"스니펫 사용: {snippet.Name}", false, MessageType.Info);
            AddMessage($"명령어: {snippet.Command}", false, MessageType.Command);

            var result = MessageBox.Show(
                $"'{snippet.Name}' 스니펫을 실행하시겠습니까?\n\n{snippet.Command}",
                "스니펫 실행",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _ = SendMessage();
            }
        }
    }

    private async Task ChatMode()
    {
        var aiProvider = AIProviderManager.Instance.CurrentProvider;
        if (aiProvider == null || !IsConnected)
            return;

        if (string.IsNullOrWhiteSpace(UserInput))
            return;

        var question = UserInput.Trim();
        UserInput = string.Empty;

        AddMessage(question, true);

        IsBusy = true;
        StatusMessage = "AI 어시스턴트가 답변하는 중...";

        try
        {
            var serverContext = $"OS: {_serverProfile?.Host}";
            var answer = await aiProvider.ChatMode(question, serverContext);
            AddMessage($"🤖 {answer}", false, MessageType.Info);
        }
        catch (Exception ex)
        {
            AddMessage($"오류 발생: {ex.Message}", false, MessageType.Error);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = IsConnected ? $"연결됨 ({_serverProfile?.ProfileName})" : "연결되지 않음";
        }
    }

    private void OpenFileTransfer()
    {
        if (_serverProfile == null)
        {
            MessageBox.Show("서버에 연결되지 않았습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var fileTransferWindow = new Views.FileTransferWindow(_serverProfile);
            fileTransferWindow.Show();
        }
        catch (Exception ex)
        {
            AddMessage($"파일 전송 창 열기 실패: {ex.Message}", false, MessageType.Error);
        }
    }

    private void OpenMonitor()
    {
        if (_serverProfile == null || _sshService == null)
        {
            MessageBox.Show("서버에 연결되지 않았습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var monitorWindow = new Views.ServerMonitorWindow(_sshService, _serverProfile);
            monitorWindow.Show();
        }
        catch (Exception ex)
        {
            AddMessage($"모니터링 창 열기 실패: {ex.Message}", false, MessageType.Error);
        }
    }

    private void OpenLogViewer()
    {
        if (_serverProfile == null)
        {
            MessageBox.Show("서버에 연결되지 않았습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var logViewerWindow = new Views.LogViewerWindow(_serverProfile);
            logViewerWindow.Show();
        }
        catch (Exception ex)
        {
            AddMessage($"로그 뷰어 창 열기 실패: {ex.Message}", false, MessageType.Error);
        }
    }

    private void OpenPortForwardingManager()
    {
        if (_sshService == null || !IsConnected)
        {
            MessageBox.Show(
                LocalizationService.Instance.GetString("ViewModel.NotConnected") ?? "서버에 연결되지 않았습니다.",
                LocalizationService.Instance.GetString("Common.Error") ?? "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var dialog = new Views.PortForwardingManagerDialog(_sshService, PortForwardings)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                // Port Forwarding 설정을 프로필에 저장
                SavePortForwardingsToProfile();

                // Port Forwarding 목록이 변경되었을 수 있으므로 AutoStart 처리
                foreach (var pf in PortForwardings.Where(p => p.AutoStart && p.Status == PortForwardingStatus.Stopped))
                {
                    _ = StartPortForwardingAsync(pf);
                }
            }
        }
        catch (Exception ex)
        {
            AddMessage($"Port Forwarding 관리자 열기 실패: {ex.Message}", false, MessageType.Error);
        }
    }

    private void LoadPortForwardingsFromProfile(ServerConfig profile)
    {
        PortForwardings.Clear();
        foreach (var pf in profile.PortForwardings)
        {
            PortForwardings.Add(pf);
        }
    }

    private void SavePortForwardingsToProfile()
    {
        if (_serverProfile == null) return;

        _serverProfile.PortForwardings.Clear();
        foreach (var pf in PortForwardings)
        {
            _serverProfile.PortForwardings.Add(pf);
        }

        // 설정 저장
        ConfigService.Save(_config);
    }

    private async Task StartPortForwardingAsync(PortForwardingConfig config)
    {
        if (_sshService == null) return;

        try
        {
            bool success = config.Type switch
            {
                PortForwardingType.Local => await _sshService.StartLocalPortForwardingAsync(config),
                PortForwardingType.Remote => await _sshService.StartRemotePortForwardingAsync(config),
                PortForwardingType.Dynamic => await _sshService.StartDynamicPortForwardingAsync(config),
                _ => false
            };

            if (success)
            {
                AddMessage($"Port Forwarding 시작: {config.Description}", false, MessageType.Info);
                // 포트 포워딩 상태 업데이트
                OnPropertyChanged(nameof(ActivePortForwardingCount));
                OnPropertyChanged(nameof(PortForwardingStatusText));
            }
            else if (!string.IsNullOrEmpty(config.ErrorMessage))
            {
                AddMessage($"Port Forwarding 시작 실패: {config.ErrorMessage}", false, MessageType.Error);
            }
        }
        catch (Exception ex)
        {
            AddMessage($"Port Forwarding 오류: {ex.Message}", false, MessageType.Error);
        }
        finally
        {
            // 항상 상태 업데이트 (실패 시에도)
            OnPropertyChanged(nameof(ActivePortForwardingCount));
            OnPropertyChanged(nameof(PortForwardingStatusText));
        }
    }

    private void AddMessage(string content, bool isUser, MessageType type = MessageType.Normal)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Ring Buffer: 최대 크기 초과 시 오래된 메시지 삭제
            Messages.AddWithLimit(new ChatMessage(content, isUser, type), MaxMessages, TrimCount);
        });
    }

    /// <summary>
    /// 외부에서 메시지 추가 (View에서 호출 가능)
    /// </summary>
    public void AddMessage(string content, MessageType type = MessageType.Normal)
    {
        AddMessage(content, false, type);
    }

    /// <summary>
    /// 자주 사용하는 명령어 목록 갱신
    /// </summary>
    public void RefreshFrequentCommands()
    {
        try
        {
            var commands = HistoryDatabaseService.Instance.GetFrequentCommands(
                limit: 10, 
                serverProfile: _serverProfile?.ProfileName);
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                FrequentCommands.Clear();
                foreach (var cmd in commands)
                {
                    FrequentCommands.Add(cmd);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"자주 사용하는 명령어 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 자주 사용하는 명령어 클릭 시 입력창에 설정
    /// </summary>
    public void UseFrequentCommand(FrequentCommand command)
    {
        if (command != null && IsConnected)
        {
            UserInput = command.Command;
            AddMessage($"📌 자주 사용: {command.Description}", false, MessageType.Info);
        }
    }

    /// <summary>
    /// 명령어 상세보기 (View에서 팝업 창 열기 위한 이벤트)
    /// </summary>
    public void ShowCommandDetail(FrequentCommand command)
    {
        // View에서 처리하도록 이벤트 발생
        CommandDetailRequested?.Invoke(this, command);
    }

    /// <summary>
    /// 명령어 상세보기 요청 이벤트
    /// </summary>
    public event EventHandler<FrequentCommand>? CommandDetailRequested;

    /// <summary>
    /// 자주 사용하는 명령어 저장
    /// </summary>
    public void SaveFrequentCommand(FrequentCommand command)
    {
        try
        {
            HistoryDatabaseService.Instance.UpdateFrequentCommand(command);
            RefreshFrequentCommands();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 자주 사용하는 명령어 삭제
    /// </summary>
    public void DeleteFrequentCommand(FrequentCommand command)
    {
        try
        {
            HistoryDatabaseService.Instance.DeleteFrequentCommand(command);
            RefreshFrequentCommands();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"삭제 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Activated;
    public event EventHandler? Deactivated;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 탭이 활성화될 때 호출 (파일 워처 활성화)
    /// </summary>
    public void OnActivated()
    {
        Activated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 탭이 비활성화될 때 호출 (파일 워처 비활성화)
    /// </summary>
    public void OnDeactivated()
    {
        Deactivated?.Invoke(this, EventArgs.Empty);
    }

    #region Spinner Methods

    /// <summary>
    /// 데이터 수신 중 스피너 시작
    /// </summary>
    private void StartDataReceivingSpinner()
    {
        _lastDataReceivedTime = DateTime.Now;

        if (_spinnerTimer == null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _spinnerTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _spinnerTimer.Tick += OnSpinnerTick;
                _spinnerTimer.Start();
                UpdateSpinnerFrame();
            });
        }

        if (_dataReceivedTimer == null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _dataReceivedTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _dataReceivedTimer.Tick += OnDataReceivedTimerTick;
                _dataReceivedTimer.Start();
            });
        }
    }

    /// <summary>
    /// 데이터 수신 중 스피너 중지
    /// </summary>
    private void StopDataReceivingSpinner()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _spinnerTimer?.Stop();
            _spinnerTimer = null;

            _dataReceivedTimer?.Stop();
            _dataReceivedTimer = null;

            SpinnerText = string.Empty;
        });
    }

    /// <summary>
    /// 스피너 프레임 업데이트
    /// </summary>
    private void UpdateSpinnerFrame()
    {
        SpinnerText = SpinnerFrames[_spinnerFrameIndex];
        _spinnerFrameIndex = (_spinnerFrameIndex + 1) % SpinnerFrames.Length;
    }

    /// <summary>
    /// 스피너 타이머 틱 (애니메이션)
    /// </summary>
    private void OnSpinnerTick(object? sender, EventArgs e)
    {
        UpdateSpinnerFrame();
    }

    /// <summary>
    /// 데이터 수신 체크 타이머 틱 (자동 숨김)
    /// </summary>
    private void OnDataReceivedTimerTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _lastDataReceivedTime;
        if (elapsed.TotalMilliseconds > 500)
        {
            StopDataReceivingSpinner();
        }
    }

    #endregion

    #region Command History Navigation

    /// <summary>
    /// 명령어를 히스토리에 추가 (중복 제거)
    /// </summary>
    private void AddToCommandHistory(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        // 이미 있으면 제거 (최신 순서로 유지)
        _commandHistoryList.Remove(command);

        // 맨 앞에 추가
        _commandHistoryList.Insert(0, command);

        // 최대 100개까지만 유지
        if (_commandHistoryList.Count > 100)
            _commandHistoryList.RemoveAt(_commandHistoryList.Count - 1);

        // 인덱스 초기화
        _commandHistoryIndex = -1;
        _currentEditingCommand = string.Empty;
    }

    /// <summary>
    /// 이전 명령어 가져오기 (Up 키)
    /// </summary>
    public string? GetPreviousCommand(string currentInput)
    {
        if (_commandHistoryList.Count == 0)
            return null;

        // 첫 Up 키 누름: 현재 입력 저장
        if (_commandHistoryIndex == -1)
        {
            _currentEditingCommand = currentInput;
            _commandHistoryIndex = 0;
        }
        // 이미 히스토리 탐색 중: 다음 이전 명령어로 이동
        else if (_commandHistoryIndex < _commandHistoryList.Count - 1)
        {
            _commandHistoryIndex++;
        }

        return _commandHistoryList[_commandHistoryIndex];
    }

    /// <summary>
    /// 다음 명령어 가져오기 (Down 키)
    /// </summary>
    public string? GetNextCommand()
    {
        if (_commandHistoryIndex <= -1)
            return null;

        _commandHistoryIndex--;

        // 맨 끝까지 왔으면 편집 중이던 명령어 복원
        if (_commandHistoryIndex < 0)
        {
            _commandHistoryIndex = -1;
            return _currentEditingCommand;
        }

        return _commandHistoryList[_commandHistoryIndex];
    }

    /// <summary>
    /// 히스토리 네비게이션 초기화 (Enter 키 등)
    /// </summary>
    public void ResetHistoryNavigation()
    {
        _commandHistoryIndex = -1;
        _currentEditingCommand = string.Empty;
    }

    #endregion

    #region Real-time Output Streaming

    /// <summary>
    /// ShellStream 실시간 출력 이벤트 핸들러
    /// </summary>
    private void OnShellOutputReceived(object? sender, ShellOutputEventArgs e)
    {
        if (_currentExecutingBlock == null || string.IsNullOrEmpty(e.Data))
            return;

        // UI 스레드에서 CommandBlock 업데이트
        Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                // 실시간으로 출력을 누적
                _currentExecutingBlock.Output += e.Data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnShellOutputReceived] 출력 업데이트 오류: {ex.Message}");
            }
        });
    }

    #endregion

    public void Dispose()
    {
        // OutputReceived 이벤트 구독 해제
        if (_sshService != null)
        {
            _sshService.OutputReceived -= OnShellOutputReceived;
        }

        Disconnect();

        // 스피너 타이머 정리
        StopDataReceivingSpinner();

        // 큰 컬렉션 정리 (메모리 누수 방지)
        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Messages?.Clear();
                CommandBlocks?.Clear();
                FrequentCommands?.Clear();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ServerSessionViewModel] Dispose 중 컬렉션 정리 오류: {ex.Message}");
        }

        // 이벤트 핸들러 정리 (메모리 누수 방지)
        PropertyChanged = null;
        Activated = null;
        Deactivated = null;
    }
}
