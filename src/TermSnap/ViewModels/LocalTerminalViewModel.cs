using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TermSnap.Core;
using TermSnap.Core.Sessions;
using TermSnap.Models;
using TermSnap.Services;
using TermSnap.ViewModels.OutputHandlers;
using TermSnap.ViewModels.Managers;
using static TermSnap.Services.ShellDetectionService;

namespace TermSnap.ViewModels;

/// <summary>
/// 로컬 터미널 세션 뷰모델 (PowerShell/CMD/WSL/GitBash)
/// 출력 쓰로틀링 및 Ring Buffer로 메모리 관리
/// 명령어 히스토리 및 스니펫 지원
/// </summary>
public class LocalTerminalViewModel : INotifyPropertyChanged, ISessionViewModel
{
    private LocalSession? _session;
    private string _userInput = string.Empty;
    private bool _isConnected = false;
    private bool _isBusy = false;
    private string _statusMessage = LocalizationService.Instance.GetString("ViewModel.NotConnected");
    private string _tabHeader = LocalizationService.Instance.GetString("ViewModel.LocalTerminal");
    private string _currentDirectory = string.Empty;
    private string? _gitBranch = null;  // Git 브랜치 이름 (없으면 null)
    private LocalSession.LocalShellType _shellType;
    private bool _useBlockUI = true;
    private bool _showWelcome = true;  // 웰컴 화면 표시 여부
    private string? _workingFolder;    // 선택한 작업 폴더
    private bool _showSnippetPanel = false; // 스니펫 패널 표시 여부
    private DetectedShell? _selectedShell; // 선택된 쉘 정보
    private bool _isInteractiveMode = false; // 인터랙티브 모드 (claude, vim 등)
    private string _interactiveStatusMessage = string.Empty; // 인터랙티브 상태 메시지
    private bool _isFileTreeVisible = false; // 파일 트리 패널 표시 여부
    private bool _isFileViewerVisible = false; // 파일 뷰어 패널 표시 여부
    private string? _fileTreeCurrentPath = null; // 파일 트리 현재 경로

    // AI CLI 경과 시간 추적
    private DateTime? _aicliStartTime;
    private DispatcherTimer? _elapsedTimer;
    private string _aicliElapsedTime = string.Empty;
    private string _aicliProgramName = string.Empty;

    // 데이터 수신 스피너 (탭 헤더에 표시)
    private DispatcherTimer? _spinnerTimer;
    private DispatcherTimer? _dataReceivedTimer;
    private string _spinnerText = string.Empty;
    private int _spinnerIndex = 0;
    private static readonly string[] SpinnerFrames = { "/", "-", "\\", "|" };
    private DateTime? _lastDataReceivedTime;

    // 인터랙티브 모드 원시 출력 이벤트 (터미널 컨트롤용)
    public event Action<string>? RawOutputReceived;

