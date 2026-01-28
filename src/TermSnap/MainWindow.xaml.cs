using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TermSnap.Core.Sessions;
using TermSnap.Mcp;
using TermSnap.Models;
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
    private ISessionViewModel? _currentTrackedSession;

    // View 캐싱 Dictionary (탭 전환 시 View 재사용)
    private readonly Dictionary<ISessionViewModel, UIElement> _viewCache = new();

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // 키보드 단축키 등록
        KeyDown += MainWindow_KeyDown;

        // 분할 모드 변경 및 탭 전환 감지
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Sessions 컬렉션 변경 감지 (탭 닫기 시 구독 해제)
        _viewModel.Sessions.CollectionChanged += Sessions_CollectionChanged;

        // 창 닫기 이벤트 (UI 설정 저장)
        Closing += MainWindow_Closing;

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

            // FileTreePanel 이벤트 연결
            ConnectFileTreePanelEvents();
        };
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSplitMode) ||
            e.PropertyName == nameof(MainViewModel.SplitOrientation))
        {
            UpdateSplitLayout();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedSession))
        {
            // 이전 세션의 PropertyChanged 구독 해제
            if (_currentTrackedSession != null && _currentTrackedSession is INotifyPropertyChanged oldSession)
            {
                oldSession.PropertyChanged -= CurrentSession_PropertyChanged;
            }

            // 새 세션의 PropertyChanged 구독
            if (_viewModel.CurrentSession != null && _viewModel.CurrentSession is INotifyPropertyChanged newSession)
            {
                newSession.PropertyChanged += CurrentSession_PropertyChanged;
                _currentTrackedSession = _viewModel.CurrentSession;
            }

            // View 캐싱: 현재 선택된 세션의 View 할당
            UpdateSessionView();

            // 탭 전환 시 파일 트리 동기화
            _ = SyncFileTreePanelAsync();
            UpdateSnippetPanelVisibility();
        }
        else if (e.PropertyName == nameof(MainViewModel.SecondarySession))
        {
            // 분할 모드에서 보조 세션 변경 시에도 View 업데이트
            UpdateSessionView();
        }
    }

    /// <summary>
    /// Sessions 컬렉션 변경 감지 (탭 닫기 시 구독 해제)
    /// </summary>
    private void Sessions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 제거된 세션의 PropertyChanged 구독 해제 및 View 캐시 정리
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged removedSession)
                {
                    removedSession.PropertyChanged -= CurrentSession_PropertyChanged;

                    // 현재 추적 중인 세션이 제거되었다면 null로 설정
                    if (_currentTrackedSession == item)
                    {
                        _currentTrackedSession = null;
                    }
                }

                // View 캐시에서 제거
                if (item is ISessionViewModel sessionVm && _viewCache.Remove(sessionVm, out var cachedView))
                {
                    System.Diagnostics.Debug.WriteLine($"[ViewCache] 제거됨: {sessionVm.TabHeader}");

                    // View가 IDisposable이면 정리
                    if (cachedView is IDisposable disposableView)
                    {
                        disposableView.Dispose();
                    }
                }
            }
        }
    }

    /// <summary>
    /// CurrentSession의 속성 변경 감지
    /// </summary>
    private void CurrentSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISessionViewModel.IsFileTreeVisible))
        {
            // 파일 트리 가시성 변경 시 동기화
            _ = SyncFileTreePanelAsync();
        }
        else if (e.PropertyName == nameof(ISessionViewModel.ShowSnippetPanel))
        {
            // 스니펫 패널 가시성 변경 시 업데이트
            UpdateSnippetPanelVisibility();
        }
        else if (e.PropertyName == nameof(ISessionViewModel.IsConnected))
        {
            // SSH 연결/해제 시 패널 교차 표시 업데이트
            UpdateSnippetPanelVisibility();
        }
    }

    /// <summary>
    /// 현재 세션에 맞게 파일 트리 패널 동기화
    /// </summary>
    private async System.Threading.Tasks.Task SyncFileTreePanelAsync()
    {
        if (_viewModel.CurrentSession == null)
        {
            FileTreePanelControl.Visibility = Visibility.Collapsed;
            return;
        }

        // 파일 트리 가시성 설정
        if (_viewModel.CurrentSession is LocalTerminalViewModel localVm)
        {
            FileTreePanelControl.Visibility = localVm.IsFileTreeVisible ? Visibility.Visible : Visibility.Collapsed;

            if (localVm.IsFileTreeVisible)
            {
                // 로컬 모드로 초기화
                await FileTreePanelControl.InitializeLocalAsync(localVm.FileTreeCurrentPath ?? localVm.CurrentDirectory);
            }
        }
        else if (_viewModel.CurrentSession is ServerSessionViewModel serverVm)
        {
            FileTreePanelControl.Visibility = serverVm.IsFileTreeVisible ? Visibility.Visible : Visibility.Collapsed;

            if (serverVm.IsFileTreeVisible && serverVm.IsConnected && serverVm.ServerProfile != null)
            {
                // SSH 모드로 초기화
                var sftpService = new SftpService(serverVm.ServerProfile);
                await sftpService.ConnectAsync();
                var sftpClient = sftpService.GetSftpClient();
                if (sftpClient != null)
                {
                    await FileTreePanelControl.InitializeSshAsync(sftpClient, sftpService, serverVm.FileTreeCurrentPath ?? "/");
                }
            }
        }
        else
        {
            FileTreePanelControl.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// FileTreePanel 이벤트 연결
    /// </summary>
    private void ConnectFileTreePanelEvents()
    {
        FileTreePanelControl.CloseRequested += FileTreePanelCloseRequested;
        FileTreePanelControl.OpenInTerminalRequested += FileTreePanelOpenInTerminalRequested;
        FileTreePanelControl.FileDoubleClicked += FileTreePanelFileDoubleClicked;
        FileTreePanelControl.DirectoryChanged += FileTreePanelDirectoryChanged;
    }

    /// <summary>
    /// FileTreePanel 닫기 요청 처리
    /// </summary>
    private void FileTreePanelCloseRequested(object? sender, EventArgs e)
    {
        if (_viewModel.CurrentSession is LocalTerminalViewModel localVm)
        {
            localVm.IsFileTreeVisible = false;
        }
        else if (_viewModel.CurrentSession is ServerSessionViewModel serverVm)
        {
            serverVm.IsFileTreeVisible = false;
        }
    }

    /// <summary>
    /// FileTreePanel 터미널에서 열기 요청 처리
    /// </summary>
    private void FileTreePanelOpenInTerminalRequested(object? sender, string path)
    {
        if (path != null && _viewModel.CurrentSession != null)
        {
            _viewModel.CurrentSession.UserInput = $"cd \"{path}\"";
        }
    }

    /// <summary>
    /// FileTreePanel 파일 더블클릭 처리
    /// </summary>
    private async void FileTreePanelFileDoubleClicked(object? sender, FileTreeItem item)
    {
        if (item == null || item.IsDirectory)
            return;

        var extension = System.IO.Path.GetExtension(item.FullPath).ToLowerInvariant();

        // 로컬 세션: 뷰어에서 볼 수 있는 파일은 FileViewerPanel에 표시, 나머지는 기본 애플리케이션으로 열기
        if (_viewModel.CurrentSession is LocalTerminalViewModel localVm)
        {
            // FileViewerPanel에서 지원하는 파일 타입 확인
            if (FileViewerPanel.IsViewableFile(extension))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] Opening viewable file: {item.FullPath}");

                    // 파일 뷰어 패널 표시
                    localVm.IsFileViewerVisible = true;
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] IsFileViewerVisible set to true");

                    // MainContentControl에서 LocalTerminalView 가져오기
                    // (TabControl.ContentTemplate이 비어있으므로 ContentControl 사용)
                    LocalTerminalView? currentView = MainContentControl.Content as LocalTerminalView;

                    System.Diagnostics.Debug.WriteLine($"[MainWindow] IsSplitMode: {_viewModel.IsSplitMode}");
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] currentView: {(currentView != null ? "found" : "null")}");

                    if (currentView != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainWindow] Calling OpenFileInViewerAsync");
                        await currentView.OpenFileInViewerAsync(item.FullPath);
                        System.Diagnostics.Debug.WriteLine($"[MainWindow] OpenFileInViewerAsync completed");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[MainWindow] FileViewerPanel: currentView is null");
                        MessageBox.Show("파일 뷰어를 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] Error opening file: {ex.Message}\n{ex.StackTrace}");
                    MessageBox.Show($"파일을 열 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            // 기타 파일: 기본 애플리케이션으로 열기
            else
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = item.FullPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"파일을 열 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        // SSH 세션: SFTP로 파일 편집기 열기
        else if (_viewModel.CurrentSession is ServerSessionViewModel serverVm && serverVm.ServerProfile != null)
        {
            try
            {
                var sftpService = new SftpService(serverVm.ServerProfile);
                await sftpService.ConnectAsync();

                var editorWindow = new FileEditorWindow(sftpService, item.FullPath);
                editorWindow.Owner = this;
                editorWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파일을 열 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// FileTreePanel 디렉토리 변경 처리
    /// </summary>
    private void FileTreePanelDirectoryChanged(object? sender, string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        // ViewModel에 경로 저장 (각 탭마다 독립적으로 유지)
        if (_viewModel.CurrentSession is LocalTerminalViewModel localVm)
        {
            localVm.FileTreeCurrentPath = path;
        }
        else if (_viewModel.CurrentSession is ServerSessionViewModel serverVm)
        {
            serverVm.FileTreeCurrentPath = path;
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

        // View 업데이트
        UpdateSessionView();
    }

    /// <summary>
    /// 현재 세션의 View를 캐싱하거나 재사용하여 ContentControl에 할당
    /// </summary>
    private void UpdateSessionView()
    {
        // 주 세션 View 업데이트
        if (_viewModel.SelectedSession != null)
        {
            var primaryView = GetOrCreateView(_viewModel.SelectedSession);

            if (!_viewModel.IsSplitMode)
            {
                // 단일 모드: MainContentControl에 할당
                if (MainContentControl.Content != primaryView)
                {
                    MainContentControl.Content = primaryView;
                }
            }
            else
            {
                // 분할 모드: 방향에 따라 적절한 ContentControl에 할당
                var container = _viewModel.SplitOrientation switch
                {
                    SplitPaneContainer.SplitOrientation.Horizontal => HorizontalSplitGrid,
                    SplitPaneContainer.SplitOrientation.Vertical => VerticalSplitGrid,
                    _ => null
                };

                if (container != null)
                {
                    // 첫 번째 ContentControl 찾기
                    var contentControl = FindContentControl(container, 0);
                    if (contentControl != null && contentControl.Content != primaryView)
                    {
                        contentControl.Content = primaryView;
                    }
                }
            }
        }

        // 보조 세션 View 업데이트 (분할 모드)
        if (_viewModel.IsSplitMode && _viewModel.SecondarySession != null)
        {
            var secondaryView = GetOrCreateView(_viewModel.SecondarySession);

            var container = _viewModel.SplitOrientation switch
            {
                SplitPaneContainer.SplitOrientation.Horizontal => HorizontalSplitGrid,
                SplitPaneContainer.SplitOrientation.Vertical => VerticalSplitGrid,
                _ => null
            };

            if (container != null)
            {
                // 두 번째 ContentControl 찾기
                var contentControl = FindContentControl(container, 2);
                if (contentControl != null && contentControl.Content != secondaryView)
                {
                    contentControl.Content = secondaryView;
                }
            }
        }
    }

    /// <summary>
    /// 세션의 View를 캐시에서 가져오거나 새로 생성
    /// </summary>
    private UIElement GetOrCreateView(ISessionViewModel session)
    {
        // 캐시에 있으면 재사용
        if (_viewCache.TryGetValue(session, out var cachedView))
        {
            System.Diagnostics.Debug.WriteLine($"[ViewCache] 재사용: {session.TabHeader}");
            return cachedView;
        }

        // 없으면 새로 생성하고 캐싱
        UIElement newView = session switch
        {
            ServerSessionViewModel serverVm => new ServerSessionView { DataContext = serverVm },
            LocalTerminalViewModel localVm => new LocalTerminalView { DataContext = localVm },
            NewSessionSelectorViewModel selectorVm => new NewSessionSelectorView { DataContext = selectorVm },
            _ => throw new NotSupportedException($"지원하지 않는 세션 타입: {session.GetType().Name}")
        };

        _viewCache[session] = newView;
        System.Diagnostics.Debug.WriteLine($"[ViewCache] 새로 생성: {session.TabHeader}");

        return newView;
    }

    /// <summary>
    /// Grid에서 특정 위치(Row/Column)의 ContentControl 찾기
    /// </summary>
    private ContentControl? FindContentControl(Grid container, int position)
    {
        foreach (var child in container.Children)
        {
            if (child is ContentControl contentControl)
            {
                // Grid.Row 또는 Grid.Column 값 확인
                var row = Grid.GetRow(contentControl);
                var column = Grid.GetColumn(contentControl);

                if (row == position || column == position)
                {
                    return contentControl;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 스니펫 패널 가시성 업데이트 (로컬 터미널/SSH 서버 교차 표시)
    /// </summary>
    private void UpdateSnippetPanelVisibility()
    {
        if (_viewModel.CurrentSession is LocalTerminalViewModel localVm)
        {
            // 로컬 터미널: 스니펫 패널 표시
            SnippetPanel.Visibility = localVm.ShowSnippetPanel ? Visibility.Visible : Visibility.Collapsed;
            FrequentCommandsPanel.Visibility = Visibility.Collapsed;
        }
        else if (_viewModel.CurrentSession is ServerSessionViewModel serverVm && serverVm.IsConnected)
        {
            // SSH 서버: Frequent Commands 패널 표시
            SnippetPanel.Visibility = Visibility.Collapsed;
            FrequentCommandsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            // 연결 안됨: 둘 다 숨김
            SnippetPanel.Visibility = Visibility.Collapsed;
            FrequentCommandsPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 통계 대시보드 버튼 클릭
    /// </summary>
    private void StatisticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dashboard = new StatisticsDashboardWindow();
            dashboard.Owner = this;
            dashboard.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 통계 대시보드 열기 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// Frequent Commands 검색 버튼 클릭
    /// </summary>
    private void SearchCommands_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentSession is ServerSessionViewModel serverVm)
        {
            // 명령어 관리 창 열기
            var commandManager = new CommandManagerWindow(serverVm.FrequentCommands);
            commandManager.Owner = this;
            commandManager.ShowDialog();
        }
    }

    /// <summary>
    /// Frequent Command 편집 버튼 클릭
    /// </summary>
    private void EditFrequentCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is FrequentCommand command &&
            _viewModel.CurrentSession is ServerSessionViewModel)
        {
            // 명령어 상세 정보 표시 - ShowCommandDetailCmd 실행
            if (_viewModel.CurrentSession is ServerSessionViewModel serverVm)
            {
                serverVm.ShowCommandDetailCmd.Execute(command);
            }
        }
    }

    #region FrequentCommands 드래그앤드롭

    private FrequentCommand? _draggedCommand = null;

    /// <summary>
    /// FrequentCommands 드래그 시작
    /// </summary>
    private void FrequentCommandsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is ListBox listBox)
        {
            var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null && item.DataContext is FrequentCommand command)
            {
                _draggedCommand = command;
                DragDrop.DoDragDrop(item, command, DragDropEffects.Move);
                _draggedCommand = null;
            }
        }
    }

    /// <summary>
    /// FrequentCommands 드래그 오버
    /// </summary>
    private void FrequentCommandsList_DragOver(object sender, DragEventArgs e)
    {
        if (_draggedCommand != null)
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>
    /// FrequentCommands 드롭
    /// </summary>
    private void FrequentCommandsList_Drop(object sender, DragEventArgs e)
    {
        if (_draggedCommand == null || _viewModel.CurrentSession is not ServerSessionViewModel serverVm)
            return;

        var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (targetItem?.DataContext is FrequentCommand targetCommand)
        {
            var commands = serverVm.FrequentCommands;
            int oldIndex = commands.IndexOf(_draggedCommand);
            int newIndex = commands.IndexOf(targetCommand);

            if (oldIndex != -1 && newIndex != -1 && oldIndex != newIndex)
            {
                // 아이템 이동
                commands.Move(oldIndex, newIndex);

                // DisplayOrder 업데이트
                for (int i = 0; i < commands.Count; i++)
                {
                    commands[i].DisplayOrder = i;
                }

                // DB에 저장 (비동기)
                Task.Run(() =>
                {
                    try
                    {
                        foreach (var cmd in commands)
                        {
                            HistoryDatabaseService.Instance.UpdateFrequentCommandOrder(cmd.Command, cmd.DisplayOrder);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"FrequentCommand 정렬 저장 실패: {ex.Message}");
                    }
                });
            }
        }

        _draggedCommand = null;
    }

    /// <summary>
    /// 부모 요소 찾기 (드래그앤드롭용)
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        do
        {
            if (current is T ancestor)
                return ancestor;
            current = VisualTreeHelper.GetParent(current);
        }
        while (current != null);
        return null;
    }

    #endregion

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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // FileTreePanel 이벤트 구독 해제
        try
        {
            FileTreePanelControl.CloseRequested -= FileTreePanelCloseRequested;
            FileTreePanelControl.OpenInTerminalRequested -= FileTreePanelOpenInTerminalRequested;
            FileTreePanelControl.FileDoubleClicked -= FileTreePanelFileDoubleClicked;
            FileTreePanelControl.DirectoryChanged -= FileTreePanelDirectoryChanged;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] FileTreePanel 이벤트 해제 실패: {ex.Message}");
        }

        // CurrentSession PropertyChanged 이벤트 해제
        if (_currentTrackedSession != null && _currentTrackedSession is INotifyPropertyChanged trackedSession)
        {
            trackedSession.PropertyChanged -= CurrentSession_PropertyChanged;
        }

        // Sessions 컬렉션 변경 이벤트 해제
        if (_viewModel != null)
        {
            _viewModel.Sessions.CollectionChanged -= Sessions_CollectionChanged;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

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

    /// <summary>
    /// 창 닫기 이벤트 - UI 설정 저장
    /// </summary>
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            // 모든 로컬 터미널 세션의 UI 설정 저장
            foreach (var session in _viewModel.Sessions)
            {
                if (session is LocalTerminalViewModel localVm)
                {
                    localVm.SaveUISettings();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] UI 설정 저장 실패: {ex.Message}");
        }
    }
}
