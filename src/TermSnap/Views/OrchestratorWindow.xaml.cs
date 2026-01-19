using System;
using System.Windows;
using System.Windows.Forms;
using TermSnap.Services;
using MessageBox = System.Windows.MessageBox;

namespace TermSnap.Views;

/// <summary>
/// Claude Code Orchestrator 제어 창
/// </summary>
public partial class OrchestratorWindow : Window
{
    private readonly OrchestratorService _orchestratorService;
    private string _currentProjectPath = "";

    public OrchestratorWindow()
    {
        InitializeComponent();
        _orchestratorService = new OrchestratorService();
        _orchestratorService.StateChanged += OnStateChanged;

        // MCP 서버 빌드 상태 확인
        UpdateMcpBuildStatus();
    }

    private void UpdateMcpBuildStatus()
    {
        if (_orchestratorService.IsMcpServerBuilt)
        {
            StatusText.Text = "MCP 서버 준비됨";
            BuildMcpButton.IsEnabled = true;
            LaunchButton.IsEnabled = true;
        }
        else
        {
            StatusText.Text = "MCP 서버 빌드 필요";
            LaunchButton.IsEnabled = false;
        }
    }

    private void OnStateChanged(object? sender, OrchestratorState state)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateUI(state);
        });
    }

    private void UpdateUI(OrchestratorState state)
    {
        // 태스크 목록
        TaskListView.ItemsSource = state.Tasks;

        // 워커 목록
        WorkerListView.ItemsSource = state.Workers;

        // 파일 락 목록
        LockListView.ItemsSource = state.FileLocks;

        // 진행률
        var progress = _orchestratorService.GetProgress(state);
        ProgressBar.Value = progress.PercentComplete;
        ProgressText.Text = $"{progress.PercentComplete}%";

        // 통계
        StatsText.Text = $"완료: {progress.Completed} | 진행: {progress.InProgress} | 대기: {progress.Pending} | 실패: {progress.Failed}";

        // 상태 텍스트
        if (progress.Total == 0)
        {
            StatusText.Text = "태스크 없음";
        }
        else if (progress.Completed == progress.Total)
        {
            StatusText.Text = "모든 태스크 완료!";
        }
        else
        {
            StatusText.Text = $"진행 중... ({progress.InProgress}개 작업 중)";
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "프로젝트 폴더 선택",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ProjectPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void LoadStateButton_Click(object sender, RoutedEventArgs e)
    {
        var projectPath = ProjectPathTextBox.Text.Trim();
        if (string.IsNullOrEmpty(projectPath))
        {
            MessageBox.Show("프로젝트 경로를 입력하세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!System.IO.Directory.Exists(projectPath))
        {
            MessageBox.Show("프로젝트 폴더가 존재하지 않습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _currentProjectPath = projectPath;
        var state = _orchestratorService.GetState(projectPath);

        if (state != null)
        {
            UpdateUI(state);
            _orchestratorService.StartWatching(projectPath);
            StatusText.Text = "상태 로드됨 - 자동 갱신 중";
        }
        else
        {
            StatusText.Text = "오케스트레이터 상태 없음 (실행 필요)";
            TaskListView.ItemsSource = null;
            WorkerListView.ItemsSource = null;
            LockListView.ItemsSource = null;
            ProgressBar.Value = 0;
            ProgressText.Text = "0%";
            StatsText.Text = "";
        }
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var projectPath = ProjectPathTextBox.Text.Trim();
        if (string.IsNullOrEmpty(projectPath))
        {
            MessageBox.Show("프로젝트 경로를 입력하세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!System.IO.Directory.Exists(projectPath))
        {
            MessageBox.Show("프로젝트 폴더가 존재하지 않습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Worker 수 가져오기
        var workerCount = WorkerCountComboBox.SelectedIndex + 1;
        var cleanStart = CleanStartCheckBox.IsChecked == true;

        LaunchButton.IsEnabled = false;
        StatusText.Text = "오케스트레이터 실행 중...";

        try
        {
            var (success, output) = await _orchestratorService.LaunchOrchestratorAsync(
                projectPath, workerCount, cleanStart);

            if (success)
            {
                _currentProjectPath = projectPath;
                StatusText.Text = "오케스트레이터 실행됨";

                // 잠시 후 상태 로드 시작
                await System.Threading.Tasks.Task.Delay(2000);
                LoadStateButton_Click(sender, e);
            }
            else
            {
                MessageBox.Show($"실행 실패: {output}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "실행 실패";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "오류 발생";
        }
        finally
        {
            LaunchButton.IsEnabled = true;
        }
    }

    private async void BuildMcpButton_Click(object sender, RoutedEventArgs e)
    {
        BuildMcpButton.IsEnabled = false;
        LaunchButton.IsEnabled = false;
        StatusText.Text = "MCP 서버 빌드 중...";

        try
        {
            var (success, output) = await _orchestratorService.BuildMcpServerAsync();

            if (success)
            {
                StatusText.Text = "MCP 서버 빌드 완료";
                UpdateMcpBuildStatus();
            }
            else
            {
                MessageBox.Show($"빌드 실패:\n{output}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "빌드 실패";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"빌드 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "빌드 오류";
        }
        finally
        {
            BuildMcpButton.IsEnabled = true;
            UpdateMcpBuildStatus();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _orchestratorService.StopWatching();
        base.OnClosed(e);
    }
}
