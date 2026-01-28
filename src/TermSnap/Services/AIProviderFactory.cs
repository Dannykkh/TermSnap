using System;
using System.Linq;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// AI 제공자 팩토리 - Factory Pattern
/// </summary>
public class AIProviderFactory
{
    /// <summary>
    /// AI 제공자 인스턴스 생성
    /// </summary>
    /// <param name="provider">제공자 타입</param>
    /// <param name="apiKey">API 키</param>
    /// <param name="model">모델 ID (선택사항)</param>
    /// <param name="baseUrl">Base URL (Ollama용, 선택사항)</param>
    /// <returns>IAIProvider 인스턴스</returns>
    public static IAIProvider CreateProvider(AIProviderType provider, string apiKey, string? model = null, string? baseUrl = null)
    {
        return provider switch
        {
            AIProviderType.Gemini => new GeminiService(apiKey, model ?? "gemini-3-flash"),
            AIProviderType.OpenAI => new OpenAIProvider(apiKey, model ?? "gpt-5.2"),
            AIProviderType.Grok => new GrokProvider(apiKey, model ?? "grok-3"),
            AIProviderType.Claude => new ClaudeProvider(apiKey, model ?? "claude-opus-4-5-20251101"),
            AIProviderType.Ollama => new OllamaProvider(model ?? "qwen3:30b-a3b", baseUrl),
            _ => throw new ArgumentException($"알 수 없는 AI 제공자: {provider}", nameof(provider))
        };
    }

    /// <summary>
    /// 모델 설정으로부터 AI 제공자 생성
    /// </summary>
    public static IAIProvider CreateProviderFromConfig(AIModelConfig modelConfig)
    {
        if (!modelConfig.IsConfigured)
        {
            var errorMsg = modelConfig.Provider == AIProviderType.Ollama
                ? $"Ollama 모델 '{modelConfig.ModelDisplayName}'이 설정되지 않았습니다."
                : $"모델 '{modelConfig.ModelDisplayName}'의 API 키가 설정되지 않았습니다.";
            throw new InvalidOperationException(errorMsg);
        }

        return CreateProvider(modelConfig.Provider, modelConfig.ApiKey, modelConfig.ModelId, modelConfig.BaseUrl);
    }

    /// <summary>
    /// 티어 기반 AI 제공자 생성 (스마트 라우팅용)
    /// </summary>
    /// <param name="tier">모델 티어</param>
    /// <returns>IAIProvider 인스턴스 또는 null</returns>
    public static IAIProvider? GetProviderByTier(ModelTier tier)
    {
        AppConfig? config;
        try
        {
            config = ConfigService.Load();
        }
        catch
        {
            return null;
        }

        if (config?.AIModels == null || config.AIModels.Count == 0)
            return null;

        // 해당 티어의 설정된 모델 찾기
        var modelConfig = config.AIModels
            .FirstOrDefault(m => m.IsConfigured && m.Tier == tier);

        // 해당 티어가 없으면 인접 티어에서 찾기
        if (modelConfig == null)
        {
            modelConfig = tier switch
            {
                ModelTier.Fast => config.AIModels.FirstOrDefault(m => m.IsConfigured && m.Tier == ModelTier.Balanced)
                                  ?? config.AIModels.FirstOrDefault(m => m.IsConfigured && m.Tier == ModelTier.Powerful),
                ModelTier.Powerful => config.AIModels.FirstOrDefault(m => m.IsConfigured && m.Tier == ModelTier.Balanced)
                                      ?? config.AIModels.FirstOrDefault(m => m.IsConfigured && m.Tier == ModelTier.Fast),
                _ => config.AIModels.FirstOrDefault(m => m.IsConfigured)
            };
        }

        // 그래도 없으면 아무 설정된 모델
        modelConfig ??= config.AIModels.FirstOrDefault(m => m.IsConfigured);

        if (modelConfig == null)
            return null;

        return CreateProviderFromConfig(modelConfig);
    }

    /// <summary>
    /// 복잡도 기반 AI 제공자 생성
    /// </summary>
    public static IAIProvider? GetProviderByComplexity(TaskComplexity complexity)
    {
        var tier = complexity switch
        {
            TaskComplexity.Simple => ModelTier.Fast,
            TaskComplexity.Medium => ModelTier.Balanced,
            TaskComplexity.Complex => ModelTier.Powerful,
            _ => ModelTier.Balanced
        };

        return GetProviderByTier(tier);
    }
}
