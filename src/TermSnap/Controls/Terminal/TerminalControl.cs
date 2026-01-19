using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TermSnap.Controls.Terminal;

/// <summary>
/// WPF 터미널 컨트롤 - 버퍼 렌더링 및 키보드 입력 처리
/// </summary>
public class TerminalControl : FrameworkElement
{
    private readonly TerminalBuffer _buffer;
    private readonly AnsiParser _parser;
    private readonly DispatcherTimer _cursorTimer;
    private readonly DispatcherTimer _resizeDebounceTimer;
    private readonly DispatcherTimer _renderThrottleTimer;
    private bool _cursorBlinkState = true;
    private int _pendingResizeCols;
    private int _pendingResizeRows;
    private bool _renderPending = false;

    // 폰트 설정
    private Typeface _typeface = new Typeface("Consolas");
    private double _fontSize = 14;
    private double _cellWidth;
    private double _cellHeight;

    // 스크롤 (추후 구현)
    // private ScrollViewer? _scrollViewer;
    // private double _verticalOffset;

    // 링크 호버 상태
    private int _hoveredLinkRow = -1;
    private int _hoveredLinkStartCol = -1;
    private int _hoveredLinkEndCol = -1;
    private string? _hoveredLinkValue = null;
    private LinkType? _hoveredLinkType = null;
    private ToolTip? _linkToolTip;

    // 자동 스크롤 제어
    private bool _autoScroll = true;  // 맨 아래로 자동 스크롤 여부

    // 텍스트 선택 상태
    private bool _isSelecting = false;
    private Point _selectionStartPoint;
    private Point _selectionEndPoint;
    private string? _selectedText = null;

    // 입력 이벤트
    public event Action<string>? InputReceived;

    // 터미널 크기 변경 이벤트 (FrameworkElement.SizeChanged와 구분)
    public event Action<int, int>? TerminalSizeChanged;

    // 링크 클릭 이벤트 (Ctrl+Click)
    public event Action<LinkClickedEventArgs>? LinkClicked;

