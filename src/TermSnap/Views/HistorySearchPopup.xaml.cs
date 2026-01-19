using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views
{
    /// <summary>
    /// 히스토리 검색 팝업 (Ctrl+R)
    /// </summary>
    public partial class HistorySearchPopup : Window
    {
        private readonly string? _serverProfile;
        private List<CommandHistory> _allHistory;
        
        /// <summary>
        /// 선택된 명령어
        /// </summary>
        public string? SelectedCommand { get; private set; }
        
        /// <summary>
        /// 선택된 히스토리 항목
        /// </summary>
        public CommandHistory? SelectedHistory { get; private set; }

        public HistorySearchPopup(string? serverProfile = null)
        {
            InitializeComponent();
            _serverProfile = serverProfile;
            _allHistory = new List<CommandHistory>();
            
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Focus();
            LoadHistory();
        }

        private void LoadHistory()
        {
            try
            {
                if (string.IsNullOrEmpty(_serverProfile))
                {
                    _allHistory = HistoryDatabaseService.Instance.GetRecentHistory(100);
                }
                else
                {
                    _allHistory = HistoryDatabaseService.Instance.GetHistoryByServer(_serverProfile, 100);
                }
                
                ResultsListBox.ItemsSource = _allHistory;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"히스토리 로드 실패: {ex.Message}");
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchTextBox.Text.Trim();
            
            if (string.IsNullOrWhiteSpace(query))
            {
                ResultsListBox.ItemsSource = _allHistory;
                return;
            }

            try
            {
                var searchResults = HistoryDatabaseService.Instance.Search(query, 50);
                
                // 서버 프로필로 필터링
                if (!string.IsNullOrEmpty(_serverProfile))
                {
                    searchResults = searchResults.FindAll(h => 
                        h.ServerProfile == _serverProfile || string.IsNullOrEmpty(h.ServerProfile));
                }
                
                ResultsListBox.ItemsSource = searchResults;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"검색 실패: {ex.Message}");
            }
        }

        private void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 선택 변경 시 처리 (필요시)
        }

        private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectAndClose();
        }

        private void SelectAndClose()
        {
            if (ResultsListBox.SelectedItem is CommandHistory history)
            {
                SelectedHistory = history;
                SelectedCommand = history.GeneratedCommand;
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
                    else if (ResultsListBox.Items.Count > 0)
                    {
                        ResultsListBox.SelectedIndex = 0;
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
