using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// AI Tools 통합 패널 (Memory, Ralph Loop, GSD)
/// </summary>
public partial class AIToolsPanel : UserControl
{
    private string? _workingDirectory;
    private List<MemoryEntry> _memories = new();
    private bool _isRalphRunning = false;

    /// <summary>
    /// 패널 닫기 요청 이벤트
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 명령어 실행 요청 이벤트 (Ralph Loop, GSD용)
    /// </summary>
    public event EventHandler<string>? CommandRequested;

    public AIToolsPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 작업 디렉토리 설정
    /// </summary>
    public void SetWorkingDirectory(string path)
    {
        _workingDirectory = path;
        LoadMemories();
        LoadGsdContent();
    }

    #region Tab Navigation

    private void TabButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is string tab)
        {
            MemoryContent.Visibility = tab == "Memory" ? Visibility.Visible : Visibility.Collapsed;
            RalphLoopContent.Visibility = tab == "RalphLoop" ? Visibility.Visible : Visibility.Collapsed;
            GsdContent.Visibility = tab == "GSD" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    #endregion

    #region Header Buttons

    private void SetupHooksButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_workingDirectory))
        {
            MessageBox.Show(
                "작업 디렉토리가 설정되지 않았습니다.\n폴더를 먼저 열어주세요.",
                "알림",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            // Claude 훅 설정
            var hooksCreated = ClaudeHookService.EnsureMemoryHooks(_workingDirectory);
            var memoryCreated = ClaudeHookService.EnsureMemoryReference(_workingDirectory);

            if (hooksCreated || memoryCreated)
            {
                MessageBox.Show(
                    $"Claude 장기기억 설정이 완료되었습니다.\n\n" +
                    $"생성된 파일:\n" +
                    $"• .claude/settings.local.json\n" +
                    $"• MEMORY.md\n" +
                    $"• CLAUDE.md (없으면 생성)\n\n" +
                    $"경로: {_workingDirectory}",
                    "설정 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "이미 Claude 장기기억 설정이 존재합니다.",
                    "알림",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"설정 중 오류가 발생했습니다.\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Memory Tab

    private void LoadMemories()
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        try
        {
            MemoryService.Instance.SetWorkingDirectory(_workingDirectory);
            _memories = MemoryService.Instance.GetAllMemories();
            MemoryList.ItemsSource = _memories;
            MemoryStatsText.Text = $"총 {_memories.Count}개의 기억";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AIToolsPanel] 메모리 로드 실패: {ex.Message}");
        }
    }

    private void MemorySearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SearchMemories();
        }
    }

    private void MemorySearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchMemories();
    }

    private void SearchMemories()
    {
        var query = MemorySearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            MemoryList.ItemsSource = _memories;
            return;
        }

        var filtered = _memories.Where(m =>
            m.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (m.Source?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        ).ToList();

        MemoryList.ItemsSource = filtered;
        MemoryStatsText.Text = $"검색 결과: {filtered.Count}개";
    }

    private void MemoryTypeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // XAML 로드 전 이벤트 발생 시 무시
        if (MemoryList == null || MemoryStatsText == null) return;

        if (MemoryTypeFilterCombo.SelectedItem is ComboBoxItem item && item.Tag is string typeTag)
        {
            if (string.IsNullOrEmpty(typeTag))
            {
                MemoryList.ItemsSource = _memories;
                MemoryStatsText.Text = $"총 {_memories.Count}개의 기억";
            }
            else
            {
                var filtered = _memories.Where(m => m.Type.ToString() == typeTag).ToList();
                MemoryList.ItemsSource = filtered;
                MemoryStatsText.Text = $"필터 결과: {filtered.Count}개";
            }
        }
    }

    private void MemoryRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadMemories();
    }

    private void MemoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 선택 시 상세 정보 표시 (필요시 구현)
    }

    private void DeleteMemory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int id)
        {
            var result = MessageBox.Show(
                "이 기억을 삭제하시겠습니까?",
                "확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                MemoryService.Instance.DeleteMemory(id);
                LoadMemories();
            }
        }
    }

    private async void AddMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddMemoryDialog();
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() == true)
        {
            var newMemory = new MemoryEntry
            {
                Content = dialog.MemoryContent,
                Type = dialog.SelectedType,
                Importance = dialog.Importance,
                CreatedAt = DateTime.Now
            };

            await MemoryService.Instance.AddMemory(newMemory);
            LoadMemories();
        }
    }

    private void ExportMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        var memoryPath = Path.Combine(_workingDirectory, "MEMORY.md");
        if (File.Exists(memoryPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = memoryPath,
                UseShellExecute = true
            });
        }
        else
        {
            MessageBox.Show(
                "MEMORY.md 파일이 없습니다.\n'Claude Hook 설정' 버튼을 먼저 클릭해주세요.",
                "알림",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    #endregion

    #region Ralph Loop Tab

    private void RalphStartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRalphRunning)
        {
            // 중지
            _isRalphRunning = false;
            RalphStartButton.Content = "시작";
            RalphStartButton.Background = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            RalphCurrentTaskText.Text = "중지됨";
        }
        else
        {
            // 시작
            var prd = RalphPrdTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(prd))
            {
                MessageBox.Show("PRD를 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isRalphRunning = true;
            RalphStartButton.Content = "중지";
            RalphStartButton.Background = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            RalphCurrentTaskText.Text = "실행 준비 중...";

            // AI CLI 명령어 생성 및 실행 요청
            var selectedCli = (RalphAiCommandCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "claude";
            var command = $"{selectedCli} \"{prd}\"";
            CommandRequested?.Invoke(this, command);
        }
    }

    private void RalphResetButton_Click(object sender, RoutedEventArgs e)
    {
        RalphPrdTextBox.Text = "";
        RalphProgressBar.Value = 0;
        RalphCurrentTaskText.Text = "";
        RalphIterationText.Text = "반복: 0/100";
    }

    /// <summary>
    /// Ralph Loop 진행 상황 업데이트
    /// </summary>
    public void UpdateRalphProgress(int progress, int iteration, int maxIterations, string currentTask)
    {
        Dispatcher.Invoke(() =>
        {
            RalphProgressBar.Value = progress;
            RalphIterationText.Text = $"반복: {iteration}/{maxIterations}";
            RalphCurrentTaskText.Text = currentTask;
        });
    }

    #endregion

    #region GSD Tab

    private void LoadGsdContent()
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        // CONTEXT.md 로드
        var contextPath = Path.Combine(_workingDirectory, "CONTEXT.md");
        if (File.Exists(contextPath))
        {
            try
            {
                GsdContentTextBox.Text = File.ReadAllText(contextPath);
            }
            catch { }
        }
    }

    private void GsdPhaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Phase 변경 시 해당 Phase의 내용 로드
        LoadGsdPhaseContent();
    }

    private void GsdStepRadio_Checked(object sender, RoutedEventArgs e)
    {
        // Step 변경 시 해당 Step의 내용 로드
        LoadGsdStepContent();
    }

    private void LoadGsdPhaseContent()
    {
        // Phase별 내용 로드 (필요시 구현)
    }

    private void LoadGsdStepContent()
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        var step = GetCurrentGsdStep();
        var fileName = step switch
        {
            "Discuss" => "CONTEXT.md",
            "Plan" => "PLAN.md",
            "Execute" => "PRD.md",
            "Verify" => "UAT.md",
            _ => "CONTEXT.md"
        };

        var filePath = Path.Combine(_workingDirectory, fileName);
        if (File.Exists(filePath))
        {
            try
            {
                GsdContentTextBox.Text = File.ReadAllText(filePath);
            }
            catch { }
        }
        else
        {
            GsdContentTextBox.Text = "";
        }
    }

    private string GetCurrentGsdStep()
    {
        if (GsdDiscussRadio.IsChecked == true) return "Discuss";
        if (GsdPlanRadio.IsChecked == true) return "Plan";
        if (GsdExecuteRadio.IsChecked == true) return "Execute";
        if (GsdVerifyRadio.IsChecked == true) return "Verify";
        return "Discuss";
    }

    private void GsdSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        var step = GetCurrentGsdStep();
        var fileName = step switch
        {
            "Discuss" => "CONTEXT.md",
            "Plan" => "PLAN.md",
            "Execute" => "PRD.md",
            "Verify" => "UAT.md",
            _ => "CONTEXT.md"
        };

        var filePath = Path.Combine(_workingDirectory, fileName);
        try
        {
            File.WriteAllText(filePath, GsdContentTextBox.Text);
            MessageBox.Show($"{fileName} 저장 완료", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GsdNextButton_Click(object sender, RoutedEventArgs e)
    {
        // 다음 단계로 이동
        if (GsdDiscussRadio.IsChecked == true)
        {
            GsdPlanRadio.IsChecked = true;
        }
        else if (GsdPlanRadio.IsChecked == true)
        {
            GsdExecuteRadio.IsChecked = true;
        }
        else if (GsdExecuteRadio.IsChecked == true)
        {
            GsdVerifyRadio.IsChecked = true;
        }
        else if (GsdVerifyRadio.IsChecked == true)
        {
            // 다음 Phase로 이동
            if (GsdPhaseCombo.SelectedIndex < 4)
            {
                GsdPhaseCombo.SelectedIndex++;
                GsdDiscussRadio.IsChecked = true;
            }
        }
    }

    #endregion
}
