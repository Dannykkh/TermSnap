using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using TermSnap.Services;
using ICSharpCode.AvalonEdit.Search;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;

namespace TermSnap.Views;

/// <summary>
/// 파일 뷰어 패널 - Markdown, 코드, 이미지, 텍스트 파일 표시
///
/// v2.0 구현 완료:
/// ✅ 편집 모드 (IsReadOnly 토글, 저장 버튼, Ctrl+E/Ctrl+S)
/// ✅ 라인 번호 표시 (AvalonEdit 사용)
/// ✅ 변경 감지 및 저장 확인
/// ✅ 더 많은 파일 타입 지원 (IsViewableFile 헬퍼 메서드)
/// ✅ 구문 강조 (AvalonEdit HighlightingManager)
/// </summary>
public partial class FileViewerPanel : UserControl
{
    private string? _currentFilePath;
    private bool _isEditMode = false;
    private bool _isModified = false;
    private string? _originalContent;
    private Encoding _currentEncoding = Encoding.UTF8;
    private string _lineEnding = "CRLF";
    private FoldingManager? _foldingManager;
    private bool _wordWrapEnabled = false;

    // 파일 유형별 확장자 (public으로 변경하여 외부에서 접근 가능)
    public static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".ini", ".cfg", ".conf", ".env", ".gitignore", ".dockerignore"
    };

    public static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".js", ".ts", ".jsx", ".tsx", ".java", ".cpp", ".c", ".h", ".hpp",
        ".go", ".rs", ".rb", ".php", ".swift", ".kt", ".scala", ".r", ".m",
        ".html", ".htm", ".css", ".scss", ".sass", ".less",
        ".json", ".xml", ".yaml", ".yml", ".toml",
        ".sql", ".sh", ".bash", ".ps1", ".bat", ".cmd",
        ".vue", ".svelte", ".astro"
    };

    public static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd"
    };

    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svg"
    };

    public static readonly HashSet<string> ExternalExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".pdf",
        ".zip", ".rar", ".7z", ".tar", ".gz",
        ".exe", ".msi", ".dll",
        ".mp3", ".mp4", ".avi", ".mkv", ".mov", ".wav", ".flac"
    };

    /// <summary>
    /// 파일 뷰어에서 볼 수 있는 파일인지 확인
    /// </summary>
    public static bool IsViewableFile(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return TextExtensions.Contains(ext) ||
               CodeExtensions.Contains(ext) ||
               MarkdownExtensions.Contains(ext) ||
               ImageExtensions.Contains(ext);
    }

    public event Action? CloseRequested;

    private SearchPanel? _searchPanel;

    public FileViewerPanel()
    {
        InitializeComponent();

        // 검색 패널 설치 (Ctrl+F)
        _searchPanel = SearchPanel.Install(TextEditor);

        // 현재 줄 하이라이트 활성화
        TextEditor.TextArea.Options.HighlightCurrentLine = true;

        // 줄 번호와 텍스트 사이 여백 추가
        if (TextEditor.TextArea.LeftMargins.Count > 0 &&
            TextEditor.TextArea.LeftMargins[0] is FrameworkElement lineNumberMargin)
        {
            lineNumberMargin.Margin = new Thickness(8, 0, 12, 0);
        }

        // 텍스트 변경 이벤트
        TextEditor.TextChanged += TextEditor_TextChanged;

        // 커서 위치 변경 이벤트
        TextEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;

        // 키보드 단축키
        this.KeyDown += FileViewerPanel_KeyDown;

        // 마우스 휠로 폰트 크기 조절
        TextEditor.PreviewMouseWheel += TextEditor_PreviewMouseWheel;
    }

    /// <summary>
    /// 커서 위치 변경 시 상태바 업데이트
    /// </summary>
    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        var line = TextEditor.TextArea.Caret.Line;
        var column = TextEditor.TextArea.Caret.Column;
        CursorPositionText.Text = $"줄 {line}, 열 {column}";
    }

    private void FileViewerPanel_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.S:
                    if (_isEditMode && _isModified)
                    {
                        SaveFile();
                        e.Handled = true;
                    }
                    break;
                case System.Windows.Input.Key.E:
                    ToggleEditMode();
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.G:
                    ShowGoToLineDialog();
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.H:
                    if (_isEditMode)
                    {
                        ShowReplaceDialog();
                        e.Handled = true;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Ctrl+휠로 폰트 크기 조절
    /// </summary>
    private void TextEditor_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            if (e.Delta > 0)
            {
                // 확대
                if (TextEditor.FontSize < 32)
                    TextEditor.FontSize += 1;
            }
            else
            {
                // 축소
                if (TextEditor.FontSize > 8)
                    TextEditor.FontSize -= 1;
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// 줄 이동 다이얼로그
    /// </summary>
    private void ShowGoToLineDialog()
    {
        var totalLines = TextEditor.Document.LineCount;
        var currentLine = TextEditor.TextArea.Caret.Line;

        var dialog = new System.Windows.Window
        {
            Title = "줄 이동",
            Width = 300,
            Height = 150,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner = System.Windows.Window.GetWindow(this),
            ResizeMode = System.Windows.ResizeMode.NoResize,
            WindowStyle = System.Windows.WindowStyle.ToolWindow
        };

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };

        var label = new System.Windows.Controls.TextBlock
        {
            Text = $"줄 번호 입력 (1 - {totalLines}):",
            Margin = new Thickness(0, 0, 0, 8)
        };

        var textBox = new System.Windows.Controls.TextBox
        {
            Text = currentLine.ToString(),
            Margin = new Thickness(0, 0, 0, 16)
        };
        textBox.SelectAll();

        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        var okButton = new System.Windows.Controls.Button
        {
            Content = "이동",
            Width = 70,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okButton.Click += (s, e) =>
        {
            if (int.TryParse(textBox.Text, out int lineNumber) && lineNumber >= 1 && lineNumber <= totalLines)
            {
                GoToLine(lineNumber);
                dialog.Close();
            }
            else
            {
                MessageBox.Show($"1에서 {totalLines} 사이의 숫자를 입력하세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "취소",
            Width = 70,
            IsCancel = true
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        panel.Children.Add(label);
        panel.Children.Add(textBox);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;
        dialog.ShowDialog();
    }

    /// <summary>
    /// 특정 줄로 이동
    /// </summary>
    private void GoToLine(int lineNumber)
    {
        var line = TextEditor.Document.GetLineByNumber(lineNumber);
        TextEditor.TextArea.Caret.Line = lineNumber;
        TextEditor.TextArea.Caret.Column = 1;
        TextEditor.ScrollToLine(lineNumber);
        TextEditor.Focus();
    }

    /// <summary>
    /// 바꾸기 다이얼로그
    /// </summary>
    private void ShowReplaceDialog()
    {
        var dialog = new System.Windows.Window
        {
            Title = "찾아 바꾸기",
            Width = 400,
            Height = 200,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner = System.Windows.Window.GetWindow(this),
            ResizeMode = System.Windows.ResizeMode.NoResize,
            WindowStyle = System.Windows.WindowStyle.ToolWindow
        };

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };

        // 찾기 필드
        var findLabel = new System.Windows.Controls.TextBlock { Text = "찾을 내용:", Margin = new Thickness(0, 0, 0, 4) };
        var findTextBox = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 12) };

        // 선택된 텍스트가 있으면 자동 입력
        if (!string.IsNullOrEmpty(TextEditor.SelectedText))
        {
            findTextBox.Text = TextEditor.SelectedText;
        }

        // 바꾸기 필드
        var replaceLabel = new System.Windows.Controls.TextBlock { Text = "바꿀 내용:", Margin = new Thickness(0, 0, 0, 4) };
        var replaceTextBox = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 16) };

        // 버튼
        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        var replaceButton = new System.Windows.Controls.Button
        {
            Content = "바꾸기",
            Width = 70,
            Margin = new Thickness(0, 0, 8, 0)
        };
        replaceButton.Click += (s, e) =>
        {
            ReplaceNext(findTextBox.Text, replaceTextBox.Text);
        };

        var replaceAllButton = new System.Windows.Controls.Button
        {
            Content = "모두 바꾸기",
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0)
        };
        replaceAllButton.Click += (s, e) =>
        {
            var count = ReplaceAll(findTextBox.Text, replaceTextBox.Text);
            MessageBox.Show($"{count}개 항목이 바뀌었습니다.", "바꾸기", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        var closeButton = new System.Windows.Controls.Button
        {
            Content = "닫기",
            Width = 70,
            IsCancel = true
        };

        buttonPanel.Children.Add(replaceButton);
        buttonPanel.Children.Add(replaceAllButton);
        buttonPanel.Children.Add(closeButton);

        panel.Children.Add(findLabel);
        panel.Children.Add(findTextBox);
        panel.Children.Add(replaceLabel);
        panel.Children.Add(replaceTextBox);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;
        dialog.Show(); // ShowDialog 대신 Show 사용 (모달리스)
    }

    /// <summary>
    /// 다음 항목 바꾸기
    /// </summary>
    private void ReplaceNext(string find, string replace)
    {
        if (string.IsNullOrEmpty(find)) return;

        var text = TextEditor.Text;
        var startIndex = TextEditor.CaretOffset;

        var index = text.IndexOf(find, startIndex, StringComparison.OrdinalIgnoreCase);
        if (index == -1)
        {
            // 처음부터 다시 검색
            index = text.IndexOf(find, 0, StringComparison.OrdinalIgnoreCase);
        }

        if (index != -1)
        {
            TextEditor.Select(index, find.Length);
            TextEditor.ScrollToLine(TextEditor.Document.GetLineByOffset(index).LineNumber);

            // 선택된 텍스트 바꾸기
            TextEditor.Document.Replace(index, find.Length, replace);
            TextEditor.CaretOffset = index + replace.Length;
        }
        else
        {
            MessageBox.Show("찾을 수 없습니다.", "바꾸기", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// 모두 바꾸기
    /// </summary>
    private int ReplaceAll(string find, string replace)
    {
        if (string.IsNullOrEmpty(find)) return 0;

        var text = TextEditor.Text;
        var count = 0;
        var index = 0;

        TextEditor.Document.BeginUpdate();
        try
        {
            while ((index = TextEditor.Text.IndexOf(find, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                TextEditor.Document.Replace(index, find.Length, replace);
                index += replace.Length;
                count++;
            }
        }
        finally
        {
            TextEditor.Document.EndUpdate();
        }

        return count;
    }

    private void TextEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_isEditMode && !_isModified)
        {
            _isModified = true;
            UpdateFileNameDisplay();
        }
    }

    private void UpdateFileNameDisplay()
    {
        if (_currentFilePath == null) return;

        var fileName = Path.GetFileName(_currentFilePath);
        FileNameText.Text = _isModified ? $"{fileName} *" : fileName;
    }

    /// <summary>
    /// 파일 열기
    /// </summary>
    public async Task OpenFileAsync(string filePath)
    {
        Debug.WriteLine($"[FileViewerPanel] OpenFileAsync called with: {filePath}");

        if (!File.Exists(filePath))
        {
            Debug.WriteLine($"[FileViewerPanel] File not found: {filePath}");
            MessageBox.Show($"파일을 찾을 수 없습니다:\n{filePath}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _currentFilePath = filePath;
        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        Debug.WriteLine($"[FileViewerPanel] File: {fileName}, Extension: {extension}");

        FileNameText.Text = fileName;
        UpdateFileIcon(extension);

        // 외부 프로그램으로 열어야 하는 파일
        if (ExternalExtensions.Contains(extension))
        {
            Debug.WriteLine($"[FileViewerPanel] Opening with external program: {extension}");
            OpenWithExternalProgram(filePath);
            return;
        }

        // 로딩 표시
        ShowLoading(true);

        try
        {
            if (MarkdownExtensions.Contains(extension))
            {
                Debug.WriteLine($"[FileViewerPanel] Loading as Markdown");
                await LoadMarkdownAsync(filePath);
            }
            else if (ImageExtensions.Contains(extension))
            {
                Debug.WriteLine($"[FileViewerPanel] Loading as Image");
                await LoadImageAsync(filePath);
            }
            else if (CodeExtensions.Contains(extension) || TextExtensions.Contains(extension))
            {
                Debug.WriteLine($"[FileViewerPanel] Loading as Text/Code");
                await LoadTextAsync(filePath, CodeExtensions.Contains(extension));
            }
            else
            {
                Debug.WriteLine($"[FileViewerPanel] Loading as unknown text file");
                // 알 수 없는 확장자 - 텍스트로 시도
                await LoadTextAsync(filePath, false);
            }

            Debug.WriteLine($"[FileViewerPanel] File loaded successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileViewerPanel] Error loading file: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show($"파일을 열 수 없습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ShowLoading(false);
        }
    }

    private void UpdateFileIcon(string extension)
    {
        FileIcon.Kind = extension.ToLowerInvariant() switch
        {
            ".md" or ".markdown" => PackIconKind.LanguageMarkdown,
            ".cs" => PackIconKind.LanguageCsharp,
            ".py" => PackIconKind.LanguagePython,
            ".js" or ".jsx" => PackIconKind.LanguageJavascript,
            ".ts" or ".tsx" => PackIconKind.LanguageTypescript,
            ".html" or ".htm" => PackIconKind.LanguageHtml5,
            ".css" or ".scss" or ".sass" => PackIconKind.LanguageCss3,
            ".json" => PackIconKind.CodeJson,
            ".xml" => PackIconKind.Xml,
            ".yaml" or ".yml" => PackIconKind.FileCode,
            ".java" => PackIconKind.LanguageJava,
            ".cpp" or ".c" or ".h" => PackIconKind.LanguageCpp,
            ".go" => PackIconKind.LanguageGo,
            ".rs" => PackIconKind.LanguageRust,
            ".rb" => PackIconKind.LanguageRuby,
            ".php" => PackIconKind.LanguagePhp,
            ".swift" => PackIconKind.LanguageSwift,
            ".kt" => PackIconKind.LanguageKotlin,
            ".sql" => PackIconKind.Database,
            ".sh" or ".bash" => PackIconKind.Console,
            ".ps1" => PackIconKind.Powershell,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => PackIconKind.Image,
            ".pdf" => PackIconKind.FilePdfBox,
            ".docx" or ".doc" => PackIconKind.FileWord,
            ".xlsx" or ".xls" => PackIconKind.FileExcel,
            ".pptx" or ".ppt" => PackIconKind.FilePowerpoint,
            ".zip" or ".rar" or ".7z" => PackIconKind.FolderZip,
            _ => PackIconKind.File
        };
    }

    private void ShowLoading(bool show)
    {
        LoadingIndicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideAllViewers()
    {
        EmptyState.Visibility = Visibility.Collapsed;
        TextViewer.Visibility = Visibility.Collapsed;
        MarkdownViewer.Visibility = Visibility.Collapsed;
        ImageViewer.Visibility = Visibility.Collapsed;
    }

    private async Task LoadTextAsync(string filePath, bool isCode)
    {
        // 인코딩 감지
        _currentEncoding = DetectEncoding(filePath);
        var content = await File.ReadAllTextAsync(filePath, _currentEncoding);

        // 줄 끝 문자 감지
        _lineEnding = DetectLineEnding(content);

        HideAllViewers();
        TextEditor.Text = content;
        TextEditor.FontFamily = isCode ? new FontFamily("Consolas") : new FontFamily("Segoe UI");

        // 구문 강조 설정
        SetSyntaxHighlighting(filePath);

        // 코드 접기 설정
        SetupFolding(filePath);

        // 상태바 업데이트
        UpdateStatusBar();

        TextViewer.Visibility = Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 인코딩 감지 (BOM 기반)
    /// </summary>
    private Encoding DetectEncoding(string filePath)
    {
        var bom = new byte[4];
        using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            file.Read(bom, 0, 4);
        }

        // BOM 확인
        if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf)
            return Encoding.UTF8;
        if (bom[0] == 0xff && bom[1] == 0xfe)
            return Encoding.Unicode; // UTF-16 LE
        if (bom[0] == 0xfe && bom[1] == 0xff)
            return Encoding.BigEndianUnicode; // UTF-16 BE
        if (bom[0] == 0 && bom[1] == 0 && bom[2] == 0xfe && bom[3] == 0xff)
            return Encoding.UTF32;

        // BOM이 없으면 UTF-8로 가정
        return Encoding.UTF8;
    }

    /// <summary>
    /// 줄 끝 문자 감지
    /// </summary>
    private string DetectLineEnding(string content)
    {
        if (content.Contains("\r\n"))
            return "CRLF";
        if (content.Contains("\n"))
            return "LF";
        if (content.Contains("\r"))
            return "CR";
        return "CRLF"; // 기본값
    }

    /// <summary>
    /// 상태바 업데이트
    /// </summary>
    private void UpdateStatusBar()
    {
        EncodingText.Text = _currentEncoding.WebName.ToUpperInvariant();
        LineEndingText.Text = _lineEnding;
        Caret_PositionChanged(null, EventArgs.Empty);
    }

    /// <summary>
    /// 코드 접기 설정
    /// </summary>
    private void SetupFolding(string filePath)
    {
        // 기존 FoldingManager 제거
        if (_foldingManager != null)
        {
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // 중괄호 기반 언어만 코드 접기 지원
        var braceFoldingLanguages = new HashSet<string>
        {
            ".cs", ".java", ".js", ".jsx", ".ts", ".tsx", ".cpp", ".c", ".h", ".hpp",
            ".go", ".rs", ".swift", ".kt", ".scala", ".json", ".css", ".scss", ".less"
        };

        // XML 기반 언어
        var xmlFoldingLanguages = new HashSet<string>
        {
            ".xml", ".xaml", ".html", ".htm", ".svg", ".csproj", ".props", ".targets"
        };

        if (braceFoldingLanguages.Contains(extension))
        {
            _foldingManager = FoldingManager.Install(TextEditor.TextArea);
            var foldingStrategy = new BraceFoldingStrategy();
            foldingStrategy.UpdateFoldings(_foldingManager, TextEditor.Document);
        }
        else if (xmlFoldingLanguages.Contains(extension))
        {
            _foldingManager = FoldingManager.Install(TextEditor.TextArea);
            var foldingStrategy = new XmlFoldingStrategy();
            foldingStrategy.UpdateFoldings(_foldingManager, TextEditor.Document);
        }
    }

    /// <summary>
    /// 파일 확장자에 따라 구문 강조 설정
    /// </summary>
    private void SetSyntaxHighlighting(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        var highlightingName = extension switch
        {
            ".cs" => "C#",
            ".py" => "Python",
            ".js" or ".jsx" => "JavaScript",
            ".ts" or ".tsx" => "JavaScript", // TypeScript에 JavaScript 사용
            ".java" => "Java",
            ".cpp" or ".c" or ".h" or ".hpp" => "C++",
            ".xml" or ".xaml" or ".csproj" or ".props" or ".targets" => "XML",
            ".html" or ".htm" => "HTML",
            ".css" or ".scss" or ".sass" or ".less" => "CSS",
            ".json" => "JavaScript", // JSON에 JavaScript 사용
            ".sql" => "TSQL",
            ".ps1" => "PowerShell",
            ".sh" or ".bash" => "Python", // Shell에 Python 스타일 사용
            ".bat" or ".cmd" => "Python",
            ".php" => "PHP",
            ".rb" => "Ruby",
            ".yaml" or ".yml" => "Python", // YAML에 Python 스타일 사용
            ".md" or ".markdown" => "MarkDown",
            _ => null
        };

        if (highlightingName != null)
        {
            var highlighting = HighlightingManager.Instance.GetDefinition(highlightingName);
            if (highlighting != null)
            {
                // 다크 테마용 색상 적용
                ApplyDarkThemeColors(highlighting);
            }
            TextEditor.SyntaxHighlighting = highlighting;
        }
        else
        {
            TextEditor.SyntaxHighlighting = null;
        }
    }

    /// <summary>
    /// 다크 테마용 구문 강조 색상 적용
    /// </summary>
    private void ApplyDarkThemeColors(IHighlightingDefinition highlighting)
    {
        // 다크 테마 색상 매핑
        var darkColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            // 공통 색상
            { "Comment", Color.FromRgb(106, 153, 85) },      // 연한 초록
            { "String", Color.FromRgb(206, 145, 120) },      // 연한 주황
            { "Char", Color.FromRgb(206, 145, 120) },
            { "Number", Color.FromRgb(181, 206, 168) },      // 연한 연두
            { "Digits", Color.FromRgb(181, 206, 168) },
            { "Keywords", Color.FromRgb(86, 156, 214) },     // 파란색
            { "Keyword", Color.FromRgb(86, 156, 214) },
            { "MethodCall", Color.FromRgb(220, 220, 170) },  // 연한 노란
            { "MethodName", Color.FromRgb(220, 220, 170) },
            { "Punctuation", Color.FromRgb(212, 212, 212) }, // 회색
            { "Operators", Color.FromRgb(212, 212, 212) },

            // C# 관련
            { "Namespace", Color.FromRgb(78, 201, 176) },    // 청록
            { "Class", Color.FromRgb(78, 201, 176) },
            { "Struct", Color.FromRgb(78, 201, 176) },
            { "Interface", Color.FromRgb(184, 215, 163) },   // 연한 녹색
            { "Enum", Color.FromRgb(184, 215, 163) },
            { "ValueTypes", Color.FromRgb(86, 156, 214) },
            { "ReferenceTypes", Color.FromRgb(78, 201, 176) },
            { "ThisOrBaseReference", Color.FromRgb(86, 156, 214) },
            { "NullOrValueKeywords", Color.FromRgb(86, 156, 214) },
            { "GotoKeywords", Color.FromRgb(197, 134, 192) }, // 보라
            { "ContextKeywords", Color.FromRgb(86, 156, 214) },
            { "ExceptionKeywords", Color.FromRgb(197, 134, 192) },
            { "CheckedKeyword", Color.FromRgb(86, 156, 214) },
            { "UnsafeKeywords", Color.FromRgb(86, 156, 214) },
            { "OperatorKeywords", Color.FromRgb(86, 156, 214) },
            { "ParameterModifiers", Color.FromRgb(86, 156, 214) },
            { "Modifiers", Color.FromRgb(86, 156, 214) },
            { "Visibility", Color.FromRgb(86, 156, 214) },
            { "NamespaceKeywords", Color.FromRgb(197, 134, 192) },
            { "GetSetAddRemove", Color.FromRgb(86, 156, 214) },
            { "TrueFalse", Color.FromRgb(86, 156, 214) },
            { "TypeKeywords", Color.FromRgb(86, 156, 214) },
            { "SemanticKeywords", Color.FromRgb(86, 156, 214) },

            // XML/HTML 관련
            { "XmlTag", Color.FromRgb(86, 156, 214) },
            { "XmlComment", Color.FromRgb(106, 153, 85) },
            { "XmlString", Color.FromRgb(206, 145, 120) },
            { "DocComment", Color.FromRgb(106, 153, 85) },
            { "XmlDocTag", Color.FromRgb(128, 128, 128) },
            { "XmlPunctuation", Color.FromRgb(128, 128, 128) },
            { "Attributes", Color.FromRgb(156, 220, 254) },  // 밝은 파란
            { "AttributeValue", Color.FromRgb(206, 145, 120) },
            { "HtmlTag", Color.FromRgb(86, 156, 214) },
            { "HtmlAttributeName", Color.FromRgb(156, 220, 254) },
            { "HtmlAttributeValue", Color.FromRgb(206, 145, 120) },
            { "HtmlOperator", Color.FromRgb(128, 128, 128) },
            { "Entity", Color.FromRgb(206, 145, 120) },
            { "Entities", Color.FromRgb(206, 145, 120) },

            // JavaScript/TypeScript
            { "JavaScriptKeyWords", Color.FromRgb(197, 134, 192) },
            { "JavaScriptIntrinsics", Color.FromRgb(86, 156, 214) },
            { "JavaScriptLiterals", Color.FromRgb(86, 156, 214) },
            { "JavaScriptGlobalFunctions", Color.FromRgb(220, 220, 170) },

            // Python
            { "CommentMarker", Color.FromRgb(106, 153, 85) },
            { "BuiltInStatements", Color.FromRgb(197, 134, 192) },
            { "ClassStatement", Color.FromRgb(86, 156, 214) },
            { "FunctionDefinition", Color.FromRgb(86, 156, 214) },
            { "Imports", Color.FromRgb(197, 134, 192) },

            // 일반
            { "Preprocessor", Color.FromRgb(155, 155, 155) },
            { "LineComment", Color.FromRgb(106, 153, 85) },
            { "BlockComment", Color.FromRgb(106, 153, 85) },
        };

        // 하이라이팅 정의의 모든 색상을 다크 테마로 변경
        foreach (var namedColor in highlighting.NamedHighlightingColors)
        {
            if (darkColors.TryGetValue(namedColor.Name, out var color))
            {
                namedColor.Foreground = new SimpleHighlightingBrush(color);
            }
            else
            {
                // 알 수 없는 색상은 밝은 회색으로 설정 (기본값보다 밝게)
                // 기존 색상이 어두우면 밝게 조정
                if (namedColor.Foreground != null)
                {
                    var brush = namedColor.Foreground.GetBrush(null);
                    if (brush is SolidColorBrush solidBrush)
                    {
                        var c = solidBrush.Color;
                        // 어두운 색상이면 밝게 조정
                        var brightness = (c.R + c.G + c.B) / 3.0;
                        if (brightness < 128)
                        {
                            var factor = 1.8;
                            namedColor.Foreground = new SimpleHighlightingBrush(
                                Color.FromRgb(
                                    (byte)Math.Min(255, c.R * factor + 60),
                                    (byte)Math.Min(255, c.G * factor + 60),
                                    (byte)Math.Min(255, c.B * factor + 60)
                                ));
                        }
                    }
                }
            }
        }
    }

    private async Task LoadMarkdownAsync(string filePath)
    {
        var markdown = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

        HideAllViewers();
        RenderMarkdown(markdown);
        MarkdownViewer.Visibility = Visibility.Visible;
    }

    private async Task LoadImageAsync(string filePath)
    {
        await Task.Run(() =>
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(filePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    HideAllViewers();
                    ImageContent.Source = bitmap;
                    ImageViewer.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Image load error: {ex.Message}");
                }
            });
        });
    }

    // 테마 색상 (DynamicResource 대신 코드에서 참조)
    private Brush GetTextBrush() => (Brush)FindResource("MaterialDesignBody") ?? Brushes.White;
    private Brush GetSecondaryTextBrush() => (Brush)FindResource("MaterialDesignBodyLight") ?? Brushes.Gray;
    private Brush GetCodeBackgroundBrush() => new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
    private Brush GetTableBorderBrush() => (Brush)FindResource("MaterialDesignDivider") ?? Brushes.Gray;

    /// <summary>
    /// 간단한 Markdown 렌더러 (테이블 지원, 테마 적용)
    /// </summary>
    private void RenderMarkdown(string markdown)
    {
        var textBrush = GetTextBrush();
        var secondaryBrush = GetSecondaryTextBrush();
        var codeBgBrush = GetCodeBackgroundBrush();
        var borderBrush = GetTableBorderBrush();

        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            PagePadding = new Thickness(0),
            Foreground = textBrush
        };

        var lines = markdown.Split('\n');
        Paragraph? currentParagraph = null;
        bool inCodeBlock = false;
        StringBuilder codeBlockContent = new();
        List<string> tableLines = new();
        bool inTable = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // 코드 블록
            if (line.StartsWith("```"))
            {
                // 테이블 종료 확인
                if (inTable)
                {
                    RenderTable(document, tableLines, textBrush, borderBrush);
                    tableLines.Clear();
                    inTable = false;
                }

                if (inCodeBlock)
                {
                    // 코드 블록 종료
                    var codePara = new Paragraph(new Run(codeBlockContent.ToString()))
                    {
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        Background = codeBgBrush,
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                        Padding = new Thickness(12),
                        Margin = new Thickness(0, 8, 0, 8)
                    };
                    document.Blocks.Add(codePara);
                    codeBlockContent.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    inCodeBlock = true;
                }
                continue;
            }

            if (inCodeBlock)
            {
                codeBlockContent.AppendLine(line);
                continue;
            }

            // 테이블 감지 (| 로 시작하는 줄)
            if (line.TrimStart().StartsWith("|"))
            {
                // 테이블 종료 전 현재 문단 마무리
                currentParagraph = null;
                inTable = true;
                tableLines.Add(line);
                continue;
            }
            else if (inTable)
            {
                // 테이블 종료
                RenderTable(document, tableLines, textBrush, borderBrush);
                tableLines.Clear();
                inTable = false;
            }

            // 헤더
            if (line.StartsWith("# "))
            {
                var header = new Paragraph(new Run(line.Substring(2)))
                {
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    Foreground = textBrush,
                    Margin = new Thickness(0, 16, 0, 8)
                };
                document.Blocks.Add(header);
                currentParagraph = null;
                continue;
            }
            if (line.StartsWith("## "))
            {
                var header = new Paragraph(new Run(line.Substring(3)))
                {
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = textBrush,
                    Margin = new Thickness(0, 14, 0, 6)
                };
                document.Blocks.Add(header);
                currentParagraph = null;
                continue;
            }
            if (line.StartsWith("### "))
            {
                var header = new Paragraph(new Run(line.Substring(4)))
                {
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = textBrush,
                    Margin = new Thickness(0, 12, 0, 4)
                };
                document.Blocks.Add(header);
                currentParagraph = null;
                continue;
            }
            if (line.StartsWith("#### "))
            {
                var header = new Paragraph(new Run(line.Substring(5)))
                {
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = textBrush,
                    Margin = new Thickness(0, 10, 0, 4)
                };
                document.Blocks.Add(header);
                currentParagraph = null;
                continue;
            }
            if (line.StartsWith("##### "))
            {
                var header = new Paragraph(new Run(line.Substring(6)))
                {
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = textBrush,
                    Margin = new Thickness(0, 8, 0, 4)
                };
                document.Blocks.Add(header);
                currentParagraph = null;
                continue;
            }
            if (line.StartsWith("###### "))
            {
                var header = new Paragraph(new Run(line.Substring(7)))
                {
                    FontSize = 14,
                    FontWeight = FontWeights.Medium,
                    Foreground = secondaryBrush,
                    Margin = new Thickness(0, 8, 0, 4)
                };
                document.Blocks.Add(header);
                currentParagraph = null;
                continue;
            }

            // 수평선
            if (line.StartsWith("---") || line.StartsWith("***"))
            {
                var separator = new Paragraph()
                {
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0, 12, 0, 12)
                };
                document.Blocks.Add(separator);
                currentParagraph = null;
                continue;
            }

            // 리스트 (인라인 스타일 지원)
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                var listItem = new Paragraph { Foreground = textBrush, Margin = new Thickness(16, 2, 0, 2) };
                listItem.Inlines.Add(new Run("• ") { Foreground = textBrush });
                foreach (var inline in ProcessInlineStyles(line.Substring(2), textBrush))
                {
                    listItem.Inlines.Add(inline);
                }
                document.Blocks.Add(listItem);
                currentParagraph = null;
                continue;
            }

            // 번호 리스트 (인라인 스타일 지원)
            var numberedMatch = Regex.Match(line, @"^(\d+)\.\s(.+)$");
            if (numberedMatch.Success)
            {
                var listItem = new Paragraph { Foreground = textBrush, Margin = new Thickness(16, 2, 0, 2) };
                listItem.Inlines.Add(new Run($"{numberedMatch.Groups[1].Value}. ") { Foreground = textBrush });
                foreach (var inline in ProcessInlineStyles(numberedMatch.Groups[2].Value, textBrush))
                {
                    listItem.Inlines.Add(inline);
                }
                document.Blocks.Add(listItem);
                currentParagraph = null;
                continue;
            }

            // 인용구 (인라인 스타일 지원)
            if (line.StartsWith("> "))
            {
                var quote = new Paragraph
                {
                    BorderBrush = secondaryBrush,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(12, 4, 0, 4),
                    Foreground = secondaryBrush,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 4, 0, 4)
                };
                foreach (var inline in ProcessInlineStyles(line.Substring(2), secondaryBrush))
                {
                    quote.Inlines.Add(inline);
                }
                document.Blocks.Add(quote);
                currentParagraph = null;
                continue;
            }

            // 빈 줄
            if (string.IsNullOrWhiteSpace(line))
            {
                currentParagraph = null;
                continue;
            }

            // 일반 텍스트 - 인라인 스타일 처리
            var styledLine = ProcessInlineStyles(line, textBrush);
            if (currentParagraph == null)
            {
                currentParagraph = new Paragraph { Margin = new Thickness(0, 4, 0, 4), Foreground = textBrush };
                document.Blocks.Add(currentParagraph);
            }
            else
            {
                currentParagraph.Inlines.Add(new Run(" "));
            }

            foreach (var inline in styledLine)
            {
                currentParagraph.Inlines.Add(inline);
            }
        }

        // 마지막 테이블 처리
        if (inTable && tableLines.Count > 0)
        {
            RenderTable(document, tableLines, textBrush, borderBrush);
        }

        MarkdownContent.Document = document;
    }

    /// <summary>
    /// Markdown 테이블 렌더링
    /// </summary>
    private void RenderTable(FlowDocument document, List<string> tableLines, Brush textBrush, Brush borderBrush)
    {
        if (tableLines.Count < 2) return;

        // 구분선 행 제거 (|---|---|)
        var dataLines = tableLines.Where(l => !Regex.IsMatch(l.Trim(), @"^\|[\s\-:|]+\|$")).ToList();
        if (dataLines.Count == 0) return;

        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1)
        };

        // 첫 번째 행으로 열 수 결정
        var firstRowCells = ParseTableRow(dataLines[0]);
        var columnCount = firstRowCells.Length;

        // 열 정의
        for (int i = 0; i < columnCount; i++)
        {
            table.Columns.Add(new TableColumn());
        }

        var rowGroup = new TableRowGroup();
        table.RowGroups.Add(rowGroup);

        for (int rowIndex = 0; rowIndex < dataLines.Count; rowIndex++)
        {
            var cells = ParseTableRow(dataLines[rowIndex]);
            var tableRow = new TableRow();

            // 헤더 행 스타일
            bool isHeader = rowIndex == 0;

            for (int colIndex = 0; colIndex < columnCount; colIndex++)
            {
                var cellText = colIndex < cells.Length ? cells[colIndex].Trim() : "";
                var para = new Paragraph(new Run(cellText))
                {
                    Margin = new Thickness(8, 4, 8, 4),
                    Foreground = textBrush
                };

                if (isHeader)
                {
                    para.FontWeight = FontWeights.Bold;
                }

                var tableCell = new TableCell(para)
                {
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(0, 0, 1, 1)
                };

                if (isHeader)
                {
                    tableCell.Background = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128));
                }

                tableRow.Cells.Add(tableCell);
            }

            rowGroup.Rows.Add(tableRow);
        }

        document.Blocks.Add(table);
    }

    /// <summary>
    /// 테이블 행 파싱
    /// </summary>
    private string[] ParseTableRow(string line)
    {
        // |로 시작하고 끝나는 경우 제거
        var trimmed = line.Trim();
        if (trimmed.StartsWith("|")) trimmed = trimmed.Substring(1);
        if (trimmed.EndsWith("|")) trimmed = trimmed.Substring(0, trimmed.Length - 1);

        return trimmed.Split('|');
    }

    private List<Inline> ProcessInlineStyles(string text, Brush textBrush)
    {
        var inlines = new List<Inline>();

        // 이미지, 볼드, 이탤릭, 코드, 링크 처리
        // 그룹: 1-2: 이미지, 3-4: 볼드, 5-6: 이탤릭, 7-8: 코드, 9-11: 링크
        var pattern = @"(!\[(.+?)\]\((.+?)\))|(\*\*(.+?)\*\*)|(\*(.+?)\*)|(`(.+?)`)|(\[(.+?)\]\((.+?)\))";
        var lastIndex = 0;

        foreach (Match match in Regex.Matches(text, pattern))
        {
            // 매치 이전 텍스트
            if (match.Index > lastIndex)
            {
                inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)) { Foreground = textBrush });
            }

            if (match.Groups[2].Success) // 이미지 ![alt](url)
            {
                var altText = match.Groups[2].Value;
                var imageUrl = match.Groups[3].Value;
                // 이미지는 인라인으로 표시 어려우므로 [이미지: alt] 텍스트로 표시
                var imageRun = new Run($"[이미지: {altText}]")
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    FontStyle = FontStyles.Italic
                };
                inlines.Add(imageRun);
            }
            else if (match.Groups[5].Success) // 볼드 **text**
            {
                inlines.Add(new Bold(new Run(match.Groups[5].Value) { Foreground = textBrush }));
            }
            else if (match.Groups[7].Success) // 이탤릭 *text*
            {
                inlines.Add(new Italic(new Run(match.Groups[7].Value) { Foreground = textBrush }));
            }
            else if (match.Groups[9].Success) // 인라인 코드 `code`
            {
                var code = new Run(match.Groups[9].Value)
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 80))
                };
                inlines.Add(code);
            }
            else if (match.Groups[11].Success) // 링크 [text](url)
            {
                var link = new Hyperlink(new Run(match.Groups[11].Value))
                {
                    NavigateUri = new Uri(match.Groups[12].Value, UriKind.RelativeOrAbsolute),
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 180, 255))
                };
                link.RequestNavigate += (s, e) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                    }
                    catch { }
                };
                inlines.Add(link);
            }

            lastIndex = match.Index + match.Length;
        }

        // 나머지 텍스트
        if (lastIndex < text.Length)
        {
            inlines.Add(new Run(text.Substring(lastIndex)) { Foreground = textBrush });
        }

        if (inlines.Count == 0)
        {
            inlines.Add(new Run(text) { Foreground = textBrush });
        }

        return inlines;
    }

    private void OpenWithExternalProgram(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 열 수 없습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            OpenWithExternalProgram(_currentFilePath);
        }
    }

    private void EditToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleEditMode();
    }

    /// <summary>
    /// Word Wrap 토글
    /// </summary>
    private void WordWrap_Click(object sender, RoutedEventArgs e)
    {
        _wordWrapEnabled = !_wordWrapEnabled;
        TextEditor.WordWrap = _wordWrapEnabled;

        // 아이콘 색상 업데이트
        WordWrapIcon.Foreground = _wordWrapEnabled
            ? (Brush)FindResource("PrimaryBrush")
            : (Brush)FindResource("TextSecondaryBrush");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveFile();
    }

    /// <summary>
    /// 편집 모드 토글
    /// </summary>
    private void ToggleEditMode()
    {
        // 이미지와 마크다운 뷰어는 편집 모드 지원 안 함
        if (ImageViewer.Visibility == Visibility.Visible ||
            MarkdownViewer.Visibility == Visibility.Visible)
        {
            MessageBox.Show("이 파일 형식은 편집할 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isEditMode = !_isEditMode;
        TextEditor.IsReadOnly = !_isEditMode;

        // 편집 모드 진입 시 원본 저장
        if (_isEditMode)
        {
            _originalContent = TextEditor.Text;
            _isModified = false;
        }

        // UI 업데이트
        UpdateEditModeUI();
        Debug.WriteLine($"[FileViewerPanel] Edit mode: {_isEditMode}");
    }

    /// <summary>
    /// 편집 모드 UI 업데이트
    /// </summary>
    private void UpdateEditModeUI()
    {
        // 저장 버튼 표시/숨김
        SaveBtn.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;

        // 편집 버튼 아이콘 변경
        EditToggleIcon.Kind = _isEditMode ? PackIconKind.Eye : PackIconKind.Pencil;
        EditToggleIcon.Foreground = _isEditMode
            ? (Brush)FindResource("PrimaryBrush")
            : (Brush)FindResource("TextSecondaryBrush");

        // 파일명 업데이트
        UpdateFileNameDisplay();
    }

    /// <summary>
    /// 파일 저장
    /// </summary>
    private async void SaveFile()
    {
        if (string.IsNullOrEmpty(_currentFilePath) || !_isModified)
            return;

        try
        {
            Debug.WriteLine($"[FileViewerPanel] Saving file: {_currentFilePath}");

            await File.WriteAllTextAsync(_currentFilePath, TextEditor.Text, Encoding.UTF8);

            _isModified = false;
            _originalContent = TextEditor.Text;
            UpdateFileNameDisplay();

            Debug.WriteLine("[FileViewerPanel] File saved successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileViewerPanel] Error saving file: {ex.Message}");
            MessageBox.Show($"파일을 저장할 수 없습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 편집 모드 종료 (저장 확인)
    /// </summary>
    private bool ConfirmUnsavedChanges()
    {
        if (!_isEditMode || !_isModified)
            return true;

        var result = MessageBox.Show(
            "저장하지 않은 변경 사항이 있습니다.\n저장하시겠습니까?",
            "확인",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Yes:
                SaveFile();
                return true;
            case MessageBoxResult.No:
                return true;
            default:
                return false;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[FileViewerPanel] Close_Click called");

        // 저장하지 않은 변경 확인
        if (!ConfirmUnsavedChanges())
            return;

        // 편집 모드 초기화
        _isEditMode = false;
        _isModified = false;
        _originalContent = null;
        TextEditor.IsReadOnly = true;
        UpdateEditModeUI();

        _currentFilePath = null;
        HideAllViewers();
        StatusBar.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        FileNameText.Text = LocalizationService.Instance.GetString("FileViewer.SelectFile") ?? "파일을 선택하세요";
        FileIcon.Kind = PackIconKind.File;

        // 코드 접기 제거
        if (_foldingManager != null)
        {
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
        }

        Debug.WriteLine("[FileViewerPanel] Invoking CloseRequested event");
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// 패널 닫기
    /// </summary>
    public void ClosePanel()
    {
        Close_Click(this, new RoutedEventArgs());
    }

    #region 리사이즈 그립

    private bool _isResizing = false;
    private double _resizeStartX;
    private double _resizeStartWidth;

    private void ResizeGrip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isResizing = true;
        // 화면 좌표 사용 (더 안정적)
        _resizeStartX = PointToScreen(e.GetPosition(this)).X;
        _resizeStartWidth = this.ActualWidth;
        ResizeGrip.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            ResizeGrip.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void ResizeGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isResizing && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            // 화면 좌표로 이동량 계산
            var currentX = PointToScreen(e.GetPosition(this)).X;
            var delta = _resizeStartX - currentX;
            var newWidth = _resizeStartWidth + delta;

            // 최소/최대 크기 제한
            newWidth = Math.Max(200, Math.Min(800, newWidth));
            this.Width = newWidth;

            e.Handled = true;
        }
    }

    #endregion
}

/// <summary>
/// 중괄호 기반 코드 접기 전략
/// </summary>
public class BraceFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, ICSharpCode.AvalonEdit.Document.TextDocument document)
    {
        var foldings = CreateNewFoldings(document);
        manager.UpdateFoldings(foldings, -1);
    }

    private IEnumerable<NewFolding> CreateNewFoldings(ICSharpCode.AvalonEdit.Document.TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var startOffsets = new Stack<int>();

        for (int i = 0; i < document.TextLength; i++)
        {
            char c = document.GetCharAt(i);
            if (c == '{')
            {
                startOffsets.Push(i);
            }
            else if (c == '}' && startOffsets.Count > 0)
            {
                int startOffset = startOffsets.Pop();
                // 최소 2줄 이상일 때만 접기 생성
                if (document.GetLineByOffset(startOffset).LineNumber <
                    document.GetLineByOffset(i).LineNumber)
                {
                    foldings.Add(new NewFolding(startOffset, i + 1) { Name = "..." });
                }
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }
}
