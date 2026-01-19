using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Nebula.Controls.Terminal;

/// <summary>
/// ANSI/VT100 이스케이프 시퀀스 파서
/// </summary>
public class AnsiParser
{
    private readonly TerminalBuffer _buffer;
    private ParserState _state = ParserState.Normal;
    private readonly List<int> _params = new();
    private string _currentParam = "";
    private string _oscString = "";

    private enum ParserState
    {
        Normal,
        Escape,
        Csi,          // Control Sequence Introducer (ESC [)
        Osc,          // Operating System Command (ESC ])
        OscString,
        EscapeIntermediate
    }

    public AnsiParser(TerminalBuffer buffer)
    {
        _buffer = buffer;
    }

    /// <summary>
    /// 텍스트 처리
    /// </summary>
    public void Parse(string text)
    {
        foreach (char c in text)
        {
            ProcessChar(c);
        }
    }

    private void ProcessChar(char c)
    {
        switch (_state)
        {
            case ParserState.Normal:
                ProcessNormalChar(c);
                break;

            case ParserState.Escape:
                ProcessEscapeChar(c);
                break;

            case ParserState.Csi:
                ProcessCsiChar(c);
                break;

            case ParserState.Osc:
                ProcessOscChar(c);
                break;

            case ParserState.OscString:
                ProcessOscStringChar(c);
                break;

            case ParserState.EscapeIntermediate:
                ProcessEscapeIntermediateChar(c);
                break;
        }
    }

    private void ProcessNormalChar(char c)
    {
        switch (c)
        {
            case '\x1b': // ESC
                _state = ParserState.Escape;
                break;

            case '\r': // CR
                _buffer.CarriageReturn();
                break;

            case '\n': // LF
                _buffer.LineFeed();
                break;

            case '\t': // TAB
                _buffer.Tab();
                break;

            case '\b': // BS
                _buffer.Backspace();
                break;

            case '\x07': // BEL
                // 벨소리 무시
                break;

            default:
                if (c >= ' ' || c > '\x7f') // 출력 가능한 문자
                {
                    _buffer.WriteChar(c);
                }
                break;
        }
    }

    private void ProcessEscapeChar(char c)
    {
        switch (c)
        {
            case '[': // CSI
                _state = ParserState.Csi;
                _params.Clear();
                _currentParam = "";
                break;

            case ']': // OSC
                _state = ParserState.Osc;
                _oscString = "";
                break;

            case '(': // Character set G0
            case ')': // Character set G1
            case '*': // Character set G2
            case '+': // Character set G3
                _state = ParserState.EscapeIntermediate;
                break;

            case 'M': // Reverse Index
                if (_buffer.CursorY > 0)
                    _buffer.MoveCursor(0, -1);
                _state = ParserState.Normal;
                break;

            case 'D': // Index (line feed)
                _buffer.LineFeed();
                _state = ParserState.Normal;
                break;

            case 'E': // Next Line
                _buffer.CarriageReturn();
                _buffer.LineFeed();
                _state = ParserState.Normal;
                break;

            case 'c': // Reset
                _buffer.Clear();
                _buffer.ResetStyle();
                _state = ParserState.Normal;
                break;

            case '7': // Save cursor
                // TODO: 커서 위치 저장
                _state = ParserState.Normal;
                break;

            case '8': // Restore cursor
                // TODO: 커서 위치 복원
                _state = ParserState.Normal;
                break;

            case '=': // Application keypad
            case '>': // Normal keypad
                _state = ParserState.Normal;
                break;

            default:
                _state = ParserState.Normal;
                break;
        }
    }

    private void ProcessCsiChar(char c)
    {
        // 파라미터 문자
        if (c >= '0' && c <= '9')
        {
            _currentParam += c;
            return;
        }

        if (c == ';')
        {
            _params.Add(string.IsNullOrEmpty(_currentParam) ? 0 : int.Parse(_currentParam));
            _currentParam = "";
            return;
        }

        if (c == '?')
        {
            // Private mode - 파라미터 앞에 ? 가 붙음
            return;
        }

        // 마지막 파라미터 추가
        if (!string.IsNullOrEmpty(_currentParam))
        {
            _params.Add(int.Parse(_currentParam));
        }

        // CSI 명령 실행
        ExecuteCsiCommand(c);
        _state = ParserState.Normal;
    }

