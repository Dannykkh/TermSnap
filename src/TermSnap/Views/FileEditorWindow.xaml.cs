using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// 원격 파일 편집기 윈도우
/// </summary>
public partial class FileEditorWindow : Window
{
    private readonly SftpService _sftpService;
    private readonly string _remotePath;
    private string _originalContent = string.Empty;
    private bool _isModified = false;
    private Encoding _currentEncoding = Encoding.UTF8;

    public FileEditorWindow(SftpService sftpService, string remotePath)
    {
        InitializeComponent();

        _sftpService = sftpService ?? throw new ArgumentNullException(nameof(sftpService));
        _remotePath = remotePath ?? throw new ArgumentNullException(nameof(remotePath));

        FilePathText.Text = remotePath;
        Title = $"파일 편집기 - {Path.GetFileName(remotePath)}";

        // 구문 강조 설정
        SetSyntaxHighlighting(remotePath);

        // 커서 위치 이벤트
        TextEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;

        // 키보드 단축키
        this.KeyDown += FileEditorWindow_KeyDown;

        // 파일 로드
        LoadFileAsync();
    }

    /// <summary>
    /// 파일 로드
    /// </summary>
    private async void LoadFileAsync()
    {
        try
        {
            IsEnabled = false;
            Title = $"파일 편집기 - {Path.GetFileName(_remotePath)} (로딩 중...)";

            // 파일 정보 조회
            var fileInfo = await _sftpService.GetFileInfoAsync(_remotePath);
            if (fileInfo != null)
            {
                // 대용량 파일 경고 (1MB 이상)
                if (fileInfo.Size > 1024 * 1024)
                {
                    var result = MessageBox.Show(
                        $"파일 크기가 {fileInfo.SizeFormatted}입니다.\n대용량 파일은 편집이 느릴 수 있습니다.\n계속하시겠습니까?",
                        "대용량 파일 경고",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                    {
                        Close();
                        return;
                    }
                }

                FileSizeText.Text = fileInfo.SizeFormatted;
            }

            // 파일 내용 읽기
            var content = await _sftpService.ReadFileAsync(_remotePath, _currentEncoding);

            _originalContent = content;
            TextEditor.Text = content;
            _isModified = false;
            UpdateModifiedIndicator();

            Title = $"파일 편집기 - {Path.GetFileName(_remotePath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"파일을 열 수 없습니다.\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
        finally
        {
            IsEnabled = true;
        }
    }

    /// <summary>
    /// 구문 강조 설정
    /// </summary>
    private void SetSyntaxHighlighting(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        IHighlightingDefinition? highlighting = null;

        // 확장자별 구문 강조 매핑
        highlighting = ext switch
        {
            ".cs" => HighlightingManager.Instance.GetDefinition("C#"),
            ".py" => HighlightingManager.Instance.GetDefinition("Python"),
            ".js" or ".ts" => HighlightingManager.Instance.GetDefinition("JavaScript"),
            ".json" => HighlightingManager.Instance.GetDefinition("Json"),
            ".xml" or ".xaml" or ".csproj" => HighlightingManager.Instance.GetDefinition("XML"),
            ".html" or ".htm" => HighlightingManager.Instance.GetDefinition("HTML"),
            ".css" => HighlightingManager.Instance.GetDefinition("CSS"),
            ".sql" => HighlightingManager.Instance.GetDefinition("TSQL"),
            ".sh" or ".bash" or ".zsh" => HighlightingManager.Instance.GetDefinition("Boo"), // Bash에 가까운 것 사용
            ".cpp" or ".c" or ".h" or ".hpp" => HighlightingManager.Instance.GetDefinition("C++"),
            ".java" => HighlightingManager.Instance.GetDefinition("Java"),
            ".php" => HighlightingManager.Instance.GetDefinition("PHP"),
            ".rb" => HighlightingManager.Instance.GetDefinition("Ruby"),
            ".md" => HighlightingManager.Instance.GetDefinition("MarkDown"),
            _ => null
        };

        TextEditor.SyntaxHighlighting = highlighting;
        SyntaxText.Text = highlighting?.Name ?? "Plain Text";
    }

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveFileAsync();
    }

    /// <summary>
    /// 파일 저장
    /// </summary>
    private async System.Threading.Tasks.Task SaveFileAsync()
    {
        try
        {
            IsEnabled = false;
            Title = $"파일 편집기 - {Path.GetFileName(_remotePath)} (저장 중...)";

            await _sftpService.WriteFileAsync(_remotePath, TextEditor.Text, _currentEncoding);

            _originalContent = TextEditor.Text;
            _isModified = false;
            UpdateModifiedIndicator();

            Title = $"파일 편집기 - {Path.GetFileName(_remotePath)}";

            // 파일 크기 업데이트
            var fileInfo = await _sftpService.GetFileInfoAsync(_remotePath);
            if (fileInfo != null)
            {
                FileSizeText.Text = fileInfo.SizeFormatted;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"파일을 저장할 수 없습니다.\n{ex.Message}",
                "저장 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    /// <summary>
    /// 실행 취소
    /// </summary>
    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        TextEditor.Undo();
    }

    /// <summary>
    /// 다시 실행
    /// </summary>
    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        TextEditor.Redo();
    }

    /// <summary>
    /// 인코딩 변경
    /// </summary>
    private void EncodingComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (EncodingComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            var encodingName = item.Content.ToString();
            _currentEncoding = encodingName switch
            {
                "UTF-8" => new UTF8Encoding(false),
                "UTF-8 BOM" => new UTF8Encoding(true),
                "EUC-KR" => Encoding.GetEncoding("euc-kr"),
                "ASCII" => Encoding.ASCII,
                _ => Encoding.UTF8
            };
            EncodingText.Text = encodingName;
        }
    }

    /// <summary>
    /// 자동 줄바꿈 변경
    /// </summary>
    private void WordWrapCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        TextEditor.WordWrap = WordWrapCheckBox.IsChecked == true;
    }

