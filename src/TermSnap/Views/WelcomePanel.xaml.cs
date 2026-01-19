using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TermSnap.Models;
using TermSnap.Services;
using static TermSnap.Services.ShellDetectionService;

namespace TermSnap.Views;

/// <summary>
/// Warp 스타일 웰컴 패널 - 로컬 터미널 시작 화면
/// </summary>
public partial class WelcomePanel : UserControl
{
    /// <summary>
    /// 폴더가 선택되었을 때 발생
    /// </summary>
    public event EventHandler<string>? FolderSelected;

    /// <summary>
    /// 저장소 복제가 요청되었을 때 발생
    /// </summary>
    public event EventHandler<string>? CloneRepositoryRequested;

    /// <summary>
    /// 새 프로젝트 생성이 요청되었을 때 발생
    /// </summary>
    public event EventHandler<string>? NewProjectRequested;

    /// <summary>
    /// 쉘이 선택되었을 때 발생
    /// </summary>
    public event EventHandler<DetectedShell>? ShellSelected;

    /// <summary>
    /// Claude 명령어 실행 요청
    /// </summary>
    public event EventHandler<ClaudeRunOptions>? ClaudeRunRequested;

    /// <summary>
    /// 현재 선택된 쉘
    /// </summary>
    public DetectedShell? SelectedShell { get; private set; }

    /// <summary>
    /// 현재 선택된 AI CLI
    /// </summary>
    private AICLIInfo? _selectedAICLI;
    private List<AICLIInfo> _aiCLIList = new();

    /// <summary>
    /// AI CLI 사용 여부
    /// </summary>
    public bool UseAICLI => UseAICLICheckBox?.IsChecked == true;

    public WelcomePanel()
    {
        InitializeComponent();
        LoadInstalledShells();
        LoadRecentFolders();
        LoadAICLIList();

        // 모드 라디오 버튼 이벤트 연결
        ModeNormalRadio.Checked += (s, e) => UpdateModeDescription();
        ModeAutoRadio.Checked += (s, e) => UpdateModeDescription();
    }

