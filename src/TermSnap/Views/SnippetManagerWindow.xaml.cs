using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// 스니펫 관리 윈도우
/// </summary>
public partial class SnippetManagerWindow : Window
{
    private readonly AppConfig _config;
    private CommandSnippetCollection _snippets;

    public SnippetManagerWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _snippets = _config.CommandSnippets;

        LoadSnippets();
        LoadCategories();
    }

    private void LoadSnippets()
    {
        SnippetsDataGrid.ItemsSource = _snippets.Snippets;
        CountTextBlock.Text = _snippets.Snippets.Count.ToString();
    }

    private void LoadCategories()
    {
        var categories = _snippets.GetAllCategories();
        categories.Insert(0, "전체");

        CategoryComboBox.ItemsSource = categories;
        CategoryComboBox.SelectedIndex = 0;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterSnippets();
    }

    private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FilterSnippets();
    }

    private void FilterSnippets()
    {
        var searchQuery = SearchTextBox.Text;
        var selectedCategory = CategoryComboBox.SelectedItem as string;

        var filtered = _snippets.Search(searchQuery);

        if (!string.IsNullOrEmpty(selectedCategory) && selectedCategory != "전체")
        {
            filtered = filtered.Where(s => s.Category == selectedCategory).ToList();
        }

        SnippetsDataGrid.ItemsSource = filtered;
        CountTextBlock.Text = filtered.Count.ToString();
    }

    private void NewSnippet_Click(object sender, RoutedEventArgs e)
    {
        var categories = _snippets.GetAllCategories();
        var dialog = new SnippetEditDialog(null, categories);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            var snippet = dialog.Snippet;
            if (snippet != null)
            {
                _snippets.Add(snippet);
                ConfigService.Save(_config);
                LoadSnippets();
                LoadCategories();
                FilterSnippets();
            }
        }
    }

    private void EditSnippet_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var snippet = button?.DataContext as CommandSnippet;

        if (snippet != null)
        {
            var categories = _snippets.GetAllCategories();
            var dialog = new SnippetEditDialog(snippet, categories);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                ConfigService.Save(_config);
                LoadSnippets();
                LoadCategories();
                FilterSnippets();
            }
        }
    }

    private void DeleteSnippet_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var snippet = button?.DataContext as CommandSnippet;

        if (snippet != null)
        {
            var result = MessageBox.Show(
                $"'{snippet.Name}' 스니펫을 삭제하시겠습니까?",
                "삭제 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _snippets.Remove(snippet.Id);
                ConfigService.Save(_config);
                LoadSnippets();
                LoadCategories();
                FilterSnippets();
            }
        }
    }

    private void SnippetsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var snippet = SnippetsDataGrid.SelectedItem as CommandSnippet;
        if (snippet != null)
        {
            var categories = _snippets.GetAllCategories();
            var dialog = new SnippetEditDialog(snippet, categories);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                ConfigService.Save(_config);
                LoadSnippets();
                FilterSnippets();
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>
/// 스니펫 편집 다이얼로그 - Material Design 스타일 적용
/// </summary>
public partial class SnippetEditDialog : Window
{
    public CommandSnippet? Snippet { get; private set; }
    private List<string> _existingCategories;

    public SnippetEditDialog(CommandSnippet? existing, List<string>? existingCategories = null)
    {
        _existingCategories = existingCategories ?? new List<string>();
        InitializeComponent();

        if (existing != null)
        {
            Title = "스니펫 편집";
            Snippet = existing;
            NameTextBox.Text = existing.Name;
            DescriptionTextBox.Text = existing.Description;
            CommandTextBox.Text = existing.Command;
            CategoryComboBox.Text = existing.Category;
            TagsTextBox.Text = string.Join(", ", existing.Tags);
        }
        else
        {
            Title = "새 스니펫";
            Snippet = new CommandSnippet();
            CategoryComboBox.SelectedIndex = 0; // "일반" 선택
        }
    }

    private void InitializeComponent()
    {
        Width = 550;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)FindResource("MaterialDesignPaper");

        // Material Design 테마 리소스 적용
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Defaults.xaml")
        });

        var mainGrid = new Grid { Margin = new Thickness(24) };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 헤더
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 콘텐츠
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 버튼

        // 헤더
        var headerText = new TextBlock
        {
            Text = "스니펫 정보 입력",
            FontSize = 18,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 16),
            Foreground = (System.Windows.Media.Brush)FindResource("MaterialDesignBody")
        };
        Grid.SetRow(headerText, 0);
        mainGrid.Children.Add(headerText);

        // 콘텐츠 영역
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var stackPanel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };

        // 이름 입력
        stackPanel.Children.Add(CreateLabeledTextBox("이름 *", out NameTextBox));

        // 설명 입력
        stackPanel.Children.Add(CreateLabeledTextBox("설명", out DescriptionTextBox));

        // 명령어 입력 (멀티라인)
        stackPanel.Children.Add(CreateLabeledTextBox("명령어 *", out CommandTextBox, isMultiline: true, height: 80));

        // 카테고리 선택 (ComboBox)
        stackPanel.Children.Add(CreateCategoryComboBox());

        // 태그 입력
        stackPanel.Children.Add(CreateLabeledTextBox("태그 (쉼표로 구분)", out TagsTextBox));

        scrollViewer.Content = stackPanel;
        Grid.SetRow(scrollViewer, 1);
        mainGrid.Children.Add(scrollViewer);

        // 버튼 영역
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var cancelButton = new Button
        {
            Content = "취소",
            Width = 90,
            Height = 36,
            Margin = new Thickness(0, 0, 12, 0),
            Style = (Style)FindResource("MaterialDesignOutlinedButton")
        };
        cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

        var saveButton = new Button
        {
            Content = "저장",
            Width = 90,
            Height = 36,
            Style = (Style)FindResource("MaterialDesignRaisedButton")
        };
        saveButton.Click += Save_Click;

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(saveButton);

        Grid.SetRow(buttonPanel, 2);
        mainGrid.Children.Add(buttonPanel);

        Content = mainGrid;
    }

    private TextBox NameTextBox = null!;
    private TextBox DescriptionTextBox = null!;
    private TextBox CommandTextBox = null!;
    private ComboBox CategoryComboBox = null!;
    private TextBox TagsTextBox = null!;

    private StackPanel CreateLabeledTextBox(string label, out TextBox textBox, bool isMultiline = false, int height = 0)
    {
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = (System.Windows.Media.Brush)FindResource("MaterialDesignBody")
        };
        container.Children.Add(labelBlock);

        textBox = new TextBox
        {
            FontSize = 14,
            Padding = new Thickness(12, 10, 12, 10),
            Style = (Style)FindResource("MaterialDesignOutlinedTextBox")
        };

        if (isMultiline)
        {
            textBox.AcceptsReturn = true;
            textBox.TextWrapping = TextWrapping.Wrap;
            textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            textBox.MinHeight = height > 0 ? height : 60;
            textBox.VerticalContentAlignment = VerticalAlignment.Top;
        }

        container.Children.Add(textBox);
        return container;
    }

    private StackPanel CreateCategoryComboBox()
    {
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        var labelBlock = new TextBlock
        {
            Text = "카테고리",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = (System.Windows.Media.Brush)FindResource("MaterialDesignBody")
        };
        container.Children.Add(labelBlock);

        CategoryComboBox = new ComboBox
        {
            IsEditable = true,
            FontSize = 14,
            Height = 40,
            Style = (Style)FindResource("MaterialDesignOutlinedComboBox")
        };

        // 카테고리 목록 구성: "일반" + 기존 카테고리들
        var categories = new List<string> { "일반" };
        foreach (var cat in _existingCategories.Where(c => c != "일반" && c != "전체"))
        {
            if (!categories.Contains(cat))
                categories.Add(cat);
        }
        CategoryComboBox.ItemsSource = categories;

        // 힌트 텍스트 설정
        MaterialDesignThemes.Wpf.HintAssist.SetHint(CategoryComboBox, "선택하거나 새 카테고리 입력");

        container.Children.Add(CategoryComboBox);

        // 안내 텍스트
        var hintText = new TextBlock
        {
            Text = "기존 카테고리를 선택하거나 새로운 카테고리를 직접 입력할 수 있습니다.",
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("MaterialDesignBodyLight")
        };
        container.Children.Add(hintText);

        return container;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show("이름을 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(CommandTextBox.Text))
        {
            MessageBox.Show("명령어를 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            CommandTextBox.Focus();
            return;
        }

        if (Snippet == null)
            Snippet = new CommandSnippet();

        Snippet.Name = NameTextBox.Text.Trim();
        Snippet.Description = DescriptionTextBox.Text.Trim();
        Snippet.Command = CommandTextBox.Text.Trim();

        // ComboBox에서 선택하거나 입력한 값 가져오기
        var categoryText = CategoryComboBox.Text;
        Snippet.Category = string.IsNullOrWhiteSpace(categoryText) ? "일반" : categoryText.Trim();

        Snippet.Tags.Clear();
        if (!string.IsNullOrWhiteSpace(TagsTextBox.Text))
        {
            var tags = TagsTextBox.Text.Split(',')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t));

            Snippet.Tags.AddRange(tags);
        }

        DialogResult = true;
        Close();
    }
}
