using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Nebula.Models;

namespace Nebula.Views;

/// <summary>
/// 명령어 히스토리 창
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly AppConfig _config;
    private List<CommandHistory> _allHistory;
    private List<CommandHistory> _filteredHistory;

    public CommandHistory? SelectedHistory { get; private set; }

    public HistoryWindow(AppConfig config)
    {
        InitializeComponent();

        _config = config;
        _allHistory = config.CommandHistory.Items.ToList();
        _filteredHistory = _allHistory;

        LoadProfiles();
        UpdateGrid();
    }

    private void LoadProfiles()
    {
        var profiles = new List<string> { "전체" };
        profiles.AddRange(_config.ServerProfiles.Select(p => p.ProfileName));
        ProfileFilterComboBox.ItemsSource = profiles;
        ProfileFilterComboBox.SelectedIndex = 0;
    }

    private void UpdateGrid()
    {
        HistoryDataGrid.ItemsSource = _filteredHistory;
        CountTextBlock.Text = $"전체 {_filteredHistory.Count}개";
    }

    private void ApplyFilters()
    {
        _filteredHistory = _allHistory;

        // 검색어 필터
        var searchText = SearchTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            _filteredHistory = _config.CommandHistory.Search(searchText);
        }

        // 프로필 필터
        if (ProfileFilterComboBox.SelectedIndex > 0)
        {
            var selectedProfile = ProfileFilterComboBox.SelectedItem.ToString();
            _filteredHistory = _filteredHistory.Where(h => h.ServerProfile == selectedProfile).ToList();
        }

        // 성공만 필터
        if (SuccessOnlyCheckBox.IsChecked == true)
        {
            _filteredHistory = _filteredHistory.Where(h => h.IsSuccess).ToList();
        }

        UpdateGrid();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void ProfileFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilters();
    }

    private void SuccessOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ApplyFilters();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "모든 히스토리를 삭제하시겠습니까?",
            "히스토리 삭제",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _config.CommandHistory.Clear();
            Services.ConfigService.Save(_config);

            _allHistory.Clear();
            _filteredHistory.Clear();
            UpdateGrid();

            MessageBox.Show("히스토리가 삭제되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void HistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is CommandHistory selected)
        {
            SelectedHistory = selected;
            DialogResult = true;
            Close();
        }
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is CommandHistory selected)
        {
            SelectedHistory = selected;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("히스토리 항목을 선택해주세요.", "선택 필요", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>
/// 상태 아이콘 컨버터
/// </summary>
public class StatusIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSuccess)
        {
            return isSuccess ? "✓" : "✗";
        }
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Bool to Yes/No 컨버터
/// </summary>
public class BoolToYesNoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "예" : "";
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
