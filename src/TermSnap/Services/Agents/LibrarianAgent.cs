using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services.Agents;

/// <summary>
/// Librarian 에이전트 - 문서 검색, API 참조, 라이브러리 사용법
/// </summary>
public class LibrarianAgent : SubAgentBase
{
    private static readonly string[] Keywords =
    {
        "documentation", "docs", "api", "reference", "how to use", "example",
        "tutorial", "library", "package", "npm", "nuget", "pip", "dependency",
        "usage", "syntax", "parameter", "method", "function", "class",
        "문서", "레퍼런스", "사용법", "예제", "라이브러리", "패키지"
    };

    public override string AgentName => "Librarian";
    public override AgentRole Role => AgentRole.Librarian;
    public override ModelTier RecommendedTier => ModelTier.Fast;

    public override string SystemPrompt => @"You are the Librarian - an expert in documentation, APIs, and library usage.

Your role is to:
1. Find and explain relevant documentation
2. Provide API references and usage examples
3. Help understand library features and best practices
4. Suggest appropriate libraries for specific needs

Guidelines:
- Provide accurate, up-to-date information
- Include code examples when helpful
- Reference official documentation when possible
- Explain parameters, return values, and edge cases
- Mention version compatibility when relevant

Format your responses as:
1. Brief explanation of the concept/API
2. Basic usage example
3. Common patterns or best practices
4. Related resources or further reading";

    public LibrarianAgent(SmartRouterService? router = null) : base(router) { }

    public override (bool CanHandle, double Confidence) CanHandle(string taskDescription)
    {
        var confidence = CalculateConfidence(taskDescription, Keywords);

        // "how to" 질문이면 신뢰도 증가
        if (taskDescription.ToLower().Contains("how to") ||
            taskDescription.ToLower().Contains("what is") ||
            taskDescription.ToLower().Contains("example"))
        {
            confidence += 0.15;
        }

        return (confidence > 0.15, confidence);
    }

    public override async Task<AgentResponse> ProcessAsync(
        string input,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        // Librarian은 빠른 모델로 충분
        var provider = Router.SelectProviderByTier(ModelTier.Fast)
                      ?? Router.SelectProviderByTier(ModelTier.Balanced);

        if (provider == null)
        {
            return AgentResponse.Fail("No suitable AI provider available for Librarian");
        }

        var prompt = BuildLibrarianPrompt(input, context);
        var response = await provider.ChatMode(prompt, context.ProjectContext);

        return new AgentResponse
        {
            Success = true,
            Content = response,
            Model = $"{provider.ModelName} (Librarian)",
            Metadata = new System.Collections.Generic.Dictionary<string, object>
            {
                ["agent"] = "Librarian",
                ["role"] = "Documentation"
            }
        };
    }

    private string BuildLibrarianPrompt(string input, AgentContext context)
    {
        var prompt = $"{SystemPrompt}\n\n";

        // 프로젝트에서 사용 중인 기술 스택이 있으면 추가
        if (!string.IsNullOrEmpty(context.ProjectContext))
        {
            prompt += $"=== Project Tech Stack ===\n{context.ProjectContext}\n\n";
        }

        prompt += $"=== Query ===\n{input}\n\n";
        prompt += "Please provide documentation and examples:";

        return prompt;
    }
}
