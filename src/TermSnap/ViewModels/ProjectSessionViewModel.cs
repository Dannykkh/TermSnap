using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TermSnap.Core.Sessions;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.ViewModels;

/// <summary>
/// 프로젝트 세션 ViewModel - 서브탭(터미널/CLI)을 포함하는 컨테이너
/// </summary>
public class ProjectSessionViewModel : ISessionViewModel, INotifyPropertyChanged
{
    private string _projectPath = string.Empty;
    private string _projectName = string.Empty;
    private string _userInput = string.Empty;
    private ISessionViewModel? _selectedSubSession;
    private bool _isFileTreeVisible = false;
    private string? _fileTreeCurrentPath;
    private bool _showSnippetPanel = false;
    private bool _isShowingSubTabSelector = false;

    // SelectedSubSession의 PropertyChanged 전파용
    private INotifyPropertyChanged? _trackedSubSession;

    /// <summary>
    /// 프로젝트 경로 (서브탭 전환해도 유지)
    /// </summary>
    public string ProjectPath
    {
        get => _projectPath;
        set { _projectPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentDirectory)); }
    }

    /// <summary>
    /// 프로젝트 이름 (TabHeader로 표시)
    /// </summary>
    public string ProjectName
    {
        get => _projectName;
        set { _projectName = value; OnPropertyChanged(); OnPropertyChanged(nameof(TabHeader)); }
    }

    /// <summary>
    /// 서브탭 목록
    /// </summary>
    public ObservableCollection<ISessionViewModel> SubSessions { get; } = new();

    /// <summary>
    /// 현재 선택된 서브세션
    /// </summary>
    public ISessionViewModel? SelectedSubSession
    {
        get => _selectedSubSession;
        set
        {
            // 이전 서브세션 비활성화
            _selectedSubSession?.OnDeactivated();

            // 이전 서브세션 PropertyChanged 구독 해제
            if (_trackedSubSession != null)
            {
                _trackedSubSession.PropertyChanged -= SubSession_PropertyChanged;
            }

            _selectedSubSession = value;

            // 서브탭 선택기 숨김 (서브탭 클릭 시)
            if (value != null)
                IsShowingSubTabSelector = false;

            // 새 서브세션 활성화
            _selectedSubSession?.OnActivated();

            // 새 서브세션 PropertyChanged 구독
            if (_selectedSubSession is INotifyPropertyChanged newTracked)
            {
                newTracked.PropertyChanged += SubSession_PropertyChanged;
                _trackedSubSession = newTracked;
            }

            OnPropertyChanged();
            // 프록시 속성 갱신
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(SpinnerText));
            OnPropertyChanged(nameof(Messages));
            OnPropertyChanged(nameof(CommandBlocks));
        }
    }

    /// <summary>
    /// 서브세션의 PropertyChanged 전파
    /// </summary>
    private void SubSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 주요 프록시 속성 전파
        switch (e.PropertyName)
        {
            case nameof(ISessionViewModel.IsConnected):
                OnPropertyChanged(nameof(IsConnected));
                break;
            case nameof(ISessionViewModel.IsBusy):
                OnPropertyChanged(nameof(IsBusy));
                break;
            case nameof(ISessionViewModel.StatusMessage):
                OnPropertyChanged(nameof(StatusMessage));
                break;
            case nameof(ISessionViewModel.SpinnerText):
                OnPropertyChanged(nameof(SpinnerText));
                break;
            case nameof(ISessionViewModel.CurrentDirectory):
                OnPropertyChanged(nameof(CurrentDirectory));
                break;
        }
    }

    #region ISessionViewModel 구현 (SelectedSubSession에 프록시)

    public string TabHeader
    {
        get => string.IsNullOrEmpty(ProjectName) ? "프로젝트" : ProjectName;
        set { ProjectName = value; OnPropertyChanged(); }
    }

    public string UserInput
    {
        get => SelectedSubSession?.UserInput ?? _userInput;
        set
        {
            if (SelectedSubSession != null)
                SelectedSubSession.UserInput = value;
            else
                _userInput = value;
            OnPropertyChanged();
        }
    }

    public bool IsConnected => SelectedSubSession?.IsConnected ?? false;
    public bool IsBusy => SelectedSubSession?.IsBusy ?? false;
    public string StatusMessage => SelectedSubSession?.StatusMessage ?? string.Empty;
    public string CurrentDirectory => ProjectPath;
    public string SpinnerText => SelectedSubSession?.SpinnerText ?? string.Empty;

    public bool UseBlockUI
    {
        get => SelectedSubSession?.UseBlockUI ?? false;
        set
        {
            if (SelectedSubSession != null)
                SelectedSubSession.UseBlockUI = value;
        }
    }

    public bool IsFileTreeVisible
    {
        get => _isFileTreeVisible;
        set { _isFileTreeVisible = value; OnPropertyChanged(); }
    }

    public string? FileTreeCurrentPath
    {
        get => _fileTreeCurrentPath ?? ProjectPath;
        set { _fileTreeCurrentPath = value; OnPropertyChanged(); }
    }

    public bool ShowSnippetPanel
    {
        get => _showSnippetPanel;
        set { _showSnippetPanel = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ChatMessage> Messages => SelectedSubSession?.Messages ?? _emptyMessages;
    public ObservableCollection<CommandBlock> CommandBlocks => SelectedSubSession?.CommandBlocks ?? _emptyCommandBlocks;
    private static readonly ObservableCollection<ChatMessage> _emptyMessages = new();
    private static readonly ObservableCollection<CommandBlock> _emptyCommandBlocks = new();

    public ICommand SendMessageCommand { get; }
    public ICommand DisconnectCommand { get; }

    public SessionType Type => SessionType.Project;

    public event EventHandler? Activated;
    public event EventHandler? Deactivated;

    #endregion

    /// <summary>
    /// 서브탭 추가 선택기 표시 여부
    /// </summary>
    public bool IsShowingSubTabSelector
    {
        get => _isShowingSubTabSelector;
        set { _isShowingSubTabSelector = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 서브탭 추가 커맨드
    /// </summary>
    public ICommand AddSubTabCommand { get; }

    /// <summary>
    /// 서브탭 닫기 커맨드
    /// </summary>
    public ICommand CloseSubTabCommand { get; }

    public ProjectSessionViewModel()
    {
        SendMessageCommand = new RelayCommand(() => SelectedSubSession?.SendMessageCommand?.Execute(null));
        DisconnectCommand = new RelayCommand(() => SelectedSubSession?.DisconnectCommand?.Execute(null));
        AddSubTabCommand = new RelayCommand(() => ShowSubTabSelector());
        CloseSubTabCommand = new RelayCommand<ISessionViewModel>(session => CloseSubTab(session));
    }

    /// <summary>
    /// 서브탭 선택기 표시
    /// </summary>
    public void ShowSubTabSelector()
    {
        IsShowingSubTabSelector = true;
    }

    /// <summary>
    /// 서브탭 선택기 취소
    /// </summary>
    public void CancelSubTabSelector()
    {
        IsShowingSubTabSelector = false;
    }

    /// <summary>
    /// 프로젝트 열기 (폴더 경로 설정 + 첫 번째 터미널 서브탭 생성)
    /// </summary>
    public async System.Threading.Tasks.Task OpenProjectAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        ProjectPath = folderPath;
        ProjectName = Path.GetFileName(folderPath);
        FileTreeCurrentPath = folderPath;
        IsFileTreeVisible = true;

        // 서브탭이 없으면 첫 번째 터미널 서브탭 생성
        if (SubSessions.Count == 0)
        {
            AddTerminalSubTab(LocalSession.LocalShellType.PowerShell, folderPath);
        }
    }

    /// <summary>
    /// 터미널 서브탭 추가
    /// </summary>
    public void AddTerminalSubTab(LocalSession.LocalShellType shellType, string? workingFolder = null)
    {
        var localVm = new LocalTerminalViewModel(shellType);

        // 서브탭 헤더 설정 (터미널 1, 터미널 2, ...)
        var shellName = shellType switch
        {
            LocalSession.LocalShellType.PowerShell => "PowerShell",
            LocalSession.LocalShellType.Cmd => "CMD",
            LocalSession.LocalShellType.WSL => "WSL",
            LocalSession.LocalShellType.GitBash => "Git Bash",
            _ => "Terminal"
        };
        var count = SubSessions.Count(s => s is LocalTerminalViewModel);
        localVm.TabHeader = count == 0 ? shellName : $"{shellName} {count + 1}";

        SubSessions.Add(localVm);
        SelectedSubSession = localVm;

        // 작업 폴더가 있으면 자동으로 해당 폴더에서 시작
        var folder = workingFolder ?? ProjectPath;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            _ = localVm.OpenFolderAsync(folder);
        }
    }

    /// <summary>
    /// 서브탭 닫기
    /// </summary>
    public void CloseSubTab(ISessionViewModel? session)
    {
        if (session == null) return;

        var index = SubSessions.IndexOf(session);
        if (index < 0) return;

        session.Dispose();
        SubSessions.Remove(session);

        // 닫은 서브탭이 선택된 서브탭이면 다른 서브탭 선택
        if (SelectedSubSession == session || SelectedSubSession == null)
        {
            if (SubSessions.Count > 0)
            {
                // 가능하면 같은 인덱스, 아니면 마지막 서브탭 선택
                var newIndex = Math.Min(index, SubSessions.Count - 1);
                SelectedSubSession = SubSessions[newIndex];
            }
            else
            {
                SelectedSubSession = null;
            }
        }
    }

    public void OnActivated()
    {
        Activated?.Invoke(this, EventArgs.Empty);
        SelectedSubSession?.OnActivated();
    }

    public void OnDeactivated()
    {
        Deactivated?.Invoke(this, EventArgs.Empty);
        SelectedSubSession?.OnDeactivated();
    }

    public void Dispose()
    {
        // 서브세션 PropertyChanged 구독 해제
        if (_trackedSubSession != null)
        {
            _trackedSubSession.PropertyChanged -= SubSession_PropertyChanged;
        }

        // 모든 서브세션 정리
        foreach (var session in SubSessions)
        {
            session.Dispose();
        }
        SubSessions.Clear();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
