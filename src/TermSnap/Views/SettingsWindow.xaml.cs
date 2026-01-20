using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TermSnap.Core;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.Views;

/// <summary>
/// 설정 윈도우 - 서버 프로필, AI 제공자/모델 선택 및 API 키 관리
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private AIProviderType _selectedProvider;
    private readonly Dictionary<AIProviderType, string> _apiKeys = new();
    private readonly ObservableCollection<ServerConfig> _profiles = new();
    private bool _isLoading = false; // 초기 로드 중 플래그

    // 각 제공자별 API 키 발급 URL
    private readonly Dictionary<AIProviderType, string> _apiKeyUrls = new()
    {
        { AIProviderType.Gemini, "https://aistudio.google.com/app/apikey" },
        { AIProviderType.OpenAI, "https://platform.openai.com/api-keys" },
        { AIProviderType.Claude, "https://console.anthropic.com/settings/keys" },
        { AIProviderType.Grok, "https://console.x.ai/" }
    };

    private static readonly string DebugLogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TermSnap", "debug.log"
    );

    private static void LogDebug(string message)
    {
        try
        {
            System.IO.File.AppendAllText(DebugLogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config ?? throw new ArgumentNullException(nameof(config));

        LoadSettings();
    }

    private void LoadSettings()
    {
        _isLoading = true; // 로드 중 플래그 설정

        // 테마 설정 로드
        var isDarkMode = _config.Theme?.ToLower() == "dark";
        if (isDarkMode)
        {
            DarkThemeRadio.IsChecked = true;
        }
        else
        {
            LightThemeRadio.IsChecked = true;
        }

        // 서버 프로필 로드
        _profiles.Clear();
        foreach (var profile in _config.ServerProfiles)
        {
            _profiles.Add(profile);
        }
        ProfileListBox.ItemsSource = _profiles;
        UpdateProfileListVisibility();

        // 각 제공자별 API 키 로드 (캐시)
        LogDebug($"[LoadSettings] Loading API keys from _config with {_config.AIModels.Count} models");

        // Gemini 모델 상세 로그
        foreach (var m in _config.AIModels.Where(x => x.Provider == AIProviderType.Gemini).Take(3))
        {
            LogDebug($"[LoadSettings] Gemini model: {m.ModelId}, ApiKey.Length={m.ApiKey?.Length ?? 0}");
        }

        _apiKeys[AIProviderType.Gemini] = GetApiKeyForProvider(AIProviderType.Gemini);
        _apiKeys[AIProviderType.OpenAI] = GetApiKeyForProvider(AIProviderType.OpenAI);
        _apiKeys[AIProviderType.Claude] = GetApiKeyForProvider(AIProviderType.Claude);
        _apiKeys[AIProviderType.Grok] = GetApiKeyForProvider(AIProviderType.Grok);

        LogDebug($"[LoadSettings] Gemini API key result: {(_apiKeys[AIProviderType.Gemini].Length > 0 ? $"length={_apiKeys[AIProviderType.Gemini].Length}" : "(empty)")}");

        // 선택된 제공자 로드
        _selectedProvider = _config.SelectedProvider;

        // 제공자 라디오 버튼 선택
        switch (_selectedProvider)
        {
            case AIProviderType.None:
                NoneRadio.IsChecked = true;
                break;
            case AIProviderType.Gemini:
                GeminiRadio.IsChecked = true;
                break;
            case AIProviderType.OpenAI:
                OpenAIRadio.IsChecked = true;
                break;
            case AIProviderType.Claude:
                ClaudeRadio.IsChecked = true;
                break;
            case AIProviderType.Grok:
                GrokRadio.IsChecked = true;
                break;
        }

        UpdateProviderUI();

        // 임베딩 설정 로드
        LoadEmbeddingSettings();

        _isLoading = false; // 로드 완료
    }

    private void LoadEmbeddingSettings()
    {
        var embeddingType = _config.Embedding.Type;

        // 임베딩 사용 여부 체크박스
        EmbeddingEnabledCheckBox.IsChecked = embeddingType != EmbeddingType.Disabled;

        // 로컬/API 라디오 버튼
        if (embeddingType == EmbeddingType.API)
        {
            EmbeddingApiRadio.IsChecked = true;
        }
        else
        {
            EmbeddingLocalRadio.IsChecked = true; // 기본값은 로컬
        }

        UpdateEmbeddingUI();
    }

    /// <summary>
    /// 임베딩 UI 전체 업데이트 (체크박스 및 라디오 상태에 따라)
    /// </summary>
    private void UpdateEmbeddingUI()
    {
        var isEnabled = EmbeddingEnabledCheckBox.IsChecked == true;

        // 임베딩 타입 패널 표시 여부
        EmbeddingTypePanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

        // 로컬 모델 패널은 임베딩 활성화 + 로컬 선택 시에만 표시
        LocalModelPanel.Visibility = isEnabled && EmbeddingLocalRadio.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (isEnabled && EmbeddingLocalRadio.IsChecked == true)
        {
            UpdateLocalModelStatus();
        }
    }

    /// <summary>
    /// 로컬 모델 상태 업데이트
    /// </summary>
    private void UpdateLocalModelStatus()
    {
        var isModelAvailable = LocalEmbeddingService.IsModelAvailable();

        if (isModelAvailable)
        {
            LocalModelStatusText.Text = "설치됨 - 사용 준비 완료";
            LocalModelStatusText.Foreground = FindResource("SuccessBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Green;
            DownloadModelButton.Content = "모델 삭제";
            DownloadModelButton.Tag = "delete";
        }
        else
        {
            LocalModelStatusText.Text = "설치되지 않음 - 다운로드 필요 (~470MB)";
            LocalModelStatusText.Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Gray;
            DownloadModelButton.Content = "모델 다운로드";
            DownloadModelButton.Tag = "download";
        }
    }

    private void UpdateProfileListVisibility()
    {
        if (_profiles.Count == 0)
        {
            NoProfileText.Visibility = Visibility.Visible;
            ProfileListBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            NoProfileText.Visibility = Visibility.Collapsed;
            ProfileListBox.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 특정 제공자의 API 키를 가져옴
    /// </summary>
    private string GetApiKeyForProvider(AIProviderType provider)
    {
        var model = _config.AIModels
            .FirstOrDefault(m => m.Provider == provider && !string.IsNullOrWhiteSpace(m.ApiKey));
        return model?.ApiKey ?? string.Empty;
    }

    #region 서버 프로필 관리

    private void ProfileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 선택 변경 시 필요한 처리
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileEditorDialog();
        dialog.Owner = this;
        
        if (dialog.ShowDialog() == true && dialog.ResultProfile != null)
        {
            _profiles.Add(dialog.ResultProfile);
            UpdateProfileListVisibility();
        }
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is ServerConfig profile)
        {
            var index = _profiles.IndexOf(profile);
            if (index < 0) return;

            var dialog = new ProfileEditorDialog(profile);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.ResultProfile != null)
            {
                _profiles[index] = dialog.ResultProfile;
            }
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is ServerConfig profile)
        {
            var result = MessageBox.Show(
                $"'{profile.ProfileName}' 프로필을 삭제하시겠습니까?",
                "프로필 삭제",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _profiles.Remove(profile);
                UpdateProfileListVisibility();
            }
        }
    }

    #endregion

    #region AI 제공자/모델 관리

    /// <summary>
    /// 제공자 변경 시 UI 업데이트
    /// </summary>
    private void Provider_Changed(object sender, RoutedEventArgs e)
    {
        // 초기 로드 중에는 무시 (API 키가 덮어써지는 것 방지)
        if (_isLoading) return;

        if (sender is RadioButton radio && radio.Tag is string providerStr)
        {
            // 현재 API 키 저장 (None이 아닌 경우만)
            if (_selectedProvider != AIProviderType.None && _apiKeys.ContainsKey(_selectedProvider))
            {
                _apiKeys[_selectedProvider] = ApiKeyBox.Password;
            }

            // 새 제공자 선택
            _selectedProvider = Enum.Parse<AIProviderType>(providerStr);
            UpdateProviderUI();
        }
    }

    /// <summary>
    /// 선택된 제공자에 맞게 UI 업데이트
    /// </summary>
    private void UpdateProviderUI()
    {
        // None일 경우 API 키/모델 영역 비활성화
        if (_selectedProvider == AIProviderType.None)
        {
            ApiKeySection.Visibility = Visibility.Collapsed;
            return;
        }

        ApiKeySection.Visibility = Visibility.Visible;

        // API 키 라벨 업데이트
        var providerName = _selectedProvider switch
        {
            AIProviderType.Gemini => "Google Gemini",
            AIProviderType.OpenAI => "OpenAI",
            AIProviderType.Claude => "Anthropic Claude",
            AIProviderType.Grok => "xAI Grok",
            _ => "API"
        };
        ApiKeyLabel.Text = $"{providerName} API 키";

        // 저장된 API 키 로드
        ApiKeyBox.Password = _apiKeys.TryGetValue(_selectedProvider, out var key) ? key : string.Empty;

        // 모델 목록 로드
        var models = _config.AIModels.Where(m => m.Provider == _selectedProvider).ToList();
        ModelComboBox.ItemsSource = models;

        // 선택된 모델 설정
        var selectedModel = models.FirstOrDefault(m => m.ModelId == _config.SelectedModelId)
                           ?? models.FirstOrDefault();
        ModelComboBox.SelectedItem = selectedModel;
    }

    #endregion

    #region 테마 관리

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        // 테마 변경 시 즉시 적용
        var isDark = DarkThemeRadio.IsChecked == true;
        ThemeService.Instance.IsDarkMode = isDark;
    }

    #endregion

    private void GetApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (_apiKeyUrls.TryGetValue(_selectedProvider, out var url))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 현재 API 키 저장 (None이 아닌 경우만)
            if (_selectedProvider != AIProviderType.None)
            {
                var currentApiKey = ApiKeyBox.Password;
                _apiKeys[_selectedProvider] = currentApiKey;

                // 디버깅: API 키 저장 확인
                System.Diagnostics.Debug.WriteLine($"[Settings] Saving API key for {_selectedProvider}: {(string.IsNullOrEmpty(currentApiKey) ? "(empty)" : $"length={currentApiKey.Length}")}");
            }

            // 선택된 제공자의 모든 모델에 API 키 적용
            foreach (var provider in _apiKeys.Keys)
            {
                var apiKey = _apiKeys[provider];
                System.Diagnostics.Debug.WriteLine($"[Settings] Applying API key for {provider}: {(string.IsNullOrEmpty(apiKey) ? "(empty)" : $"length={apiKey.Length}")}");
                SetApiKeyForProvider(provider, apiKey);
            }

            // 서버 프로필 저장
            LogDebug($"[Save_Click] Saving {_profiles.Count} profiles");
            _config.ServerProfiles.Clear();
            foreach (var profile in _profiles)
            {
                LogDebug($"[Save_Click] Adding profile: {profile.ProfileName}");
                _config.ServerProfiles.Add(profile);
            }
            LogDebug($"[Save_Click] _config.ServerProfiles now has {_config.ServerProfiles.Count} profiles");

            // 선택된 제공자 및 모델 저장
            _config.SelectedProvider = _selectedProvider;
            
            if (ModelComboBox.SelectedItem is AIModelConfig selectedModel)
            {
                _config.SelectedModelId = selectedModel.ModelId;
            }

            // 테마 설정 저장
            _config.Theme = DarkThemeRadio.IsChecked == true ? "Dark" : "Light";

            // 임베딩 설정 저장
            if (EmbeddingEnabledCheckBox.IsChecked == true)
            {
                // 임베딩 사용 시: 로컬 또는 API
                _config.Embedding.Type = EmbeddingLocalRadio.IsChecked == true
                    ? EmbeddingType.Local
                    : EmbeddingType.API;
            }
            else
            {
                // 임베딩 비활성화
                _config.Embedding.Type = EmbeddingType.Disabled;
            }

            // 설정 파일 저장
            ConfigService.Save(_config);

            // 디버깅: 저장된 API 키 확인
            var savedGeminiKey = _config.AIModels.FirstOrDefault(m => m.Provider == AIProviderType.Gemini)?.ApiKey;
            System.Diagnostics.Debug.WriteLine($"[Settings] After save - Gemini API key: {(string.IsNullOrEmpty(savedGeminiKey) ? "(empty)" : $"length={savedGeminiKey.Length}")}");

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"설정 저장 중 오류가 발생했습니다:\n\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 특정 제공자의 모든 모델에 API 키 설정
    /// </summary>
    private void SetApiKeyForProvider(AIProviderType provider, string apiKey)
    {
        foreach (var model in _config.AIModels.Where(m => m.Provider == provider))
        {
            model.ApiKey = apiKey;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    #region 임베딩 설정

    /// <summary>
    /// 임베딩 사용 체크박스 변경
    /// </summary>
    private void EmbeddingEnabled_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEmbeddingUI();
    }

    /// <summary>
    /// 임베딩 타입 (로컬/API) 변경
    /// </summary>
    private void EmbeddingType_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEmbeddingUI();
    }

    private async void DownloadModel_Click(object sender, RoutedEventArgs e)
    {
        var action = DownloadModelButton.Tag as string;

        if (action == "delete")
        {
            // 모델 삭제
            var result = MessageBox.Show(
                "로컬 임베딩 모델을 삭제하시겠습니까?\n\n삭제 후 다시 사용하려면 재다운로드가 필요합니다.",
                "모델 삭제",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    LocalEmbeddingService.DeleteModel();
                    _config.Embedding.LocalModelDownloaded = false;
                    UpdateLocalModelStatus();
                    MessageBox.Show("모델이 삭제되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"모델 삭제 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        else
        {
            // 모델 다운로드
            DownloadModelButton.IsEnabled = false;
            DownloadProgressPanel.Visibility = Visibility.Visible;
            DownloadProgressBar.Value = 0;
            DownloadStatusText.Text = "다운로드 준비 중...";

            var progress = new Progress<(string status, int percent)>(p =>
            {
                DownloadStatusText.Text = p.status;
                DownloadProgressBar.Value = p.percent;
            });

            try
            {
                await AIProviderManager.Instance.DownloadLocalEmbeddingModelAsync(progress);

                _config.Embedding.LocalModelDownloaded = true;
                _config.Embedding.Type = EmbeddingType.Local;

                MessageBox.Show(
                    "모델 다운로드가 완료되었습니다!\n\n이제 로컬 임베딩을 사용할 수 있습니다.",
                    "완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                EmbeddingLocalRadio.IsChecked = true;
                UpdateLocalModelStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"모델 다운로드 실패:\n\n{ex.Message}\n\n수동 다운로드가 필요할 수 있습니다.",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                DownloadModelButton.IsEnabled = true;
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
            }
        }
    }

    #endregion
}
