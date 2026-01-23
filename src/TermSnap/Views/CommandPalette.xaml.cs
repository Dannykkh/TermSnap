using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views
{
    /// <summary>
    /// Command Palette 검색 항목
    /// </summary>
    public class PaletteItem
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Icon { get; set; } = "Circle";
        public Brush IconBackground { get; set; } = Brushes.Gray;
        public string TypeText { get; set; } = string.Empty;
        public Brush TypeBackground { get; set; } = Brushes.Gray;
        public PaletteItemType ItemType { get; set; }
        public object? Data { get; set; }
    }

    public enum PaletteItemType
    {
        History,
        Snippet,
        Action,
        Workflow
    }

    /// <summary>
    /// Command Palette (Ctrl+Shift+P)
    /// </summary>
    public partial class CommandPalette : Window
    {
        private readonly AppConfig _config;
        private readonly string? _serverProfile;
        private List<PaletteItem> _allItems = new();

        // Brush 재사용 (성능 최적화 - Frozen 상태로 생성)
        private static readonly SolidColorBrush BlueBrush = CreateFrozenBrush(33, 150, 243);
        private static readonly SolidColorBrush GreenBrush = CreateFrozenBrush(76, 175, 80);
        private static readonly SolidColorBrush PurpleBrush = CreateFrozenBrush(103, 58, 183);
        private static readonly SolidColorBrush OrangeBrush = CreateFrozenBrush(255, 152, 0);
        private static readonly SolidColorBrush TealBrush = CreateFrozenBrush(0, 188, 212);
        private static readonly SolidColorBrush IndigoBrush = CreateFrozenBrush(63, 81, 181);
        private static readonly SolidColorBrush GreyBrush = CreateFrozenBrush(96, 125, 139);
        private static readonly SolidColorBrush BrownBrush = CreateFrozenBrush(121, 85, 72);
        private static readonly SolidColorBrush RedBrush = CreateFrozenBrush(244, 67, 54);

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze(); // 성능 최적화 - 불변 객체로 만들어 스레드 간 공유 가능
            return brush;
        }

        /// <summary>
        /// 선택된 항목
        /// </summary>
        public PaletteItem? SelectedItem { get; private set; }

        /// <summary>
        /// 실행할 명령어 (히스토리/스니펫에서 선택 시)
        /// </summary>
        public string? SelectedCommand { get; private set; }

        /// <summary>
        /// 실행할 액션
        /// </summary>
        public string? SelectedAction { get; private set; }

        public CommandPalette(AppConfig config, string? serverProfile = null)
        {
            InitializeComponent();
            _config = config;
            _serverProfile = serverProfile;

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Focus();

            // 먼저 스니펫과 액션만 로드 (빠름)
            LoadSnippetsAndActions();
            UpdateResults();

            // 히스토리는 백그라운드에서 로드
            await LoadHistoryAsync();
        }

        private void LoadSnippetsAndActions()
        {
            _allItems.Clear();

            // 스니펫 로드
            foreach (var snippet in _config.CommandSnippets.Snippets)
            {
                _allItems.Add(new PaletteItem
                {
                    Title = snippet.Name,
                    Subtitle = snippet.Command,
                    Icon = "CodeBraces",
                    IconBackground = GreenBrush,
                    TypeText = "스니펫",
                    TypeBackground = GreenBrush,
                    ItemType = PaletteItemType.Snippet,
                    Data = snippet
                });
            }

            // 액션 추가
            var actions = new[]
            {
                ("새 탭 열기", "OpenNewTab", "TabPlus", OrangeBrush),
                ("서버 연결", "Connect", "LanConnect", GreenBrush),
                ("연결 해제", "Disconnect", "LanDisconnect", RedBrush),
                ("파일 전송", "OpenFileTransfer", "FolderNetwork", BlueBrush),
                ("서버 모니터링", "OpenMonitor", "MonitorDashboard", PurpleBrush),
                ("로그 뷰어", "OpenLogViewer", "FileDocumentOutline", TealBrush),
                ("설정", "OpenSettings", "Cog", GreyBrush),
                ("히스토리", "OpenHistory", "History", BrownBrush),
                ("스니펫 관리", "OpenSnippets", "CodeBracesBox", IndigoBrush),
            };

            foreach (var (title, action, icon, brush) in actions)
            {
                _allItems.Add(new PaletteItem
                {
                    Title = title,
                    Subtitle = $"액션: {action}",
                    Icon = icon,
                    IconBackground = brush,
                    TypeText = "액션",
                    TypeBackground = PurpleBrush,
                    ItemType = PaletteItemType.Action,
                    Data = action
                });
            }
        }

        private async System.Threading.Tasks.Task LoadHistoryAsync()
        {
            try
            {
                // 백그라운드 스레드에서 히스토리 로드
                var histories = await System.Threading.Tasks.Task.Run(() =>
                {
                    return string.IsNullOrEmpty(_serverProfile)
                        ? HistoryDatabaseService.Instance.GetRecentHistory(50)
                        : HistoryDatabaseService.Instance.GetHistoryByServer(_serverProfile, 50);
                });

                // UI 스레드에서 항목 추가
                foreach (var history in histories)
                {
                    _allItems.Add(new PaletteItem
                    {
                        Title = history.UserInput,
                        Subtitle = history.GeneratedCommand,
                        Icon = "History",
                        IconBackground = BlueBrush,
                        TypeText = "히스토리",
                        TypeBackground = BlueBrush,
                        ItemType = PaletteItemType.History,
                        Data = history
                    });
                }

                UpdateResults();
            }
            catch { }
        }

        private void UpdateResults()
        {
            var query = SearchTextBox?.Text?.Trim() ?? string.Empty;
            var filtered = _allItems.AsEnumerable();

            // 필터 적용
            if (FilterHistory?.IsChecked == true)
                filtered = filtered.Where(i => i.ItemType == PaletteItemType.History);
            else if (FilterSnippets?.IsChecked == true)
                filtered = filtered.Where(i => i.ItemType == PaletteItemType.Snippet);
            else if (FilterActions?.IsChecked == true)
                filtered = filtered.Where(i => i.ItemType == PaletteItemType.Action);

            // 검색어 필터
            if (!string.IsNullOrWhiteSpace(query))
            {
                var lowerQuery = query.ToLower();
                filtered = filtered.Where(i =>
                    i.Title.ToLower().Contains(lowerQuery) ||
                    i.Subtitle.ToLower().Contains(lowerQuery));
            }

            var results = filtered.Take(50).ToList();
            ResultsListBox.ItemsSource = results;

            // 결과 카운트 업데이트
            if (ResultCountText != null)
                ResultCountText.Text = $"{results.Count}개 결과";

            // 결과 없음 패널
            if (NoResultsPanel != null)
                NoResultsPanel.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 첫 번째 항목 선택
            if (results.Count > 0)
                ResultsListBox.SelectedIndex = 0;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateResults();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            UpdateResults();
        }

        private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectAndClose();
        }

        private void SelectAndClose()
        {
            if (ResultsListBox.SelectedItem is PaletteItem item)
            {
                SelectedItem = item;

                switch (item.ItemType)
                {
                    case PaletteItemType.History:
                        if (item.Data is CommandHistory history)
                            SelectedCommand = history.GeneratedCommand;
                        break;
                    case PaletteItemType.Snippet:
                        if (item.Data is CommandSnippet snippet)
                            SelectedCommand = snippet.Command;
                        break;
                    case PaletteItemType.Action:
                        SelectedAction = item.Data as string;
                        break;
                }

                DialogResult = true;
                Close();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    DialogResult = false;
                    Close();
                    break;

                case Key.Enter:
                    SelectAndClose();
                    break;

                case Key.Up:
                    if (ResultsListBox.SelectedIndex > 0)
                    {
                        ResultsListBox.SelectedIndex--;
                        ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                    }
                    e.Handled = true;
                    break;

                case Key.Down:
                    if (ResultsListBox.SelectedIndex < ResultsListBox.Items.Count - 1)
                    {
                        ResultsListBox.SelectedIndex++;
                        ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                    }
                    e.Handled = true;
                    break;
            }
        }
    }
}
