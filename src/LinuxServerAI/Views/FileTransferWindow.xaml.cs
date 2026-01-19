using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Nebula.Models;
using Nebula.Services;
using Microsoft.Win32;

namespace Nebula.Views;

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
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        var localPath = dialog.FileName;
        var fileName = Path.GetFileName(localPath);
        var remotePath = _currentPath.TrimEnd('/') + "/" + fileName;

        try
        {
            IsTransferring = true;
            TransferProgressBar.Value = 0;

            await _sftpService.UploadFileAsync(localPath, remotePath);

            MessageBox.Show(
                $"파일 업로드 완료:\n{fileName}",
                "업로드 성공",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

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
        if (FilesDataGrid.SelectedItem is not RemoteFileInfo selected)
        {
            MessageBox.Show("다운로드할 파일을 선택해주세요.", "선택 필요", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.IsDirectory)
        {
            MessageBox.Show("디렉토리는 다운로드할 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "다운로드 위치 선택",
            FileName = selected.Name,
            Filter = "모든 파일 (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        var localPath = dialog.FileName;

        try
        {
            IsTransferring = true;
            TransferProgressBar.Value = 0;

            await _sftpService.DownloadFileAsync(selected.FullPath, localPath);

            MessageBox.Show(
                $"파일 다운로드 완료:\n{selected.Name}",
                "다운로드 성공",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
