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
    Claude
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
    /// API 키가 설정되어 있는지 확인
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
