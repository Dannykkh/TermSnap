using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Nebula.Core;
using Nebula.Core.Sessions;
using Nebula.Models;
using Nebula.Services;
using static Nebula.Services.ShellDetectionService;

namespace Nebula.ViewModels;

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
    private string _statusMessage = "연결되지 않음";
    private string _tabHeader = "로컬 터미널";
    private string _currentDirectory = string.Empty;
    private string? _gitBranch = null;  // Git 브랜치 이름 (없으면 null)
    private LocalSession.LocalShellType _shellType;
    private bool _useBlockUI = true;
    private bool _showWelcome = true;  // 웰컴 화면 표시 여부
    private string? _workingFolder;    // 선택한 작업 폴더
    private bool _showSnippetPanel = false; // 스니펫 패널 표시 여부
    private DetectedShell? _selectedShell; // 선택된 쉘 정보
    private bool _isInteractiveMode = false; // 인터랙티브 모드 (claude, vim 등)
    private bool _isFileTreeVisible = false; // 파일 트리 패널 표시 여부
    private bool _isFileViewerVisible = false; // 파일 뷰어 패널 표시 여부
    private string? _fileTreeCurrentPath = null; // 파일 트리 현재 경로

    // AI CLI 경과 시간 추적
    private DateTime? _aicliStartTime;
    private DispatcherTimer? _elapsedTimer;
    private string _aicliElapsedTime = string.Empty;
    private string _aicliProgramName = string.Empty;

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

    // 출력 쓰로틀링을 위한 버퍼
    private readonly ConcurrentQueue<string> _outputBuffer = new();
    private readonly ConcurrentQueue<string> _errorBuffer = new();
    private Timer? _flushTimer;
    private const int FlushIntervalMs = 50; // 50ms마다 버퍼 플러시
    private const int MaxBufferSize = 100; // 즉시 플러시 트리거 크기
    private CommandBlock? _currentBlock;

    // 명령어 히스토리
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private string _savedInput = string.Empty; // 히스토리 탐색 전 입력 저장
    private const int MaxHistorySize = 100;

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
            StatusMessage = value ? "인터랙티브 모드 (Ctrl+C로 종료)" : $"{_selectedShell?.DisplayName ?? "터미널"} - {CurrentDirectory}";

            // 경과 시간 타이머 시작/중지
            if (value)
            {
                StartElapsedTimer();
            }
            else
            {
                StopElapsedTimer();
            }
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
    /// 로컬 터미널 스니펫 목록
    /// </summary>
    public ObservableCollection<CommandSnippet> LocalSnippets { get; } = new();

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
            GitBranch = GetGitBranch(value);
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

    public LocalTerminalViewModel(LocalSession.LocalShellType shellType = LocalSession.LocalShellType.PowerShell)
    {
        _shellType = shellType;
        _tabHeader = GetShellDisplayName(shellType);

        SendMessageCommand = new RelayCommand(async () => await ExecuteCommand(), () => CanSendMessage);
        DisconnectCommand = new RelayCommand(() => Disconnect(), () => IsConnected);
        ClearOutputCommand = new RelayCommand(() => ClearOutput());

        // 로컬 스니펫 로드
        LoadLocalSnippets();

        // 기본 쉘 감지
        _selectedShell = ShellDetectionService.Instance.GetDefaultShell();
        if (_selectedShell != null)
        {
            _shellType = _selectedShell.ShellType;
            _tabHeader = _selectedShell.DisplayName;
        }

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
    /// 로컬 스니펫 로드
    /// </summary>
    private void LoadLocalSnippets()
    {
        try
        {
            var config = ConfigService.Load();
            var snippets = config.LocalSnippets ?? new List<CommandSnippet>();

            LocalSnippets.Clear();
            foreach (var snippet in snippets.OrderByDescending(s => s.UseCount).ThenByDescending(s => s.LastUsedAt))
            {
                LocalSnippets.Add(snippet);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"로컬 스니펫 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 스니펫 저장
    /// </summary>
    public void SaveLocalSnippets()
    {
        try
        {
            var config = ConfigService.Load();
            config.LocalSnippets = LocalSnippets.ToList();
            ConfigService.Save(config);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"로컬 스니펫 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 스니펫 추가
    /// </summary>
    public void AddSnippet(CommandSnippet snippet)
    {
        LocalSnippets.Insert(0, snippet);
        SaveLocalSnippets();
    }

    /// <summary>
    /// 스니펫 삭제
    /// </summary>
    public void RemoveSnippet(CommandSnippet snippet)
    {
        LocalSnippets.Remove(snippet);
        SaveLocalSnippets();
    }

    /// <summary>
    /// 스니펫 사용 (사용 횟수 증가)
    /// </summary>
    public void UseSnippet(CommandSnippet snippet)
    {
        snippet.IncrementUseCount();
        SaveLocalSnippets();

        // 정렬 업데이트
        var sorted = LocalSnippets.OrderByDescending(s => s.UseCount).ThenByDescending(s => s.LastUsedAt).ToList();
        LocalSnippets.Clear();
        foreach (var s in sorted)
        {
            LocalSnippets.Add(s);
        }
    }

    #region 명령어 히스토리

    /// <summary>
    /// 히스토리에서 이전 명령어 (↑)
    /// </summary>
    public string? NavigateHistoryUp()
    {
        if (_commandHistory.Count == 0) return null;

        // 처음 탐색 시작할 때 현재 입력 저장
        if (_historyIndex == -1)
        {
            _savedInput = UserInput;
            _historyIndex = _commandHistory.Count;
        }

        if (_historyIndex > 0)
        {
            _historyIndex--;
            return _commandHistory[_historyIndex];
        }

        return _commandHistory.Count > 0 ? _commandHistory[0] : null;
    }

    /// <summary>
    /// 히스토리에서 다음 명령어 (↓)
    /// </summary>
    public string? NavigateHistoryDown()
    {
        if (_historyIndex == -1) return null;

        _historyIndex++;

        if (_historyIndex >= _commandHistory.Count)
        {
            // 마지막까지 내려왔으면 저장된 입력 복원
            _historyIndex = -1;
            return _savedInput;
        }

        return _commandHistory[_historyIndex];
    }

    /// <summary>
    /// 히스토리 인덱스 초기화
    /// </summary>
    public void ResetHistoryNavigation()
    {
        _historyIndex = -1;
        _savedInput = string.Empty;
    }

    /// <summary>
    /// 히스토리에 명령어 추가
    /// </summary>
    private void AddToHistory(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        // 중복 제거 (마지막 명령어와 같으면 추가 안 함)
        if (_commandHistory.Count > 0 && _commandHistory[^1] == command)
            return;

        _commandHistory.Add(command);

        // 최대 크기 초과 시 오래된 것 삭제
        while (_commandHistory.Count > MaxHistorySize)
        {
            _commandHistory.RemoveAt(0);
        }

        ResetHistoryNavigation();
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
        StatusMessage = "로컬 셸 시작 중...";
        AddMessage($"{shellName} 시작 중...", false, MessageType.Info);

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
                AddMessage($"✓ {shellName} 시작됨", false, MessageType.Success);
                AddMessage($"📁 현재 디렉토리: {CurrentDirectory}", false, MessageType.Info);
            }
            else
            {
                AddMessage("로컬 셸 시작 실패", false, MessageType.Error);
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

        // 인터랙티브 모드에서는 원시 출력을 터미널 컨트롤로 전달
        if (_isInteractiveMode)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                RawOutputReceived?.Invoke(e.RawData ?? e.Data);
            });
            return;
        }

        // 버퍼에 출력 추가 (쓰로틀링)
        if (e.IsError)
        {
            _errorBuffer.Enqueue(e.Data);
        }
        else
        {
            _outputBuffer.Enqueue(e.Data);
        }

        // 버퍼가 너무 크면 즉시 플러시
        if (_outputBuffer.Count + _errorBuffer.Count > MaxBufferSize)
        {
            FlushOutputBuffer();
        }
    }

    /// <summary>
    /// 버퍼 플러시 타이머 시작
    /// </summary>
    private void StartFlushTimer()
    {
        _flushTimer?.Dispose();
        _flushTimer = new Timer(_ => FlushOutputBuffer(), null, FlushIntervalMs, FlushIntervalMs);
    }

    /// <summary>
    /// 버퍼 플러시 타이머 중지
    /// </summary>
    private void StopFlushTimer()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
        // 남은 버퍼 모두 플러시
        FlushOutputBuffer();
    }

    /// <summary>
    /// 출력 버퍼를 UI에 플러시
    /// </summary>
    private void FlushOutputBuffer()
    {
        System.Diagnostics.Debug.WriteLine($"[FlushOutputBuffer] Output count: {_outputBuffer.Count}, Error count: {_errorBuffer.Count}, CurrentBlock: {_currentBlock != null}");

        if (_outputBuffer.IsEmpty && _errorBuffer.IsEmpty)
            return;

        var outputLines = new StringBuilder();
        var errorLines = new StringBuilder();

        // 출력 버퍼에서 모든 라인 수집
        while (_outputBuffer.TryDequeue(out var line))
        {
            outputLines.AppendLine(line);
        }

        // 에러 버퍼에서 모든 라인 수집
        while (_errorBuffer.TryDequeue(out var line))
        {
            errorLines.AppendLine(line);
        }

        if (outputLines.Length == 0 && errorLines.Length == 0)
            return;

        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            var outputText = outputLines.ToString().TrimEnd();
            var errorText = errorLines.ToString().TrimEnd();

            System.Diagnostics.Debug.WriteLine($"[FlushOutputBuffer UI] OutputText length: {outputText.Length}, ErrorText length: {errorText.Length}");
            System.Diagnostics.Debug.WriteLine($"[FlushOutputBuffer UI] OutputText: '{outputText.Substring(0, Math.Min(100, outputText.Length))}'");

            // Block UI 모드에서 현재 블록에 출력 추가
            if (_currentBlock != null)
            {
                System.Diagnostics.Debug.WriteLine($"[FlushOutputBuffer UI] Adding to block, current output length: {_currentBlock.Output?.Length ?? 0}");
                if (outputLines.Length > 0)
                {
                    _currentBlock.Output += outputText + "\n";
                }
                if (errorLines.Length > 0)
                {
                    _currentBlock.Error += errorText + "\n";
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[FlushOutputBuffer UI] _currentBlock is NULL!");
            }

            // 터미널 뷰 (Messages)에도 항상 추가
            if (outputLines.Length > 0)
            {
                AddMessage(outputText, false, MessageType.Normal);
            }
            if (errorLines.Length > 0)
            {
                AddMessage(errorText, false, MessageType.Error);
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnStateChanged(object? sender, ConnectionState state)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = state == ConnectionState.Connected;
            StatusMessage = state switch
            {
                ConnectionState.Connected => $"{GetShellDisplayName(_shellType)} - {CurrentDirectory}",
                ConnectionState.Connecting => "연결 중...",
                ConnectionState.Disconnected => "연결 해제됨",
                ConnectionState.Error => "오류 발생",
                _ => "알 수 없음"
            };
        });
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

        // 항상 블록 생성 (블록보기/터미널보기 모두 CommandBlocks 사용)
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

        IsBusy = true;
        StatusMessage = "명령어 실행 중...";
        var stopwatch = Stopwatch.StartNew();

        // 출력 버퍼 플러시 타이머 시작
        StartFlushTimer();

        try
        {
            // 인터랙티브 프로그램: 입력만 보내고 대기하지 않음
            if (isInteractiveProgram)
            {
                await _session.SendRawInputAsync(command);
                AICLIProgramName = programName ?? "터미널";
                IsInteractiveMode = true;
                // 블록은 Executing 상태로 유지 (출력 계속 수신)
                IsBusy = false;
                return;
            }

            var result = await _session.ExecuteCommandAsync(command);
            stopwatch.Stop();

            // 플러시 타이머 중지 및 잔여 버퍼 플러시
            StopFlushTimer();

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
            StopFlushTimer();
            
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
        StatusMessage = "연결 해제됨";
        TabHeader = $"{GetShellDisplayName(_shellType)} (연결 해제됨)";
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
                    UserInput = $"[{shellName} 시작]",
                    GeneratedCommand = $"cd \"{folderPath}\"",
                    Status = BlockStatus.Executing,
                    CurrentDirectory = folderPath,
                    IsLocalSession = true
                };
                Application.Current.Dispatcher.Invoke(() => CommandBlocks.AddWithLimit(_currentBlock, MaxCommandBlocks, TrimCount));
            }

            // 플러시 타이머 시작 (초기 출력 캡처)
            StartFlushTimer();

            var result = await _session.ConnectAsync();

            if (result)
            {
                IsConnected = true;

                // cd 명령어로 해당 폴더로 이동
                var cdResult = await _session.ExecuteCommandAsync($"cd \"{folderPath}\"");
                CurrentDirectory = folderPath;

                // 잠시 대기하여 초기 출력 수집
                await Task.Delay(300);
                StopFlushTimer();

                // 초기 블록 완료 처리
                if (_currentBlock != null)
                {
                    _currentBlock.Status = BlockStatus.Success;
                    _currentBlock.CurrentDirectory = folderPath;
                }

                StatusMessage = $"{shellName} - {CurrentDirectory}";
                AddMessage($"✓ {shellName} 시작됨", MessageType.Success);
                AddMessage($"📁 작업 폴더: {folderPath}", MessageType.Info);
            }
            else
            {
                AddMessage("로컬 셸 시작 실패", MessageType.Error);
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
            AICLIElapsedTime = elapsed.ToString(@"hh\:mm\:ss");
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
        StopFlushTimer();
        StopElapsedTimer();
        Disconnect();
    }

    /// <summary>
    /// 지정된 디렉토리의 Git 브랜치를 가져옵니다
    /// </summary>
    /// <param name="directory">확인할 디렉토리 경로</param>
    /// <returns>Git 브랜치 이름 (Git 저장소가 아니면 null)</returns>
    private static string? GetGitBranch(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        try
        {
            // .git 디렉토리가 있는지 확인 (상위 디렉토리까지 검색)
            var currentDir = new System.IO.DirectoryInfo(directory);
            while (currentDir != null)
            {
                var gitDir = System.IO.Path.Combine(currentDir.FullName, ".git");
                if (System.IO.Directory.Exists(gitDir))
                {
                    // .git/HEAD 파일 읽기
                    var headFile = System.IO.Path.Combine(gitDir, "HEAD");
                    if (System.IO.File.Exists(headFile))
                    {
                        var headContent = System.IO.File.ReadAllText(headFile).Trim();

                        // ref: refs/heads/main -> "main"
                        if (headContent.StartsWith("ref: refs/heads/"))
                        {
                            return headContent.Substring("ref: refs/heads/".Length);
                        }
                        // detached HEAD (커밋 해시)
                        else if (headContent.Length == 40) // SHA-1 해시
                        {
                            return headContent.Substring(0, 7); // 짧은 해시
                        }
                    }
                    break;
                }

                currentDir = currentDir.Parent;
            }
        }
        catch
        {
            // Git 브랜치를 가져오는 중 오류 발생 시 무시
        }

        return null;
    }
}
