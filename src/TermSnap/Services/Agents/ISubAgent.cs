using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services.Agents;

/// <summary>
/// 서브에이전트 인터페이스 - 전문 분야별 에이전트
/// </summary>
public interface ISubAgent
{
    /// <summary>
    /// 에이전트 이름
    /// </summary>
    string AgentName { get; }

    /// <summary>
    /// 에이전트 역할
    /// </summary>
    AgentRole Role { get; }

    /// <summary>
    /// 시스템 프롬프트
    /// </summary>
    string SystemPrompt { get; }

    /// <summary>
    /// 권장 모델 티어
    /// </summary>
    ModelTier RecommendedTier { get; }

    /// <summary>
    /// 작업 처리
    /// </summary>
    /// <param name="input">입력 (질문 또는 작업 설명)</param>
    /// <param name="context">에이전트 컨텍스트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>에이전트 응답</returns>
    Task<AgentResponse> ProcessAsync(
        string input,
        AgentContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 이 에이전트가 작업을 처리할 수 있는지 판단
    /// </summary>
    /// <param name="taskDescription">작업 설명</param>
    /// <returns>처리 가능 여부와 신뢰도 (0-1)</returns>
    (bool CanHandle, double Confidence) CanHandle(string taskDescription);
}

/// <summary>
/// 서브에이전트 기본 구현
/// </summary>
public abstract class SubAgentBase : ISubAgent
{
    protected readonly SmartRouterService Router;

    protected SubAgentBase(SmartRouterService? router = null)
    {
        Router = router ?? new SmartRouterService();
    }

    public abstract string AgentName { get; }
    public abstract AgentRole Role { get; }
    public abstract string SystemPrompt { get; }
    public virtual ModelTier RecommendedTier => AgentRoleInfo.GetRecommendedTier(Role);

    public virtual async Task<AgentResponse> ProcessAsync(
        string input,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var provider = Router.SelectProviderByTier(RecommendedTier);
            if (provider == null)
            {
                return AgentResponse.Fail("No available AI provider");
            }

            var prompt = BuildPrompt(input, context);
            var response = await provider.ChatMode(prompt, context.ProjectContext);

            return new AgentResponse
            {
                Success = true,
                Content = response,
                Model = $"{provider.ModelName} ({AgentName})",
                Metadata = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["agent"] = AgentName,
                    ["role"] = Role.ToString()
                }
            };
        }
        catch (System.Exception ex)
        {
            return AgentResponse.Fail($"{AgentName} error: {ex.Message}");
        }
    }

    public abstract (bool CanHandle, double Confidence) CanHandle(string taskDescription);

    protected virtual string BuildPrompt(string input, AgentContext context)
    {
        var prompt = $"{SystemPrompt}\n\n";

        if (!string.IsNullOrEmpty(context.ProjectContext))
        {
            prompt += $"=== Project Context ===\n{context.ProjectContext}\n\n";
        }

        prompt += $"=== Task/Question ===\n{input}";

        if (context.RelevantFiles?.Count > 0)
        {
            prompt += $"\n\n=== Relevant Files ===\n{string.Join("\n", context.RelevantFiles)}";
        }

        return prompt;
    }

    /// <summary>
    /// 키워드 매칭 기반 신뢰도 계산
    /// </summary>
    protected double CalculateConfidence(string text, string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var lowerText = text.ToLower();
        var matchCount = 0;

        foreach (var keyword in keywords)
        {
            if (lowerText.Contains(keyword.ToLower()))
                matchCount++;
        }

        return keywords.Length > 0 ? (double)matchCount / keywords.Length : 0;
    }
}
