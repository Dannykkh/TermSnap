using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TermSnap.Core;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// 명령어 관리 통합 창 - 검색, 상세보기, 편집, 삭제
/// </summary>
public partial class CommandManagerWindow : Window
{
    private readonly List<FrequentCommand> _allCommands;
    private List<FrequentCommand> _filteredCommands;
    private FrequentCommand? _currentCommand;
    private readonly Action<FrequentCommand>? _onSave;
    private readonly Action<FrequentCommand>? _onDelete;

    public enum WindowMode
    {
        Search,
        Detail
    }

    private WindowMode _mode = WindowMode.Search;

    /// <summary>
    /// 선택된 명령어 (결과 반환용)
    /// </summary>
    public FrequentCommand? SelectedCommand { get; private set; }

    /// <summary>
    /// 검색 모드로 열기
    /// </summary>
    public CommandManagerWindow(IEnumerable<FrequentCommand> commands,
        Action<FrequentCommand>? onSave = null,
        Action<FrequentCommand>? onDelete = null)
    {
        InitializeComponent();

        _allCommands = commands?.ToList() ?? new List<FrequentCommand>();
        _filteredCommands = new List<FrequentCommand>();
        _onSave = onSave;
        _onDelete = onDelete;

        SetMode(WindowMode.Search);
        UpdateResultCount();

        Loaded += (s, e) => SearchTextBox.Focus();
    }

    /// <summary>
    /// 상세보기 모드로 열기
    /// </summary>
    public CommandManagerWindow(FrequentCommand command, IEnumerable<FrequentCommand> allCommands,
        Action<FrequentCommand>? onSave = null,
        Action<FrequentCommand>? onDelete = null)
    {
        InitializeComponent();

        _currentCommand = command;
        _allCommands = allCommands?.ToList() ?? new List<FrequentCommand>();
        _filteredCommands = new List<FrequentCommand>();
        _onSave = onSave;
        _onDelete = onDelete;

        SetMode(WindowMode.Detail);
        ShowCommandDetail(command);

        Loaded += (s, e) => DetailDescription.Focus();
    }