    // 의존성 속성
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(TerminalControl),
            new FrameworkPropertyMetadata(new FontFamily("Consolas"), OnFontChanged));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(TerminalControl),
            new FrameworkPropertyMetadata(14.0, OnFontChanged));

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public TerminalBuffer Buffer => _buffer;

    public TerminalControl()
    {
        _buffer = new TerminalBuffer(120, 30);
        _parser = new AnsiParser(_buffer);

        // 폰트 초기화
        UpdateFont();

        // 포커스 설정
        Focusable = true;
        FocusVisualStyle = null;

        // 커서 깜빡임 타이머
        _cursorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(530)
        };
        _cursorTimer.Tick += (s, e) =>
        {
            _cursorBlinkState = !_cursorBlinkState;
            InvalidateVisual();
        };

        // 리사이즈 디바운스 타이머 (300ms)
        _resizeDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _resizeDebounceTimer.Tick += OnResizeDebounceTimerTick;

        // 렌더링 쓰로틀 타이머 (16ms = 약 60fps)
        _renderThrottleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderThrottleTimer.Tick += OnRenderThrottleTimerTick;

        // 버퍼 변경 시 렌더링 요청 (쓰로틀링 적용)
        _buffer.BufferChanged += RequestRender;

        // IME 지원
        InputMethod.SetIsInputMethodEnabled(this, true);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _cursorTimer.Start();
        Focus();

        // 폰트 업데이트 (OnRenderSizeChanged에서 ResizeToFit 호출됨)
        UpdateFont();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _cursorTimer.Stop();
    }

    private static void OnFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalControl terminal)
        {
            terminal.UpdateFont();
            terminal.InvalidateVisual();
        }
    }

    private void UpdateFont()
    {
        _fontSize = FontSize;
        _typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // 셀 크기 계산
        var formattedText = new FormattedText(
            "M",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            _fontSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        _cellWidth = formattedText.Width;
        _cellHeight = formattedText.Height;
    }

    /// <summary>
    /// 텍스트 출력
    /// </summary>
    public void Write(string text)
    {
        _parser.Parse(text);

        // 자동 스크롤이 활성화되어 있을 때만 맨 아래로 스크롤
        if (_autoScroll && _buffer.ScrollOffset != 0)
        {
            _buffer.ScrollOffset = 0;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 화면 크기에 맞게 버퍼 크기 조정 (디바운스 적용)
    /// </summary>
    public void ResizeToFit()
    {
        if (_cellWidth <= 0 || _cellHeight <= 0) return;

        int cols = Math.Max(1, (int)(ActualWidth / _cellWidth));
        int rows = Math.Max(1, (int)(ActualHeight / _cellHeight));

        // 크기가 변경되지 않았으면 스킵
        if (cols == _buffer.Columns && rows == _buffer.Rows) return;

        // 펜딩 크기 저장 및 디바운스 타이머 시작
        _pendingResizeCols = cols;
        _pendingResizeRows = rows;

        // 디바운스: 타이머 리셋
        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
    }

    /// <summary>
    /// 디바운스 타이머 만료 시 실제 리사이즈 수행
    /// </summary>
    private void OnResizeDebounceTimerTick(object? sender, EventArgs e)
    {
        _resizeDebounceTimer.Stop();

        if (_pendingResizeCols <= 0 || _pendingResizeRows <= 0) return;
        if (_pendingResizeCols == _buffer.Columns && _pendingResizeRows == _buffer.Rows) return;

        // 버퍼 리사이즈 및 이벤트 발생
        _buffer.Resize(_pendingResizeCols, _pendingResizeRows);
        TerminalSizeChanged?.Invoke(_pendingResizeCols, _pendingResizeRows);
    }

    /// <summary>
    /// 렌더링 요청 (쓰로틀링)
    /// </summary>
    private void RequestRender()
    {
        if (!_renderPending)
        {
            _renderPending = true;

            // 타이머가 실행 중이 아니면 시작
            if (!_renderThrottleTimer.IsEnabled)
            {
                _renderThrottleTimer.Start();
            }
        }
    }

    /// <summary>
    /// 렌더링 쓰로틀 타이머 만료 시 실제 렌더링 수행
    /// </summary>
    private void OnRenderThrottleTimerTick(object? sender, EventArgs e)
    {
        if (_renderPending)
        {
            _renderPending = false;
            Dispatcher.BeginInvoke(InvalidateVisual, System.Windows.Threading.DispatcherPriority.Render);
        }
        else
        {
            // 더 이상 렌더링 요청이 없으면 타이머 중지
            _renderThrottleTimer.Stop();
        }
    }

    /// <summary>
    /// 즉시 리사이즈 (디바운스 없이)
    /// </summary>
    public void ResizeToFitImmediate()
    {
        _resizeDebounceTimer.Stop();

        if (_cellWidth <= 0 || _cellHeight <= 0) return;

        int cols = Math.Max(1, (int)(ActualWidth / _cellWidth));
        int rows = Math.Max(1, (int)(ActualHeight / _cellHeight));

        if (cols == _buffer.Columns && rows == _buffer.Rows) return;

        _buffer.Resize(cols, rows);
        TerminalSizeChanged?.Invoke(cols, rows);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ResizeToFit();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // 배경
        dc.DrawRectangle(
            new SolidColorBrush(TerminalColors.DefaultBackground),
            null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        if (_cellWidth <= 0 || _cellHeight <= 0) return;

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        int scrollOffset = _buffer.ScrollOffset;
        int scrollbackCount = _buffer.ScrollbackCount;

        // 각 행 렌더링
        for (int screenRow = 0; screenRow < _buffer.Rows; screenRow++)
        {
            double y = screenRow * _cellHeight;

            // 스크롤 오프셋에 따라 스크롤백 또는 현재 버퍼에서 읽기
            int dataRow = screenRow - scrollOffset;
            bool isScrollback = dataRow < 0;
            int scrollbackIndex = scrollbackCount + dataRow;  // 스크롤백 인덱스

            for (int col = 0; col < _buffer.Columns; col++)
            {
                double x = col * _cellWidth;
                TerminalCell cell;

                if (isScrollback && scrollbackIndex >= 0 && scrollbackIndex < scrollbackCount)
                {
                    // 스크롤백 버퍼에서 읽기
                    var scrollbackLine = _buffer.GetScrollbackLine(scrollbackIndex);
                    cell = (scrollbackLine != null && col < scrollbackLine.Length)
                        ? scrollbackLine[col]
                        : TerminalCell.Empty;
                }
                else if (!isScrollback && dataRow < _buffer.Rows)
                {
                    // 현재 버퍼에서 읽기
                    cell = _buffer.GetCell(dataRow, col);
                }
                else
                {
                    cell = TerminalCell.Empty;
                }

                // Wide char의 tail 셀은 건너뜀
                if (cell.IsWideCharTail)
                    continue;

                // Wide char는 2칸 너비
                double cellRenderWidth = cell.IsWideChar ? _cellWidth * 2 : _cellWidth;

                // 배경색
                if (cell.Background != TerminalColors.DefaultBackground &&
                    cell.Background != Colors.Transparent)
                {
                    dc.DrawRectangle(
                        new SolidColorBrush(cell.Background),
                        null,
                        new Rect(x, y, cellRenderWidth, _cellHeight));
                }

                // 커서 위치 표시 (스크롤백 보는 중에는 숨김)
                if (scrollOffset == 0 && dataRow == _buffer.CursorY && col == _buffer.CursorX &&
                    _buffer.CursorVisible && _cursorBlinkState)
                {
                    dc.DrawRectangle(
                        new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                        null,
                        new Rect(x, y, cellRenderWidth, _cellHeight));
                }

                // 호버된 링크인지 확인
                bool isHoveredLink = _hoveredLinkRow == screenRow &&
                                     col >= _hoveredLinkStartCol &&
                                     col < _hoveredLinkEndCol;

                // 문자 렌더링
                if (cell.Character != ' ' && cell.Character != '\0')
                {
                    var fontWeight = cell.Bold ? FontWeights.Bold : FontWeights.Normal;
                    var typeface = new Typeface(FontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal);

                    // 호버된 링크는 파란색으로 표시
                    var textColor = isHoveredLink
                        ? Color.FromRgb(100, 149, 237)  // CornflowerBlue
                        : cell.Foreground;

                    var formattedText = new FormattedText(
                        cell.Character.ToString(),
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        _fontSize,
                        new SolidColorBrush(textColor),
                        pixelsPerDip);

                    dc.DrawText(formattedText, new Point(x, y));

                    // 밑줄 (원래 밑줄 또는 호버된 링크)
                    if (cell.Underline || isHoveredLink)
                    {
                        var underlineColor = isHoveredLink
                            ? Color.FromRgb(100, 149, 237)
                            : cell.Foreground;
                        dc.DrawLine(
                            new Pen(new SolidColorBrush(underlineColor), 1),
                            new Point(x, y + _cellHeight - 2),
                            new Point(x + cellRenderWidth, y + _cellHeight - 2));
                    }
                }
            }
        }

        // 스크롤백 표시 중이면 인디케이터 표시
        if (scrollOffset > 0)
        {
            var indicatorText = new FormattedText(
                $"↑ {scrollOffset} lines",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _typeface,
                10,
                Brushes.Yellow,
                pixelsPerDip);

            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                null,
                new Rect(ActualWidth - indicatorText.Width - 20, 5, indicatorText.Width + 10, indicatorText.Height + 4));

            dc.DrawText(indicatorText, new Point(ActualWidth - indicatorText.Width - 15, 7));
        }

        // 선택 영역 표시
        if (_isSelecting || !string.IsNullOrEmpty(_selectedText))
        {
            RenderSelection(dc);
        }
    }

    /// <summary>
    /// 선택 영역 렌더링
    /// </summary>
    private void RenderSelection(DrawingContext dc)
    {
        if (_cellWidth <= 0 || _cellHeight <= 0) return;

        // 시작점과 끝점의 셀 좌표 계산
        int startCol = (int)(_selectionStartPoint.X / _cellWidth);
        int startRow = (int)(_selectionStartPoint.Y / _cellHeight);
        int endCol = (int)(_selectionEndPoint.X / _cellWidth);
        int endRow = (int)(_selectionEndPoint.Y / _cellHeight);

        // 시작점이 끝점보다 뒤에 있으면 교환
        if (startRow > endRow || (startRow == endRow && startCol > endCol))
        {
            (startRow, endRow) = (endRow, startRow);
            (startCol, endCol) = (endCol, startCol);
        }

        // 선택 영역 하이라이트
        var selectionBrush = new SolidColorBrush(Color.FromArgb(80, 100, 149, 237));  // 반투명 파란색

        for (int row = startRow; row <= endRow && row < _buffer.Rows; row++)
        {
            double y = row * _cellHeight;
            int colStart = (row == startRow) ? startCol : 0;
            int colEnd = (row == endRow) ? endCol : _buffer.Columns - 1;

            colStart = Math.Max(0, colStart);
            colEnd = Math.Min(_buffer.Columns - 1, colEnd);

            if (colStart <= colEnd)
            {
                double x = colStart * _cellWidth;
                double width = (colEnd - colStart + 1) * _cellWidth;
                dc.DrawRectangle(selectionBrush, null, new Rect(x, y, width, _cellHeight));
            }
        }
    }

    #region 키보드 입력

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        string? input = null;

        // Ctrl 조합
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+C: 선택된 텍스트가 있으면 복사, 없으면 프로세스에 전송
            if (e.Key == Key.C)
            {
                if (!string.IsNullOrEmpty(_selectedText))
                {
                    CopySelectionToClipboard();
                    ClearSelection();
                    e.Handled = true;
                    return;
                }
                else
                {
                    input = "\x03";  // Ctrl+C를 프로세스에 전송
                }
            }
            else
            {
                input = e.Key switch
                {
                    Key.D => "\x04",
                    Key.Z => "\x1a",
                    Key.L => "\x0c",
                    Key.A => "\x01",
                    Key.E => "\x05",
                    Key.U => "\x15",
                    Key.K => "\x0b",
                    Key.W => "\x17",
                    _ => null
                };
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift)
        {
            input = e.Key switch
            {
                Key.Up => "\x1b[A",
                Key.Down => "\x1b[B",
                Key.Right => "\x1b[C",
                Key.Left => "\x1b[D",
                Key.Enter => "\r",
                Key.Tab => "\t",
                Key.Escape => "\x1b",
                Key.Back => "\x7f",
                Key.Delete => "\x1b[3~",
                Key.Home => "\x1b[H",
                Key.End => "\x1b[F",
                Key.PageUp => null,  // 스크롤에 사용
                Key.PageDown => null,  // 스크롤에 사용
                Key.Insert => "\x1b[2~",
                Key.F1 => "\x1bOP",
                Key.F2 => "\x1bOQ",
                Key.F3 => "\x1bOR",
                Key.F4 => "\x1bOS",
                Key.F5 => "\x1b[15~",
                Key.F6 => "\x1b[17~",
                Key.F7 => "\x1b[18~",
                Key.F8 => "\x1b[19~",
                Key.F9 => "\x1b[20~",
                Key.F10 => "\x1b[21~",
                Key.F11 => "\x1b[23~",
                Key.F12 => "\x1b[24~",
                _ => null
            };
        }

        if (input != null)
        {
            InputReceived?.Invoke(input);
            e.Handled = true;
        }

        // PageUp/PageDown은 스크롤에 사용
        if (e.Key == Key.PageUp)
        {
            ScrollBy(_buffer.Rows - 2);  // 거의 한 화면씩
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown)
        {
            ScrollBy(-(_buffer.Rows - 2));
            e.Handled = true;
        }
    }

    /// <summary>
    /// 스크롤 오프셋 변경
    /// </summary>
    private void ScrollBy(int lines)
    {
        int newOffset = _buffer.ScrollOffset + lines;
        newOffset = Math.Max(0, Math.Min(newOffset, _buffer.ScrollbackCount));

        if (newOffset != _buffer.ScrollOffset)
        {
            _buffer.ScrollOffset = newOffset;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 맨 아래로 스크롤
    /// </summary>
    public void ScrollToBottom()
    {
        if (_buffer.ScrollOffset != 0)
        {
            _buffer.ScrollOffset = 0;
            InvalidateVisual();
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);

        if (!string.IsNullOrEmpty(e.Text))
        {
            InputReceived?.Invoke(e.Text);
            e.Handled = true;
        }
    }

    #endregion

    #region 마우스 입력

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var position = e.GetPosition(this);

        // Ctrl+Click 링크 열기
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            var link = GetLinkAtPosition(position);
            if (link != null)
            {
                LinkClicked?.Invoke(link);
                e.Handled = true;
                return;
            }
        }

        // 텍스트 선택 시작
        _isSelecting = true;
        _selectionStartPoint = position;
        _selectionEndPoint = position;
        _selectedText = null;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var position = e.GetPosition(this);

        // 텍스트 선택 중이면 끝점 업데이트
        if (_isSelecting)
        {
            _selectionEndPoint = position;
            InvalidateVisual();
        }
        else
        {
            UpdateLinkHover(position);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_isSelecting)
        {
            _isSelecting = false;
            ReleaseMouseCapture();

            // 선택된 텍스트 추출
            ExtractSelectedText();

            // 선택된 텍스트가 있으면 클립보드에 복사 (선택 후 자동 복사는 선택 사항)
            // 여기서는 선택만 하고 Ctrl+C로 복사하도록 함
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClearLinkHover();
    }

    /// <summary>
    /// 링크 호버 상태 업데이트
    /// </summary>
    private void UpdateLinkHover(Point position)
    {
        if (_cellWidth <= 0 || _cellHeight <= 0)
        {
            ClearLinkHover();
            return;
        }

        int col = (int)(position.X / _cellWidth);
        int screenRow = (int)(position.Y / _cellHeight);

        // 해당 행의 텍스트 가져오기
        string lineText = GetLineText(screenRow);
        if (string.IsNullOrEmpty(lineText) || col >= lineText.Length)
        {
            ClearLinkHover();
            return;
        }

        // URL 패턴 검사
        var (urlMatch, urlStart, urlEnd) = FindUrlAtPositionWithRange(lineText, col);
        if (urlMatch != null)
        {
            SetLinkHover(screenRow, urlStart, urlEnd, urlMatch, LinkType.Url, position);
            return;
        }

        // 파일 경로 패턴 검사
        var (pathMatch, pathStart, pathEnd) = FindPathAtPositionWithRange(lineText, col);
        if (pathMatch != null)
        {
            SetLinkHover(screenRow, pathStart, pathEnd, pathMatch, LinkType.FilePath, position);
            return;
        }

        ClearLinkHover();
    }

    /// <summary>
    /// 링크 호버 설정
    /// </summary>
    private void SetLinkHover(int row, int startCol, int endCol, string value, LinkType type, Point mousePos)
    {
        // 이미 같은 링크면 스킵
        if (_hoveredLinkRow == row && _hoveredLinkStartCol == startCol && _hoveredLinkEndCol == endCol)
        {
            return;
        }

        _hoveredLinkRow = row;
        _hoveredLinkStartCol = startCol;
        _hoveredLinkEndCol = endCol;
        _hoveredLinkValue = value;
        _hoveredLinkType = type;

        // 커서 변경
        Cursor = Cursors.Hand;

        // 툴팁 표시
        ShowLinkToolTip(mousePos, value, type);

        InvalidateVisual();
    }

    /// <summary>
    /// 링크 호버 해제
    /// </summary>
    private void ClearLinkHover()
    {
        if (_hoveredLinkRow == -1) return;

        _hoveredLinkRow = -1;
        _hoveredLinkStartCol = -1;
        _hoveredLinkEndCol = -1;
        _hoveredLinkValue = null;
        _hoveredLinkType = null;

        Cursor = Cursors.IBeam;
        HideLinkToolTip();

        InvalidateVisual();
    }

    /// <summary>
    /// 링크 툴팁 표시
    /// </summary>
    private void ShowLinkToolTip(Point position, string linkValue, LinkType type)
    {
        if (_linkToolTip == null)
        {
            _linkToolTip = new ToolTip
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
                HasDropShadow = true
            };
        }

        var typeText = type == LinkType.Url ? "URL" : "파일";
        var displayValue = linkValue.Length > 50 ? linkValue.Substring(0, 47) + "..." : linkValue;
        _linkToolTip.Content = $"Ctrl+클릭으로 {typeText} 열기\n{displayValue}";
        _linkToolTip.IsOpen = true;
    }

    /// <summary>
    /// 링크 툴팁 숨김
    /// </summary>
    private void HideLinkToolTip()
    {
        if (_linkToolTip != null)
        {
            _linkToolTip.IsOpen = false;
        }
    }

    /// <summary>
    /// 마우스 위치에서 링크(파일 경로 또는 URL) 감지
    /// </summary>
    private LinkClickedEventArgs? GetLinkAtPosition(Point position)
    {
        if (_cellWidth <= 0 || _cellHeight <= 0) return null;

        int col = (int)(position.X / _cellWidth);
        int screenRow = (int)(position.Y / _cellHeight);

        // 해당 행의 텍스트 가져오기
        string lineText = GetLineText(screenRow);
        if (string.IsNullOrEmpty(lineText)) return null;

        // 클릭한 위치의 문자 인덱스
        if (col >= lineText.Length) return null;

        // URL 패턴 검사
        var urlMatch = FindUrlAtPosition(lineText, col);
        if (urlMatch != null)
        {
            return new LinkClickedEventArgs(LinkType.Url, urlMatch);
        }

        // 파일 경로 패턴 검사
        var pathMatch = FindPathAtPosition(lineText, col);
        if (pathMatch != null)
        {
            return new LinkClickedEventArgs(LinkType.FilePath, pathMatch);
        }

        return null;
    }

    /// <summary>
    /// 화면상의 행에서 텍스트 가져오기
    /// </summary>
    private string GetLineText(int screenRow)
    {
        if (screenRow < 0 || screenRow >= _buffer.Rows) return string.Empty;

        var sb = new StringBuilder();
        int scrollOffset = _buffer.ScrollOffset;
        int scrollbackCount = _buffer.ScrollbackCount;
        int dataRow = screenRow - scrollOffset;
        bool isScrollback = dataRow < 0;
        int scrollbackIndex = scrollbackCount + dataRow;

        for (int col = 0; col < _buffer.Columns; col++)
        {
            TerminalCell cell;

            if (isScrollback && scrollbackIndex >= 0 && scrollbackIndex < scrollbackCount)
            {
                var scrollbackLine = _buffer.GetScrollbackLine(scrollbackIndex);
                cell = (scrollbackLine != null && col < scrollbackLine.Length)
                    ? scrollbackLine[col]
                    : TerminalCell.Empty;
            }
            else if (!isScrollback && dataRow < _buffer.Rows)
            {
                cell = _buffer.GetCell(dataRow, col);
            }
            else
            {
                cell = TerminalCell.Empty;
            }

            if (!cell.IsWideCharTail)
            {
                sb.Append(cell.Character == '\0' ? ' ' : cell.Character);
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 지정된 위치에서 URL 찾기
    /// </summary>
    private string? FindUrlAtPosition(string text, int position)
    {
        // URL 정규식 패턴
        var urlPattern = new Regex(@"(https?://|www\.)[^\s<>""'`\]\)]+", RegexOptions.IgnoreCase);
        var matches = urlPattern.Matches(text);

        foreach (Match match in matches)
        {
            if (position >= match.Index && position < match.Index + match.Length)
            {
                var url = match.Value;
                // www.로 시작하면 https:// 추가
                if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }
                return url;
            }
        }

        return null;
    }

    /// <summary>
    /// 지정된 위치에서 파일 경로 찾기
    /// </summary>
    private string? FindPathAtPosition(string text, int position)
    {
        // 다양한 경로 패턴
        // Unix: /path/to/file, ~/path, ./path
        // Windows: C:\path, D:\path
        var pathPatterns = new[]
        {
            @"(?<![a-zA-Z0-9])(/[a-zA-Z0-9._-]+)+/?",           // /path/to/file
            @"~/[a-zA-Z0-9._/-]+",                               // ~/path/to/file
            @"\./[a-zA-Z0-9._/-]+",                              // ./path/to/file
            @"[A-Za-z]:\\[^\s<>""'`\]\)]+",                      // C:\path\to\file
            @"(?<![a-zA-Z0-9])([a-zA-Z0-9._-]+/)+[a-zA-Z0-9._-]+", // relative/path/file
        };

        foreach (var pattern in pathPatterns)
        {
            var regex = new Regex(pattern);
            var matches = regex.Matches(text);

            foreach (Match match in matches)
            {
                if (position >= match.Index && position < match.Index + match.Length)
                {
                    var path = match.Value.TrimEnd('/', '\\', '.', ',', ';', ':', '!', '?');
                    // 최소 길이 체크 (너무 짧은 매치 제외)
                    if (path.Length >= 3)
                    {
                        return path;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 지정된 위치에서 URL 찾기 (범위 포함)
    /// </summary>
    private (string? url, int start, int end) FindUrlAtPositionWithRange(string text, int position)
    {
        var urlPattern = new Regex(@"(https?://|www\.)[^\s<>""'`\]\)]+", RegexOptions.IgnoreCase);
        var matches = urlPattern.Matches(text);

        foreach (Match match in matches)
        {
            if (position >= match.Index && position < match.Index + match.Length)
            {
                var url = match.Value;
                if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }
                return (url, match.Index, match.Index + match.Length);
            }
        }

        return (null, 0, 0);
    }

    /// <summary>
    /// 지정된 위치에서 파일 경로 찾기 (범위 포함)
    /// </summary>
    private (string? path, int start, int end) FindPathAtPositionWithRange(string text, int position)
    {
        var pathPatterns = new[]
        {
            @"(?<![a-zA-Z0-9])(/[a-zA-Z0-9._-]+)+/?",
            @"~/[a-zA-Z0-9._/-]+",
            @"\./[a-zA-Z0-9._/-]+",
            @"[A-Za-z]:\\[^\s<>""'`\]\)]+",
            @"(?<![a-zA-Z0-9])([a-zA-Z0-9._-]+/)+[a-zA-Z0-9._-]+",
        };

        foreach (var pattern in pathPatterns)
        {
            var regex = new Regex(pattern);
            var matches = regex.Matches(text);

            foreach (Match match in matches)
            {
                if (position >= match.Index && position < match.Index + match.Length)
                {
                    var path = match.Value.TrimEnd('/', '\\', '.', ',', ';', ':', '!', '?');
                    if (path.Length >= 3)
                    {
                        int trimmedLength = match.Value.Length - (match.Value.Length - path.Length);
                        return (path, match.Index, match.Index + trimmedLength);
                    }
                }
            }
        }

        return (null, 0, 0);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // 스크롤백 처리
        int scrollAmount = e.Delta > 0 ? 3 : -3;  // 한 번에 3줄씩
        int newOffset = _buffer.ScrollOffset - scrollAmount;

        // 범위 제한: 0 ~ ScrollbackCount
        newOffset = Math.Max(0, Math.Min(newOffset, _buffer.ScrollbackCount));

        if (newOffset != _buffer.ScrollOffset)
        {
            _buffer.ScrollOffset = newOffset;

            // 맨 아래로 스크롤하면 자동 스크롤 재활성화, 그 외에는 비활성화
            _autoScroll = (newOffset == 0);

            InvalidateVisual();
        }

        e.Handled = true;
    }

    #endregion

    #region 텍스트 선택

    /// <summary>
    /// 선택된 텍스트 추출
    /// </summary>
    private void ExtractSelectedText()
    {
        if (_cellWidth <= 0 || _cellHeight <= 0)
        {
            _selectedText = null;
            return;
        }

        // 시작점과 끝점의 셀 좌표 계산
        int startCol = (int)(_selectionStartPoint.X / _cellWidth);
        int startRow = (int)(_selectionStartPoint.Y / _cellHeight);
        int endCol = (int)(_selectionEndPoint.X / _cellWidth);
        int endRow = (int)(_selectionEndPoint.Y / _cellHeight);

        // 시작점이 끝점보다 뒤에 있으면 교환
        if (startRow > endRow || (startRow == endRow && startCol > endCol))
        {
            (startRow, endRow) = (endRow, startRow);
            (startCol, endCol) = (endCol, startCol);
        }

        // 버퍼에서 텍스트 추출
        var sb = new StringBuilder();
        int scrollOffset = _buffer.ScrollOffset;
        int scrollbackCount = _buffer.ScrollbackCount;

        for (int row = startRow; row <= endRow && row < _buffer.Rows; row++)
        {
            int dataRow = row - scrollOffset;
            bool isScrollback = dataRow < 0;
            int scrollbackIndex = scrollbackCount + dataRow;

            string? lineText = null;

            if (isScrollback && scrollbackIndex >= 0 && scrollbackIndex < scrollbackCount)
            {
                var scrollbackLine = _buffer.GetScrollbackLine(scrollbackIndex);
                if (scrollbackLine != null)
                {
                    lineText = ExtractLineText(scrollbackLine, row == startRow ? startCol : 0, row == endRow ? endCol : _buffer.Columns - 1);
                }
            }
            else if (!isScrollback && dataRow >= 0 && dataRow < _buffer.Rows)
            {
                // GetCell을 사용하여 라인 텍스트 추출
                lineText = ExtractLineTextFromBuffer(dataRow, row == startRow ? startCol : 0, row == endRow ? endCol : _buffer.Columns - 1);
            }

            if (lineText != null)
            {
                sb.Append(lineText);
                if (row < endRow)
                {
                    sb.AppendLine();
                }
            }
        }

        _selectedText = sb.ToString();
    }

    /// <summary>
    /// 라인에서 텍스트 추출 (스크롤백)
    /// </summary>
    private string ExtractLineText(TerminalCell[] line, int startCol, int endCol)
    {
        startCol = Math.Max(0, startCol);
        endCol = Math.Min(line.Length - 1, endCol);

        var sb = new StringBuilder();
        for (int col = startCol; col <= endCol; col++)
        {
            sb.Append(line[col].Character);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 버퍼에서 텍스트 추출
    /// </summary>
    private string ExtractLineTextFromBuffer(int row, int startCol, int endCol)
    {
        startCol = Math.Max(0, startCol);
        endCol = Math.Min(_buffer.Columns - 1, endCol);

        var sb = new StringBuilder();
        for (int col = startCol; col <= endCol; col++)
        {
            var cell = _buffer.GetCell(row, col);
            sb.Append(cell.Character);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 선택된 텍스트를 클립보드에 복사
    /// </summary>
    public void CopySelectionToClipboard()
    {
        if (!string.IsNullOrEmpty(_selectedText))
        {
            try
            {
                Clipboard.SetText(_selectedText);
            }
            catch
            {
                // 클립보드 접근 실패 무시
            }
        }
    }

    /// <summary>
    /// 선택 영역 해제
    /// </summary>
    public void ClearSelection()
    {
        _selectedText = null;
        _isSelecting = false;
        InvalidateVisual();
    }

    #endregion

    /// <summary>
    /// 리소스 해제
    /// </summary>
    public void Dispose()
    {
        _cursorTimer.Stop();
        _resizeDebounceTimer.Stop();
        _renderThrottleTimer.Stop();
    }
}

/// <summary>
/// 링크 타입
/// </summary>
public enum LinkType
{
    Url,
    FilePath
}

/// <summary>
/// 링크 클릭 이벤트 인자
/// </summary>
public class LinkClickedEventArgs : EventArgs
{
    public LinkType LinkType { get; }
    public string Value { get; }

    public LinkClickedEventArgs(LinkType linkType, string value)
    {
        LinkType = linkType;
        Value = value;
    }
}
