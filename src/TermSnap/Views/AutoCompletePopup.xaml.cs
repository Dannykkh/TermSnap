using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// 자동완성 제안 항목
/// </summary>
public class AutoCompleteSuggestion
{
    public string UserInput { get; set; } = string.Empty;
    public string GeneratedCommand { get; set; } = string.Empty;
    public double Similarity { get; set; }
    public string SimilarityText => $"{Similarity:P0}";
    public CommandHistory? SourceHistory { get; set; }
}

/// <summary>
/// 자동완성 팝업 - 유사한 이전 질문 제안
/// </summary>
public partial class AutoCompletePopup : UserControl
{
    private CancellationTokenSource? _searchCts;
    private string _lastSearchQuery = string.Empty;

    /// <summary>
    /// 제안 선택됨 이벤트
    /// </summary>
    public event EventHandler<AutoCompleteSuggestion>? SuggestionSelected;

    /// <summary>
    /// 제안 확정됨 이벤트 (더블클릭 또는 Enter)
    /// </summary>
    public event EventHandler<AutoCompleteSuggestion>? SuggestionConfirmed;

    /// <summary>
    /// 닫기 요청 이벤트
    /// </summary>
    public event EventHandler? CloseRequested;

    public AutoCompletePopup()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 검색 수행
    /// </summary>
    public async Task SearchAsync(string query, string? serverProfile = null)
    {
        // 최소 2글자 이상
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            ClearSuggestions();
            return;
        }

        // 동일 쿼리 중복 검색 방지
        if (query == _lastSearchQuery)
            return;

        _lastSearchQuery = query;

        // 이전 검색 취소
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            ShowLoading();

            // 디바운스 (300ms)
            await Task.Delay(300, token);

            if (token.IsCancellationRequested)
                return;

            // RAG 서비스 확인
            var ragService = RAGService.Instance;
            if (ragService == null)
            {
                ShowNoResults();
                return;
            }

            // RAG 서비스로 유사 질문 검색
            var results = await ragService.FindSimilarQuestions(query, 5);

            if (token.IsCancellationRequested)
                return;

            // 현재 입력과 동일한 항목 제외
            var suggestions = results
                .Where(h => !h.UserInput.Equals(query, StringComparison.OrdinalIgnoreCase))
                .Select(h => new AutoCompleteSuggestion
                {
                    UserInput = h.UserInput,
                    GeneratedCommand = h.GeneratedCommand,
                    Similarity = CalculateSimilarity(query, h.UserInput),
                    SourceHistory = h
                })
                .OrderByDescending(s => s.Similarity)
                .Take(5)
                .ToList();

            ShowSuggestions(suggestions);
        }
        catch (OperationCanceledException)
        {
            // 취소됨 - 무시
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoComplete] 검색 오류: {ex.Message}");
            ShowNoResults();
        }
    }

    /// <summary>
    /// 제안 표시
    /// </summary>
    private void ShowSuggestions(List<AutoCompleteSuggestion> suggestions)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;

        if (suggestions.Count == 0)
        {
            ShowNoResults();
            return;
        }

        SuggestionList.ItemsSource = suggestions;
        SuggestionList.Visibility = Visibility.Visible;
        NoResultsText.Visibility = Visibility.Collapsed;
        CountText.Text = $"({suggestions.Count})";

        // 첫 번째 항목 선택
        if (suggestions.Count > 0)
        {
            SuggestionList.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// 로딩 표시
    /// </summary>
    private void ShowLoading()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        SuggestionList.Visibility = Visibility.Collapsed;
        NoResultsText.Visibility = Visibility.Collapsed;
        CountText.Text = "";
    }

    /// <summary>
    /// 결과 없음 표시
    /// </summary>
    private void ShowNoResults()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        SuggestionList.Visibility = Visibility.Collapsed;
        NoResultsText.Visibility = Visibility.Visible;
        CountText.Text = "(0)";
    }

    /// <summary>
    /// 제안 초기화
    /// </summary>
    public void ClearSuggestions()
    {
        _searchCts?.Cancel();
        _lastSearchQuery = string.Empty;
        SuggestionList.ItemsSource = null;
        LoadingPanel.Visibility = Visibility.Collapsed;
        NoResultsText.Visibility = Visibility.Collapsed;
        CountText.Text = "";
    }

    /// <summary>
    /// 선택 항목 위로 이동
    /// </summary>
    public void MoveSelectionUp()
    {
        if (SuggestionList.Items.Count == 0) return;

        var index = SuggestionList.SelectedIndex;
        if (index > 0)
        {
            SuggestionList.SelectedIndex = index - 1;
            SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
        }
    }

    /// <summary>
    /// 선택 항목 아래로 이동
    /// </summary>
    public void MoveSelectionDown()
    {
        if (SuggestionList.Items.Count == 0) return;

        var index = SuggestionList.SelectedIndex;
        if (index < SuggestionList.Items.Count - 1)
        {
            SuggestionList.SelectedIndex = index + 1;
            SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
        }
    }

    /// <summary>
    /// 현재 선택 항목 확정
    /// </summary>
    public void ConfirmSelection()
    {
        if (SuggestionList.SelectedItem is AutoCompleteSuggestion suggestion)
        {
            SuggestionConfirmed?.Invoke(this, suggestion);
        }
    }

    /// <summary>
    /// 현재 선택된 제안 반환
    /// </summary>
    public AutoCompleteSuggestion? GetSelectedSuggestion()
    {
        return SuggestionList.SelectedItem as AutoCompleteSuggestion;
    }

    /// <summary>
    /// 제안이 있는지 확인
    /// </summary>
    public bool HasSuggestions => SuggestionList.Items.Count > 0;

    private void SuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuggestionList.SelectedItem is AutoCompleteSuggestion suggestion)
        {
            SuggestionSelected?.Invoke(this, suggestion);
        }
    }

    private void SuggestionList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void SuggestionList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 텍스트 유사도 계산 (자카드 유사도)
    /// </summary>
    private static double CalculateSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0;

        var words1 = text1.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = text2.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (words1.Count == 0 && words2.Count == 0)
            return 1;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union == 0 ? 0 : (double)intersection / union;
    }
}
