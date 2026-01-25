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

namespace TermSnap.Views;

/// <summary>
/// 파일 뷰어 패널 - Markdown, 코드, 이미지, 텍스트 파일 표시
///
/// TODO (v2.0):
/// - 편집 모드 추가 (IsReadOnly 토글, 저장 버튼)
/// - 라인 번호 표시 (Claude Code처럼 몇번째 줄 수정하는지 확인용)
/// - 변경 감지 및 자동 저장
/// - 더 많은 파일 타입 지원 (.bat, .sh, .ps1, .json, .xml, .yaml 등)
///   → MainWindow.xaml.cs에서 .md만 FileViewerPanel로 보내는 로직을 확장 필요
/// </summary>
public partial class FileViewerPanel : UserControl
{
    private string? _currentFilePath;

    // 파일 유형별 확장자
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".ini", ".cfg", ".conf", ".env", ".gitignore", ".dockerignore"
    };

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".js", ".ts", ".jsx", ".tsx", ".java", ".cpp", ".c", ".h", ".hpp",
        ".go", ".rs", ".rb", ".php", ".swift", ".kt", ".scala", ".r", ".m",
        ".html", ".htm", ".css", ".scss", ".sass", ".less",
        ".json", ".xml", ".yaml", ".yml", ".toml",
        ".sql", ".sh", ".bash", ".ps1", ".bat", ".cmd",
        ".vue", ".svelte", ".astro"
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown", ".mkd"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svg"
    };

    private static readonly HashSet<string> ExternalExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".pdf",
        ".zip", ".rar", ".7z", ".tar", ".gz",
        ".exe", ".msi", ".dll",
        ".mp3", ".mp4", ".avi", ".mkv", ".mov", ".wav", ".flac"
    };

    public event Action? CloseRequested;

    public FileViewerPanel()
    {
        InitializeComponent();
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
        var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

        HideAllViewers();
        TextContent.Text = content;
        TextContent.FontFamily = isCode ? new FontFamily("Consolas") : new FontFamily("Segoe UI");
        TextViewer.Visibility = Visibility.Visible;
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

            // 리스트
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                var listItem = new Paragraph(new Run("• " + line.Substring(2)))
                {
                    Foreground = textBrush,
                    Margin = new Thickness(16, 2, 0, 2)
                };
                document.Blocks.Add(listItem);
                currentParagraph = null;
                continue;
            }

            // 번호 리스트
            var numberedMatch = Regex.Match(line, @"^(\d+)\.\s(.+)$");
            if (numberedMatch.Success)
            {
                var listItem = new Paragraph(new Run($"{numberedMatch.Groups[1].Value}. {numberedMatch.Groups[2].Value}"))
                {
                    Foreground = textBrush,
                    Margin = new Thickness(16, 2, 0, 2)
                };
                document.Blocks.Add(listItem);
                currentParagraph = null;
                continue;
            }

            // 인용구
            if (line.StartsWith("> "))
            {
                var quote = new Paragraph(new Run(line.Substring(2)))
                {
                    BorderBrush = secondaryBrush,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(12, 4, 0, 4),
                    Foreground = secondaryBrush,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 4, 0, 4)
                };
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

        // 볼드, 이탤릭, 코드, 링크 처리
        var pattern = @"(\*\*(.+?)\*\*)|(\*(.+?)\*)|(`(.+?)`)|(\[(.+?)\]\((.+?)\))";
        var lastIndex = 0;

        foreach (Match match in Regex.Matches(text, pattern))
        {
            // 매치 이전 텍스트
            if (match.Index > lastIndex)
            {
                inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)) { Foreground = textBrush });
            }

            if (match.Groups[2].Success) // 볼드
            {
                inlines.Add(new Bold(new Run(match.Groups[2].Value) { Foreground = textBrush }));
            }
            else if (match.Groups[4].Success) // 이탤릭
            {
                inlines.Add(new Italic(new Run(match.Groups[4].Value) { Foreground = textBrush }));
            }
            else if (match.Groups[6].Success) // 인라인 코드
            {
                var code = new Run(match.Groups[6].Value)
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 80))
                };
                inlines.Add(code);
            }
            else if (match.Groups[8].Success) // 링크
            {
                var link = new Hyperlink(new Run(match.Groups[8].Value))
                {
                    NavigateUri = new Uri(match.Groups[9].Value, UriKind.RelativeOrAbsolute),
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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("[FileViewerPanel] Close_Click called");

        _currentFilePath = null;
        HideAllViewers();
        EmptyState.Visibility = Visibility.Visible;
        FileNameText.Text = LocalizationService.Instance.GetString("FileViewer.SelectFile") ?? "파일을 선택하세요";
        FileIcon.Kind = PackIconKind.File;

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
