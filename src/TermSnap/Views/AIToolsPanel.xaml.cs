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
/// 각 탭마다 독립적인 인스턴스를 가짐 (탭별 프로젝트 관리)
/// </summary>
public partial class AIToolsPanel : UserControl, IDisposable
{
    private string? _workingDirectory;
    private List<MemoryEntry> _memories = new();
    private List<ConversationLogItem> _conversations = new();
    private string? _selectedConversationPath;
    private bool _disposed = false;

    // Orchestrator 상태 파일 감시
    private FileSystemWatcher? _orchestratorWatcher;
    private DateTime _lastStateUpdate = DateTime.MinValue;
    private const int StateUpdateDebounceMs = 500; // 디바운스 (너무 빈번한 갱신 방지)

    // Memory 탭용 - 탭별 독립 인스턴스
    private readonly MemoryService _memoryService = new();

    /// <summary>
    /// 이 패널의 MemoryService 인스턴스 (외부에서 접근용)
    /// </summary>
    public MemoryService MemoryService => _memoryService;

    // Skills 탭용 - 탭별 독립 인스턴스
    private readonly SkillRecommendationService _skillService = new();
    private SkillRecommendationService.RecommendationResult? _skillRecommendations;
    private ObservableCollection<SkillItemViewModel> _skillItems = new();
    private string _currentSkillFilter = "All";
    private string _currentInstallFilter = "All"; // All, Installed, NotInstalled

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
        LoadConversations();
        SetupOrchestratorWatcher();
    }

    /// <summary>
    /// Orchestrator 상태 파일 감시 설정
    /// </summary>
    private void SetupOrchestratorWatcher()
    {
        // 기존 watcher 정리
        if (_orchestratorWatcher != null)
        {
            _orchestratorWatcher.EnableRaisingEvents = false;
            _orchestratorWatcher.Dispose();
            _orchestratorWatcher = null;
        }

        if (string.IsNullOrEmpty(_workingDirectory)) return;

        var orchestratorFolder = Path.Combine(_workingDirectory, ".orchestrator");

        // 폴더가 없으면 생성 대기 (폴더 생성 시 감지)
        var watchPath = Directory.Exists(orchestratorFolder)
            ? orchestratorFolder
            : _workingDirectory;

        try
        {
            _orchestratorWatcher = new FileSystemWatcher
            {
                Path = watchPath,
                Filter = Directory.Exists(orchestratorFolder) ? "state.json" : ".orchestrator",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = !Directory.Exists(orchestratorFolder)
            };

            _orchestratorWatcher.Changed += OnOrchestratorStateChanged;
            _orchestratorWatcher.Created += OnOrchestratorStateChanged;
            _orchestratorWatcher.EnableRaisingEvents = true;

            Debug.WriteLine($"[AIToolsPanel] Orchestrator 감시 시작: {watchPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AIToolsPanel] Orchestrator 감시 설정 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// Orchestrator 상태 파일 변경 감지 핸들러
    /// </summary>
    private void OnOrchestratorStateChanged(object sender, FileSystemEventArgs e)
    {
        // .orchestrator 폴더가 생성된 경우 watcher 재설정
        if (e.Name == ".orchestrator" && e.ChangeType == WatcherChangeTypes.Created)
        {
            Dispatcher.BeginInvoke(() => SetupOrchestratorWatcher());
            return;
        }

        // state.json 변경 감지
        if (!e.Name?.EndsWith("state.json", StringComparison.OrdinalIgnoreCase) ?? true)
            return;

        // 디바운스: 너무 빈번한 갱신 방지
        var now = DateTime.Now;
        if ((now - _lastStateUpdate).TotalMilliseconds < StateUpdateDebounceMs)
            return;

        _lastStateUpdate = now;

        // UI 스레드에서 갱신
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                LoadOrchestratorProgress();
                Debug.WriteLine($"[AIToolsPanel] Orchestrator 상태 자동 갱신: {e.ChangeType}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AIToolsPanel] Orchestrator 상태 갱신 실패: {ex.Message}");
            }
        });
    }

    #region Tab Navigation

    private void TabButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is string tab)
        {
            MemoryContent.Visibility = tab == "Memory" ? Visibility.Visible : Visibility.Collapsed;
            OrchestratorContent.Visibility = tab == "Orchestrator" ? Visibility.Visible : Visibility.Collapsed;
            SkillsContent.Visibility = tab == "Skills" ? Visibility.Visible : Visibility.Collapsed;

            // Orchestrator 탭 선택 시 상태 로드
            if (tab == "Orchestrator" && !string.IsNullOrEmpty(_workingDirectory))
            {
                LoadOrchestratorStatus();
            }

            // Skills 탭 선택 시 자동 분석
            if (tab == "Skills" && _skillRecommendations == null && !string.IsNullOrEmpty(_workingDirectory))
            {
                AnalyzeProjectSkills();
            }
        }
    }

    /// <summary>
    /// Memory 서브탭 전환 이벤트
    /// </summary>
    private void MemorySubTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is string subTab)
        {
            // 서브탭 컨텐츠 가시성 전환
            if (MemoriesSubContent != null)
                MemoriesSubContent.Visibility = subTab == "Memories" ? Visibility.Visible : Visibility.Collapsed;
            if (ConversationsSubContent != null)
                ConversationsSubContent.Visibility = subTab == "Conversations" ? Visibility.Visible : Visibility.Collapsed;
            if (SearchSubContent != null)
                SearchSubContent.Visibility = subTab == "Search" ? Visibility.Visible : Visibility.Collapsed;

            // 대화 탭 선택 시 로드
            if (subTab == "Conversations" && !string.IsNullOrEmpty(_workingDirectory))
            {
                LoadConversations();
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

    /// <summary>
    /// 마우스 휠 스크롤 처리 - ListBox 등 내부 컨트롤이 휠 이벤트를 가로채는 것 방지
    /// </summary>
    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3);
            e.Handled = true;
        }
    }

    #endregion

    #region Memory Tab

    private void LoadMemories()
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        try
        {
            _memoryService.SetWorkingDirectory(_workingDirectory);
            // 시간 역순 정렬 (최신이 위에)
            _memories = _memoryService.GetAllMemories()
                .OrderByDescending(m => m.CreatedAt)
                .ToList();
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
        var query = MemorySearchBox?.Text?.Trim();

        // 검색어가 없으면 안내 메시지 표시
        if (string.IsNullOrEmpty(query))
        {
            if (SearchMemoryResultsBorder != null)
                SearchMemoryResultsBorder.Visibility = Visibility.Collapsed;
            if (SearchConversationResultsBorder != null)
                SearchConversationResultsBorder.Visibility = Visibility.Collapsed;
            if (SearchPlaceholderText != null)
                SearchPlaceholderText.Visibility = Visibility.Visible;

            MemoryStatsText.Text = $"총 {_memories.Count}개의 기억";
            return;
        }

        // 안내 메시지 숨김
        if (SearchPlaceholderText != null)
            SearchPlaceholderText.Visibility = Visibility.Collapsed;

        // 1. MEMORY.md 검색
        var filteredMemories = _memories.Where(m =>
            m.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (m.Source?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        ).ToList();

        // 검색 결과 UI 업데이트 (기억)
        if (SearchMemoryResultsList != null && SearchMemoryResultsBorder != null)
        {
            SearchMemoryResultsList.ItemsSource = filteredMemories;
            SearchMemoryResultsBorder.Visibility = filteredMemories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // 2. 대화 로그 검색 (키워드 + 내용)
        var filteredConversations = _conversations.Where(c =>
            c.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            c.Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            SearchConversationContent(c.FilePath, query)
        ).ToList();

        // 검색 결과 UI 업데이트 (대화)
        if (SearchConversationResultsList != null && SearchConversationResultsBorder != null)
        {
            SearchConversationResultsList.ItemsSource = filteredConversations;
            SearchConversationResultsBorder.Visibility = filteredConversations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // 결과 표시
        MemoryStatsText.Text = $"검색 결과: 기억 {filteredMemories.Count}개, 대화 {filteredConversations.Count}개";
    }

    /// <summary>
    /// 대화 파일 내용에서 검색
    /// </summary>
    private bool SearchConversationContent(string filePath, string query)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            var content = File.ReadAllText(filePath);
            return content.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
        LoadConversations();
    }

    private void MemoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 선택 시 상세 정보 표시 (필요시 구현)
    }

    private void MemoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MemoryList.SelectedItem is MemoryEntry memory)
        {
            // 팝업으로 메모리 상세 내용 표시
            var detailWindow = new Window
            {
                Title = $"기억 상세 - {GetMemoryTypeName(memory.Type)}",
                Width = 500,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush"),
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 헤더
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(new TextBlock
            {
                Text = $"{GetMemoryTypeIcon(memory.Type)} {GetMemoryTypeName(memory.Type)}",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
            });
            header.Children.Add(new TextBlock
            {
                Text = $"생성: {memory.CreatedAt:yyyy-MM-dd HH:mm}  |  중요도: {(memory.Importance * 100):0}%",
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // 내용
            var contentBox = new TextBox
            {
                Text = memory.Content,
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                Background = (System.Windows.Media.Brush)FindResource("CardBrush"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                BorderThickness = new Thickness(1),
                BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush"),
                Padding = new Thickness(12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 14
            };
            Grid.SetRow(contentBox, 1);
            grid.Children.Add(contentBox);

            // 닫기 버튼
            var closeButton = new Button
            {
                Content = "닫기",
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(0, 12, 0, 0)
            };
            closeButton.Click += (s, args) => detailWindow.Close();
            Grid.SetRow(closeButton, 2);
            grid.Children.Add(closeButton);

            detailWindow.Content = grid;
            detailWindow.ShowDialog();
        }
    }

    private static string GetMemoryTypeIcon(MemoryType type) => type switch
    {
        MemoryType.Fact => "📌",
        MemoryType.Preference => "💡",
        MemoryType.TechStack => "🔧",
        MemoryType.Project => "📁",
        MemoryType.Experience => "🎯",
        MemoryType.WorkPattern => "⏰",
        MemoryType.Instruction => "⚠️",
        MemoryType.Lesson => "📚",
        _ => "•"
    };

    private static string GetMemoryTypeName(MemoryType type) => type switch
    {
        MemoryType.Fact => "사실",
        MemoryType.Preference => "선호도",
        MemoryType.TechStack => "기술 스택",
        MemoryType.Project => "프로젝트",
        MemoryType.Experience => "경험",
        MemoryType.WorkPattern => "작업 패턴",
        MemoryType.Instruction => "지침",
        MemoryType.Lesson => "학습된 교훈",
        _ => "기타"
    };

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
                _memoryService.DeleteMemory(id);
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

            await _memoryService.AddMemory(newMemory);
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

    #region Orchestrator Tab

    private List<PlanFileItem> _planFiles = new();
    private string? _selectedPlanFilePath;

    /// <summary>
    /// 오케스트레이터 상태 로드
    /// </summary>
    private void LoadOrchestratorStatus()
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        // AI Provider 감지 상태 업데이트
        DetectAIProviders();

        // 플랜 파일 로드
        LoadPlanFiles();

        // 태스크 진행 상황 로드
        LoadOrchestratorProgress();
    }

    /// <summary>
    /// 플랜 파일 목록 로드
    /// </summary>
    private void LoadPlanFiles()
    {
        _planFiles.Clear();
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        // 플랜 파일 검색 경로들
        var planPatterns = new[]
        {
            "PLAN.md",
            "PRD.md",
            "plan.md",
            ".claude/plan.md",
            ".claude/plans/*.md",
            "docs/PLAN.md",
            "docs/PRD.md"
        };

        foreach (var pattern in planPatterns)
        {
            try
            {
                var fullPattern = Path.Combine(_workingDirectory, pattern);
                var directory = Path.GetDirectoryName(fullPattern) ?? _workingDirectory;
                var filePattern = Path.GetFileName(fullPattern);

                if (Directory.Exists(directory))
                {
                    var files = Directory.GetFiles(directory, filePattern);
                    foreach (var file in files)
                    {
                        if (!_planFiles.Any(p => p.FilePath == file))
                        {
                            var fileInfo = new FileInfo(file);
                            _planFiles.Add(new PlanFileItem
                            {
                                FilePath = file,
                                FileName = Path.GetFileName(file),
                                ModifiedTime = fileInfo.LastWriteTime.ToString("MM/dd HH:mm")
                            });
                        }
                    }
                }
            }
            catch { }
        }

        // 수정일 기준 정렬
        _planFiles = _planFiles.OrderByDescending(p => p.ModifiedTime).ToList();

        // UI 업데이트
        PlanFileList.ItemsSource = _planFiles;
        NoPlanFilesText.Visibility = _planFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 첫 번째 파일 자동 선택
        if (_planFiles.Count > 0)
        {
            PlanFileList.SelectedIndex = 0;
        }
    }

    private void OrchestratorLoadPlanButton_Click(object sender, RoutedEventArgs e)
    {
        LoadPlanFiles();
    }

    private void PlanFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlanFileList.SelectedItem is PlanFileItem item)
        {
            _selectedPlanFilePath = item.FilePath;
            LoadPlanPreview(item.FilePath);
        }
    }

    private void LoadPlanPreview(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            // 500자까지만 미리보기
            if (content.Length > 500)
                content = content.Substring(0, 497) + "...";
            PlanPreviewText.Text = content;
        }
        catch (Exception ex)
        {
            PlanPreviewText.Text = $"파일을 읽을 수 없습니다: {ex.Message}";
        }
    }

    /// <summary>
    /// AI Provider 감지
    /// </summary>
    private void DetectAIProviders()
    {
        var claudeAvailable = CheckCliAvailable("claude");
        var codexAvailable = CheckCliAvailable("codex");
        var geminiAvailable = CheckCliAvailable("gemini");

        // 상태 배지 업데이트
        UpdateProviderBadge(ClaudeStatusBadge, claudeAvailable);
        UpdateProviderBadge(CodexStatusBadge, codexAvailable);
        UpdateProviderBadge(GeminiStatusBadge, geminiAvailable);

        // 모드 텍스트 업데이트
        var availableCount = (claudeAvailable ? 1 : 0) + (codexAvailable ? 1 : 0) + (geminiAvailable ? 1 : 0);
        OrchestratorModeText.Text = availableCount switch
        {
            3 => "Full Mode: Claude + Codex + Gemini (3개 AI 병렬)",
            2 => "Dual Mode: 2개 AI 병렬",
            1 => "Single Mode: 단일 AI",
            _ => "No AI CLI detected"
        };
    }

    private bool CheckCliAvailable(string command)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateProviderBadge(System.Windows.Controls.Border badge, bool available)
    {
        if (badge == null) return;
        badge.Background = available
            ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
            : (System.Windows.Media.Brush)FindResource("BorderBrush");
        if (badge.Child is TextBlock text)
        {
            text.Foreground = available
                ? System.Windows.Media.Brushes.White
                : (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
        }
    }

    /// <summary>
    /// 오케스트레이터 진행 상황 로드
    /// </summary>
    private void LoadOrchestratorProgress()
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        var stateFile = Path.Combine(_workingDirectory, ".orchestrator", "state.json");
        if (!File.Exists(stateFile))
        {
            UpdateOrchestratorUI(0, 0, 0, 0, 0);
            return;
        }

        try
        {
            var content = File.ReadAllText(stateFile);
            var state = System.Text.Json.JsonDocument.Parse(content);
            var tasks = state.RootElement.GetProperty("tasks");

            int pending = 0, inProgress = 0, completed = 0, failed = 0;
            foreach (var task in tasks.EnumerateArray())
            {
                var status = task.GetProperty("status").GetString();
                switch (status)
                {
                    case "pending": pending++; break;
                    case "in_progress": inProgress++; break;
                    case "completed": completed++; break;
                    case "failed": failed++; break;
                }
            }

            var total = pending + inProgress + completed + failed;
            UpdateOrchestratorUI(total, pending, inProgress, completed, failed);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Orchestrator] 상태 로드 실패: {ex.Message}");
            UpdateOrchestratorUI(0, 0, 0, 0, 0);
        }
    }

    private void UpdateOrchestratorUI(int total, int pending, int inProgress, int completed, int failed)
    {
        var percent = total > 0 ? (int)((double)completed / total * 100) : 0;

        OrchestratorProgressBar.Value = percent;
        OrchestratorProgressText.Text = $"{percent}%";
        OrchestratorPendingText.Text = $"대기: {pending}";
        OrchestratorInProgressText.Text = $"진행: {inProgress}";
        OrchestratorCompletedText.Text = $"완료: {completed}";
        OrchestratorFailedText.Text = $"실패: {failed}";
    }

    private void OrchestratorRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadOrchestratorStatus();
    }

    private void OrchestratorViewTasksButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        var stateFile = Path.Combine(_workingDirectory, ".orchestrator", "state.json");
        if (File.Exists(stateFile))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = stateFile,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파일을 열 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("아직 태스크가 없습니다. PM 시작을 눌러 작업을 시작하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OrchestratorStartButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedPlanFilePath) || !File.Exists(_selectedPlanFilePath))
        {
            MessageBox.Show("플랜 파일을 선택해주세요.\n\nClaude Code에서 plan mode를 실행하여 플랜 파일을 먼저 생성하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 선택된 플랜 파일명
        var planFileName = Path.GetFileName(_selectedPlanFilePath);

        // PM 모드로 Claude Code 시작 명령 생성 (플랜 파일 경로 전달)
        var command = $"workpm \"{_selectedPlanFilePath}\"";
        CommandRequested?.Invoke(this, command);

        MessageBox.Show(
            $"PM 모드가 시작됩니다.\n\n" +
            $"📄 플랜 파일: {planFileName}\n\n" +
            "터미널에서 Claude가 플랜을 분석하고\n" +
            "태스크를 생성합니다.\n\n" +
            "Worker를 추가하려면 새 터미널에서\n" +
            "'pmworker'를 실행하세요.",
            "PM 시작",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OrchestratorResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        var result = MessageBox.Show(
            "모든 오케스트레이터 상태를 초기화하시겠습니까?\n\n" +
            "- 모든 태스크 삭제\n" +
            "- 파일 락 해제\n" +
            "- Worker 등록 해제",
            "초기화 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var orchestratorFolder = Path.Combine(_workingDirectory, ".orchestrator");
        try
        {
            if (Directory.Exists(orchestratorFolder))
            {
                Directory.Delete(orchestratorFolder, true);
            }

            _selectedPlanFilePath = null;
            PlanPreviewText.Text = "플랜 파일을 선택하세요";
            UpdateOrchestratorUI(0, 0, 0, 0, 0);
            MessageBox.Show("초기화 완료", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"초기화 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Skills Tab

    private async void AnalyzeProjectSkills()
    {
        if (string.IsNullOrEmpty(_workingDirectory))
        {
            SkillsProjectNameText.Text = "프로젝트: (폴더를 먼저 열어주세요)";
            SkillsStackText.Text = "감지된 기술: -";
            MessageBox.Show(
                "작업 디렉토리가 설정되지 않았습니다.\n로컬 터미널에서 폴더를 먼저 열어주세요.",
                "알림",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // 로딩 상태 시작
        SetSkillsLoadingState(true);

        try
        {
            SkillsProjectNameText.Text = $"프로젝트: {Path.GetFileName(_workingDirectory)} (분석 중...)";
            SkillsStackText.Text = "감지된 기술: 분석 중... (GitHub에서 리소스 가져오는 중)";

            // GitHub에서 동적으로 리소스 가져오기 (프로젝트 분석 포함)
            _skillRecommendations = await _skillService.GetAllAvailableResources(_workingDirectory);

            // UI 업데이트
            SkillsProjectNameText.Text = $"프로젝트: {_skillRecommendations.Stack.ProjectName}";

            var techList = _skillRecommendations.Stack.DetectedTechnologies
                .Concat(_skillRecommendations.Stack.DetectedFrameworks)
                .Distinct()
                .ToList();

            SkillsStackText.Text = techList.Any()
                ? $"감지된 기술: {string.Join(", ", techList)}"
                : "감지된 기술: (기술 스택을 감지하지 못했습니다)";

            // 리스트 아이템 생성
            RefreshSkillsList();

            if (_skillRecommendations.TotalCount == 0)
            {
                SkillsStatsText.Text = "추천: 0개 (기본 스킬을 추가해보세요)";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AIToolsPanel] 스킬 분석 실패: {ex.Message}");
            SkillsProjectNameText.Text = $"프로젝트: {Path.GetFileName(_workingDirectory)}";
            SkillsStackText.Text = $"감지된 기술: (분석 실패: {ex.Message})";
            MessageBox.Show($"프로젝트 분석 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetSkillsLoadingState(false);
        }
    }

    private void SetSkillsLoadingState(bool isLoading)
    {
        // 분석 버튼 상태
        if (SkillsAnalyzeButton != null)
        {
            SkillsAnalyzeButton.IsEnabled = !isLoading;
            var buttonText = SkillsAnalyzeButton.Content as StackPanel;
            if (buttonText?.Children.Count > 1 && buttonText.Children[1] is TextBlock textBlock)
            {
                textBlock.Text = isLoading ? "분석 중..." : "분석";
            }
        }

        // 프로그레스 바 표시 (있는 경우)
        if (SkillsProgressBar != null)
        {
            SkillsProgressBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            SkillsProgressBar.IsIndeterminate = isLoading;
        }
    }

    private void RefreshSkillsList()
    {
        if (_skillRecommendations == null) return;

        // 기존 선택 상태 저장 (리소스 이름으로)
        var selectedNames = _skillItems
            .Where(i => i.IsSelected)
            .Select(i => i.Name)
            .ToHashSet();

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

        // 설치 상태 필터링
        if (_currentInstallFilter == "Installed")
            resources = resources.Where(r => r.IsInstalled).ToList();
        else if (_currentInstallFilter == "NotInstalled")
            resources = resources.Where(r => !r.IsInstalled).ToList();

        // ViewModel으로 변환 (기존 선택 상태 복원)
        foreach (var r in resources.OrderBy(x => x.Priority).ThenBy(x => x.Type))
        {
            var item = new SkillItemViewModel(r);
            // 이전에 선택되어 있었으면 선택 상태 유지
            if (selectedNames.Contains(r.Name))
                item.IsSelected = true;
            _skillItems.Add(item);
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

    private void SkillsInstallFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is string filter)
        {
            _currentInstallFilter = filter;
            RefreshSkillsList();
            UpdateActionButton();
        }
    }

    /// <summary>
    /// 필터에 따라 액션 버튼 (설치/삭제) 업데이트
    /// </summary>
    private void UpdateActionButton()
    {
        if (SkillsActionButton == null || SkillsActionIcon == null || SkillsActionText == null)
            return;

        if (_currentInstallFilter == "Installed")
        {
            // 설치됨 필터 → 삭제 버튼
            SkillsActionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.DeleteOutline;
            SkillsActionText.Text = "삭제";
            SkillsActionButton.Background = (System.Windows.Media.Brush)FindResource("MaterialDesignValidationErrorBrush");
            SkillsActionButton.Foreground = System.Windows.Media.Brushes.White;
            SkillsActionButton.ToolTip = "선택한 리소스 삭제";
        }
        else
        {
            // 모두 / 미설치 필터 → 설치 버튼
            SkillsActionIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Download;
            SkillsActionText.Text = "설치";
            SkillsActionButton.Background = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            SkillsActionButton.Foreground = System.Windows.Media.Brushes.White;
            SkillsActionButton.ToolTip = "선택한 리소스 설치";
        }
    }

    private void SkillsSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        // 전체 선택되어 있으면 전체 해제, 아니면 전체 선택 (토글)
        var allSelected = _skillItems.Any() && _skillItems.All(i => i.IsSelected);

        foreach (var item in _skillItems)
        {
            item.IsSelected = !allSelected;
        }
    }

    private async void SkillsActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        if (_currentInstallFilter == "Installed")
        {
            // 삭제 동작
            await DeleteSelectedSkills();
        }
        else
        {
            // 설치 동작
            await InstallSelectedSkills();
        }
    }

    private async Task InstallSelectedSkills()
    {
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

        SkillsActionButton.IsEnabled = false;
        SkillsActionText.Text = "설치 중...";

        try
        {
            var resources = selectedItems.Select(i => i.Resource).ToList();
            var (success, failed) = await _skillService.InstallResources(resources, _workingDirectory);

            // MCP 서버가 포함되어 있으면 재시작 안내
            var hasMcp = resources.Any(r => r.Type == SkillRecommendationService.ResourceType.MCP);
            var restartMsg = hasMcp ? "\n\n⚠️ MCP 서버가 포함되어 있습니다.\nClaude Code를 재시작해야 적용됩니다." : "";

            MessageBox.Show(
                $"설치 완료!\n\n성공: {success}개\n실패: {failed}개{restartMsg}",
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
            SkillsActionButton.IsEnabled = true;
            UpdateActionButton();
        }
    }

    private async Task DeleteSelectedSkills()
    {
        var selectedItems = _skillItems.Where(i => i.IsSelected && i.IsInstalled).ToList();
        if (!selectedItems.Any())
        {
            MessageBox.Show("삭제할 리소스를 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // MCP 서버가 포함되어 있으면 경고
        var hasMcp = selectedItems.Any(i => i.Resource.Type == SkillRecommendationService.ResourceType.MCP);
        var mcpWarning = hasMcp ? "\n\n⚠️ MCP 서버가 포함되어 있습니다.\n삭제 후 Claude Code를 재시작해야 적용됩니다." : "";

        var result = MessageBox.Show(
            $"{selectedItems.Count}개의 리소스를 삭제하시겠습니까?\n\n" +
            "수정한 내용이 있다면 사라집니다.\n\n" +
            string.Join("\n", selectedItems.Take(5).Select(i => $"• {i.Name}")) +
            (selectedItems.Count > 5 ? $"\n... 외 {selectedItems.Count - 5}개" : "") +
            mcpWarning,
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        SkillsActionButton.IsEnabled = false;
        SkillsActionText.Text = "삭제 중...";

        try
        {
            int success = 0, failed = 0;
            foreach (var item in selectedItems)
            {
                if (await _skillService.DeleteResource(item.Resource, _workingDirectory))
                    success++;
                else
                    failed++;
            }

            // MCP 서버가 포함되어 있으면 재시작 안내
            var restartMsg = hasMcp ? "\n\n⚠️ Claude Code를 재시작해야 적용됩니다." : "";

            MessageBox.Show(
                $"삭제 완료!\n\n성공: {success}개\n실패: {failed}개{restartMsg}",
                "삭제 결과",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // 새로고침
            _skillRecommendations = null;
            AnalyzeProjectSkills();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"삭제 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SkillsActionButton.IsEnabled = true;
            UpdateActionButton();
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

    private async void DeleteSingleSkill_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_workingDirectory)) return;

        if (sender is Button btn && btn.Tag is SkillItemViewModel item)
        {
            // 삭제 확인
            var result = MessageBox.Show(
                $"'{item.Name}'을(를) 삭제하시겠습니까?\n\n수정한 내용이 있다면 사라집니다.",
                "삭제 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            btn.IsEnabled = false;

            try
            {
                var success = await _skillService.DeleteResource(item.Resource, _workingDirectory);
                if (success)
                {
                    item.IsInstalled = false;
                    MessageBox.Show($"{item.Name} 삭제 완료!", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"{item.Name} 삭제 실패 (파일을 찾을 수 없음)", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"삭제 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }

    #endregion

    #region Conversations (대화 로그)

    /// <summary>
    /// 대화 로그 목록 로드
    /// </summary>
    private void LoadConversations()
    {
        _conversations.Clear();

        if (string.IsNullOrEmpty(_workingDirectory))
        {
            UpdateConversationsUI();
            return;
        }

        var conversationsPath = Path.Combine(_workingDirectory, ".claude", "conversations");

        if (!Directory.Exists(conversationsPath))
        {
            UpdateConversationsUI();
            return;
        }

        try
        {
            // .md 파일들 로드 (날짜순 내림차순)
            var files = Directory.GetFiles(conversationsPath, "*.md")
                .OrderByDescending(f => f)
                .Take(20) // 최근 20개만
                .ToList();

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var item = new ConversationLogItem
                {
                    FilePath = file,
                    FileName = fileName,
                };

                // 날짜 파싱 시도 (YYYY-MM-DD 형식)
                if (DateTime.TryParse(fileName, out var date))
                {
                    item.Date = date;
                    item.DateDisplay = date.ToString("yyyy년 M월 d일");

                    // 오늘/어제 표시
                    if (date.Date == DateTime.Today)
                        item.DateDisplay = "📍 오늘";
                    else if (date.Date == DateTime.Today.AddDays(-1))
                        item.DateDisplay = "어제";
                }
                else
                {
                    item.DateDisplay = fileName;
                }

                // 파일 요약 추출 (첫 몇 줄)
                try
                {
                    var lines = File.ReadLines(file).Take(10).ToList();
                    var contentLines = lines.Where(l => !l.StartsWith("#") && !l.StartsWith("---") && !string.IsNullOrWhiteSpace(l)).ToList();
                    item.Summary = contentLines.FirstOrDefault()?.Trim() ?? "내용 없음";
                    if (item.Summary.Length > 50)
                        item.Summary = item.Summary.Substring(0, 47) + "...";

                    // 키워드 추출 (해시태그)
                    var content = string.Join(" ", lines);
                    var keywords = ExtractKeywords(content);
                    item.Keywords = keywords;
                    item.KeywordCount = keywords.Count > 0 ? $"#{keywords.Count}" : "";
                }
                catch
                {
                    item.Summary = "읽기 실패";
                }

                _conversations.Add(item);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AIToolsPanel] 대화 로그 로드 실패: {ex.Message}");
        }

        UpdateConversationsUI();
    }

    /// <summary>
    /// 키워드 추출 (해시태그 및 frontmatter)
    /// </summary>
    private List<string> ExtractKeywords(string content)
    {
        var keywords = new List<string>();

        // 해시태그 추출 (#keyword)
        var hashtagPattern = new System.Text.RegularExpressions.Regex(@"#(\w+)");
        var matches = hashtagPattern.Matches(content);
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var keyword = match.Groups[1].Value.ToLower();
            if (!keywords.Contains(keyword) && keyword.Length > 1)
                keywords.Add(keyword);
        }

        return keywords.Take(5).ToList(); // 최대 5개
    }

    /// <summary>
    /// 대화 UI 업데이트
    /// </summary>
    private void UpdateConversationsUI()
    {
        if (ConversationList == null) return;

        ConversationList.ItemsSource = _conversations;

        if (NoConversationsText != null)
        {
            NoConversationsText.Visibility = _conversations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 대화 선택 시 미리보기 표시
    /// </summary>
    private void ConversationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConversationList.SelectedItem is ConversationLogItem item)
        {
            _selectedConversationPath = item.FilePath;
            ShowConversationPreview(item);
        }
    }

    /// <summary>
    /// 대화 미리보기 표시
    /// </summary>
    private void ShowConversationPreview(ConversationLogItem item)
    {
        if (ConversationPreviewBorder == null || ConversationPreviewText == null || ConversationPreviewTitle == null)
            return;

        try
        {
            var content = File.ReadAllText(item.FilePath);

            // 앞부분만 표시 (500자)
            if (content.Length > 500)
                content = content.Substring(0, 497) + "...";

            ConversationPreviewTitle.Text = $"📄 {item.DateDisplay}";
            ConversationPreviewText.Text = content;
            ConversationPreviewBorder.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ConversationPreviewText.Text = $"파일을 읽을 수 없습니다: {ex.Message}";
            ConversationPreviewBorder.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 대화 파일 열기
    /// </summary>
    private void OpenConversationFile_Click(object sender, RoutedEventArgs e)
    {
        // 버튼 Tag에서 파일 경로 가져오기 (대화 목록 아이템 버튼)
        string? filePath = null;
        if (sender is Button btn && btn.Tag is string tagPath)
        {
            filePath = tagPath;
        }

        // Tag가 없으면 선택된 대화 경로 사용
        if (string.IsNullOrEmpty(filePath))
        {
            filePath = _selectedConversationPath;
        }

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            MessageBox.Show("선택된 대화 파일이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 열 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 대화 목록 새로고침
    /// </summary>
    private void ConversationRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadConversations();
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// 리소스 정리
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            // FileSystemWatcher 정리
            if (_orchestratorWatcher != null)
            {
                _orchestratorWatcher.EnableRaisingEvents = false;
                _orchestratorWatcher.Changed -= OnOrchestratorStateChanged;
                _orchestratorWatcher.Created -= OnOrchestratorStateChanged;
                _orchestratorWatcher.Dispose();
                _orchestratorWatcher = null;
            }

            Debug.WriteLine("[AIToolsPanel] Disposed");
        }
    }

    ~AIToolsPanel()
    {
        Dispose(false);
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

/// <summary>
/// 대화 로그 아이템
/// </summary>
public class ConversationLogItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string DateDisplay { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string KeywordCount { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
}

/// <summary>
/// 플랜 파일 아이템
/// </summary>
public class PlanFileItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ModifiedTime { get; set; } = string.Empty;
}
