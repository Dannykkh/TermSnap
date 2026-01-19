using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Nebula.Models;
using Nebula.Services;
using Renci.SshNet;

namespace Nebula.Views;

/// <summary>
/// Warp/IDE 스타일 파일 트리 패널
/// </summary>
public partial class FileTreePanel : UserControl, INotifyPropertyChanged
{
    private FileTreeService? _fileTreeService;
    private ObservableCollection<FileTreeItem> _rootItems = new();
    private string _currentPath = string.Empty;
    private bool _isLoading;
    private bool _isLocal = true;
    private SftpClient? _sftpClient;
    private SftpService? _sftpService;

    // 클립보드 (복사/잘라내기)
    private FileTreeItem? _clipboardItem;
    private bool _isCutOperation; // true = 잘라내기, false = 복사

    // FileSystemWatcher for auto-refresh
    private FileSystemWatcher? _fileWatcher;
    private readonly DispatcherTimer _watcherDebounceTimer;
    private bool _pendingRefresh = false;

    /// <summary>
    /// 루트 아이템 목록
    /// </summary>
    public ObservableCollection<FileTreeItem> RootItems
    {
        get => _rootItems;
        set { _rootItems = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 현재 경로
    /// </summary>
    public string CurrentPath
    {
        get => _currentPath;
        set 
        { 
            _currentPath = value; 
            OnPropertyChanged();
            PathTextBox.Text = value;
        }
    }

    /// <summary>
    /// 로딩 중 여부
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 파일 선택 이벤트
    /// </summary>
    public event EventHandler<FileTreeItem>? FileSelected;

    /// <summary>
    /// 파일 더블클릭 이벤트
    /// </summary>
    public event EventHandler<FileTreeItem>? FileDoubleClicked;

    /// <summary>
    /// 디렉토리 변경 이벤트
    /// </summary>
    public event EventHandler<string>? DirectoryChanged;

    /// <summary>
    /// 패널 닫기 요청 이벤트
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 터미널에서 열기 요청 이벤트
    /// </summary>
    public event EventHandler<string>? OpenInTerminalRequested;

    public FileTreePanel()
    {
        InitializeComponent();
        DataContext = this;
        FileTree.ItemsSource = RootItems;

        // FileSystemWatcher 디바운스 타이머 초기화
        _watcherDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _watcherDebounceTimer.Tick += WatcherDebounceTimer_Tick;

        // Unloaded 시 정리
        Unloaded += FileTreePanel_Unloaded;
    }

    private void FileTreePanel_Unloaded(object sender, RoutedEventArgs e)
    {
        DisposeFileWatcher();
        _watcherDebounceTimer.Stop();
    }

    /// <summary>
    /// 로컬 파일 시스템으로 초기화
    /// </summary>
    public async Task InitializeLocalAsync(string? startPath = null)
    {
        _isLocal = true;
        _sftpClient = null;
        _fileTreeService = new FileTreeService();

        var path = startPath ?? _fileTreeService.GetHomeDirectory();
        await NavigateToAsync(path);
    }

    /// <summary>
    /// SSH(SFTP)로 초기화
    /// </summary>
    public async Task InitializeSshAsync(SftpClient sftpClient, SftpService? sftpService = null, string? startPath = null)
    {
        // SSH 모드로 전환 시 FileSystemWatcher 정리
        DisposeFileWatcher();

        _isLocal = false;
        _sftpClient = sftpClient;
        _sftpService = sftpService;
        _fileTreeService = new FileTreeService(sftpClient);

        var path = startPath ?? _fileTreeService.GetHomeDirectory();
        await NavigateToAsync(path);
    }

    /// <summary>
    /// 특정 경로로 이동
    /// </summary>
    public async Task NavigateToAsync(string path)
    {
        if (_fileTreeService == null) return;

        IsLoading = true;
        CurrentPath = path;

        try
        {
            RootItems.Clear();

            var items = await _fileTreeService.GetDirectoryContentsAsync(path);
            foreach (var item in items)
            {
                item.Parent = null;
                RootItems.Add(item);
            }

            // 로컬 모드일 때 FileSystemWatcher 설정
            if (_isLocal)
            {
                SetupFileWatcher(path);
            }

            DirectoryChanged?.Invoke(this, path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"디렉토리를 열 수 없습니다: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 새로고침
    /// </summary>
    public async Task RefreshAsync()
    {
        if (!string.IsNullOrEmpty(CurrentPath))
        {
            await NavigateToAsync(CurrentPath);
        }
    }

    #region FileSystemWatcher

    /// <summary>
    /// 파일 워처 활성화 (탭 활성화 시 호출)
    /// </summary>
    public void EnableFileWatcher()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>
    /// 파일 워처 비활성화 (탭 비활성화 시 호출)
    /// </summary>
    public void DisableFileWatcher()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
        }
    }

    /// <summary>
    /// FileSystemWatcher 설정 (로컬 전용)
    /// </summary>
    private void SetupFileWatcher(string path)
    {
        // 기존 watcher 정리
        DisposeFileWatcher();

        // 로컬 모드가 아니면 설정하지 않음
        if (!_isLocal) return;

        // 경로가 유효하지 않으면 설정하지 않음
        if (!Directory.Exists(path)) return;

        try
        {
            _fileWatcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _fileWatcher.Created += FileWatcher_Changed;
            _fileWatcher.Deleted += FileWatcher_Changed;
            _fileWatcher.Renamed += FileWatcher_Changed;
            _fileWatcher.Changed += FileWatcher_Changed;
            _fileWatcher.Error += FileWatcher_Error;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FileSystemWatcher 설정 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// FileSystemWatcher 정리
    /// </summary>
    private void DisposeFileWatcher()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Created -= FileWatcher_Changed;
            _fileWatcher.Deleted -= FileWatcher_Changed;
            _fileWatcher.Renamed -= FileWatcher_Changed;
            _fileWatcher.Changed -= FileWatcher_Changed;
            _fileWatcher.Error -= FileWatcher_Error;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }
    }

    /// <summary>
    /// 파일 시스템 변경 이벤트 핸들러
    /// </summary>
    private void FileWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        // UI 스레드에서 디바운스 타이머 시작
        Dispatcher.BeginInvoke(() =>
        {
            _pendingRefresh = true;
            _watcherDebounceTimer.Stop();
            _watcherDebounceTimer.Start();
        });
    }

    /// <summary>
    /// FileSystemWatcher 오류 핸들러
    /// </summary>
    private void FileWatcher_Error(object sender, ErrorEventArgs e)
    {
        Debug.WriteLine($"FileSystemWatcher 오류: {e.GetException().Message}");

        // 오류 발생 시 watcher 재설정 시도
        Dispatcher.BeginInvoke(() =>
        {
            if (!string.IsNullOrEmpty(CurrentPath))
            {
                SetupFileWatcher(CurrentPath);
            }
        });
    }

    /// <summary>
    /// 디바운스 타이머 틱 - 실제 새로고침 수행
    /// </summary>
    private async void WatcherDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _watcherDebounceTimer.Stop();

        if (_pendingRefresh)
        {
            _pendingRefresh = false;
            await RefreshAsync();
        }
    }

    #endregion

    /// <summary>
    /// 트리 노드 확장 시 자식 로드 (Lazy Loading)
    /// </summary>
    private async void FileTree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem treeViewItem &&
            treeViewItem.DataContext is FileTreeItem item &&
            item.IsDirectory &&
            _fileTreeService != null)
        {
            // 플레이스홀더가 있는 경우에만 로드
            if (item.Children.Count == 1 && item.Children[0].IsPlaceholder)
            {
                item.IsLoading = true;
                item.ClearChildren();

                try
                {
                    var children = await _fileTreeService.GetDirectoryContentsAsync(item.FullPath);
                    foreach (var child in children)
                    {
                        child.Parent = item;
                        item.Children.Add(child);
                    }

                    // 빈 폴더인 경우
                    if (item.Children.Count == 0)
                    {
                        item.Children.Add(new FileTreeItem { Name = "(빈 폴더)", IsPlaceholder = true });
                    }
                }
                catch (Exception ex)
                {
                    item.Children.Add(new FileTreeItem { Name = $"오류: {ex.Message}", IsPlaceholder = true });
                }
                finally
                {
                    item.IsLoading = false;
                }
            }
        }
    }

    /// <summary>
    /// 아이템 선택 변경
    /// </summary>
    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileTreeItem item)
        {
            FileSelected?.Invoke(this, item);
        }
    }

    /// <summary>
    /// 더블 클릭
    /// </summary>
    private async void FileTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileTree.SelectedItem is FileTreeItem item)
        {
            if (item.IsDirectory)
            {
                // 디렉토리 - 해당 경로로 이동
                await NavigateToAsync(item.FullPath);
            }
            else
            {
                // 파일 - 이벤트 발생
                FileDoubleClicked?.Invoke(this, item);
            }
        }
    }

    /// <summary>
    /// 우클릭 시 아이템 선택
    /// </summary>
    private void FileTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (treeViewItem != null)
        {
            treeViewItem.IsSelected = true;
            e.Handled = true;
        }
    }

    /// <summary>
    /// 키보드 단축키 처리
    /// </summary>
    private void FileTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (FileTree.SelectedItem is not FileTreeItem item) return;

        // F2: 이름 변경
        if (e.Key == Key.F2)
        {
            ContextMenu_Rename_Click(sender, e);
            e.Handled = true;
        }
        // Delete: 삭제
        else if (e.Key == Key.Delete)
        {
            ContextMenu_Delete_Click(sender, e);
            e.Handled = true;
        }
        // Ctrl+C: 복사
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ContextMenu_Copy_Click(sender, e);
            e.Handled = true;
        }
        // Ctrl+X: 잘라내기
        else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ContextMenu_Cut_Click(sender, e);
            e.Handled = true;
        }
        // Ctrl+V: 붙여넣기
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ContextMenu_Paste_Click(sender, e);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 상위 폴더로 이동
    /// </summary>
    private async void GoUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;

        string parentPath;
        if (_isLocal)
        {
            var dirInfo = Directory.GetParent(CurrentPath);
            parentPath = dirInfo?.FullName ?? CurrentPath;
        }
        else
        {
            // Linux 경로
            var lastSlash = CurrentPath.TrimEnd('/').LastIndexOf('/');
            parentPath = lastSlash > 0 ? CurrentPath.Substring(0, lastSlash) : "/";
        }

        await NavigateToAsync(parentPath);
    }

    /// <summary>
    /// 새로고침 버튼
    /// </summary>
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    /// <summary>
    /// 숨김 파일 토글
    /// </summary>
    private async void ShowHiddenToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_fileTreeService != null)
        {
            _fileTreeService.ShowHiddenFiles = ShowHiddenToggle.IsChecked == true;
            await RefreshAsync();
        }
    }

    /// <summary>
    /// 패널 닫기
    /// </summary>
    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 경로 직접 입력
    /// </summary>
    private async void PathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var path = PathTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(path))
            {
                await NavigateToAsync(path);
            }
        }
    }

    #region 컨텍스트 메뉴 핸들러

    private void ContextMenu_Open_Click(object sender, RoutedEventArgs e)
    {
        if (FileTree.SelectedItem is FileTreeItem item)
        {
            if (item.IsDirectory)
            {
                _ = NavigateToAsync(item.FullPath);
            }
            else
            {
                FileDoubleClicked?.Invoke(this, item);
            }
        }
    }

    private void ContextMenu_Edit_Click(object sender, RoutedEventArgs e)
    {
        if (FileTree.SelectedItem is FileTreeItem item && !item.IsDirectory)
        {
            // 파일 편집기 열기 (FileEditorWindow 사용)
            try
            {
                if (_isLocal)
                {
                    // 로컬 파일 - 기본 편집기로 열기
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.FullPath,
                        UseShellExecute = true
                    });
                }
                else if (_sftpService != null)
                {
                    // 원격 파일 - FileEditorWindow 열기
                    var editorWindow = new FileEditorWindow(_sftpService, item.FullPath);
                    editorWindow.Owner = Window.GetWindow(this);
                    editorWindow.Show();
                }
                else
                {
                    MessageBox.Show("SFTP 서비스가 연결되지 않았습니다.", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파일을 열 수 없습니다: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ContextMenu_OpenInTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (FileTree.SelectedItem is FileTreeItem item)
        {
            var path = item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath);
            if (!string.IsNullOrEmpty(path))
            {
                OpenInTerminalRequested?.Invoke(this, path);
            }
        }
    }

    private async void ContextMenu_NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TextInputDialog("새 폴더", "폴더 이름을 입력하세요:");
        dialog.Owner = Window.GetWindow(this);
        
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.InputText))
        {
            var newPath = _isLocal
                ? Path.Combine(CurrentPath, dialog.InputText)
                : CurrentPath.TrimEnd('/') + "/" + dialog.InputText;

            try
            {
                if (_isLocal)
                {
                    Directory.CreateDirectory(newPath);
                }
                else if (_sftpClient != null)
                {
                    _sftpClient.CreateDirectory(newPath);
                }
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"폴더 생성 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void ContextMenu_NewFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TextInputDialog("새 파일", "파일 이름을 입력하세요:");
        dialog.Owner = Window.GetWindow(this);
        
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.InputText))
        {
            var newPath = _isLocal
                ? Path.Combine(CurrentPath, dialog.InputText)
                : CurrentPath.TrimEnd('/') + "/" + dialog.InputText;

            try
            {
                if (_isLocal)
                {
                    File.Create(newPath).Dispose();
                }
                else if (_sftpClient != null)
                {
                    _sftpClient.Create(newPath).Dispose();
                }
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파일 생성 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void ContextMenu_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (FileTree.SelectedItem is not FileTreeItem item) return;

        var dialog = new TextInputDialog("이름 변경", "새 이름을 입력하세요:", item.Name);
        dialog.Owner = Window.GetWindow(this);
        
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.InputText))
        {
            var parentPath = _isLocal
                ? Path.GetDirectoryName(item.FullPath)
                : item.FullPath.Substring(0, item.FullPath.LastIndexOf('/'));

            var newPath = _isLocal
                ? Path.Combine(parentPath!, dialog.InputText)
                : parentPath + "/" + dialog.InputText;

            try
            {
                if (_isLocal)
                {
                    if (item.IsDirectory)
                        Directory.Move(item.FullPath, newPath);
                    else
                        File.Move(item.FullPath, newPath);
                }
                else if (_sftpClient != null)
                {
                    _sftpClient.RenameFile(item.FullPath, newPath);
                }
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"이름 변경 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void ContextMenu_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (FileTree.SelectedItem is not FileTreeItem item) return;

        var result = MessageBox.Show(
            $"'{item.Name}'을(를) 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            if (_isLocal)
            {
                if (item.IsDirectory)
                    Directory.Delete(item.FullPath, true);
                else
                    File.Delete(item.FullPath);
            }
            else if (_sftpClient != null)
            {
                if (item.IsDirectory)
                    DeleteDirectoryRecursive(item.FullPath);
                else
                    _sftpClient.DeleteFile(item.FullPath);
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"삭제 실패: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteDirectoryRecursive(string path)
    {
        if (_sftpClient == null) return;

        foreach (var file in _sftpClient.ListDirectory(path))
        {
            if (file.Name == "." || file.Name == "..") continue;

            var fullPath = path.TrimEnd('/') + "/" + file.Name;
            if (file.IsDirectory)
                DeleteDirectoryRecursive(fullPath);
            else
                _sftpClient.DeleteFile(fullPath);
        }
        _sftpClient.DeleteDirectory(path);
    }

    private void ContextMenu_CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (FileTree.SelectedItem is FileTreeItem item)
        {
            Clipboard.SetText(item.FullPath);
        }
    }

    /// <summary>
    /// 파일/폴더 복사 (Ctrl+C)
    /// </summary>
    private void ContextMenu_Copy_Click(object sender, RoutedEventArgs e)
    {
        if (FileTree.SelectedItem is FileTreeItem item)
        {
            _clipboardItem = item;
            _isCutOperation = false;
        }
    }

    /// <summary>
    /// 파일/폴더 잘라내기 (Ctrl+X)
    /// </summary>
    private void ContextMenu_Cut_Click(object sender, RoutedEventArgs e)
    {
        if (FileTree.SelectedItem is FileTreeItem item)
        {
            _clipboardItem = item;
            _isCutOperation = true;
        }
    }

    /// <summary>
    /// 파일/폴더 붙여넣기 (Ctrl+V)
    /// </summary>
    private async void ContextMenu_Paste_Click(object sender, RoutedEventArgs e)
    {
        if (_clipboardItem == null) return;

        var targetPath = CurrentPath;
        var sourcePath = _clipboardItem.FullPath;
        var fileName = _isLocal
            ? Path.GetFileName(sourcePath)
            : sourcePath.Substring(sourcePath.LastIndexOf('/') + 1);

        var destinationPath = _isLocal
            ? Path.Combine(targetPath, fileName)
            : targetPath.TrimEnd('/') + "/" + fileName;

        try
        {
            if (_isLocal)
            {
                // 로컬 복사/이동
                if (_clipboardItem.IsDirectory)
                {
                    if (_isCutOperation)
                        Directory.Move(sourcePath, destinationPath);
                    else
                        CopyDirectory(sourcePath, destinationPath);
                }
                else
                {
                    if (_isCutOperation)
                        File.Move(sourcePath, destinationPath);
                    else
                        File.Copy(sourcePath, destinationPath);
                }
            }
            else if (_sftpClient != null)
            {
                // 원격 복사/이동
                if (_clipboardItem.IsDirectory)
                {
                    if (_isCutOperation)
                    {
                        // SFTP에는 Move가 없으므로 Rename 사용
                        _sftpClient.RenameFile(sourcePath, destinationPath);
                    }
                    else
                    {
                        // 디렉토리 복사 (재귀적)
                        CopyDirectoryRemote(sourcePath, destinationPath);
                    }
                }
                else
                {
                    if (_isCutOperation)
                    {
                        _sftpClient.RenameFile(sourcePath, destinationPath);
                    }
                    else
                    {
                        // 파일 복사
                        using var sourceStream = _sftpClient.OpenRead(sourcePath);
                        using var destStream = _sftpClient.Create(destinationPath);
                        sourceStream.CopyTo(destStream);
                    }
                }
            }

            // 잘라내기 작업 후 클립보드 비우기
            if (_isCutOperation)
            {
                _clipboardItem = null;
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"붙여넣기 실패: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 로컬 디렉토리 복사 (재귀적)
    /// </summary>
    private void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            var destSubDir = Path.Combine(destDir, dirName);
            CopyDirectory(dir, destSubDir);
        }
    }

    /// <summary>
    /// 원격 디렉토리 복사 (재귀적)
    /// </summary>
    private void CopyDirectoryRemote(string sourceDir, string destDir)
    {
        if (_sftpClient == null) return;

        _sftpClient.CreateDirectory(destDir);

        foreach (var file in _sftpClient.ListDirectory(sourceDir))
        {
            if (file.Name == "." || file.Name == "..") continue;

            var sourcePath = sourceDir.TrimEnd('/') + "/" + file.Name;
            var destPath = destDir.TrimEnd('/') + "/" + file.Name;

            if (file.IsDirectory)
            {
                CopyDirectoryRemote(sourcePath, destPath);
            }
            else
            {
                using var sourceStream = _sftpClient.OpenRead(sourcePath);
                using var destStream = _sftpClient.Create(destPath);
                sourceStream.CopyTo(destStream);
            }
        }
    }

    #endregion

    #region Helper Methods

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T ancestor)
                return ancestor;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region Resize Grip

    private bool _isResizing = false;
    private double _resizeStartX;
    private double _resizeStartWidth;

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isResizing = true;
        _resizeStartX = PointToScreen(e.GetPosition(this)).X;
        _resizeStartWidth = this.ActualWidth;
        ResizeGrip.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            ResizeGrip.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isResizing)
        {
            var currentX = PointToScreen(e.GetPosition(this)).X;
            var delta = currentX - _resizeStartX;
            var newWidth = _resizeStartWidth + delta;

            // 최소/최대 너비 제한
            if (newWidth >= 150 && newWidth <= 600)
            {
                this.Width = newWidth;
            }

            e.Handled = true;
        }
    }

    #endregion
}
