using System;
using System.Globalization;
using System.Windows;

namespace TermSnap.Services;

/// <summary>
/// 다국어 지원 서비스
/// </summary>
public class LocalizationService
{
    private static readonly Lazy<LocalizationService> _instance =
        new Lazy<LocalizationService>(() => new LocalizationService(), isThreadSafe: true);
    public static LocalizationService Instance => _instance.Value;

    private string _currentLanguage = "en-US";

    /// <summary>
    /// 현재 언어 (ko-KR, en-US)
    /// </summary>
    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                ApplyLanguage();
                LanguageChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// 언어 변경 이벤트
    /// </summary>
    public event EventHandler<string>? LanguageChanged;

    private LocalizationService()
    {
        // 설정에서 언어 로드
        try
        {
            var config = ConfigService.Load();
            _currentLanguage = config.Language ?? "en-US";
        }
        catch
        {
            _currentLanguage = "en-US"; // 기본값: 영어
        }
    }

    /// <summary>
    /// 언어 적용
    /// </summary>
    public void ApplyLanguage()
    {
        var app = Application.Current;
        if (app == null) return;

        // 기존 언어 리소스 제거
        ResourceDictionary? langToRemove = null;
        foreach (var dict in app.Resources.MergedDictionaries)
        {
            if (dict.Source?.ToString().Contains("Strings.") == true)
            {
                langToRemove = dict;
                break;
            }
        }
        if (langToRemove != null)
        {
            app.Resources.MergedDictionaries.Remove(langToRemove);
        }

        // 새 언어 리소스 추가
        var langPath = _currentLanguage switch
        {
            "ko-KR" => "pack://application:,,,/TermSnap;component/Resources/Strings.ko-KR.xaml",
            _ => "pack://application:,,,/TermSnap;component/Resources/Strings.en-US.xaml"
        };

        try
        {
            var langResource = new ResourceDictionary { Source = new Uri(langPath) };
            app.Resources.MergedDictionaries.Add(langResource);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"언어 리소스 로드 실패: {ex.Message}");
        }

        // CultureInfo 설정
        try
        {
            var culture = new CultureInfo(_currentLanguage);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch { }
    }

    /// <summary>
    /// 언어 전환
    /// </summary>
    public void ToggleLanguage()
    {
        CurrentLanguage = _currentLanguage == "en-US" ? "ko-KR" : "en-US";
        SaveLanguagePreference();
    }

    /// <summary>
    /// 언어 설정 저장
    /// </summary>
    public void SaveLanguagePreference()
    {
        try
        {
            var config = ConfigService.Load();
            config.Language = _currentLanguage;
            ConfigService.Save(config);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"언어 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 앱 시작 시 언어 초기화
    /// </summary>
    public void Initialize()
    {
        ApplyLanguage();
    }

    /// <summary>
    /// 사용 가능한 언어 목록
    /// </summary>
    public static string[] AvailableLanguages => new[] { "en-US", "ko-KR" };

    /// <summary>
    /// 언어 표시 이름 가져오기
    /// </summary>
    public static string GetLanguageDisplayName(string languageCode)
    {
        return languageCode switch
        {
            "ko-KR" => "한국어",
            "en-US" => "English",
            _ => languageCode
        };
    }

    /// <summary>
    /// 리소스 문자열 가져오기
    /// </summary>
    public string GetString(string key)
    {
        try
        {
            var app = Application.Current;
            if (app == null) return key;

            var resource = app.TryFindResource(key);
            if (resource is string str)
            {
                return str;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"리소스 로드 실패 ({key}): {ex.Message}");
        }

        return key; // 키를 찾지 못하면 키 자체를 반환
    }
}