    private void ExecuteCsiCommand(char command)
    {
        int p1 = _params.Count > 0 ? _params[0] : 0;
        int p2 = _params.Count > 1 ? _params[1] : 0;

        switch (command)
        {
            case 'A': // Cursor Up
                _buffer.MoveCursor(0, -(p1 == 0 ? 1 : p1));
                break;

            case 'B': // Cursor Down
                _buffer.MoveCursor(0, p1 == 0 ? 1 : p1);
                break;

            case 'C': // Cursor Forward
                _buffer.MoveCursor(p1 == 0 ? 1 : p1, 0);
                break;

            case 'D': // Cursor Back
                _buffer.MoveCursor(-(p1 == 0 ? 1 : p1), 0);
                break;

            case 'E': // Cursor Next Line
                _buffer.CarriageReturn();
                _buffer.MoveCursor(0, p1 == 0 ? 1 : p1);
                break;

            case 'F': // Cursor Previous Line
                _buffer.CarriageReturn();
                _buffer.MoveCursor(0, -(p1 == 0 ? 1 : p1));
                break;

            case 'G': // Cursor Horizontal Absolute
                _buffer.CursorX = Math.Max(0, Math.Min((p1 == 0 ? 1 : p1) - 1, _buffer.Columns - 1));
                break;

            case 'H': // Cursor Position
            case 'f':
                _buffer.SetCursorPosition(p1 == 0 ? 1 : p1, p2 == 0 ? 1 : p2);
                break;

            case 'J': // Erase in Display
                switch (p1)
                {
                    case 0:
                        _buffer.EraseToEndOfScreen();
                        break;
                    case 1:
                        _buffer.EraseToStartOfScreen();
                        break;
                    case 2:
                    case 3:
                        _buffer.EraseScreen();
                        break;
                }
                break;

            case 'K': // Erase in Line
                switch (p1)
                {
                    case 0:
                        _buffer.EraseToEndOfLine();
                        break;
                    case 1:
                        _buffer.EraseToStartOfLine();
                        break;
                    case 2:
                        _buffer.EraseLine();
                        break;
                }
                break;

            case 'L': // Insert Lines
                // TODO: 줄 삽입
                break;

            case 'M': // Delete Lines
                // TODO: 줄 삭제
                break;

            case 'P': // Delete Characters
                // TODO: 문자 삭제
                break;

            case 'S': // Scroll Up
                _buffer.ScrollUp(p1 == 0 ? 1 : p1);
                break;

            case 'T': // Scroll Down
                // TODO: 아래로 스크롤
                break;

            case 'X': // Erase Characters
                for (int i = 0; i < (p1 == 0 ? 1 : p1); i++)
                {
                    if (_buffer.CursorX + i < _buffer.Columns)
                    {
                        // 현재 커서 위치부터 n개 문자 지우기
                    }
                }
                break;

            case 'd': // Vertical Position Absolute
                _buffer.CursorY = Math.Max(0, Math.Min((p1 == 0 ? 1 : p1) - 1, _buffer.Rows - 1));
                break;

            case 'm': // SGR (Select Graphic Rendition)
                ProcessSgr();
                break;

            case 'n': // Device Status Report
                // TODO: 응답 필요
                break;

            case 'r': // Set Scrolling Region
                // TODO: 스크롤 영역 설정
                break;

            case 's': // Save Cursor Position
                // TODO: 커서 위치 저장
                break;

            case 'u': // Restore Cursor Position
                // TODO: 커서 위치 복원
                break;

            case 'h': // Set Mode
                ProcessSetMode(true);
                break;

            case 'l': // Reset Mode
                ProcessSetMode(false);
                break;

            case '@': // Insert Characters
                // TODO: 문자 삽입
                break;

            case '`': // Horizontal Position Absolute
                _buffer.CursorX = Math.Max(0, Math.Min((p1 == 0 ? 1 : p1) - 1, _buffer.Columns - 1));
                break;
        }
    }

