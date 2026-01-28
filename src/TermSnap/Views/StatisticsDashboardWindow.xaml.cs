using System;
using System.Windows;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// 사용량 통계 대시보드 창
/// </summary>
public partial class StatisticsDashboardWindow : Window
{
    private readonly UsageStatisticsService _statisticsService;
    private readonly HistoryDatabaseService _historyDb;

    public StatisticsDashboardWindow()
    {
        InitializeComponent();

        _statisticsService = UsageStatisticsService.Instance;
        _historyDb = HistoryDatabaseService.Instance;

        Loaded += StatisticsDashboardWindow_Loaded;
    }

    private void StatisticsDashboardWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadStatistics();
    }

    /// <summary>
    /// 통계 데이터 로드
    /// </summary>
    private void LoadStatistics()
    {
        try
        {
            // 전체 통계 요약
            var summary = _statisticsService.GetStatisticsSummary();
            TotalApiCallsText.Text = summary.TotalApiCalls.ToString("N0");
            CacheHitRateText.Text = $"{summary.CacheHitRate:F1}%";
            TotalCommandsText.Text = summary.TotalCommandsExecuted.ToString("N0");
            SuccessRateText.Text = $"{summary.SuccessRate:F1}%";
            SavedApiCallsText.Text = summary.SavedApiCalls.ToString("N0");

            // 세션 시작 시간
            SessionStartText.Text = _statisticsService.SessionStartTime.ToString("HH:mm:ss");

            // 일별 통계 (최근 14일)
            var dailyStats = _statisticsService.GetDailyStatistics(14);
            DailyStatsGrid.ItemsSource = dailyStats;

            // 자주 사용하는 명령어
            var frequentCommands = _historyDb.GetFrequentCommands(10);
            FrequentCommandsList.ItemsSource = frequentCommands;

            // 카테고리별 통계
            var categoryStats = _statisticsService.GetCategoryStatistics();
            CategoryStatsList.ItemsSource = categoryStats;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StatisticsDashboard] 통계 로드 오류: {ex.Message}");
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadStatistics();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
