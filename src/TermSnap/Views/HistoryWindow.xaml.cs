using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

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
        var profiles = new List<string> { LocalizationService.Instance.GetString("History.All") };
        profiles.AddRange(_config.ServerProfiles.Select(p => p.ProfileName));
        ProfileFilterComboBox.ItemsSource = profiles;
        ProfileFilterComboBox.SelectedIndex = 0;
    }

    private void UpdateGrid()
    {
        HistoryDataGrid.ItemsSource = _filteredHistory;
        CountTextBlock.Text = string.Format(LocalizationService.Instance.GetString("History.TotalCount"), _filteredHistory.Count);
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
            LocalizationService.Instance.GetString("History.ConfirmClearAll"),
            LocalizationService.Instance.GetString("History.DeleteTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _config.CommandHistory.Clear();
            Services.ConfigService.Save(_config);

            _allHistory.Clear();
            _filteredHistory.Clear();
            UpdateGrid();

            MessageBox.Show(
                LocalizationService.Instance.GetString("History.DeleteSuccess"),
                LocalizationService.Instance.GetString("Common.Complete"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
            MessageBox.Show(
                LocalizationService.Instance.GetString("History.SelectRequired"),
                LocalizationService.Instance.GetString("History.SelectionRequired"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
            return boolValue ? LocalizationService.Instance.GetString("Common.Yes") : "";
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
