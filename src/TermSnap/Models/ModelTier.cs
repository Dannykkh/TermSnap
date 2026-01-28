using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TermSnap.Models;

/// <summary>
/// AI 모델 티어 (비용/성능 기준)
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum ModelTier
{
    /// <summary>
    /// 빠른 모델 - 단순 작업용 (Haiku, GPT-4o-mini, Gemini Flash)
    /// </summary>
    Fast,

    /// <summary>
    /// 균형 잡힌 모델 - 일반 작업용 (Sonnet, GPT-4o, Gemini Pro)
    /// </summary>
    Balanced,

    /// <summary>
    /// 강력한 모델 - 복잡한 작업용 (Opus, GPT-4-turbo)
    /// </summary>
    Powerful
}

/// <summary>
/// 작업 복잡도
/// </summary>
public enum TaskComplexity
{
    /// <summary>
    /// 단순 작업 - 형식 변환, 단순 질문
    /// </summary>
    Simple,

    /// <summary>
    /// 중간 작업 - 일반 코딩, 버그 수정
    /// </summary>
    Medium,

    /// <summary>
    /// 복잡한 작업 - 아키텍처 설계, 보안 분석
    /// </summary>
    Complex
}
