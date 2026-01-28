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
    private readonly DispatcherTimer _cursorRestartTimer;
    private bool _cursorBlinkState = true;
    private int _pendingResizeCols;
    private int _pendingResizeRows;
    private bool _renderPending = false;

    // DrawingVisual 기반 라인별 캐싱 (CPU 최적화)
    private readonly VisualCollection _visualChildren;
    private DrawingVisual[] _lineVisuals;
    private DrawingVisual? _backgroundVisual;
    private DrawingVisual? _overlayVisual;

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

    // 마우스 오버 상태 (스크롤 다운 버튼 표시용)
    private bool _isMouseOver = false;
    private Rect _scrollDownButtonRect;

    // 스크롤 다운 버튼 렌더링용 재사용 가능한 리소스 (성능 최적화)
    private static readonly SolidColorBrush ScrollButtonBackgroundBrush;
    private static readonly Pen ScrollButtonBorderPen;
    private static readonly SolidColorBrush ScrollButtonArrowBrush;

    // Glyph 캐시 (성능 최적화)
    private readonly Dictionary<GlyphCacheKey, FormattedText> _glyphCache = new();
    private const int MaxGlyphCacheSize = 2000;  // 최대 캐시 크기

    // Pen 캐시 (밑줄용)
    private readonly Dictionary<Color, Pen> _penCache = new();
    private const int MaxPenCacheSize = 256;

    // Brush 캐시 (배경색용)
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = new();
    private const int MaxBrushCacheSize = 256;

    static TerminalControl()
    {
        // Frozen Brush 생성 (스레드 간 공유 가능, 성능 향상)
        var bgBrush = new SolidColorBrush(Color.FromArgb(200, 46, 46, 46));
        bgBrush.Freeze();
        ScrollButtonBackgroundBrush = bgBrush;

        var borderBrush = new SolidColorBrush(Color.FromArgb(220, 68, 68, 68));
        borderBrush.Freeze();
        ScrollButtonBorderPen = new Pen(borderBrush, 1);
        ScrollButtonBorderPen.Freeze();

        var arrowBrush = new SolidColorBrush(Color.FromArgb(230, 153, 153, 153));
        arrowBrush.Freeze();
        ScrollButtonArrowBrush = arrowBrush;
    }

    // 텍스트 선택 상태
    private bool _isSelecting = false;
    private Point _selectionStartPoint;
    private Point _selectionEndPoint;
    private string? _selectedText = null;
    private DateTime _lastCopyTime = DateTime.MinValue;  // 마지막 복사 시간
    private const double CopyProtectionSeconds = 2.0;    // 복사 후 Ctrl+C 보호 시간 (초)

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

    /// <summary>
    /// 사용자 입력 전에 스타일 리셋 (배경색 아티팩트 방지)
    /// </summary>
    public void ResetStyleBeforeInput()
    {
        _buffer.ResetStyle();
    }

    // Visual 자식 관리 (DrawingVisual 기반)
    protected override int VisualChildrenCount => _visualChildren.Count;

    protected override Visual GetVisualChild(int index)
    {
        if (index < 0 || index >= _visualChildren.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        return _visualChildren[index];
    }

    public TerminalControl()
    {
        _buffer = new TerminalBuffer(130, 40);
        _parser = new AnsiParser(_buffer);

        // 폰트 초기화
        UpdateFont();

        // 포커스 설정
        Focusable = true;
        FocusVisualStyle = null;

        // DrawingVisual 기반 라인별 캐싱 초기화
        _visualChildren = new VisualCollection(this);
        _lineVisuals = new DrawingVisual[_buffer.Rows];

        // 배경 Visual (맨 뒤)
        _backgroundVisual = new DrawingVisual();
        _visualChildren.Add(_backgroundVisual);

        // 각 라인의 Visual 생성 (중간)
        for (int i = 0; i < _buffer.Rows; i++)
        {
            _lineVisuals[i] = new DrawingVisual();
            _visualChildren.Add(_lineVisuals[i]);
        }

        // 오버레이 Visual (선택 영역, 스크롤 버튼 등 - 맨 앞)
        _overlayVisual = new DrawingVisual();
        _visualChildren.Add(_overlayVisual);

        // GPU 가속 최적화
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);  // 앤티앨리어싱 OFF (속도 향상)
        SnapsToDevicePixels = true;  // 픽셀 정렬 (선명도 향상)

        // 커서 깜빡임 타이머 (비활성화, Warp 스타일 - 커서 없음)
        _cursorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(530)
        };
        // 타이머 Tick 이벤트 없음 (커서 표시 안 함)

        // 리사이즈 디바운스 타이머 (300ms)
        _resizeDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _resizeDebounceTimer.Tick += OnResizeDebounceTimerTick;

        // 렌더링 쓰로틀 타이머 (33ms = 30fps, DrawingVisual 캐싱으로 부드럽고 효율적)
        _renderThrottleTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _renderThrottleTimer.Tick += OnRenderThrottleTimerTick;

        // 커서 재시작 타이머 (사용하지 않음, Warp 스타일)
        _cursorRestartTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _cursorRestartTimer.Tick += (s, e) =>
        {
            _cursorRestartTimer.Stop();
        };

        // 버퍼 변경 시 렌더링 요청
        _buffer.BufferChanged += RequestRender;

        // IME 지원
        InputMethod.SetIsInputMethodEnabled(this, true);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Warp 스타일: 터미널 출력에는 커서를 표시하지 않음
        // 입력창(InteractiveInputTextBox)에만 커서가 표시됨
        _buffer.CursorVisible = false;

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
            terminal._buffer.MarkAllLinesDirty();
            terminal.UpdateLines();
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

        // 폰트 변경 시 Glyph 캐시 클리어
        _glyphCache.Clear();
        _penCache.Clear();
        _brushCache.Clear();
    }

    /// <summary>
    /// Brush 캐시에서 가져오기 (없으면 생성)
    /// </summary>
    private SolidColorBrush GetOrCreateBrush(Color color)
    {
        if (_brushCache.TryGetValue(color, out var cachedBrush))
        {
            return cachedBrush;
        }

        // 캐시 크기 제한
        if (_brushCache.Count >= MaxBrushCacheSize)
        {
            _brushCache.Clear();
            System.Diagnostics.Debug.WriteLine("[Brush Cache] Cleared all entries");
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();

        _brushCache[color] = brush;
        return brush;
    }

    /// <summary>
    /// Pen 캐시에서 가져오기 (없으면 생성)
    /// </summary>
    private Pen GetOrCreatePen(Color color)
    {
        if (_penCache.TryGetValue(color, out var cachedPen))
        {
            return cachedPen;
        }

        // 캐시 크기 제한
        if (_penCache.Count >= MaxPenCacheSize)
        {
            _penCache.Clear();
            System.Diagnostics.Debug.WriteLine("[Pen Cache] Cleared all entries");
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, 1);
        pen.Freeze();

        _penCache[color] = pen;
        return pen;
    }

    /// <summary>
    /// Glyph 캐시에서 FormattedText 가져오기 (없으면 생성)
    /// </summary>
    private FormattedText GetOrCreateGlyphText(char character, Color foreground, bool bold, double pixelsPerDip)
    {
        var key = new GlyphCacheKey(character, foreground, bold, _fontSize);

        // 캐시에 있으면 반환
        if (_glyphCache.TryGetValue(key, out var cachedText))
        {
            return cachedText;
        }

        // 캐시 크기 제한 (LRU 대신 단순히 절반 비우기)
        if (_glyphCache.Count >= MaxGlyphCacheSize)
        {
            // 캐시가 가득 찼을 때 절반 비우기 (간단한 전략)
            var keysToRemove = _glyphCache.Keys.Take(MaxGlyphCacheSize / 2).ToArray();
            foreach (var k in keysToRemove)
            {
                _glyphCache.Remove(k);
            }
            System.Diagnostics.Debug.WriteLine($"[Glyph Cache] Cleared {keysToRemove.Length} entries");
        }

        // 새로 생성
        var fontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
        var typeface = new Typeface(FontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal);

        var formattedText = new FormattedText(
            character.ToString(),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            _fontSize,
            new SolidColorBrush(foreground),
            pixelsPerDip);

        // FormattedText는 Freezable이 아니므로 그냥 저장
        _glyphCache[key] = formattedText;

        return formattedText;
    }

    /// <summary>
    /// 텍스트 출력 (즉시 파싱, GPU 캐싱 + 렌더링 쓰로틀로 최적화)
    /// </summary>
    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // 즉시 파싱 (ANSI 시퀀스 무결성 보장, 흔들림 방지)
        _parser.Parse(text);

        // 자동 스크롤
        if (_autoScroll && _buffer.ScrollOffset != 0)
        {
            _buffer.ScrollOffset = 0;
        }

        // 렌더링 요청 (GPU 캐싱 + 쓰로틀링으로 CPU 사용량 최소화)
        RequestRender();
    }

    /// <summary>
    /// 화면 크기에 맞게 버퍼 크기 조정 (디바운스 적용)
    /// 단계별 크기를 사용하여 작은 윈도우 크기 변경 시 리사이즈 방지
    /// </summary>
    public void ResizeToFit()
    {
        if (_cellWidth <= 0 || _cellHeight <= 0) return;

        // 실제 컨트롤 크기 기반으로 정확한 cols/rows 계산
        int actualCols = Math.Max(1, (int)(ActualWidth / _cellWidth));
        int actualRows = Math.Max(1, (int)(ActualHeight / _cellHeight));

        // 단계별 크기로 변환 (작은 변경에도 리사이즈되지 않도록)
        int cols = GetTieredColumns(actualCols);
        int rows = GetTieredRows(actualRows);

        // 크기가 변경되지 않았으면 스킵
        if (cols == _buffer.Columns && rows == _buffer.Rows) return;

        System.Diagnostics.Debug.WriteLine($"[ResizeToFit] Actual: {actualCols}x{actualRows} → Tiered: {cols}x{rows}");

        // 펜딩 크기 저장 및 디바운스 타이머 시작
        _pendingResizeCols = cols;
        _pendingResizeRows = rows;

        // 디바운스: 타이머 리셋
        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
    }

    /// <summary>
    /// 단계별 columns 계산 (10칸 단위로 반올림)
    /// </summary>
    private int GetTieredColumns(int actualCols)
    {
        // 10칸 단위로 반올림 (더 유연한 크기 조정)
        // 예: 82 → 80, 88 → 90, 125 → 130
        int tierSize = 10;
        int tiered = ((actualCols + tierSize / 2) / tierSize) * tierSize;

        // 최소 80, 최대 200
        return Math.Max(80, Math.Min(tiered, 200));
    }

    /// <summary>
    /// 단계별 rows 계산 (5줄 단위로 반올림)
    /// </summary>
    private int GetTieredRows(int actualRows)
    {
        // 5줄 단위로 반올림 (더 유연한 크기 조정)
        // 예: 34 → 35, 38 → 40, 43 → 45
        int tierSize = 5;
        int tiered = ((actualRows + tierSize / 2) / tierSize) * tierSize;

        // 최소 24, 최대 60
        return Math.Max(24, Math.Min(tiered, 60));
    }

    /// <summary>
    /// 디바운스 타이머 만료 시 실제 리사이즈 수행
    /// </summary>
    private void OnResizeDebounceTimerTick(object? sender, EventArgs e)
    {
        _resizeDebounceTimer.Stop();

        if (_pendingResizeCols <= 0 || _pendingResizeRows <= 0) return;
        if (_pendingResizeCols == _buffer.Columns && _pendingResizeRows == _buffer.Rows) return;

        System.Diagnostics.Debug.WriteLine($"[ResizeToFit] " +
            $"OldBuffer: {_buffer.Columns}x{_buffer.Rows}, " +
            $"NewBuffer: {_pendingResizeCols}x{_pendingResizeRows}, " +
            $"ActualSize: {ActualWidth:F0}x{ActualHeight:F0}");

        // 버퍼 리사이즈
        _buffer.Resize(_pendingResizeCols, _pendingResizeRows);

        // DrawingVisual 배열 재생성
        RecreateLineVisuals();

        // 이벤트 발생
        TerminalSizeChanged?.Invoke(_pendingResizeCols, _pendingResizeRows);
    }

    /// <summary>
    /// 라인 Visual 배열 재생성 (리사이즈 시)
    /// </summary>
    private void RecreateLineVisuals()
    {
        // 기존 라인 Visuals 제거
        for (int i = 0; i < _lineVisuals.Length; i++)
        {
            _visualChildren.Remove(_lineVisuals[i]);
        }

        // 새로운 배열 생성
        _lineVisuals = new DrawingVisual[_buffer.Rows];

        // 배경 Visual 다음에 라인 Visuals 삽입 (오버레이 전에)
        for (int i = 0; i < _buffer.Rows; i++)
        {
            _lineVisuals[i] = new DrawingVisual();
            _visualChildren.Insert(1 + i, _lineVisuals[i]);
        }

        // 모든 라인 다시 그리기
        _buffer.MarkAllLinesDirty();
        UpdateLines();
    }

    /// <summary>
    /// 렌더링 요청 (쓰로틀링 적용)
    /// </summary>
    private void RequestRender()
    {
        _renderPending = true;

        // 타이머가 실행 중이 아니면 시작
        if (!_renderThrottleTimer.IsEnabled)
        {
            _renderThrottleTimer.Start();
        }
    }

    /// <summary>
    /// 렌더링 쓰로틀 타이머 만료 시 실제 렌더링 수행 (dirty line만)
    /// </summary>
    private void OnRenderThrottleTimerTick(object? sender, EventArgs e)
    {
        if (_renderPending)
        {
            _renderPending = false;
            UpdateLines();  // dirty line만 다시 그리기 (CPU 90% 절감)
        }
    }

    /// <summary>
    /// 즉시 리사이즈 (디바운스 없이)
    /// 단계별 크기를 사용하여 작은 윈도우 크기 변경 시 리사이즈 방지
    /// </summary>
    public void ResizeToFitImmediate()
    {
        _resizeDebounceTimer.Stop();

        if (_cellWidth <= 0 || _cellHeight <= 0)
        {
            System.Diagnostics.Debug.WriteLine($"[ResizeToFitImmediate] 셀 크기 미설정: {_cellWidth}x{_cellHeight}");
            return;
        }

        // 실제 컨트롤 크기 기반으로 정확한 cols/rows 계산
        int actualCols = Math.Max(1, (int)(ActualWidth / _cellWidth));
        int actualRows = Math.Max(1, (int)(ActualHeight / _cellHeight));

        // 단계별 크기로 변환
        int cols = GetTieredColumns(actualCols);
        int rows = GetTieredRows(actualRows);

        if (cols == _buffer.Columns && rows == _buffer.Rows)
        {
            System.Diagnostics.Debug.WriteLine($"[ResizeToFitImmediate] 크기 변경 없음: {cols}x{rows} (actual: {actualCols}x{actualRows})");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ResizeToFitImmediate] " +
            $"OldBuffer: {_buffer.Columns}x{_buffer.Rows}, " +
            $"NewBuffer: {cols}x{rows} (actual: {actualCols}x{actualRows}), " +
            $"ActualSize: {ActualWidth:F0}x{ActualHeight:F0}, " +
            $"CellSize: {_cellWidth:F2}x{_cellHeight:F2}");

        _buffer.Resize(cols, rows);
        TerminalSizeChanged?.Invoke(cols, rows);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        System.Diagnostics.Debug.WriteLine($"[OnRenderSizeChanged] " +
            $"OldSize: {sizeInfo.PreviousSize.Width:F0}x{sizeInfo.PreviousSize.Height:F0}, " +
            $"NewSize: {ActualWidth:F0}x{ActualHeight:F0}, " +
            $"CellSize: {_cellWidth:F2}x{_cellHeight:F2}, " +
            $"BufferSize: {_buffer.Columns}x{_buffer.Rows}");

        ResizeToFit();
    }

    /// <summary>
    /// DrawingVisual 기반 라인별 렌더링 (dirty line만)
    /// </summary>
    private void UpdateLines()
    {
        if (_cellWidth <= 0 || _cellHeight <= 0) return;

        // 배경 업데이트 (항상)
        UpdateBackground();

        // dirty line만 업데이트 (CPU 90% 절감)
        foreach (var dirtyRow in _buffer.GetDirtyLines())
        {
            if (dirtyRow >= 0 && dirtyRow < _buffer.Rows)
            {
                UpdateLine(dirtyRow);
            }
        }
        _buffer.ClearDirtyLines();

        // 오버레이 업데이트 (선택, 스크롤 인디케이터 등)
        UpdateOverlay();
    }

    /// <summary>
    /// 배경 렌더링
    /// </summary>
    private void UpdateBackground()
    {
        if (_backgroundVisual == null) return;

        using (var dc = _backgroundVisual.RenderOpen())
        {
            var backgroundBrush = GetOrCreateBrush(TerminalColors.DefaultBackground);
            dc.DrawRectangle(
                backgroundBrush,
                null,
                new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }

    /// <summary>
    /// 단일 라인 렌더링 (DrawingVisual 캐싱)
    /// </summary>
    private void UpdateLine(int screenRow)
    {
        if (screenRow < 0 || screenRow >= _lineVisuals.Length) return;

        var visual = _lineVisuals[screenRow];
        using (var dc = visual.RenderOpen())
        {
            double y = screenRow * _cellHeight;
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            int scrollOffset = _buffer.ScrollOffset;
            int scrollbackCount = _buffer.ScrollbackCount;
            int dataRow = screenRow - scrollOffset;
            bool isScrollback = dataRow < 0;
            int scrollbackIndex = scrollbackCount + dataRow;

            for (int col = 0; col < _buffer.Columns; col++)
            {
                double x = col * _cellWidth;
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

                if (cell.IsWideCharTail)
                    continue;

                double cellRenderWidth = cell.IsWideChar ? _cellWidth * 2 : _cellWidth;

                // 배경색 (공백 문자에는 배경색을 렌더링하지 않음 - 커서 아티팩트 방지)
                // 실제 문자가 있는 셀에만 배경색 적용
                if (cell.Background != TerminalColors.DefaultBackground &&
                    cell.Background != Colors.Transparent &&
                    cell.Character != ' ' && cell.Character != '\0')
                {
                    var cellBackgroundBrush = GetOrCreateBrush(cell.Background);
                    dc.DrawRectangle(
                        cellBackgroundBrush,
                        null,
                        new Rect(x, 0, cellRenderWidth, _cellHeight));
                }

                // 호버된 링크인지 확인
                bool isHoveredLink = _hoveredLinkRow == screenRow &&
                                     col >= _hoveredLinkStartCol &&
                                     col < _hoveredLinkEndCol;

                // 문자 렌더링
                if (cell.Character != ' ' && cell.Character != '\0')
                {
                    var textColor = isHoveredLink
                        ? Color.FromRgb(100, 149, 237)
                        : cell.Foreground;

                    var formattedText = GetOrCreateGlyphText(cell.Character, textColor, cell.Bold, pixelsPerDip);
                    dc.DrawText(formattedText, new Point(x, 0));

                    // 밑줄
                    if (cell.Underline || isHoveredLink)
                    {
                        var underlineColor = isHoveredLink
                            ? Color.FromRgb(100, 149, 237)
                            : cell.Foreground;
                        var underlinePen = GetOrCreatePen(underlineColor);
                        dc.DrawLine(
                            underlinePen,
                            new Point(x, _cellHeight - 2),
                            new Point(x + cellRenderWidth, _cellHeight - 2));
                    }
                }
            }
        }

        // Visual 위치 설정
        visual.Offset = new Vector(0, screenRow * _cellHeight);
    }

    /// <summary>
    /// 오버레이 렌더링 (선택 영역, 스크롤 인디케이터, 버튼)
    /// </summary>
    private void UpdateOverlay()
    {
        if (_overlayVisual == null) return;

        using (var dc = _overlayVisual.RenderOpen())
        {
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            int scrollOffset = _buffer.ScrollOffset;

            // 스크롤백 인디케이터
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

                var indicatorBgBrush = GetOrCreateBrush(Color.FromArgb(180, 0, 0, 0));
                dc.DrawRectangle(
                    indicatorBgBrush,
                    null,
                    new Rect(ActualWidth - indicatorText.Width - 20, 5, indicatorText.Width + 10, indicatorText.Height + 4));

                dc.DrawText(indicatorText, new Point(ActualWidth - indicatorText.Width - 15, 7));
            }

            // 선택 영역
            if (_isSelecting || !string.IsNullOrEmpty(_selectedText))
            {
                RenderSelection(dc);
            }

            // 스크롤 다운 버튼
            if (_isMouseOver && scrollOffset > 0)
            {
                RenderScrollDownButton(dc, pixelsPerDip);
            }
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

        // 선택 영역 하이라이트 (캐시된 브러시 사용)
        var selectionBrush = GetOrCreateBrush(Color.FromArgb(80, 100, 149, 237));

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

    /// <summary>
    /// 스크롤 다운 버튼 렌더링 (오른쪽 아래, 다크 테마)
    /// </summary>
    private void RenderScrollDownButton(DrawingContext dc, double pixelsPerDip)
    {
        // 버튼 크기 및 위치
        const double buttonSize = 36;
        const double margin = 16;
        double x = ActualWidth - buttonSize - margin;
        double y = ActualHeight - buttonSize - margin;

        _scrollDownButtonRect = new Rect(x, y, buttonSize, buttonSize);

        // 둥근 사각형 배경 (재사용 가능한 Brush 사용)
        const double radius = 6.0;
        dc.DrawRoundedRectangle(ScrollButtonBackgroundBrush, ScrollButtonBorderPen, _scrollDownButtonRect, radius, radius);

        // 아래 화살표 아이콘 (↓) - FormattedText는 캐싱하지 않음 (위치가 동적)
        double centerX = x + buttonSize / 2;
        double centerY = y + buttonSize / 2;

        var scrollText = new FormattedText(
            "↓",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            20,
            ScrollButtonArrowBrush,
            pixelsPerDip);

        dc.DrawText(scrollText, new Point(centerX - scrollText.Width / 2, centerY - scrollText.Height / 2));
    }

    #region 키보드 입력

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // 이 컨트롤이 포커스를 가지고 있지 않으면 키 입력 무시
        // (인터랙티브 입력창 등 다른 컨트롤이 포커스를 가진 경우)
        if (!IsFocused && !IsKeyboardFocusWithin)
        {
            return;
        }

        string? input = null;

        // Ctrl 조합
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+C: 선택된 텍스트가 있거나 최근 복사가 있었으면 복사만 (프로세스에 전송 안 함)
            if (e.Key == Key.C)
            {
                // 선택 영역이 있는지 확인 (시각적으로 선택 영역이 보이는지)
                bool hasVisualSelection = _selectionStartPoint != _selectionEndPoint;
                bool hasSelectedText = !string.IsNullOrEmpty(_selectedText);
                bool recentlyCopied = (DateTime.Now - _lastCopyTime).TotalSeconds < CopyProtectionSeconds;

                if (hasSelectedText || hasVisualSelection)
                {
                    // 선택된 텍스트가 있으면 복사
                    if (hasSelectedText)
                    {
                        CopySelectionToClipboard();
                        _lastCopyTime = DateTime.Now;
                    }
                    e.Handled = true;
                    return;
                }
                else if (recentlyCopied)
                {
                    // 최근에 복사했으면 Ctrl+C를 무시 (종료 방지)
                    e.Handled = true;
                    return;
                }
                else
                {
                    input = "\x03";  // Ctrl+C를 프로세스에 전송
                }
            }
            // Ctrl+V: 클립보드 텍스트 붙여넣기
            else if (e.Key == Key.V)
            {
                e.Handled = true; // Ctrl+V는 항상 처리됨으로 표시
                try
                {
                    // STA 스레드에서 클립보드 접근
                    string? text = null;
                    if (Clipboard.ContainsText())
                    {
                        text = Clipboard.GetText();
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        System.Diagnostics.Debug.WriteLine($"[TerminalControl] Ctrl+V: 붙여넣기 텍스트 길이={text.Length}");
                        InputReceived?.Invoke(text);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[TerminalControl] Ctrl+V: 클립보드에 텍스트 없음");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TerminalControl] Ctrl+V 실패: {ex.Message}");
                }
                return;
            }
            // Ctrl+Enter: 줄바꿈
            else if (e.Key == Key.Enter)
            {
                InputReceived?.Invoke("\n");
                e.Handled = true;
                return;
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
            _buffer.MarkAllLinesDirty();
            UpdateLines();
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
            _buffer.MarkAllLinesDirty();
            UpdateLines();
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);

        // 이 컨트롤이 포커스를 가지고 있지 않으면 텍스트 입력 무시
        if (!IsFocused && !IsKeyboardFocusWithin)
        {
            return;
        }

        if (!string.IsNullOrEmpty(e.Text))
        {
            InputReceived?.Invoke(e.Text);
            e.Handled = true;
        }
    }

    #endregion

    #region 마우스 입력

    // 마우스 트래킹 상태
    private bool _lastMouseButtonLeft = false;
    private bool _lastMouseButtonMiddle = false;
    private bool _lastMouseButtonRight = false;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var position = e.GetPosition(this);

        // 스크롤 다운 버튼 클릭 체크
        if (_isMouseOver && _buffer.ScrollOffset > 0 && _scrollDownButtonRect.Contains(position))
        {
            ScrollToBottom();
            _autoScroll = true;  // 자동 스크롤 재활성화
            e.Handled = true;
            return;
        }

        // 마우스 트래킹이 활성화되어 있으면 ConPTY로 전송
        if (_buffer.MouseTrackingEnabled || _buffer.MouseButtonTracking)
        {
            SendMouseEvent(position, 0, true);
            _lastMouseButtonLeft = true;
            e.Handled = true;
            return;
        }

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
        UpdateOverlay();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var position = e.GetPosition(this);

        // Shift 키가 눌려있으면 마우스 트래킹 무시하고 텍스트 선택 모드
        bool forceTextSelection = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || _isSelecting;

        // 마우스 트래킹 - any event tracking 모드 (Shift 키로 우회 가능)
        if (_buffer.MouseAnyEventTracking && !forceTextSelection)
        {
            int button = _lastMouseButtonLeft ? 0 : _lastMouseButtonMiddle ? 1 : _lastMouseButtonRight ? 2 : -1;
            if (button >= 0)
            {
                SendMouseEvent(position, button + 32, true); // +32 = motion indicator
            }
            return;
        }

        // 텍스트 선택 중이면 끝점 업데이트
        if (_isSelecting)
        {
            _selectionEndPoint = position;
            UpdateOverlay();
        }
        else
        {
            UpdateLinkHover(position);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var position = e.GetPosition(this);

        // 텍스트 선택 중이면 마우스 트래킹보다 선택 완료를 우선
        if (_isSelecting)
        {
            _isSelecting = false;
            ReleaseMouseCapture();

            // 선택된 텍스트 추출
            ExtractSelectedText();

            // 오버레이 업데이트
            UpdateOverlay();

            e.Handled = true;
            return;
        }

        // 마우스 트래킹이 활성화되어 있으면 릴리스 이벤트 전송
        if (_buffer.MouseTrackingEnabled || _buffer.MouseButtonTracking)
        {
            SendMouseEvent(position, 3, false); // 3 = release
            _lastMouseButtonLeft = false;
            e.Handled = true;
            return;
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _isMouseOver = true;
        UpdateOverlay();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _isMouseOver = false;
        ClearLinkHover();
        UpdateOverlay();
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

        // 이전 링크 라인 다시 그리기
        if (_hoveredLinkRow >= 0 && _hoveredLinkRow < _buffer.Rows)
        {
            _buffer.MarkLineDirty(_hoveredLinkRow);
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

        // 새 링크 라인 다시 그리기
        if (row >= 0 && row < _buffer.Rows)
        {
            _buffer.MarkLineDirty(row);
        }

        UpdateLines();
    }

    /// <summary>
    /// 링크 호버 해제
    /// </summary>
    private void ClearLinkHover()
    {
        if (_hoveredLinkRow == -1) return;

        // 이전 링크 라인 다시 그리기
        if (_hoveredLinkRow >= 0 && _hoveredLinkRow < _buffer.Rows)
        {
            _buffer.MarkLineDirty(_hoveredLinkRow);
        }

        _hoveredLinkRow = -1;
        _hoveredLinkStartCol = -1;
        _hoveredLinkEndCol = -1;
        _hoveredLinkValue = null;
        _hoveredLinkType = null;

        Cursor = Cursors.IBeam;
        HideLinkToolTip();

        UpdateLines();
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

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);

        var position = e.GetPosition(this);

        // 마우스 트래킹이 활성화되어 있으면 ConPTY로 전송
        if (_buffer.MouseTrackingEnabled || _buffer.MouseButtonTracking)
        {
            SendMouseEvent(position, 2, true); // 2 = right button
            _lastMouseButtonRight = true;
            e.Handled = true;
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);

        var position = e.GetPosition(this);

        // 마우스 트래킹이 활성화되어 있으면 릴리스 이벤트 전송
        if (_buffer.MouseTrackingEnabled || _buffer.MouseButtonTracking)
        {
            SendMouseEvent(position, 3, false); // 3 = release
            _lastMouseButtonRight = false;
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var position = e.GetPosition(this);

        // 마우스 트래킹이 활성화되어 있으면 휠 이벤트 전송
        if (_buffer.MouseTrackingEnabled || _buffer.MouseButtonTracking)
        {
            int button = e.Delta > 0 ? 64 : 65; // 64 = wheel up, 65 = wheel down
            SendMouseEvent(position, button, true);
            e.Handled = true;
            return;
        }

        // 스크롤백 처리
        // e.Delta > 0: 휠 위로 = 위로 스크롤 (과거 내용) = 오프셋 증가
        // e.Delta < 0: 휠 아래로 = 아래로 스크롤 (최신 내용) = 오프셋 감소
        int scrollAmount = e.Delta > 0 ? 3 : -3;  // 한 번에 3줄씩
        int newOffset = _buffer.ScrollOffset + scrollAmount;

        // 범위 제한: 0 ~ ScrollbackCount
        newOffset = Math.Max(0, Math.Min(newOffset, _buffer.ScrollbackCount));

        if (newOffset != _buffer.ScrollOffset)
        {
            _buffer.ScrollOffset = newOffset;

            // 맨 아래로 스크롤하면 자동 스크롤 재활성화, 그 외에는 비활성화
            _autoScroll = (newOffset == 0);

            _buffer.MarkAllLinesDirty();
            UpdateLines();
        }

        e.Handled = true;
    }

    /// <summary>
    /// 마우스 이벤트를 xterm escape sequence로 변환하여 전송
    /// </summary>
    private void SendMouseEvent(Point position, int button, bool pressed)
    {
        if (_cellWidth <= 0 || _cellHeight <= 0) return;

        // 셀 좌표 계산 (1-based)
        int col = (int)(position.X / _cellWidth) + 1;
        int row = (int)(position.Y / _cellHeight) + 1;

        // 범위 체크
        col = Math.Max(1, Math.Min(col, _buffer.Columns));
        row = Math.Max(1, Math.Min(row, _buffer.Rows));

        string sequence;

        if (_buffer.MouseSgrMode)
        {
            // SGR mode: ESC [ < Cb ; Cx ; Cy M/m
            char terminator = pressed ? 'M' : 'm';
            sequence = $"\x1b[<{button};{col};{row}{terminator}";
        }
        else
        {
            // Normal mode: ESC [ M Cb Cx Cy
            // Cb, Cx, Cy는 각각 32를 더한 값
            char cb = (char)(button + 32);
            char cx = (char)(col + 32);
            char cy = (char)(row + 32);
            sequence = $"\x1b[M{cb}{cx}{cy}";
        }

        System.Diagnostics.Debug.WriteLine($"[Mouse] Sending: button={button}, col={col}, row={row}, sequence bytes={string.Join(" ", System.Text.Encoding.UTF8.GetBytes(sequence).Select(b => $"{b:X2}"))}");
        InputReceived?.Invoke(sequence);
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

        System.Diagnostics.Debug.WriteLine($"[Selection] Start: ({_selectionStartPoint.X:F1}, {_selectionStartPoint.Y:F1}) -> Cell({startCol}, {startRow})");
        System.Diagnostics.Debug.WriteLine($"[Selection] End: ({_selectionEndPoint.X:F1}, {_selectionEndPoint.Y:F1}) -> Cell({endCol}, {endRow})");
        System.Diagnostics.Debug.WriteLine($"[Selection] CellSize: {_cellWidth:F1} x {_cellHeight:F1}");

        // 컬럼 범위 제한
        startCol = Math.Max(0, Math.Min(startCol, _buffer.Columns - 1));
        endCol = Math.Max(0, Math.Min(endCol, _buffer.Columns - 1));

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

        // 화면에 표시된 전체 행 수 계산
        int visibleRows = Math.Max(_buffer.Rows, endRow + 1);

        for (int screenRow = startRow; screenRow <= endRow; screenRow++)
        {
            // 화면 좌표를 데이터 좌표로 변환
            // scrollOffset이 양수이면 위로 스크롤한 상태 (스크롤백 영역 보임)
            int dataRow = screenRow - scrollOffset;
            bool isScrollback = dataRow < 0;
            int scrollbackIndex = scrollbackCount + dataRow;

            string? lineText = null;
            int lineStartCol = (screenRow == startRow) ? startCol : 0;
            int lineEndCol = (screenRow == endRow) ? endCol : _buffer.Columns - 1;

            if (isScrollback && scrollbackIndex >= 0 && scrollbackIndex < scrollbackCount)
            {
                // 스크롤백 영역에서 추출
                var scrollbackLine = _buffer.GetScrollbackLine(scrollbackIndex);
                if (scrollbackLine != null)
                {
                    lineText = ExtractLineText(scrollbackLine, lineStartCol, lineEndCol);
                }
            }
            else if (!isScrollback && dataRow >= 0 && dataRow < _buffer.Rows)
            {
                // 현재 버퍼에서 추출
                lineText = ExtractLineTextFromBuffer(dataRow, lineStartCol, lineEndCol);
            }

            if (lineText != null)
            {
                sb.Append(lineText);
                if (screenRow < endRow)
                {
                    sb.AppendLine();
                }
            }
        }

        _selectedText = sb.ToString();
        System.Diagnostics.Debug.WriteLine($"[Selection] Result: '{_selectedText}' (length: {_selectedText?.Length ?? 0})");
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
        UpdateOverlay();
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

/// <summary>
/// Glyph 캐시 키 (문자 + 스타일 조합)
/// </summary>
internal struct GlyphCacheKey : IEquatable<GlyphCacheKey>
{
    public char Character { get; }
    public Color Foreground { get; }
    public bool Bold { get; }
    public double FontSize { get; }

    public GlyphCacheKey(char character, Color foreground, bool bold, double fontSize)
    {
        Character = character;
        Foreground = foreground;
        Bold = bold;
        FontSize = fontSize;
    }

    public bool Equals(GlyphCacheKey other)
    {
        return Character == other.Character &&
               Foreground.Equals(other.Foreground) &&
               Bold == other.Bold &&
               FontSize.Equals(other.FontSize);
    }

    public override bool Equals(object? obj)
    {
        return obj is GlyphCacheKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Character, Foreground, Bold, FontSize);
    }
}