    /// <summary>
    /// 설치된 쉘 목록 로드
    /// </summary>
    private void LoadInstalledShells()
    {
        var shells = ShellDetectionService.Instance.DetectInstalledShells();
        ShellList.ItemsSource = shells;

        // 기본 쉘 표시만 (이벤트 발생하지 않음)
        var defaultShell = ShellDetectionService.Instance.GetDefaultShell();
        if (defaultShell != null)
        {
            SelectedShell = defaultShell;
            SelectedShellText.Text = $"{defaultShell.DisplayName} 선택됨 (기본)";
            SelectedShellBorder.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 쉘 선택
    /// </summary>
    private void SelectShell(DetectedShell shell)
    {
        System.Diagnostics.Debug.WriteLine($"[SelectShell] {shell.DisplayName} - {shell.Path}");
        SelectedShell = shell;
        SelectedShellText.Text = $"{shell.DisplayName} 선택됨 ({shell.Path})";
        SelectedShellBorder.Visibility = Visibility.Visible;
        ShellSelected?.Invoke(this, shell);
    }

    /// <summary>
    /// 쉘 아이템 클릭
    /// </summary>
    private void ShellItem_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[ShellItem_Click] sender: {sender?.GetType().Name}");

        if (sender is Button button)
        {
            System.Diagnostics.Debug.WriteLine($"[ShellItem_Click] button.Tag: {button.Tag?.GetType().Name ?? "NULL"}");

            if (button.Tag is DetectedShell shell)
            {
                System.Diagnostics.Debug.WriteLine($"[ShellItem_Click] Selecting: {shell.DisplayName}");
                SelectShell(shell);
            }
            else if (button.DataContext is DetectedShell dcShell)
            {
                // Tag가 안되면 DataContext 사용
                System.Diagnostics.Debug.WriteLine($"[ShellItem_Click] Using DataContext: {dcShell.DisplayName}");
                SelectShell(dcShell);
            }
        }
    }

    /// <summary>
    /// 최근 폴더 목록 새로고침
    /// </summary>
    public void RefreshRecentFolders()
    {
        LoadRecentFolders();
    }

    private void LoadRecentFolders()
    {
        var config = ConfigService.Load();
        var recentFolders = config.GetRecentFolders();

        if (recentFolders.Count > 0)
        {
            RecentFoldersList.ItemsSource = recentFolders;
            RecentFoldersList.Visibility = Visibility.Visible;
            NoRecentFoldersText.Visibility = Visibility.Collapsed;
            RecentFoldersSection.Visibility = Visibility.Visible;
        }
        else
        {
            RecentFoldersList.Visibility = Visibility.Collapsed;
            NoRecentFoldersText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 폴더 열기 버튼 클릭
    /// </summary>
    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "작업할 폴더를 선택하세요",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var path = dialog.SelectedPath;
            SaveRecentFolder(path);
            FolderSelected?.Invoke(this, path);
        }
    }

    /// <summary>
    /// 저장소 복제 버튼 클릭
    /// </summary>
    private void CloneRepositoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CloneRepositoryDialog();
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() == true)
        {
            var repoUrl = dialog.RepositoryUrl;
            var targetPath = dialog.TargetPath;

            if (!string.IsNullOrWhiteSpace(repoUrl) && !string.IsNullOrWhiteSpace(targetPath))
            {
                // git clone 명령어 실행을 위해 이벤트 발생
                CloneRepositoryRequested?.Invoke(this, $"git clone \"{repoUrl}\" \"{targetPath}\"");
                
                // 해당 폴더를 최근 폴더에 저장
                SaveRecentFolder(targetPath);
            }
        }
    }

    /// <summary>
    /// 새 프로젝트 버튼 클릭
    /// </summary>
    private void NewProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "새 프로젝트를 만들 위치를 선택하세요",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            // 프로젝트 이름 입력 대화상자
            var nameDialog = new TextInputDialog("새 프로젝트", "프로젝트 이름을 입력하세요:");
            nameDialog.Owner = Window.GetWindow(this);

            if (nameDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(nameDialog.InputText))
            {
                var projectPath = System.IO.Path.Combine(dialog.SelectedPath, nameDialog.InputText);
                
                try
                {
                    System.IO.Directory.CreateDirectory(projectPath);
                    SaveRecentFolder(projectPath);
                    NewProjectRequested?.Invoke(this, projectPath);
                    FolderSelected?.Invoke(this, projectPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"프로젝트 폴더를 만들 수 없습니다: {ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    /// <summary>
    /// 최근 폴더 목록에서 선택
    /// </summary>
    private void RecentFoldersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecentFoldersList.SelectedItem is RecentFolder folder)
        {
            // 폴더가 존재하는지 확인
            if (System.IO.Directory.Exists(folder.Path))
            {
                SaveRecentFolder(folder.Path); // 최근 목록 갱신
                FolderSelected?.Invoke(this, folder.Path);
            }
            else
            {
                var result = MessageBox.Show(
                    $"폴더가 존재하지 않습니다:\n{folder.Path}\n\n목록에서 제거하시겠습니까?",
                    "폴더를 찾을 수 없음",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    RemoveRecentFolder(folder.Path);
                }
            }

            RecentFoldersList.SelectedItem = null;
        }
    }

    /// <summary>
    /// 최근 폴더 제거 버튼 클릭
    /// </summary>
    private void RemoveRecentFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string path)
        {
            e.Handled = true; // 부모 ListBoxItem의 선택 이벤트 방지
            RemoveRecentFolder(path);
        }
    }

    /// <summary>
    /// 최근 폴더 모두 지우기
    /// </summary>
    private void ClearRecentButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "최근 폴더 목록을 모두 지우시겠습니까?",
            "확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var config = ConfigService.Load();
            config.RecentFolders.Clear();
            ConfigService.Save(config);
            LoadRecentFolders();
        }
    }

    private void SaveRecentFolder(string path)
    {
        var config = ConfigService.Load();
        config.AddRecentFolder(path);
        ConfigService.Save(config);
        LoadRecentFolders();
    }

    private void RemoveRecentFolder(string path)
    {
        var config = ConfigService.Load();
        config.RemoveRecentFolder(path);
        ConfigService.Save(config);
        LoadRecentFolders();
    }

    /// <summary>
    /// AI CLI 목록 로드
    /// </summary>
    private void LoadAICLIList()
    {
        _aiCLIList = AICLISettings.GetAvailableCLIs();
        AICLICombo.ItemsSource = _aiCLIList;

        // 저장된 설정에서 마지막 선택한 CLI 로드
        var config = ConfigService.Load();
        var lastCLI = _aiCLIList.FirstOrDefault(c => c.Id == config.AICLISettings.SelectedCLI);

        if (lastCLI != null)
        {
            AICLICombo.SelectedItem = lastCLI;
        }
        else
        {
            AICLICombo.SelectedIndex = 0; // 기본: Claude
        }

        // 체크박스 상태 복원
        UseAICLICheckBox.IsChecked = config.AICLISettings.IsExpanded;
        AICLIOptionsPanel.Visibility = config.AICLISettings.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// AI CLI 사용 체크박스 변경
    /// </summary>
    private void UseAICLICheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var isChecked = UseAICLICheckBox.IsChecked == true;
        AICLIOptionsPanel.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;

        var config = ConfigService.Load();
        config.AICLISettings.IsExpanded = isChecked;
        ConfigService.Save(config);
    }

    /// <summary>
    /// AI CLI 옵션 반환 (폴더 열기 시 사용)
    /// </summary>
    public ClaudeRunOptions? GetAICLIOptions()
    {
        if (!UseAICLI || _selectedAICLI == null) return null;

        // CLI가 설치되어 있지 않으면 null 반환
        if (_selectedAICLI.Id != "custom" && !CheckCLIInstalled(_selectedAICLI.Command))
        {
            return null;
        }

        var command = _selectedAICLI.Id == "custom"
            ? ConfigService.Load().AICLISettings.CustomCommand
            : _selectedAICLI.Command;

        if (string.IsNullOrEmpty(command)) return null;

        // 자동 모드 플래그 추가
        var isAutoMode = ModeAutoRadio.IsChecked == true;
        if (isAutoMode && !string.IsNullOrEmpty(_selectedAICLI.AutoModeFlag))
        {
            command += " " + _selectedAICLI.AutoModeFlag;
        }

        return new ClaudeRunOptions
        {
            Command = command,
            AutoMode = isAutoMode
        };
    }

    /// <summary>
    /// AI CLI 선택 변경
    /// </summary>
    private void AICLICombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AICLICombo.SelectedItem is AICLIInfo cli)
        {
            _selectedAICLI = cli;
            UpdateAICLIButtons(cli);

            // 설정 저장
            var config = ConfigService.Load();
            config.AICLISettings.SelectedCLI = cli.Id;
            ConfigService.Save(config);
        }
    }

    /// <summary>
    /// AI CLI UI 업데이트
    /// </summary>
    private void UpdateAICLIButtons(AICLIInfo cli)
    {
        // 자동 모드 지원 여부에 따라 라디오 버튼 활성화
        if (!string.IsNullOrEmpty(cli.AutoModeFlag))
        {
            ModeAutoRadio.IsEnabled = true;
            ModeAutoRadio.Content = $"자동 모드 ({cli.AutoModeFlag})";
        }
        else
        {
            ModeAutoRadio.IsEnabled = false;
            ModeAutoRadio.Content = "자동 모드 (미지원)";
            ModeNormalRadio.IsChecked = true;
        }

        // 설치 상태 업데이트
        UpdateInstallStatus(cli);

        // 모드 설명 및 명령어 미리보기 업데이트
        UpdateModeDescription();
        UpdateCommandPreview();
    }

    /// <summary>
    /// 모드 설명 업데이트
    /// </summary>
    private void UpdateModeDescription()
    {
        if (ModeAutoRadio.IsChecked == true)
        {
            ModeDescText.Text = "권한 확인 없이 자동으로 작업을 수행합니다 (주의 필요)";
        }
        else
        {
            ModeDescText.Text = "각 작업마다 권한 확인이 필요합니다 (안전)";
        }
        UpdateCommandPreview();
    }

    /// <summary>
    /// 명령어 미리보기 업데이트 (현재 미사용)
    /// </summary>
    private void UpdateCommandPreview()
    {
        // XAML에서 명령어 미리보기 제거됨
    }

    /// <summary>
    /// CLI 설치 상태 업데이트
    /// </summary>
    private void UpdateInstallStatus(AICLIInfo cli)
    {
        // 커스텀은 설치 상태 표시 안함
        if (cli.Id == "custom")
        {
            InstallStatusBorder.Visibility = Visibility.Collapsed;
            return;
        }

        InstallStatusBorder.Visibility = Visibility.Visible;
        InstallDescText.Text = cli.InstallDescription;

        // 웹사이트 버튼
        WebsiteButton.Visibility = !string.IsNullOrEmpty(cli.WebsiteUrl)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // 설치 여부 체크
        var isInstalled = CheckCLIInstalled(cli.Command);

        if (isInstalled)
        {
            InstalledPanel.Visibility = Visibility.Visible;
            NotInstalledPanel.Visibility = Visibility.Collapsed;
            InstallButton.Visibility = Visibility.Collapsed;
            InstallStatusBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");

            // 버전 정보 표시 시도
            var version = GetCLIVersion(cli.Command);
            InstalledVersionText.Text = !string.IsNullOrEmpty(version)
                ? $"설치됨 ({version})"
                : "설치됨";
        }
        else
        {
            InstalledPanel.Visibility = Visibility.Collapsed;
            NotInstalledPanel.Visibility = Visibility.Visible;
            InstallButton.Visibility = !string.IsNullOrEmpty(cli.InstallCommand)
                ? Visibility.Visible
                : Visibility.Collapsed;
            InstallStatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0x98, 0x00));
        }
    }

    /// <summary>
    /// CLI 설치 여부 체크
    /// </summary>
    private bool CheckCLIInstalled(string command)
    {
        if (string.IsNullOrEmpty(command)) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// CLI 버전 정보 가져오기
    /// </summary>
    private string? GetCLIVersion(string command)
    {
        if (string.IsNullOrEmpty(command)) return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                // 첫 줄에서 버전 번호 추출
                var firstLine = output.Split('\n').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(firstLine))
                {
                    // 버전 번호만 추출 (예: "v1.0.0" 또는 "1.0.0")
                    var versionMatch = System.Text.RegularExpressions.Regex.Match(
                        firstLine, @"v?(\d+\.\d+(\.\d+)?)");
                    if (versionMatch.Success)
                    {
                        return "v" + versionMatch.Groups[1].Value;
                    }
                    return firstLine.Length > 20 ? firstLine.Substring(0, 20) + "..." : firstLine;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 설치 버튼 클릭 - 터미널에서 설치 명령어 실행
    /// </summary>
    private void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAICLI == null || string.IsNullOrEmpty(_selectedAICLI.InstallCommand)) return;

        var result = MessageBox.Show(
            $"{_selectedAICLI.Name}을(를) 설치하시겠습니까?\n\n" +
            $"실행할 명령어:\n{_selectedAICLI.InstallCommand}\n\n" +
            $"요구사항: {_selectedAICLI.InstallDescription}",
            "AI CLI 설치",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            // 설치 명령어 실행 이벤트 발생
            ClaudeRunRequested?.Invoke(this, new ClaudeRunOptions
            {
                Command = _selectedAICLI.InstallCommand,
                AutoMode = false
            });
        }
    }

    /// <summary>
    /// 웹사이트 버튼 클릭
    /// </summary>
    private void WebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAICLI == null || string.IsNullOrEmpty(_selectedAICLI.WebsiteUrl)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _selectedAICLI.WebsiteUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"웹사이트를 열 수 없습니다: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 설치 상태 새로고침
    /// </summary>
    public void RefreshInstallStatus()
    {
        if (_selectedAICLI != null)
        {
            UpdateInstallStatus(_selectedAICLI);
        }
    }

    /// <summary>
    /// AI CLI 고급 옵션 다이얼로그 열기
    /// </summary>
    private void RunAICLIAdvancedButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AICLIOptionsDialog();
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() == true)
        {
            ClaudeRunRequested?.Invoke(this, new ClaudeRunOptions
            {
                Command = dialog.GeneratedCommand,
                AutoMode = dialog.IsAutoMode,
                InitialPrompt = dialog.InitialPrompt
            });

            // 콤보박스 업데이트 (다이얼로그에서 변경된 경우)
            if (dialog.SelectedCLI != null)
            {
                var cli = _aiCLIList.FirstOrDefault(c => c.Id == dialog.SelectedCLI.Id);
                if (cli != null && AICLICombo.SelectedItem != cli)
                {
                    AICLICombo.SelectedItem = cli;
                }
            }
        }
    }

}

/// <summary>
/// Claude 실행 옵션
/// </summary>
public class ClaudeRunOptions
{
    /// <summary>
    /// 실행할 명령어
    /// </summary>
    public string Command { get; set; } = "claude";

    /// <summary>
    /// 자동 모드 여부 (--dangerously-skip-permissions)
    /// </summary>
    public bool AutoMode { get; set; } = false;

    /// <summary>
    /// 초기 프롬프트 (실행 후 바로 전송)
    /// </summary>
    public string? InitialPrompt { get; set; }

    /// <summary>
    /// 작업 폴더 경로
    /// </summary>
    public string? WorkingFolder { get; set; }
}
