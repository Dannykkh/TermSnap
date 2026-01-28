using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services.Agents;

/// <summary>
/// Oracle 에이전트 - 고급 조언, 전략 수립, 아키텍처 설계
/// </summary>
public class OracleAgent : SubAgentBase
{
    private static readonly string[] Keywords =
    {
        "architecture", "design", "pattern", "strategy", "refactor", "optimize",
        "best practice", "decision", "trade-off", "scalability", "approach",
        "should i", "recommend", "advice", "suggestion", "how should",
        "아키텍처", "설계", "패턴", "전략", "리팩토링", "최적화", "조언", "추천"
    };

    public override string AgentName => "Oracle";
    public override AgentRole Role => AgentRole.Oracle;
    public override ModelTier RecommendedTier => ModelTier.Powerful;

    public override string SystemPrompt => @"You are the Oracle - an expert software architect and technical strategist.

Your role is to:
1. Provide high-level architectural guidance and strategic recommendations
2. Analyze trade-offs between different approaches
3. Suggest best practices and design patterns appropriate for the context
4. Help make informed technical decisions

Guidelines:
- Consider scalability, maintainability, and long-term impact
- Explain the 'why' behind recommendations
- Present multiple options when appropriate, with pros/cons
- Be concise but thorough
- Use concrete examples when helpful

When analyzing a problem:
1. Understand the current state and constraints
2. Identify the key decision points
3. Evaluate options based on trade-offs
4. Provide a clear recommendation with rationale";

    public OracleAgent(SmartRouterService? router = null) : base(router) { }

    public override (bool CanHandle, double Confidence) CanHandle(string taskDescription)
    {
        var confidence = CalculateConfidence(taskDescription, Keywords);

        // 질문 형태면 신뢰도 증가
        if (taskDescription.Contains("?") ||
            taskDescription.ToLower().Contains("should") ||
            taskDescription.ToLower().Contains("how to approach"))
        {
            confidence += 0.2;
        }

        return (confidence > 0.15, confidence);
    }

    public override async Task<AgentResponse> ProcessAsync(
        string input,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        // Oracle은 항상 Powerful 모델 사용
        var provider = Router.SelectProviderByTier(ModelTier.Powerful)
                      ?? Router.SelectProviderByTier(ModelTier.Balanced);

        if (provider == null)
        {
            return AgentResponse.Fail("No suitable AI provider available for Oracle");
        }

        var prompt = BuildOraclePrompt(input, context);
        var response = await provider.ChatMode(prompt, context.ProjectContext);

        return new AgentResponse
        {
            Success = true,
            Content = response,
            Model = $"{provider.ModelName} (Oracle)",
            Metadata = new System.Collections.Generic.Dictionary<string, object>
            {
                ["agent"] = "Oracle",
                ["role"] = "Strategy"
            }
        };
    }

    private string BuildOraclePrompt(string input, AgentContext context)
    {
        var prompt = $"{SystemPrompt}\n\n";

        if (!string.IsNullOrEmpty(context.ProjectContext))
        {
            prompt += $"=== Project Context ===\n{context.ProjectContext}\n\n";
        }

        prompt += $"=== Question/Task ===\n{input}\n\n";
        prompt += "Please provide your analysis and recommendations:";

        return prompt;
    }
}