    /// <summary>
    /// 모드 설정
    /// </summary>
    private void SetMode(WindowMode mode)
    {
        _mode = mode;

        if (mode == WindowMode.Search)
        {
            Title = "명령어 검색";
            TitleIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Magnify;
            TitleText.Text = "명령어 검색";

            SearchPanel.Visibility = Visibility.Visible;
            ResultsListBox.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            SearchButtons.Visibility = Visibility.Visible;
            DetailButtons.Visibility = Visibility.Collapsed;
            BackToSearchButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            Title = "명령어 상세";
            TitleIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CodeBraces;
            TitleText.Text = "명령어 상세";

            SearchPanel.Visibility = Visibility.Collapsed;
            ResultsListBox.Visibility = Visibility.Collapsed;
            InitialPanel.Visibility = Visibility.Collapsed;
            NoResultsPanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            SearchButtons.Visibility = Visibility.Collapsed;
            DetailButtons.Visibility = Visibility.Visible;

            // 검색에서 상세로 이동한 경우에만 "목록" 버튼 표시
            BackToSearchButton.Visibility = _filteredCommands.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 명령어 상세 정보 표시
    /// </summary>
    private void ShowCommandDetail(FrequentCommand command)
    {
        _currentCommand = command;
        DetailDescription.Text = command.Description ?? "";
        DetailCommand.Text = command.Command ?? "";
        DetailUseCount.Text = $"총 {command.TotalUseCount}회 사용";
        DetailLastUsed.Text = command.LastUsed != default
            ? $"마지막 사용: {command.LastUsed:yyyy-MM-dd HH:mm}"
            : "사용 기록 없음";
        ResultCountText.Text = "";
    }

    /// <summary>
    /// 검색 실행
    /// </summary>
    private void PerformSearch()
    {
        var searchText = SearchTextBox.Text?.Trim().ToLower() ?? "";

        if (string.IsNullOrEmpty(searchText))
        {
            _filteredCommands.Clear();
            ResultsListBox.ItemsSource = null;
            InitialPanel.Visibility = Visibility.Visible;
            NoResultsPanel.Visibility = Visibility.Collapsed;
            UpdateResultCount();
            return;
        }

        InitialPanel.Visibility = Visibility.Collapsed;

        _filteredCommands = _allCommands
            .Where(c =>
                (c.Description?.ToLower().Contains(searchText) ?? false) ||
                (c.Command?.ToLower().Contains(searchText) ?? false))
            .OrderByDescending(c => c.TotalUseCount)
            .ThenByDescending(c => c.LastUsed)
            .ToList();

        ResultsListBox.ItemsSource = _filteredCommands;

        NoResultsPanel.Visibility = _filteredCommands.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateResultCount();
    }

    /// <summary>
    /// 결과 개수 업데이트
    /// </summary>
    private void UpdateResultCount()
    {
        if (_filteredCommands.Count > 0)
        {
            ResultCountText.Text = $"{_filteredCommands.Count}개 결과 (전체 {_allCommands.Count}개 중)";
        }
        else if (string.IsNullOrEmpty(SearchTextBox.Text))
        {
            ResultCountText.Text = $"전체 {_allCommands.Count}개의 명령어";
        }
        else
        {
            ResultCountText.Text = "검색 결과 없음";
        }
    }

    /// <summary>
    /// 선택된 명령어 사용
    /// </summary>
    private void UseSelectedCommand()
    {
        if (_mode == WindowMode.Detail && _currentCommand != null)
        {
            // 상세 모드: 편집된 명령어 사용
            SelectedCommand = new FrequentCommand
            {
                Description = DetailDescription.Text,
                Command = DetailCommand.Text,
                TotalUseCount = _currentCommand.TotalUseCount,
                LastUsed = DateTime.Now
            };
            DialogResult = true;
            Close();
        }
        else if (ResultsListBox.SelectedItem is FrequentCommand command)
        {
            SelectedCommand = command;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("명령어를 선택해주세요.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    #region Event Handlers

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PerformSearch();
        }
        else if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Down && _filteredCommands.Count > 0)
        {
            ResultsListBox.Focus();
            if (ResultsListBox.SelectedIndex < 0)
            {
                ResultsListBox.SelectedIndex = 0;
            }
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PerformSearch();
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        PerformSearch();
    }

    private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        UseSelectedCommand();
    }

    private void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 선택 변경 시 상세보기로 전환 가능 (옵션)
    }

    private void UseButton_Click(object sender, RoutedEventArgs e)
    {
        UseSelectedCommand();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BackToSearch_Click(object sender, RoutedEventArgs e)
    {
        SetMode(WindowMode.Search);
        UpdateResultCount();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCommand == null) return;

        if (string.IsNullOrWhiteSpace(DetailCommand.Text))
        {
            MessageBox.Show("명령어를 입력해주세요.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _currentCommand.Description = DetailDescription.Text?.Trim() ?? "";
        _currentCommand.Command = DetailCommand.Text?.Trim() ?? "";

        _onSave?.Invoke(_currentCommand);

        MessageBox.Show("저장되었습니다.", "알림",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCommand == null) return;

        var result = MessageBox.Show(
            $"'{_currentCommand.Description}'을(를) 삭제하시겠습니까?",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _onDelete?.Invoke(_currentCommand);
            DialogResult = false;
            Close();
        }
    }

    private async void GenerateAIDescription_Click(object sender, RoutedEventArgs e)
    {
        var command = DetailCommand.Text?.Trim();
        if (string.IsNullOrEmpty(command))
        {
            MessageBox.Show("명령어를 먼저 입력해주세요.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var aiProvider = AIProviderManager.Instance.CurrentProvider;
        if (aiProvider == null)
        {
            MessageBox.Show("AI 제공자가 설정되지 않았습니다.\n설정에서 API 키를 입력해주세요.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // 버튼 비활성화
            if (sender is System.Windows.Controls.Button btn)
                btn.IsEnabled = false;

            DetailDescription.Text = "AI가 설명을 생성하는 중...";

            var explanation = await aiProvider.ExplainCommand(command);
            DetailDescription.Text = explanation;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"AI 설명 생성 실패: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
            DetailDescription.Text = "";
        }
        finally
        {
            if (sender is System.Windows.Controls.Button btn)
                btn.IsEnabled = true;
        }
    }

    #endregion
}
