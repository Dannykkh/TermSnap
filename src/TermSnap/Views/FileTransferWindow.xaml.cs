using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TermSnap.Models;
using TermSnap.Services;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;  // FolderBrowserDialog용 (KeyEventArgs 충돌 방지)

namespace TermSnap.Views;

/// <summary>
/// 파일 전송 (SFTP) 창
/// </summary>
public partial class FileTransferWindow : Window
{
    private readonly SftpService _sftpService;
    private string _currentPath = "/";
    private List<RemoteFileInfo> _currentFiles = new();
    private bool _isTransferring = false;

    public bool IsTransferring
    {
        get => _isTransferring;
        set
        {
            _isTransferring = value;
            // 프로그레스 바 표시/숨김은 Visibility 바인딩으로 처리
        }
    }

    public FileTransferWindow(ServerConfig config)
    {
        InitializeComponent();

        _sftpService = new SftpService(config);
        _sftpService.TransferProgress += OnTransferProgress;

        Title = $"파일 전송 - {config.ProfileName}";

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _sftpService.ConnectAsync();
            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"SFTP 연결 실패:\n{ex.Message}",
                "연결 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private async Task LoadDirectoryAsync(string path)
    {
        try
        {
            _currentFiles = await _sftpService.ListDirectoryAsync(path);
            FilesDataGrid.ItemsSource = _currentFiles;
            _currentPath = path;
            PathTextBox.Text = _currentPath;
            CountTextBlock.Text = $"전체 {_currentFiles.Count}개 ({_currentFiles.Count(f => f.IsDirectory)}개 폴더, {_currentFiles.Count(f => !f.IsDirectory)}개 파일)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"디렉토리 로드 실패:\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnTransferProgress(object? sender, FileTransferProgressEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            TransferProgressBar.Value = e.ProgressPercentage;
            TransferStatusText.Text = e.IsUpload ? $"업로드 중: {e.FileName}" : $"다운로드 중: {e.FileName}";

            var totalMB = e.TotalBytes / (1024.0 * 1024.0);
            var transferredMB = e.TransferredBytes / (1024.0 * 1024.0);
            TransferDetailsText.Text = $"{transferredMB:F2} / {totalMB:F2} MB ({e.ProgressPercentage:F1}%)";
        });
    }

    private async void FilesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesDataGrid.SelectedItem is not RemoteFileInfo selected)
            return;