    private void ProcessSgr()
    {
        if (_params.Count == 0)
        {
            _buffer.ResetStyle();
            return;
        }

        for (int i = 0; i < _params.Count; i++)
        {
            int p = _params[i];

            switch (p)
            {
                case 0: // Reset
                    _buffer.ResetStyle();
                    break;

                case 1: // Bold
                    _buffer.CurrentBold = true;
                    break;

                case 4: // Underline
                    _buffer.CurrentUnderline = true;
                    break;

                case 7: // Inverse
                    _buffer.CurrentInverse = true;
                    break;

                case 22: // Normal intensity
                    _buffer.CurrentBold = false;
                    break;

                case 24: // Underline off
                    _buffer.CurrentUnderline = false;
                    break;

                case 27: // Inverse off
                    _buffer.CurrentInverse = false;
                    break;

                // Foreground colors (30-37)
                case >= 30 and <= 37:
                    _buffer.CurrentForeground = TerminalColors.GetColor(p - 30);
                    break;

                case 38: // Extended foreground color
                    if (i + 2 < _params.Count && _params[i + 1] == 5)
                    {
                        // 256 color: 38;5;n
                        _buffer.CurrentForeground = TerminalColors.GetColor(_params[i + 2]);
                        i += 2;
                    }
                    else if (i + 4 < _params.Count && _params[i + 1] == 2)
                    {
                        // True color: 38;2;r;g;b
                        _buffer.CurrentForeground = TerminalColors.FromRgb(
                            (byte)_params[i + 2],
                            (byte)_params[i + 3],
                            (byte)_params[i + 4]);
                        i += 4;
                    }
                    break;

                case 39: // Default foreground
                    _buffer.CurrentForeground = TerminalColors.DefaultForeground;
                    break;

                // Background colors (40-47)
                case >= 40 and <= 47:
                    _buffer.CurrentBackground = TerminalColors.GetColor(p - 40);
                    break;

                case 48: // Extended background color
                    if (i + 2 < _params.Count && _params[i + 1] == 5)
                    {
                        // 256 color: 48;5;n
                        _buffer.CurrentBackground = TerminalColors.GetColor(_params[i + 2]);
                        i += 2;
                    }
                    else if (i + 4 < _params.Count && _params[i + 1] == 2)
                    {
                        // True color: 48;2;r;g;b
                        _buffer.CurrentBackground = TerminalColors.FromRgb(
                            (byte)_params[i + 2],
                            (byte)_params[i + 3],
                            (byte)_params[i + 4]);
                        i += 4;
                    }
                    break;

                case 49: // Default background
                    _buffer.CurrentBackground = TerminalColors.DefaultBackground;
                    break;

                // Bright foreground colors (90-97)
                case >= 90 and <= 97:
                    _buffer.CurrentForeground = TerminalColors.GetColor(p - 90 + 8);
                    break;

                // Bright background colors (100-107)
                case >= 100 and <= 107:
                    _buffer.CurrentBackground = TerminalColors.GetColor(p - 100 + 8);
                    break;
            }
        }
    }

    private void ProcessSetMode(bool enable)
    {
        foreach (int p in _params)
        {
            switch (p)
            {
                case 25: // Cursor visibility
                    _buffer.CursorVisible = enable;
                    break;

                case 1049: // Alternate screen buffer
                    if (enable)
                        _buffer.EnterAlternateScreen();
                    else
                        _buffer.ExitAlternateScreen();
                    break;

                case 2004: // Bracketed paste mode
                    // TODO: 괄호 붙여넣기 모드
                    break;
            }
        }
    }

    private void ProcessOscChar(char c)
    {
        if (c >= '0' && c <= '9')
        {
            _oscString += c;
            return;
        }

        if (c == ';')
        {
            _state = ParserState.OscString;
            _oscString = "";
            return;
        }

        _state = ParserState.Normal;
    }

    private void ProcessOscStringChar(char c)
    {
        if (c == '\x07' || c == '\x1b') // BEL or ESC
        {
            // OSC 종료 - 타이틀 설정 등 무시
            _state = ParserState.Normal;
            return;
        }

        _oscString += c;
    }

    private void ProcessEscapeIntermediateChar(char c)
    {
        // Character set 지정 등 무시
        _state = ParserState.Normal;
    }
}
