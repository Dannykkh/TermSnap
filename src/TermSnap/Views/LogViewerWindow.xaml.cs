using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// 실시간 로그 뷰어 윈도우
/// </summary>
public partial class LogViewerWindow : Window
{
    private readonly LogStreamService _logService;
    private readonly ObservableCollection<LogEntry> _allLogEntries = new();
    private readonly ObservableCollection<LogEntry> _filteredLogEntries = new();
    private string _currentFilter = string.Empty;
    private LogLevel? _levelFilter = null;
    private const int MaxLogEntries = 10000;

    public LogViewerWindow(ServerConfig config)
    {
        InitializeComponent();

        _logService = new LogStreamService(config);
        _logService.LogLineReceived += OnLogLineReceived;
        _logService.StreamingStateChanged += OnStreamingStateChanged;
        _logService.ErrorOccurred += OnErrorOccurred;

        LogListBox.ItemsSource = _filteredLogEntries;

        Title = $"{LocalizationService.Instance.GetString("LogViewer.Title")} - {config.ProfileName}";

        // 연결 시작
        _ = InitializeAsync();
    }

    private async System.Threading.Tasks.Task InitializeAsync()
    {
        try
        {
            StatusText.Text = LocalizationService.Instance.GetString("LogViewer.Connecting");
            await _logService.ConnectAsync();
            StatusText.Text = LocalizationService.Instance.GetString("LogViewer.ConnectedSelect");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("LogViewer.ConnectionFailed"), ex.Message),
                LocalizationService.Instance.GetString("FileTransfer.ConnectionError"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    /// <summary>
    /// 시작 버튼 클릭
    /// </summary>
    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var logPath = LogFileComboBox.Text?.Trim();
        if (string.IsNullOrEmpty(logPath))
        {
            MessageBox.Show(
                LocalizationService.Instance.GetString("LogViewer.EnterLogPath"),
                LocalizationService.Instance.GetString("LogViewer.InputRequired"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            StartButton.IsEnabled = false;
            StatusText.Text = LocalizationService.Instance.GetString("LogViewer.StartingStream");

            await _logService.StartStreamingAsync(logPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("LogViewer.StreamStartFailed"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StartButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 중지 버튼 클릭
    /// </summary>
    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StopButton.IsEnabled = false;
            StatusText.Text = LocalizationService.Instance.GetString("LogViewer.Stopping");
            await _logService.StopStreamingAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("LogViewer.StopFailed"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 지우기 버튼 클릭
    /// </summary>
    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _allLogEntries.Clear();
        _filteredLogEntries.Clear();
        UpdateLineCount();
    }

    /// <summary>
    /// 로그 라인 수신
    /// </summary>
    private void OnLogLineReceived(object? sender, LogLineEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var entry = new LogEntry
            {
                Line = e.Line,
                Timestamp = e.Timestamp,
                Level = e.Level
            };

            _allLogEntries.Add(entry);

            // 최대 라인 수 제한
            while (_allLogEntries.Count > MaxLogEntries)
            {
                _allLogEntries.RemoveAt(0);
            }

            // 필터 적용 후 추가
            if (PassesFilter(entry))
            {
                _filteredLogEntries.Add(entry);

                // 필터된 목록도 제한
                while (_filteredLogEntries.Count > MaxLogEntries)
                {
                    _filteredLogEntries.RemoveAt(0);
                }

                // 자동 스크롤
                if (AutoScrollCheckBox.IsChecked == true && _filteredLogEntries.Count > 0)
                {
                    LogListBox.ScrollIntoView(_filteredLogEntries[_filteredLogEntries.Count - 1]);
                }
            }

            UpdateLineCount();
        });
    }

    /// <summary>
    /// 스트리밍 상태 변경
    /// </summary>
    private void OnStreamingStateChanged(object? sender, bool isStreaming)
    {
        Dispatcher.Invoke(() =>
        {
            StartButton.IsEnabled = !isStreaming;
            StopButton.IsEnabled = isStreaming;
            LogFileComboBox.IsEnabled = !isStreaming;

            if (isStreaming)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(0, 200, 0));
                StatusText.Text = LocalizationService.Instance.GetString("LogViewer.Streaming");
            }
            else
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(128, 128, 128));
                StatusText.Text = LocalizationService.Instance.GetString("LogViewer.Stopped");
            }
        });
    }

    /// <summary>
    /// 오류 발생
    /// </summary>
    private void OnErrorOccurred(object? sender, string errorMessage)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = string.Format(LocalizationService.Instance.GetString("LogViewer.ErrorOccurred"), errorMessage);
        });
    }

    /// <summary>
    /// 필터 텍스트 변경
    /// </summary>
    private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentFilter = FilterTextBox.Text?.Trim() ?? string.Empty;
        ApplyFilter();
    }

    /// <summary>
    /// 레벨 필터 변경
    /// </summary>
    private void LevelFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LevelFilterComboBox.SelectedItem is ComboBoxItem item)
        {
            var content = item.Content?.ToString();
            _levelFilter = content switch
            {
                "Error" => LogLevel.Error,
                "Warning" => LogLevel.Warning,
                "Info" => LogLevel.Info,
                "Debug" => LogLevel.Debug,
                _ => null
            };
        }
        ApplyFilter();
    }

    /// <summary>
    /// 필터 적용
    /// </summary>
    private void ApplyFilter()
    {
        _filteredLogEntries.Clear();

        foreach (var entry in _allLogEntries)
        {
            if (PassesFilter(entry))
            {
                _filteredLogEntries.Add(entry);
            }
        }

        UpdateLineCount();

        // 스크롤을 마지막으로
        if (_filteredLogEntries.Count > 0 && AutoScrollCheckBox.IsChecked == true)
        {
            LogListBox.ScrollIntoView(_filteredLogEntries[_filteredLogEntries.Count - 1]);
        }
    }

    /// <summary>
    /// 필터 통과 여부 확인
    /// </summary>
    private bool PassesFilter(LogEntry entry)
    {
        // 레벨 필터
        if (_levelFilter.HasValue && entry.Level != _levelFilter.Value)
        {
            return false;
        }

        // 텍스트 필터
        if (!string.IsNullOrEmpty(_currentFilter))
        {
            if (!entry.Line.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 라인 수 업데이트
    /// </summary>
    private void UpdateLineCount()
    {
        if (LineCountText == null) return; // UI 요소가 아직 초기화되지 않았을 때

        LineCountText.Text = _levelFilter.HasValue || !string.IsNullOrEmpty(_currentFilter)
            ? string.Format(LocalizationService.Instance.GetString("LogViewer.LineCountWithFilter"), _filteredLogEntries.Count, _allLogEntries.Count)
            : string.Format(LocalizationService.Instance.GetString("LogViewer.LineCountTotal"), _allLogEntries.Count);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_logService.IsStreaming)
        {
            await _logService.StopStreamingAsync();
        }
        _logService.Dispose();
        base.OnClosing(e);
    }
}

/// <summary>
/// 로그 항목
/// </summary>
public class LogEntry
{
    public string Line { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }

    public string TimestampFormatted => Timestamp.ToString("HH:mm:ss.fff");

    public SolidColorBrush LevelColor => Level switch
    {
        LogLevel.Error => new SolidColorBrush(Color.FromRgb(244, 67, 54)),    // Red
        LogLevel.Warning => new SolidColorBrush(Color.FromRgb(255, 152, 0)),   // Orange
        LogLevel.Info => new SolidColorBrush(Color.FromRgb(33, 150, 243)),     // Blue
        LogLevel.Debug => new SolidColorBrush(Color.FromRgb(158, 158, 158)),   // Gray
        _ => new SolidColorBrush(Color.FromRgb(255, 255, 255))                 // White
    };
}