    // 인터랙티브 프로그램 목록
    private static readonly HashSet<string> InteractivePrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        // AI CLI 도구들
        "claude", "codex", "gemini", "aider",
        // 텍스트 에디터
        "vim", "vi", "nano", "less", "more",
        // 시스템 모니터링
        "top", "htop",
        // 프로그래밍 REPL
        "python", "python3", "node", "irb", "ghci", "lua", "julia",
        // 데이터베이스 클라이언트
        "mysql", "psql", "sqlite3", "redis-cli", "mongo",
        // 네트워크 도구
        "ssh", "telnet", "ftp", "sftp",
        // 쉘
        "bash", "zsh", "fish", "sh"
    };

    // 출력 핸들러 (인터랙티브/비인터랙티브 모드 분리)
    private IOutputHandler? _outputHandler;
    private InteractiveOutputHandler? _interactiveHandler;
    private NonInteractiveOutputHandler? _nonInteractiveHandler;
    private CommandBlock? _currentBlock;

    // 인터랙티브 모드 출력 버퍼 (탭 전환 시 출력 손실 방지)
    private readonly StringBuilder _interactiveOutputBuffer = new StringBuilder();
    private const int MaxInteractiveBufferSize = 1024 * 1024; // 1MB 제한

    // 관리자 클래스들
    private readonly HistoryManager _historyManager = new();
    private readonly SnippetManager _snippetManager = new();

    // Ring Buffer 설정 - 메모리 누수 방지
    private const int MaxMessages = 500;        // 최대 메시지 수
    private const int MaxCommandBlocks = 200;   // 최대 명령 블록 수
    private const int TrimCount = 50;           // 한 번에 삭제할 개수

    /// <summary>
    /// 채팅 메시지 (Ring Buffer 적용 - 최대 500개)
    /// </summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    /// <summary>
    /// Warp 스타일 Command Block 목록 (Ring Buffer 적용 - 최대 200개)
    /// </summary>
    public ObservableCollection<CommandBlock> CommandBlocks { get; } = new();

    /// <summary>
    /// Block UI 사용 여부
    /// </summary>
    public bool UseBlockUI
    {
        get => _useBlockUI;
        set { _useBlockUI = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 웰컴 화면 표시 여부 (폴더 미선택 시 true)
    /// </summary>
    public bool ShowWelcome
    {
        get => _showWelcome;
        set { _showWelcome = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 현재 작업 폴더
    /// </summary>
    public string? WorkingFolder
    {
        get => _workingFolder;
        private set { _workingFolder = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 스니펫 패널 표시 여부
    /// </summary>
    public bool ShowSnippetPanel
    {
        get => _showSnippetPanel;
        set { _showSnippetPanel = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 인터랙티브 모드 여부 (claude, vim 등 실행 중)
    /// </summary>
    public bool IsInteractiveMode
    {
        get => _isInteractiveMode;
        private set
        {
            _isInteractiveMode = value;
            OnPropertyChanged();
            StatusMessage = value ? LocalizationService.Instance.GetString("ViewModel.InteractiveMode") : $"{_selectedShell?.DisplayName ?? LocalizationService.Instance.GetString("ViewModel.LocalTerminal")} - {CurrentDirectory}";

            // 출력 핸들러 전환
            SwitchOutputHandler(value);

            // 경과 시간 타이머 시작/중지
            if (value)
            {
                StartElapsedTimer();
                // 인터랙티브 모드 시작 시 버퍼 초기화
                _interactiveOutputBuffer.Clear();
            }
            else
            {
                StopElapsedTimer();
                // 인터랙티브 모드 종료 시 버퍼 비우기 (메모리 절약)
                _interactiveOutputBuffer.Clear();
            }
        }
    }

    /// <summary>
    /// 인터랙티브 상태 메시지 (Warp 스타일)
    /// </summary>
    public string InteractiveStatusMessage
    {
        get => _interactiveStatusMessage;
        set { _interactiveStatusMessage = value; OnPropertyChanged(); }
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
    /// 파일 뷰어 패널 표시 여부
    /// </summary>
    public bool IsFileViewerVisible
    {
        get => _isFileViewerVisible;
        set { _isFileViewerVisible = value; OnPropertyChanged(); }
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
    /// AI CLI 경과 시간 문자열 (예: "00:05:23")
    /// </summary>
    public string AICLIElapsedTime
    {
        get => _aicliElapsedTime;
        private set { _aicliElapsedTime = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 실행 중인 AI CLI 프로그램 이름
    /// </summary>
    public string AICLIProgramName
    {
        get => _aicliProgramName;
        private set { _aicliProgramName = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 데이터 수신 중 스피너 텍스트 (/, -, \, |)
    /// </summary>
    public string SpinnerText
    {
        get => _spinnerText;
        private set { _spinnerText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 로컬 터미널 스니펫 목록
    /// </summary>
    public ObservableCollection<CommandSnippet> LocalSnippets => _snippetManager.Snippets;

    public string TabHeader
    {
        get => _tabHeader;
        set { _tabHeader = value; OnPropertyChanged(); }
    }

    public string UserInput
    {
        get => _userInput;
        set { _userInput = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSendMessage)); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSendMessage)); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSendMessage)); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string CurrentDirectory
    {
        get => _currentDirectory;
        private set
        {
            _currentDirectory = value;
            OnPropertyChanged();

            // Git 브랜치 업데이트
            GitBranch = GitBranchDetector.GetBranch(value);
        }
    }

    /// <summary>
    /// 현재 디렉토리의 Git 브랜치 (Git 저장소가 아니면 null)
    /// </summary>
    public string? GitBranch
    {
        get => _gitBranch;
        private set
        {
            _gitBranch = value;
            OnPropertyChanged();
        }
    }

    public bool CanSendMessage => IsConnected && !IsBusy && !string.IsNullOrWhiteSpace(UserInput);

    public LocalSession.LocalShellType ShellType => _shellType;

    /// <summary>
    /// 세션 타입 (ISessionViewModel 구현)
    /// </summary>
    public SessionType Type => _shellType == LocalSession.LocalShellType.WSL ? SessionType.WSL : SessionType.Local;

    public ICommand SendMessageCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ClearOutputCommand { get; }
    public ICommand ToggleFileTreeCommand { get; }

    public LocalTerminalViewModel(LocalSession.LocalShellType shellType = LocalSession.LocalShellType.PowerShell)
    {
        _shellType = shellType;
        _tabHeader = GetShellDisplayName(shellType);

        SendMessageCommand = new RelayCommand(async () => await ExecuteCommand(), () => CanSendMessage);
        DisconnectCommand = new RelayCommand(() => Disconnect(), () => IsConnected);
        ClearOutputCommand = new RelayCommand(() => ClearOutput());
        ToggleFileTreeCommand = new RelayCommand(() => IsFileTreeVisible = !IsFileTreeVisible);

        // 로컬 스니펫 로드
        _snippetManager.Load();

        // 기본 쉘 감지
        _selectedShell = ShellDetectionService.Instance.GetDefaultShell();
        if (_selectedShell != null)
        {
            _shellType = _selectedShell.ShellType;
            _tabHeader = _selectedShell.DisplayName;
        }

        // 출력 핸들러 초기화
        InitializeOutputHandlers();

        AddMessage("로컬 터미널이 준비되었습니다.", false, MessageType.Info);
    }


    /// <summary>
    /// 쉘 설정 (WelcomePanel에서 호출)
    /// </summary>
    public void SetShell(DetectedShell shell)
    {
        Debug.WriteLine($"[SetShell] Setting shell: {shell.DisplayName}, Path: {shell.Path}");
        _selectedShell = shell;
        _shellType = shell.ShellType;
        _tabHeader = shell.DisplayName;
        OnPropertyChanged(nameof(TabHeader));
        AddMessage($"쉘 선택됨: {shell.DisplayName} ({shell.Path})", false, MessageType.Info);
    }

    /// <summary>
    /// 현재 선택된 쉘
    /// </summary>
    public DetectedShell? SelectedShell => _selectedShell;

    /// <summary>
    /// 스니펫 저장 (외부 호출용)
    /// </summary>
    public void SaveLocalSnippets()
    {
        _snippetManager.Save();
    }

    /// <summary>
    /// 스니펫 추가
    /// </summary>
    public void AddSnippet(CommandSnippet snippet)
    {
        _snippetManager.Add(snippet);
    }

    /// <summary>
    /// 스니펫 삭제
    /// </summary>
    public void RemoveSnippet(CommandSnippet snippet)
    {
        _snippetManager.Remove(snippet);
    }

    /// <summary>
    /// 스니펫 사용 (사용 횟수 증가)
    /// </summary>
    public void UseSnippet(CommandSnippet snippet)
    {
        _snippetManager.Use(snippet);
    }

    #region 명령어 히스토리

    /// <summary>
    /// 히스토리에서 이전 명령어 (↑)
    /// </summary>
    public string? NavigateHistoryUp()
    {
        return _historyManager.NavigateUp(UserInput);
    }

    /// <summary>
    /// 히스토리에서 다음 명령어 (↓)
    /// </summary>
    public string? NavigateHistoryDown()
    {
        return _historyManager.NavigateDown();
    }

    /// <summary>
    /// 히스토리 인덱스 초기화
    /// </summary>
    public void ResetHistoryNavigation()
    {
        _historyManager.ResetNavigation();
    }

    /// <summary>
    /// 히스토리에 명령어 추가
    /// </summary>
    private void AddToHistory(string command)
    {
        _historyManager.Add(command);
    }

    #endregion

    private string GetShellDisplayName(LocalSession.LocalShellType shellType)
    {
        return shellType switch
        {
            LocalSession.LocalShellType.PowerShell => "PowerShell",
            LocalSession.LocalShellType.Cmd => "CMD",
            LocalSession.LocalShellType.WSL => "WSL",
            LocalSession.LocalShellType.GitBash => "Git Bash",
            _ => "Local Terminal"
        };
    }

    /// <summary>
    /// 로컬 셸에 연결
    /// </summary>
    public async Task ConnectAsync()
    {
        if (IsConnected) return;

        IsBusy = true;
        var shellName = _selectedShell?.DisplayName ?? GetShellDisplayName(_shellType);
        Debug.WriteLine($"[ConnectAsync] _selectedShell: {_selectedShell?.DisplayName ?? "NULL"}, Path: {_selectedShell?.Path ?? "NULL"}");
        StatusMessage = LocalizationService.Instance.GetString("LocalTerminal.Starting");
        AddMessage(string.Format(LocalizationService.Instance.GetString("LocalTerminal.ShellStarting"), shellName), false, MessageType.Info);

        try
        {
            // 선택된 쉘 정보가 있으면 커스텀 경로 사용
            if (_selectedShell != null)
            {
                Debug.WriteLine($"[ConnectAsync] Using custom shell: {_selectedShell.Path} {_selectedShell.Arguments}");
                _session = new LocalSession(
                    _selectedShell.Path,
                    _selectedShell.Arguments,
                    _selectedShell.DisplayName,
                    _selectedShell.ShellType);
            }
            else
            {
                Debug.WriteLine($"[ConnectAsync] Using default shell type: {_shellType}");
                _session = new LocalSession(_shellType);
            }

            // 출력 이벤트 연결
            _session.OutputReceived += OnOutputReceived;
            _session.StateChanged += OnStateChanged;

            var result = await _session.ConnectAsync();

            if (result)
            {
                IsConnected = true;
                CurrentDirectory = _session.CurrentDirectory;
                StatusMessage = $"{shellName} - {CurrentDirectory}";
                TabHeader = $"{shellName}";
                AddMessage(string.Format(LocalizationService.Instance.GetString("LocalTerminal.ShellStarted"), shellName), false, MessageType.Success);
                AddMessage(string.Format(LocalizationService.Instance.GetString("LocalTerminal.CurrentDirectoryInfo"), CurrentDirectory), false, MessageType.Info);

                // 환영 박스 표시
                ShowWelcomeBox(shellName);
            }
            else
            {
                AddMessage(LocalizationService.Instance.GetString("LocalTerminal.StartFailed"), false, MessageType.Error);
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = "시작 실패";
            AddMessage($"오류: {ex.Message}", false, MessageType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnOutputReceived(object? sender, TerminalOutputEventArgs e)
    {
        // 빈 데이터 무시
        if (string.IsNullOrEmpty(e.Data))
            return;

        // 핸들러에 위임
        _outputHandler?.HandleOutput(e);
    }


    private void OnStateChanged(object? sender, ConnectionState state)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = state == ConnectionState.Connected;
            StatusMessage = state switch
            {
                ConnectionState.Connected => $"{GetShellDisplayName(_shellType)} - {CurrentDirectory}",
                ConnectionState.Connecting => LocalizationService.Instance.GetString("ViewModel.Connecting"),
                ConnectionState.Disconnected => LocalizationService.Instance.GetString("ViewModel.Disconnected"),
                ConnectionState.Error => "오류 발생",
                _ => "알 수 없음"
            };
        });
    }

    /// <summary>
    /// 출력 핸들러 초기화
    /// </summary>
    private void InitializeOutputHandlers()
    {
        // 인터랙티브 핸들러
        _interactiveHandler = new InteractiveOutputHandler();
        _interactiveHandler.RawOutputReceived += (data) =>
        {
            // 버퍼에 저장 (탭이 안보일 때 대비)
            AppendToInteractiveBuffer(data);

            // 데이터 수신 시 스피너 시작
            StartDataReceivingSpinner();

            // TerminalControl에도 전달 (View 캐싱되어 있으면 계속 업데이트됨)
            RawOutputReceived?.Invoke(data);
        };

        // 비인터랙티브 핸들러
        _nonInteractiveHandler = new NonInteractiveOutputHandler();
        _nonInteractiveHandler.AddMessageCallback = (content, type) => AddMessage(content, false, type);

        // 기본적으로 비인터랙티브 모드 사용
        _outputHandler = _nonInteractiveHandler;
    }

    /// <summary>
    /// 출력 핸들러 전환 (인터랙티브 <-> 비인터랙티브)
    /// </summary>
    private void SwitchOutputHandler(bool toInteractive)
    {
        if (toInteractive)
        {
            _outputHandler = _interactiveHandler;
        }
        else
        {
            _outputHandler = _nonInteractiveHandler;
        }
    }

    /// <summary>
    /// 현재 입력창의 명령어 실행 (외부 호출용)
    /// </summary>
    public async Task ExecuteCurrentInputAsync()
    {
        await ExecuteCommand();
    }

    /// <summary>
    /// 명령어 실행
    /// </summary>
    private async Task ExecuteCommand()
    {
        if (_session == null || string.IsNullOrWhiteSpace(UserInput))
            return;

        var command = UserInput.Trim();
        UserInput = string.Empty;

        // 인터랙티브 모드: 원시 입력 전송
        if (_isInteractiveMode)
        {
            await _session.SendRawInputAsync(command);
            // 출력은 이벤트 핸들러에서 현재 블록에 추가됨
            return;
        }

        // 히스토리에 추가
        AddToHistory(command);

        // 인터랙티브 프로그램인지 확인
        var programName = GetProgramName(command);
        var isInteractiveProgram = !string.IsNullOrEmpty(programName) && InteractivePrograms.Contains(programName);

        // ✅ 인터랙티브 프로그램이면 블록/타이머 없이 바로 실행
        if (isInteractiveProgram)
        {
            System.Diagnostics.Debug.WriteLine("[ExecuteCommand] 인터랙티브 프로그램 감지, 즉시 모드 전환");
            AICLIProgramName = programName ?? "터미널";

            IsInteractiveMode = true;
            IsBusy = true;
            StatusMessage = "인터랙티브 모드 실행 중...";

            try
            {
                await _session.SendRawInputAsync(command);
                IsBusy = false;
                return;  // ✅ 블록 생성이나 타이머 시작 없이 종료
            }
            catch (Exception ex)
            {
                IsBusy = false;
                AddMessage($"명령어 실행 실패: {ex.Message}", false, MessageType.Error);
                return;
            }
        }

        // 비인터랙티브 모드: 블록 생성 및 타이머 시작
        _currentBlock = new CommandBlock
        {
            UserInput = command,
            GeneratedCommand = command, // 로컬 터미널은 직접 명령어 실행
            Status = BlockStatus.Executing,
            CurrentDirectory = CurrentDirectory,
            IsLocalSession = true  // 로컬 세션 표시
        };
        // Ring Buffer: 최대 크기 초과 시 오래된 블록 삭제
        Application.Current.Dispatcher.Invoke(() => CommandBlocks.AddWithLimit(_currentBlock, MaxCommandBlocks, TrimCount));

        // 터미널 뷰용 메시지도 추가 (호환성)
        if (!_useBlockUI)
        {
            AddMessage($"$ {command}", true);
        }

        // 핸들러에 현재 블록 설정
        _outputHandler?.SetCurrentBlock(_currentBlock);

        IsBusy = true;
        StatusMessage = "명령어 실행 중...";
        var stopwatch = Stopwatch.StartNew();

        try
        {

            var result = await _session.ExecuteCommandAsync(command);
            stopwatch.Stop();

            CurrentDirectory = result.CurrentDirectory;

            if (_currentBlock != null)
            {
                // 최종 출력 병합 (실행 결과에서 받은 것과 스트리밍으로 받은 것)
                if (!string.IsNullOrEmpty(result.Output) && !_currentBlock.Output.Contains(result.Output))
                {
                    _currentBlock.Output = result.Output;
                }
                if (!string.IsNullOrEmpty(result.Error) && !_currentBlock.Error.Contains(result.Error))
                {
                    _currentBlock.Error = result.Error;
                }
                _currentBlock.Status = result.IsSuccess ? BlockStatus.Success : BlockStatus.Failed;
                _currentBlock.Duration = stopwatch.Elapsed;
                _currentBlock.CurrentDirectory = CurrentDirectory;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(result.Output))
                {
                    AddMessage(result.Output, false, MessageType.Normal);
                }
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    AddMessage(result.Error, false, MessageType.Error);
                }
                if (result.IsSuccess)
                {
                    AddMessage($"✓ 완료 (종료 코드: {result.ExitCode})", false, MessageType.Success);
                }
                else
                {
                    AddMessage($"✗ 실패 (종료 코드: {result.ExitCode})", false, MessageType.Error);
                }
            }

            TabHeader = $"{GetShellDisplayName(_shellType)} ({CurrentDirectory})";
            StatusMessage = $"{GetShellDisplayName(_shellType)} - {CurrentDirectory}";
        }
        catch (Exception ex)
        {
            if (_currentBlock != null)
            {
                _currentBlock.Error = ex.Message;
                _currentBlock.Status = BlockStatus.Failed;
            }
            else
            {
                AddMessage($"오류: {ex.Message}", false, MessageType.Error);
            }
        }
        finally
        {
            // 인터랙티브 모드에서는 블록 유지 (출력 계속 수신)
            if (!_isInteractiveMode)
            {
                _currentBlock = null;
            }
            IsBusy = false;
        }
    }

    private void Disconnect()
    {
        if (_session != null)
        {
            _session.OutputReceived -= OnOutputReceived;
            _session.StateChanged -= OnStateChanged;
            _session.Dispose();
            _session = null;
        }

        IsConnected = false;
        StatusMessage = LocalizationService.Instance.GetString("ViewModel.Disconnected");
        TabHeader = $"{GetShellDisplayName(_shellType)}{LocalizationService.Instance.GetString("ViewModel.DisconnectedSuffix")}";
        AddMessage("로컬 셸이 종료되었습니다.", false, MessageType.Info);
    }

    private void ClearOutput()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Messages.Clear();
            CommandBlocks.Clear();
        });
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
    /// 폴더 열기 및 해당 폴더에서 터미널 시작
    /// </summary>
    public async Task OpenFolderAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !System.IO.Directory.Exists(folderPath))
        {
            AddMessage($"폴더를 찾을 수 없습니다: {folderPath}", MessageType.Error);
            return;
        }

        // 작업 폴더 설정
        WorkingFolder = folderPath;
        CurrentDirectory = folderPath;
        ShowWelcome = false;  // 웰컴 화면 숨기기

        // 폴더 이름을 탭 헤더에 표시 (쉘 이름 포함)
        var shellName = _selectedShell?.DisplayName ?? GetShellDisplayName(_shellType);
        var folderName = System.IO.Path.GetFileName(folderPath);
        TabHeader = $"{shellName} ({folderName})";

        // 아직 연결되지 않았으면 연결
        if (!IsConnected)
        {
            // 선택된 쉘 정보가 있으면 커스텀 경로 사용
            if (_selectedShell != null)
            {
                _session = new LocalSession(
                    _selectedShell.Path,
                    _selectedShell.Arguments,
                    _selectedShell.DisplayName,
                    _selectedShell.ShellType);
            }
            else
            {
                _session = new LocalSession(_shellType);
            }

            // 출력 이벤트 연결
            _session.OutputReceived += OnOutputReceived;
            _session.StateChanged += OnStateChanged;

            // 초기 출력을 받을 블록 생성 (Block UI 모드)
            if (_useBlockUI)
            {
                _currentBlock = new CommandBlock
                {
                    UserInput = string.Format(LocalizationService.Instance.GetString("LocalTerminal.ShellStartedBracket"), shellName),
                    GeneratedCommand = $"cd \"{folderPath}\"",
                    Status = BlockStatus.Executing,
                    CurrentDirectory = folderPath,
                    IsLocalSession = true
                };
                Application.Current.Dispatcher.Invoke(() => CommandBlocks.AddWithLimit(_currentBlock, MaxCommandBlocks, TrimCount));

                // 핸들러에 현재 블록 설정
                _outputHandler?.SetCurrentBlock(_currentBlock);
            }

            var result = await _session.ConnectAsync();

            if (result)
            {
                IsConnected = true;

                // cd 명령어로 해당 폴더로 이동
                var cdResult = await _session.ExecuteCommandAsync($"cd \"{folderPath}\"");
                CurrentDirectory = folderPath;

                // 잠시 대기하여 초기 출력 수집
                await Task.Delay(300);

                // 초기 블록 완료 처리
                if (_currentBlock != null)
                {
                    _currentBlock.Status = BlockStatus.Success;
                    _currentBlock.CurrentDirectory = folderPath;
                }

                StatusMessage = $"{shellName} - {CurrentDirectory}";
                AddMessage(string.Format(LocalizationService.Instance.GetString("LocalTerminal.ShellStarted"), shellName), MessageType.Success);
                AddMessage(string.Format(LocalizationService.Instance.GetString("LocalTerminal.WorkingFolderInfo"), folderPath), MessageType.Info);
            }
            else
            {
                AddMessage(LocalizationService.Instance.GetString("LocalTerminal.StartFailed"), MessageType.Error);
            }
        }
        else
        {
            // 이미 연결되어 있으면 cd 명령어로 이동
            var cdResult = await _session!.ExecuteCommandAsync($"cd \"{folderPath}\"");
            CurrentDirectory = folderPath;
            StatusMessage = $"{GetShellDisplayName(_shellType)} - {CurrentDirectory}";
        }
    }

    /// <summary>
    /// 명령어에서 프로그램 이름 추출
    /// </summary>
    private static string? GetProgramName(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        var program = parts[0];

        // 경로가 포함된 경우 파일명만 추출
        if (program.Contains('/') || program.Contains('\\'))
        {
            program = System.IO.Path.GetFileNameWithoutExtension(program);
        }

        // .exe 확장자 제거
        if (program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            program = program[..^4];
        }

        return program;
    }

    /// <summary>
    /// 인터랙티브 모드 종료 (Ctrl+C)
    /// </summary>
    public async Task ExitInteractiveModeAsync()
    {
        if (_session == null) return;

        await _session.SendCtrlCAsync();

        // 약간의 지연 후 인터랙티브 모드 종료
        await Task.Delay(100);

        IsInteractiveMode = false;

        // 현재 블록 완료 처리
        if (_currentBlock != null)
        {
            _currentBlock.Status = BlockStatus.Success;
            _currentBlock = null;
        }
    }

    /// <summary>
    /// 환영 박스 표시 (Claude Code CLI 스타일)
    /// </summary>
    private void ShowWelcomeBox(string shellName)
    {
        // 앱 버전 정보 가져오기
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = $"v{version?.Major}.{version?.Minor}.{version?.Build ?? 0}";

        // 박스 내용
        var title = $"TermSnap {versionString}";
        var shellInfo = $"{shellName}";
        var dirInfo = $"Directory: {CurrentDirectory}";
        var welcome = "로컬 터미널 세션에 오신 것을 환영합니다!";

        // 박스 너비 계산 (가장 긴 줄 기준)
        int maxLength = Math.Max(
            Math.Max(title.Length, shellInfo.Length),
            Math.Max(dirInfo.Length, welcome.Length)
        ) + 4;  // 양쪽 여백

        // 박스 그리기
        var box = new StringBuilder();
        box.AppendLine("┌" + new string('─', maxLength) + "┐");
        box.AppendLine("│ " + title.PadRight(maxLength - 1) + "│");
        box.AppendLine("│ " + shellInfo.PadRight(maxLength - 1) + "│");
        box.AppendLine("│" + new string(' ', maxLength) + "│");
        box.AppendLine("│ " + dirInfo.PadRight(maxLength - 1) + "│");
        box.AppendLine("│" + new string(' ', maxLength) + "│");
        box.AppendLine("│ " + welcome.PadRight(maxLength - 1) + "│");
        box.AppendLine("└" + new string('─', maxLength) + "┘");

        // UI 메시지로 추가 (ConPTY 출력이 아닌 UI 레이어)
        Application.Current.Dispatcher.Invoke(() =>
        {
            AddMessage(box.ToString(), false, MessageType.Info);
        });
    }

    /// <summary>
    /// 추가 정보 박스 표시 (최근 세션, 릴리즈 뉴스)
    /// </summary>
    #region AI CLI 경과 시간 타이머

    /// <summary>
    /// 경과 시간 타이머 시작
    /// </summary>
    private void StartElapsedTimer()
    {
        _aicliStartTime = DateTime.Now;
        AICLIElapsedTime = "00:00:00";

        _elapsedTimer?.Stop();
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += OnElapsedTimerTick;
        _elapsedTimer.Start();
    }

    /// <summary>
    /// 경과 시간 타이머 중지
    /// </summary>
    private void StopElapsedTimer()
    {
        _elapsedTimer?.Stop();
        _elapsedTimer = null;
        _aicliStartTime = null;
        AICLIElapsedTime = string.Empty;
        AICLIProgramName = string.Empty;
    }

    /// <summary>
    /// 경과 시간 업데이트
    /// </summary>
    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        if (_aicliStartTime.HasValue)
        {
            var elapsed = DateTime.Now - _aicliStartTime.Value;

            // 24시간 이상이면 일자 포함, 미만이면 시:분:초만
            if (elapsed.TotalDays >= 1)
            {
                AICLIElapsedTime = $"{(int)elapsed.TotalDays}d {elapsed:hh\\:mm\\:ss}";
            }
            else
            {
                AICLIElapsedTime = elapsed.ToString(@"hh\:mm\:ss");
            }
        }
    }

    /// <summary>
    /// AI CLI 프로그램 이름 설정 (인터랙티브 모드 진입 시 호출)
    /// </summary>
    public void SetAICLIProgramName(string programName)
    {
        AICLIProgramName = programName;
    }

    #endregion

    #region 데이터 수신 스피너

    /// <summary>
    /// 데이터 수신 스피너 시작
    /// </summary>
    private void StartDataReceivingSpinner()
    {
        _lastDataReceivedTime = DateTime.Now;

        // 스피너 타이머가 없으면 생성 및 시작
        if (_spinnerTimer == null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _spinnerTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100) // 100ms마다 스피너 업데이트
                };
                _spinnerTimer.Tick += OnSpinnerTick;
                _spinnerTimer.Start();

                // 즉시 스피너 표시
                UpdateSpinnerFrame();
            });
        }

        // 데이터 수신 확인 타이머 (500ms 동안 데이터 없으면 스피너 중지)
        if (_dataReceivedTimer == null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _dataReceivedTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _dataReceivedTimer.Tick += OnDataReceivedTimerTick;
                _dataReceivedTimer.Start();
            });
        }
    }

    /// <summary>
    /// 스피너 프레임 업데이트
    /// </summary>
    private void OnSpinnerTick(object? sender, EventArgs e)
    {
        UpdateSpinnerFrame();
    }

    /// <summary>
    /// 스피너 프레임 갱신
    /// </summary>
    private void UpdateSpinnerFrame()
    {
        _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
        SpinnerText = SpinnerFrames[_spinnerIndex];
    }

    /// <summary>
    /// 데이터 수신 확인 타이머 (일정 시간 데이터 없으면 스피너 중지)
    /// </summary>
    private void OnDataReceivedTimerTick(object? sender, EventArgs e)
    {
        if (_lastDataReceivedTime.HasValue)
        {
            var elapsed = DateTime.Now - _lastDataReceivedTime.Value;
            if (elapsed.TotalMilliseconds > 500)
            {
                // 500ms 동안 데이터 없으면 스피너 중지
                StopDataReceivingSpinner();
            }
        }
    }

    /// <summary>
    /// 데이터 수신 스피너 중지
    /// </summary>
    private void StopDataReceivingSpinner()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_spinnerTimer != null)
            {
                _spinnerTimer.Stop();
                _spinnerTimer = null;
            }

            if (_dataReceivedTimer != null)
            {
                _dataReceivedTimer.Stop();
                _dataReceivedTimer = null;
            }

            SpinnerText = string.Empty;
            _lastDataReceivedTime = null;
        });
    }

    #endregion

    /// <summary>
    /// 특수 키 전송 (Tab, 화살표 등)
    /// </summary>
    public async Task SendSpecialKeyAsync(string key)
    {
        if (_session == null) return;
        await _session.SendKeyAsync(key);
    }

    /// <summary>
    /// 터미널 크기 변경 (ConPTY에 알림)
    /// </summary>
    public void ResizeTerminal(int columns, int rows)
    {
        _session?.ResizeTerminal(columns, rows);
    }

    /// <summary>
    /// 인터랙티브 출력 버퍼에 데이터 추가 (크기 제한 적용)
    /// </summary>
    private void AppendToInteractiveBuffer(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        _interactiveOutputBuffer.Append(data);

        // 버퍼 크기 제한: 최대 크기 초과 시 앞부분 삭제
        if (_interactiveOutputBuffer.Length > MaxInteractiveBufferSize)
        {
            var excessLength = _interactiveOutputBuffer.Length - MaxInteractiveBufferSize;
            _interactiveOutputBuffer.Remove(0, excessLength);
            System.Diagnostics.Debug.WriteLine($"[InteractiveBuffer] 버퍼 크기 초과, {excessLength}자 제거");
        }
    }

    /// <summary>
    /// 현재 인터랙티브 출력 버퍼 내용 (탭 복원 시 사용)
    /// </summary>
    public string GetInteractiveBuffer()
    {
        return _interactiveOutputBuffer.ToString();
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

    public void Dispose()
    {
        // 핸들러 정리
        _outputHandler?.Dispose();
        _interactiveHandler?.Dispose();
        _nonInteractiveHandler?.Dispose();

        // 타이머 정리
        StopElapsedTimer();
        StopDataReceivingSpinner();
        Disconnect();

        // 큰 컬렉션 정리 (메모리 누수 방지)
        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Messages?.Clear();
                CommandBlocks?.Clear();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalTerminalViewModel] Dispose 중 컬렉션 정리 오류: {ex.Message}");
        }

        // 이벤트 핸들러 정리 (메모리 누수 방지)
        PropertyChanged = null;
        Activated = null;
        Deactivated = null;
    }
}
