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
                string.Format(LocalizationService.Instance.GetString("FileTransfer.SFTPConnectionFailed"), ex.Message),
                LocalizationService.Instance.GetString("FileTransfer.ConnectionError"),
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
            CountTextBlock.Text = string.Format(
                LocalizationService.Instance.GetString("FileTransfer.FileCount"),
                _currentFiles.Count,
                _currentFiles.Count(f => f.IsDirectory),
                _currentFiles.Count(f => !f.IsDirectory));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("FileTransfer.DirectoryLoadFailed"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnTransferProgress(object? sender, FileTransferProgressEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            TransferProgressBar.Value = e.ProgressPercentage;
            TransferStatusText.Text = e.IsUpload
                ? string.Format(LocalizationService.Instance.GetString("FileTransfer.Uploading"), e.FileName)
                : string.Format(LocalizationService.Instance.GetString("FileTransfer.Downloading"), e.FileName);

            var totalMB = e.TotalBytes / (1024.0 * 1024.0);
            var transferredMB = e.TransferredBytes / (1024.0 * 1024.0);
            TransferDetailsText.Text = string.Format(
                LocalizationService.Instance.GetString("FileTransfer.TransferDetails"),
                transferredMB,
                totalMB,
                e.ProgressPercentage);
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
                string.Format(LocalizationService.Instance.GetString("FileTransfer.LargeFileWarning"), file.SizeFormatted),
                LocalizationService.Instance.GetString("FileTransfer.LargeFileWarningTitle"),
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
                string.Format(LocalizationService.Instance.GetString("FileTransfer.CannotOpenEditor"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
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
                    string.Format(LocalizationService.Instance.GetString("FileTransfer.UploadComplete"), fileName),
                    LocalizationService.Instance.GetString("FileTransfer.UploadSuccess"),
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
                        TransferStatusText.Text = string.Format(LocalizationService.Instance.GetString("FileTransfer.UploadMultipleFiles"), fileNames.Length);
                        TransferDetailsText.Text = $"{percent}% " + LocalizationService.Instance.GetString("Common.Complete");
                    });
                });

                await _sftpService.UploadMultipleAsync(files, maxParallel: 4, progress);

                MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("FileTransfer.UploadMultipleComplete"), fileNames.Length),
                    LocalizationService.Instance.GetString("FileTransfer.UploadSuccess"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("FileTransfer.UploadFailed"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
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
            MessageBox.Show(
                LocalizationService.Instance.GetString("FileTransfer.SelectFilesToDownload"),
                LocalizationService.Instance.GetString("History.SelectionRequired"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
                    string.Format(LocalizationService.Instance.GetString("FileTransfer.DownloadComplete"), selected.Name),
                    LocalizationService.Instance.GetString("FileTransfer.DownloadSuccess"),
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
                        TransferStatusText.Text = string.Format(LocalizationService.Instance.GetString("FileTransfer.DownloadMultipleFiles"), selectedFiles.Count);
                        TransferDetailsText.Text = $"{percent}% " + LocalizationService.Instance.GetString("Common.Complete");
                    });
                });

                await _sftpService.DownloadMultipleAsync(files, maxParallel: 4, progress);

                MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("FileTransfer.DownloadMultipleComplete"), selectedFiles.Count, targetFolder),
                    LocalizationService.Instance.GetString("FileTransfer.DownloadSuccess"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("FileTransfer.DownloadFailed"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
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
        var dialog = new TextInputDialog(
            LocalizationService.Instance.GetString("FileTransfer.NewFolderName"),
            LocalizationService.Instance.GetString("FileTransfer.CreateFolderTitle"));
        if (dialog.ShowDialog() != true)
            return;

        var folderName = dialog.InputText.Trim();
        if (string.IsNullOrWhiteSpace(folderName))
            return;

        var remotePath = _currentPath.TrimEnd('/') + "/" + folderName;

        try
        {
            await _sftpService.CreateDirectoryAsync(remotePath);
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("FileTransfer.FolderCreated"), folderName),
                LocalizationService.Instance.GetString("Common.Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("FileTransfer.FolderCreationFailed"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilesDataGrid.SelectedItem is not RemoteFileInfo selected)
        {
            MessageBox.Show(
                LocalizationService.Instance.GetString("FileTransfer.SelectItemToRename"),
                LocalizationService.Instance.GetString("History.SelectionRequired"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new TextInputDialog(
            LocalizationService.Instance.GetString("FileTransfer.NewName"),
            LocalizationService.Instance.GetString("FileTransfer.RenameTitle"),
            selected.Name);
        if (dialog.ShowDialog() != true)
            return;

        var newName = dialog.InputText.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == selected.Name)
            return;

        var newPath = _currentPath.TrimEnd('/') + "/" + newName;

        try
        {
            await _sftpService.RenameAsync(selected.FullPath, newPath);
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("FileTransfer.Renamed"), selected.Name, newName),
                LocalizationService.Instance.GetString("Common.Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("FileTransfer.RenameFailed"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilesDataGrid.SelectedItem is not RemoteFileInfo selected)
        {
            MessageBox.Show(
                LocalizationService.Instance.GetString("FileTransfer.SelectItemToDelete"),
                LocalizationService.Instance.GetString("History.SelectionRequired"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            string.Format(LocalizationService.Instance.GetString("FileTransfer.ConfirmDelete"), selected.Name),
            LocalizationService.Instance.GetString("FileTransfer.DeleteConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _sftpService.DeleteAsync(selected.FullPath, selected.IsDirectory);
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("FileTransfer.Deleted"), selected.Name),
                LocalizationService.Instance.GetString("Common.Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("FileTransfer.DeleteFailed"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