        if (selected.IsDirectory)
        {
            // 디렉토리면 이동
            await LoadDirectoryAsync(selected.FullPath);
        }
        else if (SftpService.IsTextFile(selected.Name))
        {
            // 텍스트 파일이면 편집기 열기
            OpenFileEditor(selected);
        }
    }

    /// <summary>
    /// 파일 편집기 열기
    /// </summary>
    private void OpenFileEditor(RemoteFileInfo file)
    {
        // 대용량 파일 경고 (5MB 이상)
        if (file.Size > 5 * 1024 * 1024)
        {
            var result = MessageBox.Show(
                $"파일 크기가 {file.SizeFormatted}입니다.\n대용량 파일은 편집기에서 열기가 느릴 수 있습니다.\n계속하시겠습니까?",
                "대용량 파일 경고",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        try
        {
            var editorWindow = new FileEditorWindow(_sftpService, file.FullPath);
            editorWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"파일 편집기를 열 수 없습니다.\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == "/")
            return;

        var parentPath = Path.GetDirectoryName(_currentPath.Replace('\\', '/').TrimEnd('/'));
        if (string.IsNullOrEmpty(parentPath))
            parentPath = "/";
        else
            parentPath = parentPath.Replace('\\', '/');

        await LoadDirectoryAsync(parentPath);
    }

    private async void GoButton_Click(object sender, RoutedEventArgs e)
    {
        var path = PathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return;

        await LoadDirectoryAsync(path);
    }

    private async void PathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            await LoadDirectoryAsync(PathTextBox.Text.Trim());
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadDirectoryAsync(_currentPath);
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "업로드할 파일 선택",
            Filter = "모든 파일 (*.*)|*.*",
            Multiselect = true  // ⭐ 다중 파일 선택 가능
        };

        if (dialog.ShowDialog() != true)
            return;

        var fileNames = dialog.FileNames;
        if (fileNames.Length == 0)
            return;

        try
        {
            IsTransferring = true;
            TransferProgressBar.Value = 0;

            // 단일 파일이면 기존 방식 (진행률 표시 유지)
            if (fileNames.Length == 1)
            {
                var localPath = fileNames[0];
                var fileName = Path.GetFileName(localPath);
                var remotePath = _currentPath.TrimEnd('/') + "/" + fileName;

                await _sftpService.UploadFileAsync(localPath, remotePath);

                MessageBox.Show(
                    $"파일 업로드 완료:\n{fileName}",
                    "업로드 성공",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            // 다중 파일이면 병렬 업로드 (4개씩 동시 전송)
            else
            {
                var files = fileNames.Select(localPath =>
                {
                    var fileName = Path.GetFileName(localPath);
                    var remotePath = _currentPath.TrimEnd('/') + "/" + fileName;
                    return (localPath, remotePath);
                }).ToList();

                // 전체 파일 진행률 표시
                var progress = new Progress<int>(percent =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TransferProgressBar.Value = percent;
                        TransferStatusText.Text = $"업로드 중: {fileNames.Length}개 파일";
                        TransferDetailsText.Text = $"{percent}% 완료";
                    });
                });

                await _sftpService.UploadMultipleAsync(files, maxParallel: 4, progress);

                MessageBox.Show(
                    $"파일 업로드 완료:\n{fileNames.Length}개 파일",
                    "업로드 성공",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"파일 업로드 실패:\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsTransferring = false;
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        // 선택된 파일들 가져오기 (디렉토리 제외)
        var selectedFiles = FilesDataGrid.SelectedItems
            .Cast<RemoteFileInfo>()
            .Where(f => !f.IsDirectory)
            .ToList();

        if (selectedFiles.Count == 0)
        {
            MessageBox.Show("다운로드할 파일을 선택해주세요.\n(디렉토리는 다운로드할 수 없습니다)", "선택 필요", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            IsTransferring = true;
            TransferProgressBar.Value = 0;

            // 단일 파일이면 SaveFileDialog 사용 (기존 방식)
            if (selectedFiles.Count == 1)
            {
                var selected = selectedFiles[0];
                var dialog = new SaveFileDialog
                {
                    Title = "다운로드 위치 선택",
                    FileName = selected.Name,
                    Filter = "모든 파일 (*.*)|*.*"
                };

                if (dialog.ShowDialog() != true)
                    return;

                var localPath = dialog.FileName;

                await _sftpService.DownloadFileAsync(selected.FullPath, localPath);

                MessageBox.Show(
                    $"파일 다운로드 완료:\n{selected.Name}",
                    "다운로드 성공",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            // 다중 파일이면 폴더 선택 후 병렬 다운로드
            else
            {
                using var folderDialog = new WinForms.FolderBrowserDialog
                {
                    Description = $"다운로드할 폴더 선택 ({selectedFiles.Count}개 파일)",
                    ShowNewFolderButton = true
                };

                if (folderDialog.ShowDialog() != WinForms.DialogResult.OK)
                    return;

                var targetFolder = folderDialog.SelectedPath;

                var files = selectedFiles.Select(f =>
                {
                    var localPath = Path.Combine(targetFolder, f.Name);
                    return (f.FullPath, localPath);
                }).ToList();

                // 전체 파일 진행률 표시
                var progress = new Progress<int>(percent =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TransferProgressBar.Value = percent;
                        TransferStatusText.Text = $"다운로드 중: {selectedFiles.Count}개 파일";
                        TransferDetailsText.Text = $"{percent}% 완료";
                    });
                });

                await _sftpService.DownloadMultipleAsync(files, maxParallel: 4, progress);

                MessageBox.Show(
                    $"파일 다운로드 완료:\n{selectedFiles.Count}개 파일 → {targetFolder}",
                    "다운로드 성공",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"파일 다운로드 실패:\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsTransferring = false;
        }
    }

    private async void CreateFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TextInputDialog("새 폴더 이름", "폴더 생성");
        if (dialog.ShowDialog() != true)
            return;

        var folderName = dialog.InputText.Trim();
        if (string.IsNullOrWhiteSpace(folderName))
            return;

        var remotePath = _currentPath.TrimEnd('/') + "/" + folderName;

        try
        {
            await _sftpService.CreateDirectoryAsync(remotePath);
            MessageBox.Show($"폴더가 생성되었습니다:\n{folderName}", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"폴더 생성 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilesDataGrid.SelectedItem is not RemoteFileInfo selected)
        {
            MessageBox.Show("이름을 변경할 항목을 선택해주세요.", "선택 필요", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new TextInputDialog("새 이름", "이름 변경", selected.Name);
        if (dialog.ShowDialog() != true)
            return;

        var newName = dialog.InputText.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == selected.Name)
            return;

        var newPath = _currentPath.TrimEnd('/') + "/" + newName;

        try
        {
            await _sftpService.RenameAsync(selected.FullPath, newPath);
            MessageBox.Show($"이름이 변경되었습니다:\n{selected.Name} → {newName}", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"이름 변경 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilesDataGrid.SelectedItem is not RemoteFileInfo selected)
        {
            MessageBox.Show("삭제할 항목을 선택해주세요.", "선택 필요", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"다음 항목을 삭제하시겠습니까?\n\n{selected.Name}",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _sftpService.DeleteAsync(selected.FullPath, selected.IsDirectory);
            MessageBox.Show($"삭제되었습니다:\n{selected.Name}", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"삭제 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _sftpService?.Dispose();
        base.OnClosed(e);
    }
}
