using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TermSnap.Core.Sessions;
using TermSnap.Models;
using TermSnap.Services;
using TermSnap.ViewModels.Managers;

namespace TermSnap.ViewModels;

/// <summary>
/// 메인 윈도우 뷰모델 - 다중 탭 관리자
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly AppConfig _config;
    private ISessionViewModel? _selectedSession;
    private ISessionViewModel? _previousSession;

    // 리소스 모니터 (전체 프로그램 공통)
    private readonly ResourceMonitor _resourceMonitor = new();
    private DispatcherTimer? _resourceMonitorTimer;

    public ObservableCollection<ISessionViewModel> Sessions { get; } = new();

    public ISessionViewModel? SelectedSession
    {
        get => _selectedSession;
        set
        {
            // 이전 세션 비활성화 (파일 워처 끄기)
            _previousSession?.OnDeactivated();

            _selectedSession = value;

            // 새 세션 활성화 (파일 워처 켜기)
            _selectedSession?.OnActivated();

            _previousSession = _selectedSession;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentSession));
            OnPropertyChanged(nameof(CurrentDirectory));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsServerSession));
        }
    }

    /// <summary>
    /// 현재 선택된 세션 (SelectedSession의 별칭, 키보드 단축키 호환용)
    /// </summary>
    public ISessionViewModel? CurrentSession => SelectedSession;

    /// <summary>
    /// CPU 사용률 (%)
    /// </summary>
    public double CpuUsage => _resourceMonitor.CpuUsage;

    /// <summary>
    /// 메모리 사용량 (MB)
    /// </summary>
    public long MemoryUsageMB => _resourceMonitor.MemoryUsageMB;

    /// <summary>
    /// 현재 세션의 현재 디렉토리
    /// </summary>
    public string CurrentDirectory
    {
        get
        {
            if (CurrentSession is LocalTerminalViewModel localVm)
                return localVm.CurrentDirectory;
            if (CurrentSession is ServerSessionViewModel serverVm)
                return serverVm.CurrentDirectory;
            return string.Empty;
        }
    }

    /// <summary>
    /// 현재 세션의 연결 상태
    /// </summary>
    public bool IsConnected
    {
        get
        {
            if (CurrentSession is LocalTerminalViewModel localVm)
                return localVm.IsConnected;
            if (CurrentSession is ServerSessionViewModel serverVm)
                return serverVm.IsConnected;
            return false;
        }
    }

    /// <summary>
    /// 현재 세션의 Busy 상태
    /// </summary>
    public bool IsBusy
    {
        get
        {
            if (CurrentSession is LocalTerminalViewModel localVm)
                return localVm.IsBusy;
            if (CurrentSession is ServerSessionViewModel serverVm)
                return serverVm.IsBusy;
            return false;
        }
    }

    /// <summary>
    /// 현재 세션이 SSH 세션인지 여부
    /// </summary>
    public bool IsServerSession => CurrentSession is ServerSessionViewModel;

    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenSnippetsCommand { get; }
    public ICommand OpenHistoryCommand { get; }
    public ICommand OpenQAManagerCommand { get; }

    /// <summary>
    /// 오케스트레이터 창 열기
    /// </summary>
    public ICommand OpenOrchestratorCommand { get; }

    /// <summary>
    /// 로컬 터미널 탭 열기 (PowerShell)
    /// </summary>
    public ICommand NewLocalTerminalCommand { get; }

    /// <summary>
    /// 새 세션 추가 커맨드 (키보드 단축키 호환용)
    /// </summary>
    public ICommand AddNewSessionCommand { get; }

    /// <summary>
    /// 세션 닫기 커맨드 (키보드 단축키 호환용)
    /// </summary>
    public ICommand CloseSessionCommand { get; }

    /// <summary>
    /// 세션 연결 커맨드 (키보드 단축키 호환용)
    /// </summary>
    public ICommand ConnectSessionCommand { get; }

    /// <summary>
    /// 로컬 터미널 탭 열기 (CMD)
    /// </summary>
    public ICommand NewLocalCmdCommand { get; }

    /// <summary>
    /// 로컬 터미널 탭 열기 (WSL)
    /// </summary>
    public ICommand NewLocalWslCommand { get; }

    /// <summary>
    /// 로컬 터미널 탭 열기 (Git Bash)
    /// </summary>
    public ICommand NewLocalGitBashCommand { get; }

    /// <summary>
    /// 다음 탭으로 이동
    /// </summary>
    public ICommand NextTabCommand { get; }

    /// <summary>
    /// 이전 탭으로 이동
    /// </summary>
    public ICommand PrevTabCommand { get; }

    /// <summary>
    /// 특정 번호의 탭으로 이동
    /// </summary>
    public ICommand GoToTabCommand { get; }

    /// <summary>
    /// 수평 분할 (좌/우)
    /// </summary>
    public ICommand SplitHorizontalCommand { get; }

    /// <summary>
    /// 수직 분할 (상/하)
    /// </summary>
    public ICommand SplitVerticalCommand { get; }

    /// <summary>
    /// 분할 해제
    /// </summary>
    public ICommand UnsplitCommand { get; }

    /// <summary>
    /// 파일 트리 토글 (공용)
    /// </summary>
    public ICommand ToggleFileTreeCommand { get; }

    /// <summary>
    /// 스니펫 패널 토글 (공용)
    /// </summary>
    public ICommand ToggleSnippetPanelCommand { get; }

    /// <summary>
    /// 블록 UI 토글 (공용)
    /// </summary>
    public ICommand ToggleBlockUICommand { get; }

    /// <summary>
    /// 출력 지우기 (공용)
    /// </summary>
    public ICommand ClearOutputCommand { get; }

    private bool _isSplitMode;
    private ISessionViewModel? _secondarySession;
    private Views.SplitPaneContainer.SplitOrientation _splitOrientation = Views.SplitPaneContainer.SplitOrientation.None;

    /// <summary>
    /// 분할 모드 여부
    /// </summary>
    public bool IsSplitMode
    {
        get => _isSplitMode;
        set
        {
            _isSplitMode = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 보조 세션 (분할 시)
    /// </summary>
    public ISessionViewModel? SecondarySession
    {
        get => _secondarySession;
        set
        {
            _secondarySession = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 분할 방향
    /// </summary>
    public Views.SplitPaneContainer.SplitOrientation SplitOrientation
    {
        get => _splitOrientation;
        set
        {
            _splitOrientation = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 현재 선택된 AI 제공자/모델 표시 문자열
    /// </summary>
    public string CurrentAIDisplayName
    {
        get
        {
            if (_config.SelectedProvider == AIProviderType.None)
            {
                return LocalizationService.Instance.GetString("ViewModel.NoAI");
            }

            var providerName = _config.SelectedProvider switch
            {
                AIProviderType.Gemini => "Gemini",
                AIProviderType.OpenAI => "OpenAI",
                AIProviderType.Claude => "Claude",
                AIProviderType.Grok => "Grok",
                _ => "Unknown"
            };

            // 모델명 가져오기
            var model = _config.AIModels.FirstOrDefault(m => 
                m.Provider == _config.SelectedProvider && m.ModelId == _config.SelectedModelId);
            
            if (model != null)
            {
                return $"{providerName}: {model.ModelDisplayName}";
            }

            return providerName;
        }
    }

    public MainViewModel()
    {
        _config = ConfigService.Load();

        NewTabCommand = new RelayCommand(() => CreateNewTab());
        CloseTabCommand = new RelayCommand<ISessionViewModel>(session => CloseTab(session));
        OpenSettingsCommand = new RelayCommand(() => OpenSettings());
        OpenSnippetsCommand = new RelayCommand(() => OpenSnippets());
        OpenHistoryCommand = new RelayCommand(() => OpenHistory());
        OpenQAManagerCommand = new RelayCommand(() => OpenQAManager());
        OpenOrchestratorCommand = new RelayCommand(() => OpenOrchestrator());
        NewLocalTerminalCommand = new RelayCommand(() => CreateLocalTerminalTab(LocalSession.LocalShellType.PowerShell));
        NewLocalCmdCommand = new RelayCommand(() => CreateLocalTerminalTab(LocalSession.LocalShellType.Cmd));
        NewLocalWslCommand = new RelayCommand(() => CreateLocalTerminalTab(LocalSession.LocalShellType.WSL));
        NewLocalGitBashCommand = new RelayCommand(() => CreateLocalTerminalTab(LocalSession.LocalShellType.GitBash));

        // 탭 전환 커맨드
        NextTabCommand = new RelayCommand(() => NavigateToNextTab());
        PrevTabCommand = new RelayCommand(() => NavigateToPrevTab());
        GoToTabCommand = new RelayCommand<string>(index => GoToTab(index));

        // 분할 커맨드
        SplitHorizontalCommand = new RelayCommand(() => SplitPane(Views.SplitPaneContainer.SplitOrientation.Horizontal));
        SplitVerticalCommand = new RelayCommand(() => SplitPane(Views.SplitPaneContainer.SplitOrientation.Vertical));
        UnsplitCommand = new RelayCommand(() => Unsplit());

        // 키보드 단축키 호환용 별칭 커맨드
        AddNewSessionCommand = new RelayCommand(() => CreateNewTab());
        CloseSessionCommand = new RelayCommand<ISessionViewModel>(session => CloseTab(session));
        ConnectSessionCommand = new RelayCommand<ServerSessionViewModel>(session =>
        {
            if (session != null)
                _ = ConnectToServerAsync(session);
        });

        // 공용 하단 바 커맨드
        ToggleFileTreeCommand = new RelayCommand(() =>
        {
            if (CurrentSession is LocalTerminalViewModel localVm)
                localVm.IsFileTreeVisible = !localVm.IsFileTreeVisible;
            else if (CurrentSession is ServerSessionViewModel serverVm)
                serverVm.IsFileTreeVisible = !serverVm.IsFileTreeVisible;
        });

        ToggleSnippetPanelCommand = new RelayCommand(() =>
        {
            if (CurrentSession is LocalTerminalViewModel localVm)
                localVm.ShowSnippetPanel = !localVm.ShowSnippetPanel;
        });

        ToggleBlockUICommand = new RelayCommand(() =>
        {
            if (CurrentSession != null)
                CurrentSession.UseBlockUI = !CurrentSession.UseBlockUI;
        });

        ClearOutputCommand = new RelayCommand(() =>
        {
            if (CurrentSession is LocalTerminalViewModel localVm)
                localVm.ClearOutputCommand?.Execute(null);
            else if (CurrentSession is ServerSessionViewModel serverVm)
            {
                serverVm.Messages.Clear();
                serverVm.CommandBlocks.Clear();
            }
        });

        // 첫 번째 탭 생성 (세션 선택 화면 표시)
        CreateNewTab();

        // 리소스 모니터링 시작 (전체 프로그램 공통)
        StartResourceMonitoring();
    }

    /// <summary>
    /// 리소스 모니터링 타이머 시작
    /// </summary>
    private void StartResourceMonitoring()
    {
        _resourceMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1) // 1초마다 업데이트
        };
        _resourceMonitorTimer.Tick += OnResourceMonitorTimerTick;
        _resourceMonitorTimer.Start();

        // 초기값 설정
        _resourceMonitor.Update();
    }

    /// <summary>
    /// 리소스 모니터링 타이머 틱
    /// </summary>
    private void OnResourceMonitorTimerTick(object? sender, EventArgs e)
    {
        _resourceMonitor.Update();
        OnPropertyChanged(nameof(CpuUsage));
        OnPropertyChanged(nameof(MemoryUsageMB));
    }

    /// <summary>
    /// 새 탭 생성 - 세션 선택 화면 표시
    /// </summary>
    private void CreateNewTab()
    {
        var selectorSession = new NewSessionSelectorViewModel();
        
        // 선택 이벤트 연결
        selectorSession.LocalTerminalSelected += () => ReplaceWithLocalTerminal(selectorSession);
        selectorSession.SshServerSelected += () => ReplaceWithSshSession(selectorSession);
        
        Sessions.Add(selectorSession);
        SelectedSession = selectorSession;
    }

    /// <summary>
    /// 세션 선택 화면을 로컬 터미널로 교체
    /// </summary>
    private void ReplaceWithLocalTerminal(NewSessionSelectorViewModel selectorSession)
    {
        var index = Sessions.IndexOf(selectorSession);
        if (index < 0) return;

        // 로컬 터미널 세션 생성
        var localSession = new LocalTerminalViewModel(LocalSession.LocalShellType.PowerShell);
        
        // 교체
        Sessions[index] = localSession;
        SelectedSession = localSession;
        
        // 기존 선택 세션 정리
        selectorSession.Dispose();

        // 자동 연결하지 않음 - WelcomePanel에서 쉘 선택 후 폴더 선택 시 연결됨
        // _ = localSession.ConnectAsync();
    }

    /// <summary>
    /// 세션 선택 화면을 SSH 세션으로 교체
    /// </summary>
    private void ReplaceWithSshSession(NewSessionSelectorViewModel selectorSession)
    {
        var index = Sessions.IndexOf(selectorSession);
        if (index < 0) return;

        // SSH 세션 생성
        var sshSession = new ServerSessionViewModel(_config);

        // 교체
        Sessions[index] = sshSession;
        SelectedSession = sshSession;

        // 기존 선택 세션 정리
        selectorSession.Dispose();

        // 자동 연결하지 않음 - ServerSessionView에서 프로필 목록 표시
        // 사용자가 "연결" 버튼을 클릭해야 연결됨
    }

    /// <summary>
    /// 로컬 터미널 탭 생성 (특정 셸 타입)
    /// </summary>
    private void CreateLocalTerminalTab(LocalSession.LocalShellType shellType)
    {
        var localSession = new LocalTerminalViewModel(shellType);
        Sessions.Add(localSession);
        SelectedSession = localSession;

        // 자동 연결하지 않음 - WelcomePanel에서 쉘 선택 후 폴더 선택 시 연결됨
        // _ = localSession.ConnectAsync();
    }

    private static string GetShellDisplayName(LocalSession.LocalShellType shellType)
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

    private async Task ConnectToServerAsync(ServerSessionViewModel session)
    {
        // 프로필이 없으면 빈 탭 유지 (강제 설정 창 열기 제거)
        if (_config.ServerProfiles.Count == 0)
        {
            return;
        }

        ServerConfig? selectedProfile = null;

        if (_config.ServerProfiles.Count == 1)
        {
            // 프로필이 하나만 있으면 자동 선택
            selectedProfile = _config.ServerProfiles[0];
        }
        else
        {
            // 여러 프로필이 있으면 선택 창 표시
            var profileSelector = new Views.ProfileSelectorWindow(_config.ServerProfiles);
            if (profileSelector.ShowDialog() == true)
            {
                selectedProfile = profileSelector.SelectedProfile;

                if (selectedProfile == null)
                {
                    // "새 프로필" 버튼을 눌렀을 경우
                    OpenSettings();
                    return;
                }
            }
            else
            {
                // 취소하면 빈 탭 유지
                return;
            }
        }

        if (selectedProfile == null)
            return;

        try
        {
            await session.ConnectAsync(selectedProfile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("ViewModel.ConnectionFailed"), ex.Message),
                LocalizationService.Instance.GetString("ViewModel.ConnectionError"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

private void CloseTab(ISessionViewModel? session)
    {
        if (session == null)
            return;

        // 연결 해제
        session.Dispose();

        // 탭 제거
        Sessions.Remove(session);

        // 탭이 모두 닫히면 새 탭 생성 (선택 화면)
        if (Sessions.Count == 0)
        {
            CreateNewTab();
        }
        else if (SelectedSession == session)
        {
            // 닫은 탭이 선택된 탭이면 다른 탭 선택
            SelectedSession = Sessions.FirstOrDefault();
        }
    }

    private void OpenSettings()
    {
        var settingsWindow = new Views.SettingsWindow(_config);
        if (settingsWindow.ShowDialog() == true)
        {
            // AI 표시 이름 업데이트
            OnPropertyChanged(nameof(CurrentAIDisplayName));
        }
    }

    private void OpenSnippets()
    {
        var snippetsWindow = new Views.SnippetManagerWindow(_config);
        snippetsWindow.ShowDialog();
    }

    private void OpenHistory()
    {
        var historyWindow = new Views.HistoryWindow(_config);
        if (historyWindow.ShowDialog() == true && historyWindow.SelectedHistory != null)
        {
            // 선택한 히스토리의 명령어를 현재 탭의 입력창에 채우기
            if (SelectedSession != null)
            {
                SelectedSession.UserInput = historyWindow.SelectedHistory.GeneratedCommand;
            }
        }
    }

    private void OpenQAManager()
    {
        var qaWindow = new Views.QAManagerWindow();
        qaWindow.ShowDialog();
    }

    private void OpenOrchestrator()
    {
        var orchestratorWindow = new Views.OrchestratorWindow();
        orchestratorWindow.ShowDialog();
    }

    /// <summary>
    /// 다음 탭으로 이동
    /// </summary>
    private void NavigateToNextTab()
    {
        if (Sessions.Count <= 1) return;

        var currentIndex = Sessions.IndexOf(SelectedSession!);
        var nextIndex = (currentIndex + 1) % Sessions.Count;
        SelectedSession = Sessions[nextIndex];
    }

    /// <summary>
    /// 이전 탭으로 이동
    /// </summary>
    private void NavigateToPrevTab()
    {
        if (Sessions.Count <= 1) return;

        var currentIndex = Sessions.IndexOf(SelectedSession!);
        var prevIndex = (currentIndex - 1 + Sessions.Count) % Sessions.Count;
        SelectedSession = Sessions[prevIndex];
    }

    /// <summary>
    /// 특정 번호의 탭으로 이동 (0-based index)
    /// </summary>
    private void GoToTab(string? indexStr)
    {
        if (string.IsNullOrEmpty(indexStr)) return;
        if (!int.TryParse(indexStr, out var index)) return;
        if (index < 0 || index >= Sessions.Count) return;

        SelectedSession = Sessions[index];
    }

    /// <summary>
    /// 화면 분할
    /// </summary>
    private void SplitPane(Views.SplitPaneContainer.SplitOrientation orientation)
    {
        if (IsSplitMode)
        {
            // 이미 분할된 상태면 방향만 변경
            SplitOrientation = orientation;
            return;
        }

        // 새 로컬 터미널 세션 생성 (분할용)
        var newSession = new LocalTerminalViewModel(LocalSession.LocalShellType.PowerShell);
        SecondarySession = newSession;
        SplitOrientation = orientation;
        IsSplitMode = true;

        // 세션 시작
        _ = newSession.ConnectAsync();
    }

    /// <summary>
    /// 분할 해제
    /// </summary>
    private void Unsplit()
    {
        if (!IsSplitMode) return;

        SecondarySession?.Dispose();
        SecondarySession = null;
        SplitOrientation = Views.SplitPaneContainer.SplitOrientation.None;
        IsSplitMode = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// 간단한 RelayCommand 구현
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}

/// <summary>
/// 제네릭 RelayCommand 구현
/// </summary>
public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;

    public void Execute(object? parameter) => _execute((T?)parameter);
}
