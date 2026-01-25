using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TermSnap.Models;

/// <summary>
/// AI 제공자 타입
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum AIProviderType
{
    None,    // AI 사용 안 함
    Gemini,
    OpenAI,
    Grok,
    Claude,
    Ollama   // 로컬 모델 (Ollama)
}

/// <summary>
/// AI 모델 설정 정보
/// </summary>
public class AIModelConfig
{
    [JsonConverter(typeof(StringEnumConverter))]
    public AIProviderType Provider { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string ModelDisplayName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Ollama 등 로컬 모델용 Base URL (기본값: http://localhost:11434)
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API 키가 설정되어 있는지 확인 (Ollama는 BaseUrl만 있으면 됨)
    /// </summary>
    public bool IsConfigured => Provider == AIProviderType.Ollama
        ? !string.IsNullOrWhiteSpace(BaseUrl) || true  // Ollama는 기본 URL 사용 가능
        : !string.IsNullOrWhiteSpace(ApiKey);
}
