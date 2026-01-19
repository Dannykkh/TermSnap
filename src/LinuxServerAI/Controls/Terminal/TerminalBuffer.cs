using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Nebula.Controls.Terminal;

/// <summary>
/// 터미널 버퍼 - 문자 그리드 및 커서 관리
/// </summary>
public class TerminalBuffer
{
    private TerminalCell[,] _cells;
    private readonly List<TerminalCell[]> _scrollbackBuffer;
    private readonly int _maxScrollback;

    // 대체 화면 버퍼 (Alternate Screen Buffer)
    private TerminalCell[,]? _alternateScreenCells;
    private bool _isAlternateScreen = false;
    private int _savedCursorX;
    private int _savedCursorY;

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

    public TerminalBuffer(int columns = 120, int rows = 30, int maxScrollback = 10000)
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

        // 기존 내용 복사
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
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 현재 스타일로 빈 셀 생성
    /// </summary>
    private TerminalCell CreateEmptyCell()
    {
        return new TerminalCell
        {
            Character = ' ',
            Foreground = CurrentForeground,
            Background = CurrentBackground,
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
        }

        CursorX += charWidth;
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 줄바꿈 (LF)
    /// </summary>
    public void LineFeed()
    {
        CursorY++;
        if (CursorY >= Rows)
        {
            ScrollUp();
            CursorY = Rows - 1;
        }
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 캐리지 리턴 (CR)
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
        for (int i = 0; i < lines; i++)
        {
            // 맨 윗줄을 스크롤백에 저장
            var topLine = new TerminalCell[Columns];
            for (int x = 0; x < Columns; x++)
            {
                topLine[x] = _cells[0, x];
            }
            _scrollbackBuffer.Add(topLine);

            // 스크롤백 크기 제한
            while (_scrollbackBuffer.Count > _maxScrollback)
            {
                _scrollbackBuffer.RemoveAt(0);
            }

            // 한 줄씩 위로 이동
            for (int y = 0; y < Rows - 1; y++)
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
        BufferChanged?.Invoke();
    }

    /// <summary>
    /// 커서 위치 설정 (1-based to 0-based)
    /// </summary>
    public void SetCursorPosition(int row, int col)
    {
        CursorY = Math.Max(0, Math.Min(row - 1, Rows - 1));
        CursorX = Math.Max(0, Math.Min(col - 1, Columns - 1));
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
}
