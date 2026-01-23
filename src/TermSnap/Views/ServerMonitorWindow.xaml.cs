using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// 서버 모니터링 대시보드
/// </summary>
public partial class ServerMonitorWindow : Window
{
    private readonly ServerMonitorService _monitorService;
    private readonly SshService _sshService;
    private readonly ServerConfig _config;
    private DispatcherTimer? _autoRefreshTimer;
    private readonly List<string> _monitoredServices = new()
    {
        "nginx", "apache2", "mysql", "mariadb", "postgresql",
        "redis", "docker", "ssh", "fail2ban"
    };

    public ServerMonitorWindow(SshService sshService, ServerConfig config)
    {
        InitializeComponent();

        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _monitorService = new ServerMonitorService(sshService);

        Title = $"{LocalizationService.Instance.GetString("ServerMonitor.Title")} - {config.ProfileName}";

        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            // 서버 통계 로드
            var stats = await _monitorService.GetServerStatsAsync();

            if (stats.IsSuccess)
            {
                UpdateUI(stats);
            }
            else
            {
                MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("ServerMonitor.LoadError"), stats.ErrorMessage),
                    LocalizationService.Instance.GetString("Common.Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            // 서비스 상태 로드
            await LoadServicesAsync();

            // 상위 프로세스 로드
            await LoadTopProcessesAsync();

            LastUpdatedText.Text = string.Format(LocalizationService.Instance.GetString("ServerMonitor.LastUpdated"), DateTime.Now);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("ServerMonitor.LoadFailed"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UpdateUI(ServerStats stats)
    {
        // CPU
        CpuPercentText.Text = $"{stats.CpuUsage:F1}%";
        CpuProgressBar.Value = stats.CpuUsage;
        ProcessCountText.Text = string.Format(LocalizationService.Instance.GetString("ServerMonitor.ProcessCountLabel"), stats.ProcessCount);

        // 메모리
        MemoryPercentText.Text = $"{stats.MemoryUsage:F1}%";
        MemoryProgressBar.Value = stats.MemoryUsage;
        MemoryDetailText.Text = string.Format(LocalizationService.Instance.GetString("ServerMonitor.MemoryUsageDetail"), stats.UsedMemory, stats.TotalMemory);

        // 디스크
        DiskPercentText.Text = $"{stats.DiskUsage:F1}%";
        DiskProgressBar.Value = stats.DiskUsage;
        DiskDetailText.Text = string.Format(LocalizationService.Instance.GetString("ServerMonitor.DiskUsageDetail"), stats.UsedDisk, stats.TotalDisk);

        // 시스템 정보
        OsInfoText.Text = string.Format(LocalizationService.Instance.GetString("ServerMonitor.OSInfoLabel"), stats.OsInfo);
        KernelVersionText.Text = string.Format(LocalizationService.Instance.GetString("ServerMonitor.KernelVersionLabel"), stats.KernelVersion);
        UptimeText.Text = string.Format(LocalizationService.Instance.GetString("ServerMonitor.UptimeLabel"), stats.Uptime);

        // 네트워크
        NetworkRxText.Text = string.Format(LocalizationService.Instance.GetString("ServerMonitor.NetworkRxLabel"), stats.NetworkRx);
        NetworkTxText.Text = string.Format(LocalizationService.Instance.GetString("ServerMonitor.NetworkTxLabel"), stats.NetworkTx);
        LoadAverageText.Text = string.Format(LocalizationService.Instance.GetString("ServerMonitor.LoadAverageLabel"), stats.LoadAverage);
    }

    private async Task LoadServicesAsync()
    {
        var services = new List<ServiceStatus>();

        foreach (var serviceName in _monitoredServices)
        {
            var status = await _monitorService.GetServiceStatusAsync(serviceName);
            services.Add(status);
        }

        ServicesDataGrid.ItemsSource = services;
    }

    private async Task LoadTopProcessesAsync()
    {
        var processes = await _monitorService.GetTopProcessesAsync(10);
        TopProcessesText.Text = processes;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private void AutoRefreshCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoRefreshCheckBox.IsChecked == true)
        {
            // 자동 새로고침 시작 (10초마다)
            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _autoRefreshTimer.Tick += async (s, ev) => await LoadDataAsync();
            _autoRefreshTimer.Start();
        }
        else
        {
            // 자동 새로고침 중지
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer = null;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoRefreshTimer?.Stop();
        base.OnClosed(e);
    }
}