    /// <summary>
    /// 줄 번호 표시 변경
    /// </summary>
    private void ShowLineNumbersCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        TextEditor.ShowLineNumbers = ShowLineNumbersCheckBox.IsChecked == true;
    }

    /// <summary>
    /// 텍스트 변경 이벤트
    /// </summary>
    private void TextEditor_TextChanged(object? sender, EventArgs e)
    {
        _isModified = TextEditor.Text != _originalContent;
        UpdateModifiedIndicator();
    }

    /// <summary>
    /// 수정 표시 업데이트
    /// </summary>
    private void UpdateModifiedIndicator()
    {
        ModifiedIndicator.Visibility = _isModified ? Visibility.Visible : Visibility.Collapsed;
        Title = _isModified
            ? $"* 파일 편집기 - {Path.GetFileName(_remotePath)}"
            : $"파일 편집기 - {Path.GetFileName(_remotePath)}";
    }

    /// <summary>
    /// 커서 위치 변경 이벤트
    /// </summary>
    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        var line = TextEditor.TextArea.Caret.Line;
        var column = TextEditor.TextArea.Caret.Column;
        LineColumnText.Text = $"줄 {line}, 열 {column}";
    }

    /// <summary>
    /// 키보드 단축키 처리
    /// </summary>
    private async void FileEditorWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.S:
                    await SaveFileAsync();
                    e.Handled = true;
                    break;
            }
        }
    }

    /// <summary>
    /// 창 닫기 전 확인
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isModified)
        {
            var result = MessageBox.Show(
                "변경 사항이 저장되지 않았습니다.\n저장하시겠습니까?",
                "저장 확인",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    // 동기적으로 저장 (간단히 처리)
                    try
                    {
                        _sftpService.WriteFileAsync(_remotePath, TextEditor.Text, _currentEncoding).Wait();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                        e.Cancel = true;
                    }
                    break;
                case MessageBoxResult.Cancel:
                    e.Cancel = true;
                    break;
            }
        }

        base.OnClosing(e);
    }
}
