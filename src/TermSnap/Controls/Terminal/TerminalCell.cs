using System;
using System.Windows.Media;

namespace TermSnap.Controls.Terminal;

/// <summary>
/// 터미널 셀 - 문자, 전경색, 배경색 정보
/// </summary>
public struct TerminalCell
{
    public char Character;
    public Color Foreground;
    public Color Background;
    public bool Bold;
    public bool Underline;
    public bool Inverse;
    public bool IsWideChar;      // 2칸 차지하는 문자 (한글, CJK 등)
    public bool IsWideCharTail;  // Wide char의 두 번째 칸 (렌더링 시 건너뜀)

    public static TerminalCell Empty => new()
    {
        Character = ' ',
        Foreground = Colors.White,
        Background = Colors.Transparent,
        Bold = false,
        Underline = false,
        Inverse = false,
        IsWideChar = false,
        IsWideCharTail = false
    };
}

/// <summary>
/// 문자 너비 판단 유틸리티
/// </summary>
public static class CharWidthHelper
{
    /// <summary>
    /// 문자가 2칸 너비인지 확인 (한글, CJK, 이모지 등)
    /// </summary>
    public static bool IsWideChar(char c)
    {
        // Unicode 범위로 판단
        int code = c;

        // 한글 (Hangul)
        if (code >= 0xAC00 && code <= 0xD7AF) return true;  // 한글 음절
        if (code >= 0x1100 && code <= 0x11FF) return true;  // 한글 자모
        if (code >= 0x3130 && code <= 0x318F) return true;  // 한글 호환 자모
        if (code >= 0xA960 && code <= 0xA97F) return true;  // 한글 자모 확장-A
        if (code >= 0xD7B0 && code <= 0xD7FF) return true;  // 한글 자모 확장-B

        // CJK (Chinese, Japanese, Korean)
        if (code >= 0x4E00 && code <= 0x9FFF) return true;   // CJK 통합 한자
        if (code >= 0x3400 && code <= 0x4DBF) return true;   // CJK 통합 한자 확장 A
        if (code >= 0x20000 && code <= 0x2A6DF) return true; // CJK 통합 한자 확장 B
        if (code >= 0x2A700 && code <= 0x2B73F) return true; // CJK 통합 한자 확장 C
        if (code >= 0x2B740 && code <= 0x2B81F) return true; // CJK 통합 한자 확장 D
        if (code >= 0xF900 && code <= 0xFAFF) return true;   // CJK 호환 한자

        // 일본어
        if (code >= 0x3040 && code <= 0x309F) return true;  // 히라가나
        if (code >= 0x30A0 && code <= 0x30FF) return true;  // 가타카나
        if (code >= 0x31F0 && code <= 0x31FF) return true;  // 가타카나 확장

        // 전각 문자
        if (code >= 0xFF00 && code <= 0xFFEF) return true;  // 전각 및 반각 형태

        // 기타 넓은 문자
        if (code >= 0x2E80 && code <= 0x2EFF) return true;  // CJK 부수 보충
        if (code >= 0x3000 && code <= 0x303F) return true;  // CJK 기호 및 구두점

        return false;
    }

    /// <summary>
    /// Surrogate pair 처리 (이모지 등)
    /// </summary>
    public static bool IsWideChar(string text, int index)
    {
        if (index >= text.Length) return false;

        char c = text[index];

        // 기본 판단
        if (IsWideChar(c)) return true;

        // Surrogate pair (이모지 등)
        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            return true;  // 대부분의 이모지는 wide
        }

        return false;
    }
}

/// <summary>
/// 터미널 색상 팔레트 (256색 + True Color 지원)
/// </summary>
public static class TerminalColors
{
    // 기본 16색 팔레트
    private static readonly Color[] BasicColors =
    {
        Color.FromRgb(0, 0, 0),       // 0: Black
        Color.FromRgb(187, 0, 0),     // 1: Red
        Color.FromRgb(0, 187, 0),     // 2: Green
        Color.FromRgb(187, 187, 0),   // 3: Yellow
        Color.FromRgb(0, 0, 187),     // 4: Blue
        Color.FromRgb(187, 0, 187),   // 5: Magenta
        Color.FromRgb(0, 187, 187),   // 6: Cyan
        Color.FromRgb(187, 187, 187), // 7: White
        Color.FromRgb(85, 85, 85),    // 8: Bright Black
        Color.FromRgb(255, 85, 85),   // 9: Bright Red
        Color.FromRgb(85, 255, 85),   // 10: Bright Green
        Color.FromRgb(255, 255, 85),  // 11: Bright Yellow
        Color.FromRgb(85, 85, 255),   // 12: Bright Blue
        Color.FromRgb(255, 85, 255),  // 13: Bright Magenta
        Color.FromRgb(85, 255, 255),  // 14: Bright Cyan
        Color.FromRgb(255, 255, 255)  // 15: Bright White
    };

    // 256색 팔레트 캐시
    private static Color[]? _palette256;

    public static Color GetColor(int index)
    {
        if (index < 0) return Colors.White;
        if (index < 16) return BasicColors[index];
        if (index < 256)
        {
            _palette256 ??= Generate256Palette();
            return _palette256[index];
        }
        return Colors.White;
    }

    public static Color FromRgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static Color[] Generate256Palette()
    {
        var palette = new Color[256];

        // 0-15: 기본 색상
        for (int i = 0; i < 16; i++)
            palette[i] = BasicColors[i];

        // 16-231: 6x6x6 컬러 큐브
        for (int i = 16; i < 232; i++)
        {
            int idx = i - 16;
            int r = idx / 36;
            int g = (idx % 36) / 6;
            int b = idx % 6;
            palette[i] = Color.FromRgb(
                (byte)(r > 0 ? 55 + r * 40 : 0),
                (byte)(g > 0 ? 55 + g * 40 : 0),
                (byte)(b > 0 ? 55 + b * 40 : 0));
        }

        // 232-255: 그레이스케일
        for (int i = 232; i < 256; i++)
        {
            byte gray = (byte)(8 + (i - 232) * 10);
            palette[i] = Color.FromRgb(gray, gray, gray);
        }

        return palette;
    }

    public static Color DefaultForeground => Colors.LightGray;
    public static Color DefaultBackground => Color.FromRgb(30, 30, 30);
}
