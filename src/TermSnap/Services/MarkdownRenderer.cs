using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TermSnap.Services;

/// <summary>
/// 마크다운을 FlowDocument로 렌더링하는 공통 서비스
/// </summary>
public class MarkdownRenderer
{
    private Brush _textBrush = Brushes.Black;
    private Brush _secondaryBrush = Brushes.Gray;
    private Brush _codeBgBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
    private Brush _borderBrush = Brushes.Gray;
    private Brush _accentBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));

    /// <summary>
    /// 기본 텍스트 색상 설정
    /// </summary>
    public Brush TextBrush
    {
        get => _textBrush;
        set => _textBrush = value;
    }

    /// <summary>
    /// 보조 텍스트 색상 설정
    /// </summary>
    public Brush SecondaryBrush
    {
        get => _secondaryBrush;
        set => _secondaryBrush = value;
    }

    /// <summary>
    /// 코드 블록 배경색 설정
    /// </summary>
    public Brush CodeBackgroundBrush
    {
        get => _codeBgBrush;
        set => _codeBgBrush = value;
    }

    /// <summary>
    /// 테이블/구분선 테두리 색상 설정
    /// </summary>
    public Brush BorderBrush
    {
        get => _borderBrush;
        set => _borderBrush = value;
    }

    /// <summary>
    /// 강조 색상 (헤더 등)
    /// </summary>
    public Brush AccentBrush
    {
        get => _accentBrush;
        set => _accentBrush = value;
    }

    /// <summary>
    /// 마크다운을 FlowDocument로 렌더링
    /// </summary>
    public FlowDocument Render(string markdown)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            PagePadding = new Thickness(0),
            Foreground = _textBrush
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
            if (line.TrimStart().StartsWith("```"))
            {
                // 테이블 종료 확인
                if (inTable)
                {
                    RenderTable(document, tableLines);
                    tableLines.Clear();
                    inTable = false;
                }

                if (inCodeBlock)
                {
                    // 코드 블록 종료
                    var codePara = new Paragraph(new Run(codeBlockContent.ToString().TrimEnd()))
                    {
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        Background = _codeBgBrush,
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
                currentParagraph = null;
                inTable = true;
                tableLines.Add(line);
                continue;
            }
            else if (inTable)
            {
                RenderTable(document, tableLines);
                tableLines.Clear();
                inTable = false;
            }

            // 헤더
            if (line.StartsWith("# "))
            {
                var header = new Paragraph(new Run(line.Substring(2)))
                {
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = _accentBrush,
                    Margin = new Thickness(0, 12, 0, 6)
                };
                document.Blocks.Add(header);
                currentParagraph = null;
                continue;
            }
            if (line.StartsWith("## "))
            {
                var header = new Paragraph(new Run(line.Substring(3)))
                {
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = _textBrush,
                    Margin = new Thickness(0, 10, 0, 5)
                };
                document.Blocks.Add(header);
                currentParagraph = null;
                continue;
            }
            if (line.StartsWith("### "))
            {
                var header = new Paragraph(new Run(line.Substring(4)))
                {
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = _textBrush,
                    Margin = new Thickness(0, 8, 0, 4)
                };
                document.Blocks.Add(header);
                currentParagraph = null;
                continue;
            }
            if (line.StartsWith("#### "))
            {
                var header = new Paragraph(new Run(line.Substring(5)))
                {
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = _textBrush,
                    Margin = new Thickness(0, 6, 0, 3)
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
                    BorderBrush = _borderBrush,
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0, 8, 0, 8)
                };
                document.Blocks.Add(separator);
                currentParagraph = null;
                continue;
            }

            // 체크박스 리스트
            if (line.TrimStart().StartsWith("- [ ]") || line.TrimStart().StartsWith("- [x]") ||
                line.TrimStart().StartsWith("- [X]"))
            {
                var indent = line.Length - line.TrimStart().Length;
                var isChecked = line.Contains("[x]") || line.Contains("[X]");
                var text = line.TrimStart().Substring(6);
                var checkPara = new Paragraph
                {
                    Margin = new Thickness(indent * 4 + 12, 2, 0, 2)
                };
                checkPara.Inlines.Add(new Run(isChecked ? "☑ " : "☐ ")
                {
                    Foreground = isChecked ? _secondaryBrush : _accentBrush
                });
                foreach (var inline in ProcessInlineStyles(text))
                {
                    checkPara.Inlines.Add(inline);
                }
                document.Blocks.Add(checkPara);
                currentParagraph = null;
                continue;
            }

            // 불릿 리스트
            if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
            {
                var indent = line.Length - line.TrimStart().Length;
                var listItem = new Paragraph { Foreground = _textBrush, Margin = new Thickness(indent * 4 + 12, 2, 0, 2) };
                listItem.Inlines.Add(new Run("• ") { Foreground = _textBrush });
                foreach (var inline in ProcessInlineStyles(line.TrimStart().Substring(2)))
                {
                    listItem.Inlines.Add(inline);
                }
                document.Blocks.Add(listItem);
                currentParagraph = null;
                continue;
            }

            // 번호 리스트
            var numberedMatch = Regex.Match(line.TrimStart(), @"^(\d+)\.\s(.+)$");
            if (numberedMatch.Success)
            {
                var indent = line.Length - line.TrimStart().Length;
                var listItem = new Paragraph { Foreground = _textBrush, Margin = new Thickness(indent * 4 + 12, 2, 0, 2) };
                listItem.Inlines.Add(new Run($"{numberedMatch.Groups[1].Value}. ") { Foreground = _textBrush });
                foreach (var inline in ProcessInlineStyles(numberedMatch.Groups[2].Value))
                {
                    listItem.Inlines.Add(inline);
                }
                document.Blocks.Add(listItem);
                currentParagraph = null;
                continue;
            }

            // 인용구
            if (line.StartsWith("> "))
            {
                var quote = new Paragraph
                {
                    BorderBrush = _secondaryBrush,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(12, 4, 0, 4),
                    Foreground = _secondaryBrush,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 4, 0, 4)
                };
                foreach (var inline in ProcessInlineStyles(line.Substring(2)))
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
            var styledLine = ProcessInlineStyles(line);
            if (currentParagraph == null)
            {
                currentParagraph = new Paragraph { Margin = new Thickness(0, 3, 0, 3), Foreground = _textBrush };
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
            RenderTable(document, tableLines);
        }

        return document;
    }

    /// <summary>
    /// 인라인 스타일 처리 (볼드, 이탤릭, 코드, 링크)
    /// </summary>
    private List<Inline> ProcessInlineStyles(string text)
    {
        var inlines = new List<Inline>();

        // 볼드+이탤릭: ***text*** 또는 ___text___
        // 볼드: **text** 또는 __text__
        // 이탤릭: *text* 또는 _text_
        // 인라인 코드: `code`
        // 링크: [text](url)

        var pattern = @"(\*\*\*|___)(.+?)\1|(\*\*|__)(.+?)\3|(\*|_)(.+?)\5|`([^`]+)`|\[([^\]]+)\]\(([^)]+)\)";
        var matches = Regex.Matches(text, pattern);

        int lastIndex = 0;
        foreach (Match match in matches)
        {
            // 매치 전 텍스트
            if (match.Index > lastIndex)
            {
                inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)) { Foreground = _textBrush });
            }

            if (match.Groups[1].Success) // 볼드+이탤릭
            {
                inlines.Add(new Run(match.Groups[2].Value)
                {
                    FontWeight = FontWeights.Bold,
                    FontStyle = FontStyles.Italic,
                    Foreground = _textBrush
                });
            }
            else if (match.Groups[3].Success) // 볼드
            {
                inlines.Add(new Run(match.Groups[4].Value)
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = _textBrush
                });
            }
            else if (match.Groups[5].Success) // 이탤릭
            {
                inlines.Add(new Run(match.Groups[6].Value)
                {
                    FontStyle = FontStyles.Italic,
                    Foreground = _textBrush
                });
            }
            else if (match.Groups[7].Success) // 인라인 코드
            {
                inlines.Add(new Run(match.Groups[7].Value)
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
                    Foreground = _textBrush
                });
            }
            else if (match.Groups[8].Success) // 링크
            {
                var hyperlink = new Hyperlink(new Run(match.Groups[8].Value))
                {
                    NavigateUri = new Uri(match.Groups[9].Value, UriKind.RelativeOrAbsolute),
                    Foreground = _accentBrush
                };
                inlines.Add(hyperlink);
            }

            lastIndex = match.Index + match.Length;
        }

        // 남은 텍스트
        if (lastIndex < text.Length)
        {
            inlines.Add(new Run(text.Substring(lastIndex)) { Foreground = _textBrush });
        }

        if (inlines.Count == 0)
        {
            inlines.Add(new Run(text) { Foreground = _textBrush });
        }

        return inlines;
    }

    /// <summary>
    /// 마크다운 테이블 렌더링
    /// </summary>
    private void RenderTable(FlowDocument document, List<string> tableLines)
    {
        if (tableLines.Count < 2) return;

        // 구분선 행 제거 (|---|---|)
        var dataLines = tableLines.Where(l => !Regex.IsMatch(l.Trim(), @"^\|[\s\-:|]+\|$")).ToList();
        if (dataLines.Count == 0) return;

        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = _borderBrush,
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
            var row = new TableRow();

            // 첫 번째 행은 헤더
            bool isHeader = rowIndex == 0;

            for (int colIndex = 0; colIndex < columnCount; colIndex++)
            {
                var cellText = colIndex < cells.Length ? cells[colIndex] : "";
                var cell = new TableCell(new Paragraph(new Run(cellText)
                {
                    FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = _textBrush
                }))
                {
                    BorderBrush = _borderBrush,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(6, 4, 6, 4)
                };

                if (isHeader)
                {
                    cell.Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128));
                }

                row.Cells.Add(cell);
            }

            rowGroup.Rows.Add(row);
        }

        document.Blocks.Add(table);
    }

    /// <summary>
    /// 테이블 행 파싱
    /// </summary>
    private string[] ParseTableRow(string line)
    {
        // | 로 분리하고 앞뒤 빈 셀 제거
        var cells = line.Split('|')
            .Select(c => c.Trim())
            .ToArray();

        // 앞뒤 빈 요소 제거
        if (cells.Length > 0 && string.IsNullOrEmpty(cells[0]))
            cells = cells.Skip(1).ToArray();
        if (cells.Length > 0 && string.IsNullOrEmpty(cells[cells.Length - 1]))
            cells = cells.Take(cells.Length - 1).ToArray();

        return cells;
    }
}
