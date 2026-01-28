using System;
using System.Windows;
using System.Windows.Controls;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// 서브 프로세스 패널 - 실행 중인 자식 프로세스 관리
/// </summary>
public partial class SubProcessPanel : UserControl
{
    private SubProcessManager? _manager;

    /// <summary>
    /// 패널 닫기 요청 이벤트
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 로그 보기 요청 이벤트
    /// </summary>
    public event EventHandler<SubProcessInfo>? ViewLogRequested;

    public SubProcessPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 서브 프로세스 매니저 설정
    /// </summary>
    public void SetManager(SubProcessManager manager)
    {
        if (_manager != null)
        {
            _manager.ProcessStarted -= Manager_ProcessStarted;
            _manager.ProcessStopped -= Manager_ProcessStopped;
        }

        _manager = manager;

        if (_manager != null)
        {
            _manager.ProcessStarted += Manager_ProcessStarted;
            _manager.ProcessStopped += Manager_ProcessStopped;

            ProcessListView.ItemsSource = _manager.Processes;
            UpdateVisibility();
        }
    }

    private void Manager_ProcessStarted(object? sender, SubProcessInfo e)
    {
        UpdateVisibility();
    }

    private void Manager_ProcessStopped(object? sender, SubProcessInfo e)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_manager == null) return;

        var hasProcesses = _manager.Processes.Count > 0;
        EmptyMessage.Visibility = hasProcesses ? Visibility.Collapsed : Visibility.Visible;
        ProcessListView.Visibility = hasProcesses ? Visibility.Visible : Visibility.Collapsed;

        var runningCount = 0;
        foreach (var p in _manager.Processes)
        {
            if (p.IsRunning) runningCount++;
        }
        ProcessCountText.Text = $" ({runningCount})";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = _manager?.RefreshProcessesAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ViewLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SubProcessInfo info)
        {
            ViewLogRequested?.Invoke(this, info);
        }
    }

    private async void KillOrRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SubProcessInfo info && _manager != null)
        {
            if (info.IsRunning)
            {
                // 실행 중이면 종료
                var result = MessageBox.Show(
                    $"'{info.ProcessName}' 프로세스를 종료하시겠습니까?",
                    "프로세스 종료",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _manager.KillProcessAsync(info.ProcessId);
                    UpdateVisibility();
                }
            }
            else
            {
                // 종료됐으면 목록에서 제거
                _manager.RemoveStoppedProcess(info);
                UpdateVisibility();
            }
        }
    }
}
