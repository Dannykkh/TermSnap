using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// 프로필 선택 윈도우
/// </summary>
public partial class ProfileSelectorWindow : Window
{
    public ServerConfig? SelectedProfile { get; private set; }
    private readonly List<ServerConfig> _profiles;

    public ProfileSelectorWindow(List<ServerConfig> profiles)
    {
        InitializeComponent();
        _profiles = profiles ?? new List<ServerConfig>();

        LoadProfiles();
    }

    private void LoadProfiles()
    {
        // 즐겨찾기 우선, 그 다음 최근 연결 순으로 정렬
        var sortedProfiles = _profiles
            .OrderByDescending(p => p.IsFavorite)
            .ThenByDescending(p => p.LastConnected)
            .ToList();

        ProfileListBox.ItemsSource = sortedProfiles;

        if (sortedProfiles.Any())
        {
            ProfileListBox.SelectedIndex = 0;
        }
    }

    private void ProfileListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProfileListBox.SelectedItem != null)
        {
            SelectProfile();
        }
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        SelectProfile();
    }

    private void SelectProfile()
    {
        if (ProfileListBox.SelectedItem is ServerConfig profile)
        {
            SelectedProfile = profile;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show(
                LocalizationService.Instance.GetString("ProfileSelector.SelectProfileMessage"),
                LocalizationService.Instance.GetString("Common.Notification"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        // 새 프로필은 SelectedProfile을 null로 설정하고 DialogResult를 true로 반환
        // 호출자는 이를 통해 설정 창을 열 수 있음
        SelectedProfile = null;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
