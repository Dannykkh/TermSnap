using System;
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
}
