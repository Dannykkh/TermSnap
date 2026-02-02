using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
/// AI Tools 통합 패널 (Memory, Ralph Loop, GSD, Skills)
/// </summary>
public partial class AIToolsPanel : UserControl
{
    private string? _workingDirectory;
    private List<MemoryEntry> _memories = new();
    private bool _isRalphRunning = false;

    // Skills 탭용
    private readonly SkillRecommendationService _skillService = new();
    private SkillRecommendationService.RecommendationResult? _skillRecommendations;
    private ObservableCollection<SkillItemViewModel> _skillItems = new();
    private string _currentSkillFilter = "All";

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
            SkillsContent.Visibility = tab == "Skills" ? Visibility.Visible : Visibility.Collapsed;

            // Skills 탭 선택 시 자동 분석
            if (tab == "Skills" && _skillRecommendations == null && !string.IsNullOrEmpty(_workingDirectory))
            {
                AnalyzeProjectSkills();
            }
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

    #region Skills Tab

    private async void AnalyzeProjectSkills()
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        try
        {
            SkillsProjectNameText.Text = $"프로젝트: {Path.GetFileName(_workingDirectory)} (분석 중...)";
            SkillsStackText.Text = "감지된 기술: 분석 중...";

            _skillRecommendations = await _skillService.AnalyzeAndRecommend(_workingDirectory);

            // UI 업데이트
            SkillsProjectNameText.Text = $"프로젝트: {_skillRecommendations.Stack.ProjectName}";

            var techList = _skillRecommendations.Stack.DetectedTechnologies
                .Concat(_skillRecommendations.Stack.DetectedFrameworks)
                .Distinct()
                .ToList();

            SkillsStackText.Text = techList.Any()
                ? $"감지된 기술: {string.Join(", ", techList)}"
                : "감지된 기술: (없음)";

            // 리스트 아이템 생성
            RefreshSkillsList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AIToolsPanel] 스킬 분석 실패: {ex.Message}");
            SkillsProjectNameText.Text = $"프로젝트: {Path.GetFileName(_workingDirectory)}";
            SkillsStackText.Text = "감지된 기술: (분석 실패)";
        }
    }

    private void RefreshSkillsList()
    {
        if (_skillRecommendations == null) return;

        _skillItems.Clear();

        // 필터에 따라 리소스 수집
        var resources = new List<SkillRecommendationService.RecommendedResource>();

        if (_currentSkillFilter == "All" || _currentSkillFilter == "Skill")
            resources.AddRange(_skillRecommendations.Skills);
        if (_currentSkillFilter == "All" || _currentSkillFilter == "Agent")
            resources.AddRange(_skillRecommendations.Agents);
        if (_currentSkillFilter == "All" || _currentSkillFilter == "Command")
            resources.AddRange(_skillRecommendations.Commands);
        if (_currentSkillFilter == "All" || _currentSkillFilter == "Hook")
            resources.AddRange(_skillRecommendations.Hooks);
        if (_currentSkillFilter == "All" || _currentSkillFilter == "MCP")
            resources.AddRange(_skillRecommendations.MCPs);

        // ViewModel으로 변환
        foreach (var r in resources.OrderBy(x => x.Priority).ThenBy(x => x.Type))
        {
            _skillItems.Add(new SkillItemViewModel(r));
        }

        SkillsList.ItemsSource = _skillItems;
        UpdateSkillsStats();
    }

    private void UpdateSkillsStats()
    {
        if (_skillRecommendations == null)
        {
            SkillsStatsText.Text = "추천: 0개";
            return;
        }

        var total = _skillRecommendations.TotalCount;
        var installed = _skillRecommendations.Skills.Count(s => s.IsInstalled) +
                        _skillRecommendations.Agents.Count(a => a.IsInstalled) +
                        _skillRecommendations.Commands.Count(c => c.IsInstalled) +
                        _skillRecommendations.Hooks.Count(h => h.IsInstalled) +
                        _skillRecommendations.MCPs.Count(m => m.IsInstalled);

        SkillsStatsText.Text = $"추천: {total}개 (설치됨: {installed}개)";
    }

    private void SkillsAnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        _skillRecommendations = null;
        AnalyzeProjectSkills();
    }

    private void SkillsFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is string filter)
        {
            _currentSkillFilter = filter;
            RefreshSkillsList();
        }
    }

    private void SkillsSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _skillItems)
        {
            if (!item.IsInstalled)
                item.IsSelected = true;
        }
    }

    private async void SkillsInstallSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        var selectedItems = _skillItems.Where(i => i.IsSelected && !i.IsInstalled).ToList();
        if (!selectedItems.Any())
        {
            MessageBox.Show("설치할 리소스를 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"{selectedItems.Count}개의 리소스를 설치하시겠습니까?\n\n" +
            string.Join("\n", selectedItems.Take(5).Select(i => $"• {i.Name}")) +
            (selectedItems.Count > 5 ? $"\n... 외 {selectedItems.Count - 5}개" : ""),
            "설치 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        SkillsInstallButton.IsEnabled = false;
        SkillsInstallButton.Content = "설치 중...";

        try
        {
            var resources = selectedItems.Select(i => i.Resource).ToList();
            var (success, failed) = await _skillService.InstallResources(resources, _workingDirectory);

            MessageBox.Show(
                $"설치 완료!\n\n성공: {success}개\n실패: {failed}개",
                "설치 결과",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // 새로고침
            _skillRecommendations = null;
            AnalyzeProjectSkills();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"설치 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SkillsInstallButton.IsEnabled = true;
            SkillsInstallButton.Content = "설치";
        }
    }

    private async void InstallSingleSkill_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        if (sender is Button btn && btn.Tag is SkillItemViewModel item)
        {
            btn.IsEnabled = false;

            try
            {
                var success = await _skillService.InstallResource(item.Resource, _workingDirectory);
                if (success)
                {
                    item.IsInstalled = true;
                    MessageBox.Show($"{item.Name} 설치 완료!", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"{item.Name} 설치 실패", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설치 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }

    #endregion
}

/// <summary>
/// Skills 리스트용 ViewModel
/// </summary>
public class SkillItemViewModel : INotifyPropertyChanged
{
    public SkillRecommendationService.RecommendedResource Resource { get; }

    public string Name => Resource.Name;
    public string Description => Resource.Description;
    public string Category => Resource.Category;

    public string TypeName => Resource.Type switch
    {
        SkillRecommendationService.ResourceType.Skill => "스킬",
        SkillRecommendationService.ResourceType.Agent => "에이전트",
        SkillRecommendationService.ResourceType.Command => "커맨드",
        SkillRecommendationService.ResourceType.Hook => "훅",
        SkillRecommendationService.ResourceType.MCP => "MCP",
        _ => "기타"
    };

    public string PriorityIcon => Resource.Priority switch
    {
        1 => "🔴",  // 필수
        2 => "🟡",  // 권장
        _ => "⚪"   // 선택
    };

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    private bool _isInstalled;
    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            _isInstalled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInstalled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNotInstalled)));
        }
    }

    public bool IsNotInstalled => !IsInstalled;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SkillItemViewModel(SkillRecommendationService.RecommendedResource resource)
    {
        Resource = resource;
        _isInstalled = resource.IsInstalled;
    }
}
