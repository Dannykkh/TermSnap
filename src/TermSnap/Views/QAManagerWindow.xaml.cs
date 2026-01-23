using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TermSnap.Models;
using TermSnap.Services;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace TermSnap.Views;

/// <summary>
/// Q&A 지식베이스 관리 창
/// </summary>
public partial class QAManagerWindow : Window
{
    private readonly QADatabaseService _qaService;
    private List<QAEntry> _allEntries = new();
    private QAEntry? _selectedEntry;
    private bool _isNewEntry = true;

    public QAManagerWindow()
    {
        InitializeComponent();
        _qaService = QADatabaseService.Instance;
        
        Loaded += QAManagerWindow_Loaded;
    }

    private void QAManagerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadEntries();
        LoadCategories();
        UpdateStatistics();
        ClearEditPanel();
    }

    /// <summary>
    /// 모든 항목 로드
    /// </summary>
    private void LoadEntries()
    {
        _allEntries = _qaService.GetAllEntries();
        QAListBox.ItemsSource = _allEntries;
    }

    /// <summary>
    /// 카테고리 로드
    /// </summary>
    private void LoadCategories()
    {
        var categories = _qaService.GetCategories();
        CategoryComboBox.ItemsSource = categories;
    }

    /// <summary>
    /// 통계 업데이트
    /// </summary>
    private void UpdateStatistics()
    {
        var (total, withEmbedding, totalUse) = _qaService.GetStatistics();
        StatsTextBlock.Text = $"총 {total}개 | 벡터화됨: {withEmbedding}개 | 총 사용: {totalUse}회";
    }

    /// <summary>
    /// 편집 패널 초기화
    /// </summary>
    private void ClearEditPanel()
    {
        _selectedEntry = null;
        _isNewEntry = true;
        
        EditHeaderText.Text = "새 Q&A 추가";
        QuestionTextBox.Text = "";
        AnswerTextBox.Text = "";
        CategoryComboBox.Text = "";
        TagsTextBox.Text = "";
        EmbeddingStatusText.Text = "";
        
        DeleteButton.Visibility = Visibility.Collapsed;
        QAListBox.SelectedItem = null;
    }

    /// <summary>
    /// 항목 선택 시
    /// </summary>
    private void QAListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QAListBox.SelectedItem is QAEntry entry)
        {
            _selectedEntry = entry;
            _isNewEntry = false;
            
            EditHeaderText.Text = "Q&A 수정";
            QuestionTextBox.Text = entry.Question;
            AnswerTextBox.Text = entry.Answer;
            CategoryComboBox.Text = entry.Category ?? "";
            TagsTextBox.Text = entry.Tags ?? "";
            
            // 임베딩 상태 표시
            if (!string.IsNullOrEmpty(entry.EmbeddingVector))
            {
                EmbeddingStatusText.Text = "✅ 벡터 임베딩 완료";
            }
            else
            {
                EmbeddingStatusText.Text = "⚠️ 벡터 임베딩 없음 (저장 시 자동 생성)";
            }
            
            DeleteButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 새 항목 추가 버튼
    /// </summary>
    private void AddNewEntry_Click(object sender, RoutedEventArgs e)
    {
        ClearEditPanel();
        QuestionTextBox.Focus();
    }

    /// <summary>
    /// 저장 버튼
    /// </summary>
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var question = QuestionTextBox.Text.Trim();
        var answer = AnswerTextBox.Text.Trim();

        if (string.IsNullOrEmpty(question) || string.IsNullOrEmpty(answer))
        {
            MessageBox.Show(
                LocalizationService.Instance.GetString("QAManager.EnterQuestionAndAnswer"),
                LocalizationService.Instance.GetString("QAManager.ValidationError"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            SaveButton.IsEnabled = false;
            SaveButton.Content = LocalizationService.Instance.GetString("QAManager.Saving");

            if (_isNewEntry)
            {
                // 새 항목 추가
                var entry = new QAEntry
                {
                    Question = question,
                    Answer = answer,
                    Category = string.IsNullOrEmpty(CategoryComboBox.Text) ? null : CategoryComboBox.Text,
                    Tags = string.IsNullOrEmpty(TagsTextBox.Text) ? null : TagsTextBox.Text
                };

                await _qaService.AddEntry(entry);
                MessageBox.Show(
                    LocalizationService.Instance.GetString("QAManager.QAAdded"),
                    LocalizationService.Instance.GetString("Common.Success"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (_selectedEntry != null)
            {
                // 기존 항목 수정
                _selectedEntry.Question = question;
                _selectedEntry.Answer = answer;
                _selectedEntry.Category = string.IsNullOrEmpty(CategoryComboBox.Text) ? null : CategoryComboBox.Text;
                _selectedEntry.Tags = string.IsNullOrEmpty(TagsTextBox.Text) ? null : TagsTextBox.Text;

                await _qaService.UpdateEntry(_selectedEntry);
                MessageBox.Show(
                    LocalizationService.Instance.GetString("QAManager.QAUpdated"),
                    LocalizationService.Instance.GetString("Common.Success"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            LoadEntries();
            LoadCategories();
            UpdateStatistics();
            ClearEditPanel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("QAManager.SaveFailed"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
            SaveButton.Content = "💾 저장";
        }
    }

    /// <summary>
    /// 삭제 버튼
    /// </summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry == null) return;

        var result = MessageBox.Show(
            $"'{_selectedEntry.Question}'을(를) 삭제하시겠습니까?",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _qaService.DeleteEntry(_selectedEntry.Id);
            LoadEntries();
            UpdateStatistics();
            ClearEditPanel();
        }
    }

    /// <summary>
    /// 취소 버튼
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ClearEditPanel();
    }

    /// <summary>
    /// 검색 버튼
    /// </summary>
    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await PerformSearch();
    }

    /// <summary>
    /// 검색창 Enter 키
    /// </summary>
    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await PerformSearch();
        }
    }

    /// <summary>
    /// 검색 수행
    /// </summary>
    private async System.Threading.Tasks.Task PerformSearch()
    {
        var query = SearchTextBox.Text.Trim();

        if (string.IsNullOrEmpty(query))
        {
            LoadEntries();
            return;
        }

        try
        {
            SearchButton.IsEnabled = false;
            SearchButton.Content = "검색 중...";

            // 하이브리드 검색 수행
            var results = await _qaService.HybridSearch(query, 50);
            
            _allEntries = results.Select(r => r.Entry).ToList();
            QAListBox.ItemsSource = _allEntries;

            StatsTextBlock.Text = $"검색 결과: {results.Count}개";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(LocalizationService.Instance.GetString("QAManager.SearchFailed"), ex.Message),
                LocalizationService.Instance.GetString("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SearchButton.IsEnabled = true;
            SearchButton.Content = "🔍 검색";
        }
    }

    /// <summary>
    /// JSON 가져오기
    /// </summary>
    private async void ImportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON 파일 (*.json)|*.json",
            Title = "Q&A 데이터 가져오기"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = await File.ReadAllTextAsync(dialog.FileName);
                var entries = JsonConvert.DeserializeObject<List<QAEntry>>(json);

                if (entries == null || entries.Count == 0)
                {
                    MessageBox.Show(
                        LocalizationService.Instance.GetString("QAManager.NoDataToImport"),
                        LocalizationService.Instance.GetString("Common.Notification"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("QAManager.ConfirmImport"), entries.Count),
                    LocalizationService.Instance.GetString("QAManager.ImportConfirmTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var imported = 0;
                    foreach (var entry in entries)
                    {
                        try
                        {
                            await _qaService.AddEntry(entry);
                            imported++;
                        }
                        catch { /* 개별 항목 실패 무시 */ }
                    }

                    MessageBox.Show(
                        string.Format(LocalizationService.Instance.GetString("QAManager.ImportComplete"), imported),
                        LocalizationService.Instance.GetString("QAManager.Complete"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadEntries();
                    LoadCategories();
                    UpdateStatistics();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("QAManager.ImportFailed"), ex.Message),
                    LocalizationService.Instance.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// JSON 내보내기
    /// </summary>
    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON 파일 (*.json)|*.json",
            Title = "Q&A 데이터 내보내기",
            FileName = $"qa_export_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var entries = _qaService.GetAllEntries();
                
                // 내보내기용으로 간소화 (임베딩 벡터 제외)
                var exportData = entries.Select(e => new
                {
                    e.Question,
                    e.Answer,
                    e.Category,
                    e.Tags
                }).ToList();

                var json = JsonConvert.SerializeObject(exportData, Formatting.Indented);
                await File.WriteAllTextAsync(dialog.FileName, json);

                MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("QAManager.ExportComplete"), entries.Count),
                    LocalizationService.Instance.GetString("QAManager.Complete"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("QAManager.ExportFailed"), ex.Message),
                    LocalizationService.Instance.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// 임베딩 일괄 생성
    /// </summary>
    private async void GenerateEmbeddings_Click(object sender, RoutedEventArgs e)
    {
        var (total, withEmbedding, _) = _qaService.GetStatistics();
        var withoutEmbedding = total - withEmbedding;

        if (withoutEmbedding == 0)
        {
            MessageBox.Show(
                LocalizationService.Instance.GetString("QAManager.AllVectorized"),
                LocalizationService.Instance.GetString("Common.Notification"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            string.Format(LocalizationService.Instance.GetString("QAManager.ConfirmEmbedding"), withoutEmbedding),
            LocalizationService.Instance.GetString("QAManager.EmbeddingConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                GenerateEmbeddingsButton.IsEnabled = false;
                GenerateEmbeddingsButton.Content = "생성 중...";

                var processed = await _qaService.GenerateEmbeddingsForExistingEntries(100);

                MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("QAManager.EmbeddingComplete"), processed),
                    LocalizationService.Instance.GetString("QAManager.Complete"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("QAManager.EmbeddingFailed"), ex.Message),
                    LocalizationService.Instance.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateEmbeddingsButton.IsEnabled = true;
                GenerateEmbeddingsButton.Content = "🧠 임베딩 일괄 생성";
            }
        }
    }
}
