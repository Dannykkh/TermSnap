using System;
using System.Windows;
using MaterialDesignThemes.Wpf;

namespace Nebula.Services;

/// <summary>
/// 테마 관리 서비스 - Light/Dark 모드 전환
/// </summary>
public class ThemeService
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();

    private bool _isDarkMode = false;
    
    /// <summary>
    /// 현재 다크 모드 여부
    /// </summary>
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode != value)
            {
                _isDarkMode = value;
                ApplyTheme();
                ThemeChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// 테마 변경 이벤트
    /// </summary>
    public event EventHandler<bool>? ThemeChanged;

    private ThemeService()
    {
        // 설정에서 테마 로드
        try
        {
            var config = ConfigService.Load();
            _isDarkMode = config.Theme?.ToLower() == "dark";
        }
        catch
        {
            _isDarkMode = false;
        }
    }

    /// <summary>
    /// 테마 적용
    /// </summary>
    public void ApplyTheme()
    {
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();

        if (_isDarkMode)
        {
            theme.SetBaseTheme(BaseTheme.Dark);
        }
        else
        {
            theme.SetBaseTheme(BaseTheme.Light);
        }

        paletteHelper.SetTheme(theme);

        // 커스텀 테마 리소스 적용
        ApplyCustomThemeResources();
    }

    /// <summary>
    /// 커스텀 테마 리소스 적용
    /// </summary>
    private void ApplyCustomThemeResources()
    {
        var app = Application.Current;
        if (app == null) return;

        // 기존 테마 리소스 제거
        ResourceDictionary? themeToRemove = null;
        foreach (var dict in app.Resources.MergedDictionaries)
        {
            if (dict.Source?.ToString().Contains("LightTheme.xaml") == true ||
                dict.Source?.ToString().Contains("DarkTheme.xaml") == true)
            {
                themeToRemove = dict;
                break;
            }
        }
        if (themeToRemove != null)
        {
            app.Resources.MergedDictionaries.Remove(themeToRemove);
        }

        // 새 테마 리소스 추가
        var themePath = _isDarkMode
            ? "pack://application:,,,/Nebula;component/Themes/DarkTheme.xaml"
            : "pack://application:,,,/Nebula;component/Themes/LightTheme.xaml";

        try
        {
            var themeResource = new ResourceDictionary { Source = new Uri(themePath) };
            app.Resources.MergedDictionaries.Add(themeResource);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"테마 리소스 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 테마 토글
    /// </summary>
    public void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        SaveThemePreference();
    }

    /// <summary>
    /// 테마 설정 저장
    /// </summary>
    public void SaveThemePreference()
    {
        try
        {
            var config = ConfigService.Load();
            config.Theme = _isDarkMode ? "Dark" : "Light";
            ConfigService.Save(config);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"테마 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 앱 시작 시 테마 초기화
    /// </summary>
    public void Initialize()
    {
        ApplyTheme();
    }
}
