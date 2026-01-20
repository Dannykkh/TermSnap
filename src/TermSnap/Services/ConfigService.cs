using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// 애플리케이션 설정 관리 서비스
/// </summary>
public class ConfigService
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TermSnap"
    );

    private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");
    private static readonly string BackupFilePath = Path.Combine(ConfigDirectory, "config.backup.json");
    private static readonly string DebugLogPath = Path.Combine(ConfigDirectory, "debug.log");

    private static void LogDebug(string message)
    {
        try
        {
            File.AppendAllText(DebugLogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    /// <summary>
    /// 설정 로드 (자동 마이그레이션 포함)
    /// </summary>
    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
            {
                return CreateDefaultConfig();
            }

            var json = File.ReadAllText(ConfigFilePath);

            // ObjectCreationHandling.Replace: JSON 역직렬화 시 기존 컬렉션에 추가하지 않고 교체
            var settings = new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };
            var config = JsonConvert.DeserializeObject<AppConfig>(json, settings);

            if (config == null)
            {
                return CreateDefaultConfig();
            }

            // 구버전 설정 마이그레이션
            if (config.ConfigVersion < 2)
            {
                config = MigrateFromV1(json);
                Save(config); // 마이그레이션 후 저장
            }

            // AIModels의 Provider 값이 제대로 설정되어 있는지 확인하고,
            // 문제가 있으면 기본 모델 목록으로 재초기화 (API 키는 유지)
            config = EnsureValidAIModels(config);

            return config;
        }
        catch (Exception ex)
        {
            throw new Exception($"설정 파일 로드 실패: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 설정 저장
    /// </summary>
    public static void Save(AppConfig config)
    {
        try
        {
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

            // 기존 파일이 있으면 백업
            if (File.Exists(ConfigFilePath))
            {
                File.Copy(ConfigFilePath, BackupFilePath, true);
            }

            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            throw new Exception($"설정 파일 저장 실패: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 기본 설정 생성
    /// </summary>
    private static AppConfig CreateDefaultConfig()
    {
        var config = new AppConfig
        {
            EncryptedGeminiApiKey = "",
            ServerProfiles = new(),
            CommandSnippets = CommandSnippetCollection.CreateDefault(), // 기본 스니펫 생성
            AutoExecuteCommands = false,
            ShowCommandBeforeExecution = true,
            MaxRetryAttempts = 3,
            Theme = "Light",
            ConfigVersion = 2
        };

        Save(config);
        return config;
    }

    /// <summary>
    /// AIModels가 올바르게 설정되어 있는지 확인하고, 문제가 있으면 재초기화
    /// (JSON 직렬화 시 Provider enum 값이 깨지는 문제 해결)
    /// </summary>
    private static AppConfig EnsureValidAIModels(AppConfig config)
    {
        LogDebug($"[EnsureValidAIModels] Starting - config has {config.AIModels?.Count ?? 0} models");

        // 기본 모델 목록 생성
        var defaultConfig = new AppConfig();
        var defaultModels = defaultConfig.AIModels;

        // 기존 config에서 API 키 추출 (ModelId 기반 + Provider 기반)
        var apiKeysByModelId = new Dictionary<string, string>();
        var apiKeysByProvider = new Dictionary<AIProviderType, string>();

        if (config.AIModels == null)
        {
            LogDebug("[EnsureValidAIModels] WARNING: config.AIModels is NULL!");
            config.AIModels = defaultModels;
            return config;
        }

        foreach (var model in config.AIModels)
        {
            LogDebug($"[EnsureValidAIModels] Checking model: {model.ModelId}, Provider={model.Provider}, ApiKey.Length={model.ApiKey?.Length ?? 0}");

            if (!string.IsNullOrEmpty(model.ApiKey))
            {
                LogDebug($"[EnsureValidAIModels] Found API key for {model.Provider}/{model.ModelId}");

                // ModelId로 매핑 (가장 정확)
                if (!string.IsNullOrEmpty(model.ModelId))
                {
                    apiKeysByModelId[model.ModelId] = model.ApiKey;
                }
                // Provider로도 매핑 (fallback)
                if (model.Provider != AIProviderType.None && !apiKeysByProvider.ContainsKey(model.Provider))
                {
                    apiKeysByProvider[model.Provider] = model.ApiKey;
                }
            }
        }

        LogDebug($"[EnsureValidAIModels] Extracted {apiKeysByModelId.Count} keys by ModelId, {apiKeysByProvider.Count} keys by Provider");

        // 기본 모델 목록에 기존 API 키 적용
        foreach (var model in defaultModels)
        {
            // 1. ModelId로 먼저 찾기
            if (apiKeysByModelId.TryGetValue(model.ModelId, out var apiKeyById))
            {
                model.ApiKey = apiKeyById;
                LogDebug($"[EnsureValidAIModels] Applied API key to {model.ModelId} by ModelId");
            }
            // 2. Provider로 찾기 (fallback)
            else if (apiKeysByProvider.TryGetValue(model.Provider, out var apiKeyByProvider))
            {
                model.ApiKey = apiKeyByProvider;
                LogDebug($"[EnsureValidAIModels] Applied API key to {model.ModelId} by Provider");
            }
        }

        // 새로운 모델 목록으로 교체
        config.AIModels = defaultModels;

        // 최종 결과 로그
        var configuredCount = config.AIModels.Count(m => !string.IsNullOrEmpty(m.ApiKey));
        LogDebug($"[EnsureValidAIModels] Done - {configuredCount} models have API keys");

        return config;
    }

    /// <summary>
    /// V1 설정을 V2로 마이그레이션
    /// </summary>
    private static AppConfig MigrateFromV1(string json)
    {
        try
        {
            var oldConfig = JObject.Parse(json);

            var newConfig = new AppConfig
            {
                ConfigVersion = 2,
                AutoExecuteCommands = oldConfig["AutoExecuteCommands"]?.Value<bool>() ?? false,
                ShowCommandBeforeExecution = oldConfig["ShowCommandBeforeExecution"]?.Value<bool>() ?? true,
                MaxRetryAttempts = oldConfig["MaxRetryAttempts"]?.Value<int>() ?? 3,
                Theme = oldConfig["Theme"]?.Value<string>() ?? "Light"
            };

            // Gemini API 키 마이그레이션 (암호화)
            var apiKey = oldConfig["GeminiApiKey"]?.Value<string>();
            if (!string.IsNullOrEmpty(apiKey))
            {
                newConfig.EncryptedGeminiApiKey = EncryptionService.Encrypt(apiKey);
            }

            // SSH 설정 마이그레이션
            var sshConfig = oldConfig["SshConfig"];
            if (sshConfig != null)
            {
                var profile = new ServerConfig
                {
                    ProfileName = "마이그레이션된 서버",
                    Host = sshConfig["Host"]?.Value<string>() ?? "",
                    Port = sshConfig["Port"]?.Value<int>() ?? 22,
                    Username = sshConfig["Username"]?.Value<string>() ?? "",
                    PrivateKeyPath = sshConfig["PrivateKeyPath"]?.Value<string>() ?? "",
                    AuthType = (AuthenticationType)(sshConfig["AuthType"]?.Value<int>() ?? 0)
                };

                // 비밀번호 마이그레이션 (암호화)
                var password = sshConfig["Password"]?.Value<string>();
                if (!string.IsNullOrEmpty(password))
                {
                    profile.EncryptedPassword = EncryptionService.Encrypt(password);
                }

                if (!string.IsNullOrEmpty(profile.Host))
                {
                    newConfig.ServerProfiles.Add(profile);
                    newConfig.LastUsedProfile = profile.ProfileName;
                }
            }

            return newConfig;
        }
        catch (Exception ex)
        {
            throw new Exception($"설정 마이그레이션 실패: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gemini API 키 저장 (암호화)
    /// </summary>
    public static void SaveGeminiApiKey(AppConfig config, string apiKey)
    {
        config.EncryptedGeminiApiKey = EncryptionService.Encrypt(apiKey);
        Save(config);
    }

    /// <summary>
    /// Gemini API 키 가져오기 (복호화)
    /// </summary>
    public static string GetGeminiApiKey(AppConfig config)
    {
        if (string.IsNullOrEmpty(config.EncryptedGeminiApiKey))
            return string.Empty;

        try
        {
            return EncryptionService.Decrypt(config.EncryptedGeminiApiKey);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 설정 파일 경로 가져오기
    /// </summary>
    public static string GetConfigFilePath() => ConfigFilePath;

    /// <summary>
    /// 설정 파일 존재 여부 확인
    /// </summary>
    public static bool ConfigExists() => File.Exists(ConfigFilePath);

    /// <summary>
    /// 백업에서 복원
    /// </summary>
    public static bool RestoreFromBackup()
    {
        try
        {
            if (File.Exists(BackupFilePath))
            {
                File.Copy(BackupFilePath, ConfigFilePath, true);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
