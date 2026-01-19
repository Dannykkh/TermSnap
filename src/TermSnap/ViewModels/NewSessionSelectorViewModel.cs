using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TermSnap.Models;

namespace TermSnap.ViewModels;

/// <summary>
/// 새 세션 선택 화면 ViewModel
/// </summary>
public class NewSessionSelectorViewModel : ISessionViewModel, INotifyPropertyChanged
{
    private string _tabHeader = "새 탭";
    private string _userInput = string.Empty;

    /// <summary>
    /// 로컬 터미널 선택 시 발생
    /// </summary>
    public event Action? LocalTerminalSelected;

    /// <summary>
    /// SSH 서버 연결 선택 시 발생
    /// </summary>
    public event Action? SshServerSelected;

    public string TabHeader
    {
        get => _tabHeader;
        set { _tabHeader = value; OnPropertyChanged(); }
    }

    public string UserInput
    {
        get => _userInput;
        set { _userInput = value; OnPropertyChanged(); }
    }

    public bool IsConnected => false;
    public bool IsBusy => false;
    public string StatusMessage => "세션 타입을 선택하세요";
    public string CurrentDirectory => string.Empty;
    public bool UseBlockUI { get; set; } = false;

    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public ObservableCollection<CommandBlock> CommandBlocks { get; } = new();

    public ICommand SendMessageCommand { get; }
    public ICommand DisconnectCommand { get; }

    /// <summary>
    /// 로컬 터미널 선택 커맨드
    /// </summary>
    public ICommand SelectLocalTerminalCommand { get; }

    /// <summary>
    /// SSH 서버 연결 선택 커맨드
    /// </summary>
    public ICommand SelectSshServerCommand { get; }

    public SessionType Type => SessionType.Selector;

    public NewSessionSelectorViewModel()
    {
        SendMessageCommand = new RelayCommand(() => { });
        DisconnectCommand = new RelayCommand(() => { });

        SelectLocalTerminalCommand = new RelayCommand(() => LocalTerminalSelected?.Invoke());
        SelectSshServerCommand = new RelayCommand(() => SshServerSelected?.Invoke());
    }

    public event EventHandler? Activated;
    public event EventHandler? Deactivated;

    /// <summary>
    /// 탭이 활성화될 때 호출
    /// </summary>
    public void OnActivated()
    {
        Activated?.Invoke(this, EventArgs.Empty);
        // 세션 선택 화면은 파일 워처를 사용하지 않음
    }

    /// <summary>
    /// 탭이 비활성화될 때 호출
    /// </summary>
    public void OnDeactivated()
    {
        Deactivated?.Invoke(this, EventArgs.Empty);
        // 세션 선택 화면은 파일 워처를 사용하지 않음
    }

    public void Dispose()
    {
        // 정리할 리소스 없음
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
