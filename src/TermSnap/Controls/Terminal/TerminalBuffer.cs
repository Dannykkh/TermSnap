using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace TermSnap.Controls.Terminal;

/// <summary>
/// 터미널 버퍼 - 문자 그리드 및 커서 관리
/// </summary>
public class TerminalBuffer
{
    private TerminalCell[,] _cells;
    private readonly List<TerminalCell[]> _scrollbackBuffer;
    private readonly int _maxScrollback;

    // 변경된 라인 추적 (DrawingVisual 캐싱 최적화)
    private readonly HashSet<int> _dirtyLines = new();
    private bool _allLinesDirty = true;

    // 대체 화면 버퍼 (Alternate Screen Buffer)
    private TerminalCell[,]? _alternateScreenCells;
    private bool _isAlternateScreen = false;
    private int _savedCursorX;
    private int _savedCursorY;

    // 스크롤 영역 (CSI r)
    private int _scrollTop = 0;     // 0-based
    private int _scrollBottom = -1;  // 0-based, -1 = Rows-1

    // 마우스 트래킹 모드 (xterm 확장)
    public bool MouseTrackingEnabled { get; set; } = false;        // ?1000h
    public bool MouseButtonTracking { get; set; } = false;         // ?1002h
    public bool MouseAnyEventTracking { get; set; } = false;       // ?1003h
    public bool MouseFocusTracking { get; set; } = false;          // ?1004h
    public bool MouseSgrMode { get; set; } = false;                // ?1006h

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int CursorX { get; set; }
    public int CursorY { get; set; }
    public bool CursorVisible { get; set; } = true;

    // 현재 스타일
    public Color CurrentForeground { get; set; } = TerminalColors.DefaultForeground;
    public Color CurrentBackground { get; set; } = TerminalColors.DefaultBackground;
    public bool CurrentBold { get; set; }
    public bool CurrentUnderline { get; set; }
    public bool CurrentInverse { get; set; }

    // 스크롤백
    public int ScrollbackCount => _scrollbackBuffer.Count;
    public int ScrollOffset { get; set; }

    // 변경 알림
    public event Action? BufferChanged;

    public TerminalBuffer(int columns = 130, int rows = 40, int maxScrollback = 10000)
    {
        Columns = columns;
        Rows = rows;
        _maxScrollback = maxScrollback;
        _cells = new TerminalCell[rows, columns];
        _scrollbackBuffer = new List<TerminalCell[]>();
        Clear();
    }

    /// <summary>
    /// 버퍼 크기 변경
    /// </summary>
    public void Resize(int columns, int rows)
    {
        if (columns == Columns && rows == Rows) return;

        var newCells = new TerminalCell[rows, columns];

        // 기존 내용 복사 (위쪽부터 복사)
        int copyRows = Math.Min(rows, Rows);
        int copyCols = Math.Min(columns, Columns);

        for (int y = 0; y < copyRows; y++)
        {
            for (int x = 0; x < copyCols; x++)
            {
                newCells[y, x] = _cells[y, x];
            }
            // 나머지 열 초기화
            for (int x = copyCols; x < columns; x++)
            {
                newCells[y, x] = TerminalCell.Empty;
            }
        }

        // 나머지 행 초기화
        for (int y = copyRows; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                newCells[y, x] = TerminalCell.Empty;
            }
        }

        _cells = newCells;
        Columns = columns;
        Rows = rows;

        // 커서 위치 조정
        CursorX = Math.Min(CursorX, columns - 1);
        CursorY = Math.Min(CursorY, rows - 1);

        // 모든 라인 다시 그리기
        MarkAllLinesDirty();

