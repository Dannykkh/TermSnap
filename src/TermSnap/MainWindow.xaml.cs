using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TermSnap.Core.Sessions;
using TermSnap.Mcp;
using TermSnap.Services;
using TermSnap.ViewModels;
using TermSnap.Views;

namespace TermSnap;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // 키보드 단축키 등록
        KeyDown += MainWindow_KeyDown;

        // 분할 모드 변경 및 탭 전환 감지
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // 초기 레이아웃 설정
        Loaded += (s, e) =>
        {
            UpdateSplitLayout();

            // MCP IPC 서버 시작
            try
            {
                IpcService.Instance.Start(_viewModel);
                System.Diagnostics.Debug.WriteLine("[MainWindow] MCP IPC 서버 시작됨");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] MCP IPC 서버 시작 실패: {ex.Message}");
            }
        };
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSplitMode) ||
            e.PropertyName == nameof(MainViewModel.SplitOrientation))
        {
            UpdateSplitLayout();
        }
    }

    private void UpdateSplitLayout()
    {
        // 모든 레이아웃 숨기기
        MainContentControl.Visibility = Visibility.Collapsed;
        HorizontalSplitGrid.Visibility = Visibility.Collapsed;
        VerticalSplitGrid.Visibility = Visibility.Collapsed;

        if (!_viewModel.IsSplitMode)
        {
            // 단일 모드
            MainContentControl.Visibility = Visibility.Visible;
        }
        else
        {
            // 분할 모드
            switch (_viewModel.SplitOrientation)
            {
                case SplitPaneContainer.SplitOrientation.Horizontal:
                    HorizontalSplitGrid.Visibility = Visibility.Visible;
                    break;
                case SplitPaneContainer.SplitOrientation.Vertical:
                    VerticalSplitGrid.Visibility = Visibility.Visible;
                    break;
                default:
                    MainContentControl.Visibility = Visibility.Visible;
                    break;
            }
        }
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+Shift+P: Command Palette
        if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OpenCommandPalette();
            e.Handled = true;
        }
        // Ctrl+R: History Search
        else if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenHistorySearch();
            e.Handled = true;
        }
        // Ctrl+T: New Tab
        else if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.AddNewSessionCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+W: Close Tab
        else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_viewModel.CurrentSession != null)
                _viewModel.CloseSessionCommand.Execute(_viewModel.CurrentSession);
            e.Handled = true;
        }
        // Ctrl+,: Settings
        else if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenSettings();
            e.Handled = true;
        }
    }

    private void OpenCommandPalette()
    {
        var config = ConfigService.Load();
        string? serverProfile = null;
        if (_viewModel.CurrentSession is ServerSessionViewModel sshSession)
        {
            serverProfile = sshSession.ServerProfile?.ProfileName;
        }
        
        var palette = new CommandPalette(config, serverProfile);
        palette.Owner = this;
        
        if (palette.ShowDialog() == true)
        {
            // 선택된 항목 처리
            if (!string.IsNullOrEmpty(palette.SelectedCommand) && _viewModel.CurrentSession != null)
            {
                _viewModel.CurrentSession.UserInput = palette.SelectedCommand;
            }
            else if (!string.IsNullOrEmpty(palette.SelectedAction))
            {
                ExecuteAction(palette.SelectedAction);
            }
        }
    }

    private void OpenHistorySearch()
    {
        string? serverProfile = null;
        if (_viewModel.CurrentSession is ServerSessionViewModel sshSession)
        {
            serverProfile = sshSession.ServerProfile?.ProfileName;
        }
        
        var popup = new HistorySearchPopup(serverProfile);
        popup.Owner = this;
        
        if (popup.ShowDialog() == true && !string.IsNullOrEmpty(popup.SelectedCommand))
        {
            if (_viewModel.CurrentSession != null)
            {
                _viewModel.CurrentSession.UserInput = popup.SelectedCommand;
            }
        }
    }

    private void ExecuteAction(string action)
    {
        switch (action)
        {
            case "OpenNewTab":
                _viewModel.AddNewSessionCommand.Execute(null);
                break;
            case "Connect":
                if (_viewModel.CurrentSession != null)
                    _viewModel.ConnectSessionCommand.Execute(_viewModel.CurrentSession);
                break;
            case "Disconnect":
                _viewModel.CurrentSession?.DisconnectCommand.Execute(null);
                break;
            case "OpenFileTransfer":
                if (_viewModel.CurrentSession is ServerSessionViewModel sshSession1)
                    sshSession1.OpenFileTransferCommand.Execute(null);
                break;
            case "OpenMonitor":
                if (_viewModel.CurrentSession is ServerSessionViewModel sshSession2)
                    sshSession2.OpenMonitorCommand.Execute(null);
                break;
            case "OpenLogViewer":
                if (_viewModel.CurrentSession is ServerSessionViewModel sshSession3)
                    sshSession3.OpenLogViewerCommand.Execute(null);
                break;
            case "OpenSettings":
                OpenSettings();
                break;
            case "OpenHistory":
                new HistoryWindow(ConfigService.Load()).Show();
                break;
            case "OpenSnippets":
                new SnippetManagerWindow(ConfigService.Load()).Show();
                break;
        }
    }

    private void OpenSettings()
    {
        var settingsWindow = new SettingsWindow(ConfigService.Load());
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }

    /// <summary>
    /// 커스텀 탭 헤더 클릭 시 해당 세션 선택
    /// </summary>
    private void TabHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ISessionViewModel session)
        {
            _viewModel.SelectedSession = session;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // MCP IPC 서버 종료
        try
        {
            IpcService.Instance.Stop();
            System.Diagnostics.Debug.WriteLine("[MainWindow] MCP IPC 서버 종료됨");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] MCP IPC 서버 종료 실패: {ex.Message}");
        }

        // 리소스 정리 - 모든 세션 종료
        if (_viewModel != null)
        {
            foreach (var session in _viewModel.Sessions)
            {
                session.Dispose();
            }
        }
    }
}