        BufferChanged?.Invoke();
    }


    /// <summary>
    /// 화면 전체 지우기
    /// </summary>
    public void Clear()
    {
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                _cells[y, x] = CreateEmptyCell();
            }
        }
        CursorX = 0;
        CursorY = 0;

        // 모든 라인 다시 그리기
        MarkAllLinesDirty();

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 빈 셀 생성 (Erase 용도)
    /// ECMA-48: Erase 명령은 항상 기본 배경색으로 지움
    /// 현재 스타일(역상 등)과 관계없이 기본 배경색 사용
    /// </summary>
    private TerminalCell CreateEmptyCell()
    {
        return new TerminalCell
        {
            Character = ' ',
            Foreground = TerminalColors.DefaultForeground,
            Background = TerminalColors.DefaultBackground,
            Bold = false,
            Underline = false,
            Inverse = false,
            IsWideChar = false,
            IsWideCharTail = false
        };
    }

    /// <summary>
    /// 문자 쓰기
    /// </summary>
    public void WriteChar(char c)
    {
        bool isWide = CharWidthHelper.IsWideChar(c);
        int charWidth = isWide ? 2 : 1;

        // Wide char가 줄 끝에 걸리면 다음 줄로
        if (isWide && CursorX >= Columns - 1)
        {
            // 현재 위치에 공백 넣고 줄바꿈
            if (CursorY >= 0 && CursorY < Rows && CursorX >= 0 && CursorX < Columns)
            {
                _cells[CursorY, CursorX] = CreateEmptyCell();
            }
            CursorX = 0;
            LineFeed();
        }
        else if (CursorX >= Columns)
        {
            // 자동 줄바꿈
            CursorX = 0;
            LineFeed();
        }

        if (CursorY >= 0 && CursorY < Rows && CursorX >= 0 && CursorX < Columns)
        {
            // 메인 셀
            _cells[CursorY, CursorX] = new TerminalCell
            {
                Character = c,
                Foreground = CurrentInverse ? CurrentBackground : CurrentForeground,
                Background = CurrentInverse ? CurrentForeground : CurrentBackground,
                Bold = CurrentBold,
                Underline = CurrentUnderline,
                Inverse = CurrentInverse,
                IsWideChar = isWide,
                IsWideCharTail = false
            };

            // Wide char의 두 번째 칸 (tail)
            if (isWide && CursorX + 1 < Columns)
            {
                _cells[CursorY, CursorX + 1] = new TerminalCell
                {
                    Character = '\0',  // 빈 문자
                    Foreground = CurrentInverse ? CurrentBackground : CurrentForeground,
                    Background = CurrentInverse ? CurrentForeground : CurrentBackground,
                    Bold = CurrentBold,
                    Underline = CurrentUnderline,
                    Inverse = CurrentInverse,
                    IsWideChar = false,
                    IsWideCharTail = true
                };
            }

            // 라인 변경 마크
            MarkLineDirty(CursorY);
        }

        CursorX += charWidth;
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 줄바꿈 (LF)
    /// </summary>
    public void LineFeed()
    {
        int scrollBottom = _scrollBottom < 0 ? Rows - 1 : _scrollBottom;

        CursorY++;

        // 스크롤 영역 내에서 하단을 벗어나면 스크롤
        if (CursorY > scrollBottom)
        {
            ScrollUp();
            CursorY = scrollBottom;
        }
        else if (CursorY >= Rows)
        {
            // 스크롤 영역 밖에서도 전체 화면 경계 체크
            ScrollUp();
            CursorY = Rows - 1;
        }
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 캐리지 리턴 (CR) - 커서만 줄 시작으로 이동 (VT100 표준)
    /// </summary>
    public void CarriageReturn()
    {
        CursorX = 0;
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 백스페이스
    /// </summary>
    public void Backspace()
    {
        if (CursorX > 0)
        {
            CursorX--;
            BufferChanged?.Invoke();
        }
    }

    /// <summary>
    /// 탭
    /// </summary>
    public void Tab()
    {
        int nextTab = ((CursorX / 8) + 1) * 8;
        CursorX = Math.Min(nextTab, Columns - 1);
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 화면 위로 스크롤 (맨 윗줄이 스크롤백으로)
    /// </summary>
    public void ScrollUp(int lines = 1)
    {
        int scrollTop = _scrollTop;
        int scrollBottom = _scrollBottom < 0 ? Rows - 1 : _scrollBottom;

        for (int i = 0; i < lines; i++)
        {
            // 스크롤 영역이 전체 화면이면 스크롤백에 저장
            if (scrollTop == 0 && scrollBottom == Rows - 1)
            {
                var topLine = new TerminalCell[Columns];
                for (int x = 0; x < Columns; x++)
                {
                    topLine[x] = _cells[scrollTop, x];
                }
                _scrollbackBuffer.Add(topLine);

                // 스크롤백 크기 제한
                while (_scrollbackBuffer.Count > _maxScrollback)
                {
                    _scrollbackBuffer.RemoveAt(0);
                }
            }

            // 스크롤 영역 내에서 한 줄씩 위로 이동
            for (int y = scrollTop; y < scrollBottom; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    _cells[y, x] = _cells[y + 1, x];
                }
            }

            // 스크롤 영역의 마지막 줄 비우기
            for (int x = 0; x < Columns; x++)
            {
                _cells[scrollBottom, x] = CreateEmptyCell();
            }
        }

        // 스크롤된 라인들 마크
        for (int y = scrollTop; y <= scrollBottom; y++)
        {
            MarkLineDirty(y);
        }

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 화면 아래로 스크롤 (CSI T)
    /// </summary>
    public void ScrollDown(int lines = 1)
    {
        int scrollTop = _scrollTop;
        int scrollBottom = _scrollBottom < 0 ? Rows - 1 : _scrollBottom;

        for (int i = 0; i < lines; i++)
        {
            // 스크롤 영역 내에서 한 줄씩 아래로 이동
            for (int y = scrollBottom; y > scrollTop; y--)
            {
                for (int x = 0; x < Columns; x++)
                {
                    _cells[y, x] = _cells[y - 1, x];
                }
            }

            // 스크롤 영역의 첫 줄 비우기
            for (int x = 0; x < Columns; x++)
            {
                _cells[scrollTop, x] = CreateEmptyCell();
            }
        }

        // 스크롤된 라인들 마크
        for (int y = scrollTop; y <= scrollBottom; y++)
        {
            MarkLineDirty(y);
        }

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 줄 삽입 (CSI L) - 현재 줄에 빈 줄 삽입하고 아래 줄들을 밀어냄
    /// </summary>
    public void InsertLines(int count = 1)
    {
        if (CursorY < 0 || CursorY >= Rows) return;

        for (int i = 0; i < count && CursorY + i < Rows; i++)
        {
            // 아래쪽 줄들을 한 줄씩 아래로 이동
            for (int y = Rows - 1; y > CursorY; y--)
            {
                for (int x = 0; x < Columns; x++)
                {
                    _cells[y, x] = _cells[y - 1, x];
                }
            }

            // 현재 줄 비우기
            for (int x = 0; x < Columns; x++)
            {
                _cells[CursorY, x] = CreateEmptyCell();
            }
        }

        // 영향받은 라인들 마크
        for (int y = CursorY; y < Rows; y++)
        {
            MarkLineDirty(y);
        }

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 줄 삭제 (CSI M) - 현재 줄을 삭제하고 아래 줄들을 위로 이동
    /// </summary>
    public void DeleteLines(int count = 1)
    {
        if (CursorY < 0 || CursorY >= Rows) return;

        for (int i = 0; i < count && CursorY < Rows; i++)
        {
            // 아래쪽 줄들을 한 줄씩 위로 이동
            for (int y = CursorY; y < Rows - 1; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    _cells[y, x] = _cells[y + 1, x];
                }
            }

            // 마지막 줄 비우기
            for (int x = 0; x < Columns; x++)
            {
                _cells[Rows - 1, x] = CreateEmptyCell();
            }
        }

        // 영향받은 라인들 마크
        for (int y = CursorY; y < Rows; y++)
        {
            MarkLineDirty(y);
        }

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 문자 삽입 (CSI @) - 현재 위치에 빈 문자 삽입하고 오른쪽 문자들 이동
    /// </summary>
    public void InsertChars(int count = 1)
    {
        if (CursorY < 0 || CursorY >= Rows || CursorX < 0 || CursorX >= Columns) return;

        int insertCount = Math.Min(count, Columns - CursorX);

        // 오른쪽 문자들을 이동
        for (int x = Columns - 1; x >= CursorX + insertCount; x--)
        {
            _cells[CursorY, x] = _cells[CursorY, x - insertCount];
        }

        // 삽입 위치에 빈 셀 넣기
        for (int x = CursorX; x < CursorX + insertCount && x < Columns; x++)
        {
            _cells[CursorY, x] = CreateEmptyCell();
        }

        // 현재 라인 마크
        MarkLineDirty(CursorY);

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 문자 삭제 (CSI P) - 현재 위치의 문자 삭제하고 오른쪽 문자들을 왼쪽으로 이동
    /// </summary>
    public void DeleteChars(int count = 1)
    {
        if (CursorY < 0 || CursorY >= Rows || CursorX < 0 || CursorX >= Columns) return;

        int deleteCount = Math.Min(count, Columns - CursorX);

        // 오른쪽 문자들을 왼쪽으로 이동
        for (int x = CursorX; x < Columns - deleteCount; x++)
        {
            _cells[CursorY, x] = _cells[CursorY, x + deleteCount];
        }

        // 끝 부분을 빈 셀로 채우기
        for (int x = Columns - deleteCount; x < Columns; x++)
        {
            _cells[CursorY, x] = CreateEmptyCell();
        }

        // 현재 라인 마크
        MarkLineDirty(CursorY);

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 문자 지우기 (CSI X) - 현재 위치부터 n개 문자를 빈 칸으로 교체
    /// </summary>
    public void EraseChars(int count = 1)
    {
        if (CursorY < 0 || CursorY >= Rows || CursorX < 0 || CursorX >= Columns) return;

        int eraseCount = Math.Min(count, Columns - CursorX);

        for (int x = CursorX; x < CursorX + eraseCount; x++)
        {
            _cells[CursorY, x] = CreateEmptyCell();
        }

        // 현재 라인 마크
        MarkLineDirty(CursorY);

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 커서 위치 저장 (ESC 7, CSI s)
    /// </summary>
    public void SaveCursor()
    {
        _savedCursorX = CursorX;
        _savedCursorY = CursorY;
    }

    /// <summary>
    /// 커서 위치 복원 (ESC 8, CSI u)
    /// </summary>
    public void RestoreCursor()
    {
        CursorX = Math.Max(0, Math.Min(_savedCursorX, Columns - 1));
        CursorY = Math.Max(0, Math.Min(_savedCursorY, Rows - 1));
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 스크롤 영역 설정 (CSI r)
    /// </summary>
    /// <param name="top">상단 줄 (1-based, 0이면 1로 취급)</param>
    /// <param name="bottom">하단 줄 (1-based, 0이면 Rows로 취급)</param>
    public void SetScrollingRegion(int top, int bottom)
    {
        // 1-based to 0-based 변환
        int scrollTop = (top == 0 ? 1 : top) - 1;
        int scrollBottom = (bottom == 0 ? Rows : bottom) - 1;

        // 범위 검증
        scrollTop = Math.Max(0, Math.Min(scrollTop, Rows - 1));
        scrollBottom = Math.Max(0, Math.Min(scrollBottom, Rows - 1));

        // 상단이 하단보다 크면 무시
        if (scrollTop >= scrollBottom)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[TerminalBuffer] Invalid scroll region: top={top}, bottom={bottom}");
            return;
        }

        _scrollTop = scrollTop;
        _scrollBottom = scrollBottom;

        // 커서를 홈 위치로 (VT100 표준)
        CursorX = 0;
        CursorY = 0;

        System.Diagnostics.Debug.WriteLine(
            $"[TerminalBuffer] Set scroll region: {_scrollTop}-{_scrollBottom} (0-based)");
    }

    /// <summary>
    /// 스크롤 영역 리셋 (전체 화면)
    /// </summary>
    public void ResetScrollingRegion()
    {
        _scrollTop = 0;
        _scrollBottom = -1; // -1은 Rows-1을 의미
        System.Diagnostics.Debug.WriteLine("[TerminalBuffer] Reset scroll region to full screen");
    }

    /// <summary>
    /// 커서 위치 설정 (1-based to 0-based)
    /// </summary>
    public void SetCursorPosition(int row, int col)
    {
        int newY = Math.Max(0, Math.Min(row - 1, Rows - 1));
        int newX = Math.Max(0, Math.Min(col - 1, Columns - 1));

        // 범위 초과 시 디버그 로그 (터미널 크기 조정 필요)
        if (col - 1 >= Columns || row - 1 >= Rows)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[TerminalBuffer] Cursor position out of range: requested ({row},{col}), " +
                $"clamped to ({newY + 1},{newX + 1}), buffer size: {Columns}x{Rows}");
        }

        CursorY = newY;
        CursorX = newX;
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 커서 이동
    /// </summary>
    public void MoveCursor(int deltaX, int deltaY)
    {
        CursorX = Math.Max(0, Math.Min(CursorX + deltaX, Columns - 1));
        CursorY = Math.Max(0, Math.Min(CursorY + deltaY, Rows - 1));
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 커서부터 줄 끝까지 지우기
    /// </summary>
    public void EraseToEndOfLine()
    {
        for (int x = CursorX; x < Columns; x++)
        {
            _cells[CursorY, x] = CreateEmptyCell();
        }

        // 현재 라인 마크
        MarkLineDirty(CursorY);

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 줄 처음부터 커서까지 지우기
    /// </summary>
    public void EraseToStartOfLine()
    {
        for (int x = 0; x <= CursorX && x < Columns; x++)
        {
            _cells[CursorY, x] = CreateEmptyCell();
        }

        // 현재 라인 마크
        MarkLineDirty(CursorY);

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 현재 줄 전체 지우기
    /// </summary>
    public void EraseLine()
    {
        for (int x = 0; x < Columns; x++)
        {
            _cells[CursorY, x] = CreateEmptyCell();
        }

        // 현재 라인 마크
        MarkLineDirty(CursorY);

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 커서부터 화면 끝까지 지우기
    /// </summary>
    public void EraseToEndOfScreen()
    {
        // 현재 줄의 커서부터 끝까지
        EraseToEndOfLine();
        // 아래 줄들 전체
        for (int y = CursorY + 1; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                _cells[y, x] = CreateEmptyCell();
            }
            MarkLineDirty(y);
        }
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 화면 처음부터 커서까지 지우기
    /// </summary>
    public void EraseToStartOfScreen()
    {
        // 위 줄들 전체
        for (int y = 0; y < CursorY; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                _cells[y, x] = CreateEmptyCell();
            }
            MarkLineDirty(y);
        }
        // 현재 줄의 처음부터 커서까지
        EraseToStartOfLine();
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 화면 전체 지우기 (스크롤백 유지)
    /// </summary>
    public void EraseScreen()
    {
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                _cells[y, x] = CreateEmptyCell();
            }
        }

        // 모든 라인 다시 그리기
        MarkAllLinesDirty();

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 스타일 리셋
    /// </summary>
    public void ResetStyle()
    {
        CurrentForeground = TerminalColors.DefaultForeground;
        CurrentBackground = TerminalColors.DefaultBackground;
        CurrentBold = false;
        CurrentUnderline = false;
        CurrentInverse = false;
    }

    /// <summary>
    /// 대체 화면 버퍼로 전환 (ESC [?1049h)
    /// </summary>
    public void EnterAlternateScreen()
    {
        if (_isAlternateScreen) return;

        // 현재 메인 화면 백업
        _alternateScreenCells = new TerminalCell[Rows, Columns];
        Array.Copy(_cells, _alternateScreenCells, _cells.Length);

        // 커서 위치 저장
        _savedCursorX = CursorX;
        _savedCursorY = CursorY;

        // 새로운 빈 화면으로 전환
        _cells = new TerminalCell[Rows, Columns];
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                _cells[y, x] = CreateEmptyCell();
            }
        }

        // 커서를 홈 위치로
        CursorX = 0;
        CursorY = 0;

        _isAlternateScreen = true;

        // 모든 라인 다시 그리기
        MarkAllLinesDirty();

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 메인 화면 버퍼로 복귀 (ESC [?1049l)
    /// </summary>
    public void ExitAlternateScreen()
    {
        if (!_isAlternateScreen || _alternateScreenCells == null) return;

        // 백업한 메인 화면 복원
        _cells = _alternateScreenCells;
        _alternateScreenCells = null;

        // 커서 위치 복원
        CursorX = _savedCursorX;
        CursorY = _savedCursorY;

        _isAlternateScreen = false;

        // 모든 라인 다시 그리기
        MarkAllLinesDirty();

        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 셀 가져오기
    /// </summary>
    public TerminalCell GetCell(int row, int col)
    {
        if (row >= 0 && row < Rows && col >= 0 && col < Columns)
        {
            return _cells[row, col];
        }
        return TerminalCell.Empty;
    }

    /// <summary>
    /// 스크롤백 줄 가져오기
    /// </summary>
    public TerminalCell[]? GetScrollbackLine(int index)
    {
        if (index >= 0 && index < _scrollbackBuffer.Count)
        {
            return _scrollbackBuffer[index];
        }
        return null;
    }

    /// <summary>
    /// 현재 화면 텍스트 가져오기
    /// </summary>
    public string GetScreenText()
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                sb.Append(_cells[y, x].Character);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    #region Dirty Line Tracking (DrawingVisual 최적화)

    /// <summary>
    /// 특정 라인을 변경됨으로 마크
    /// </summary>
    public void MarkLineDirty(int row)
    {
        if (row >= 0 && row < Rows)
        {
            _dirtyLines.Add(row);
        }
    }

    /// <summary>
    /// 모든 라인을 변경됨으로 마크
    /// </summary>
    public void MarkAllLinesDirty()
    {
        _allLinesDirty = true;
        _dirtyLines.Clear();
    }

    /// <summary>
    /// 변경된 라인 목록 가져오기
    /// </summary>
    public IEnumerable<int> GetDirtyLines()
    {
        if (_allLinesDirty)
        {
            for (int i = 0; i < Rows; i++)
            {
                yield return i;
            }
        }
        else
        {
            foreach (var line in _dirtyLines)
            {
                yield return line;
            }
        }
    }

    /// <summary>
    /// Dirty 플래그 초기화
    /// </summary>
    public void ClearDirtyLines()
    {
        _allLinesDirty = false;
        _dirtyLines.Clear();
    }

    #endregion
}
