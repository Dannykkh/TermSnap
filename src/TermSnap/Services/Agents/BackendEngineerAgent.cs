using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services.Agents;

/// <summary>
/// Backend Engineer 에이전트 - API, 데이터베이스, 서버 로직
/// </summary>
public class BackendEngineerAgent : SubAgentBase
{
    private static readonly string[] Keywords =
    {
        "api", "rest", "graphql", "grpc", "database", "sql", "nosql",
        "backend", "server", "endpoint", "controller", "service", "repository",
        "crud", "authentication", "authorization", "jwt", "oauth", "session",
        "query", "migration", "orm", "entity", "model", "validation",
        "백엔드", "서버", "데이터베이스", "인증", "쿼리"
    };

    public override string AgentName => "Backend Engineer";
    public override AgentRole Role => AgentRole.BackendEngineer;
    public override ModelTier RecommendedTier => ModelTier.Balanced;

    public override string SystemPrompt => @"You are an expert Backend Engineer specializing in server-side development.

Your expertise includes:
- RESTful API design and implementation
- Database design and optimization (SQL, NoSQL)
- Authentication and authorization systems
- Service architecture and patterns
- Performance optimization and caching
- Error handling and logging

Guidelines:
- Follow clean architecture principles
- Write secure, validated code
- Design APIs following REST conventions
- Use proper error handling and status codes
- Implement input validation at all entry points
- Consider scalability and performance
- Write testable, modular code

When implementing APIs:
1. Define clear request/response contracts
2. Validate all inputs
3. Handle errors gracefully with proper status codes
4. Log important operations
5. Consider rate limiting and security";

    public BackendEngineerAgent(SmartRouterService? router = null) : base(router) { }

    public override (bool CanHandle, double Confidence) CanHandle(string taskDescription)
    {
        var confidence = CalculateConfidence(taskDescription, Keywords);

        // API 또는 DB 관련이면 추가 신뢰도
        if (taskDescription.ToLower().Contains("endpoint") ||
            taskDescription.ToLower().Contains("database") ||
            taskDescription.ToLower().Contains("query"))
        {
            confidence += 0.1;
        }

        return (confidence > 0.15, confidence);
    }

    public override async Task<AgentResponse> ProcessAsync(
        string input,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        var provider = Router.SelectProviderByTier(ModelTier.Balanced)
                      ?? Router.SelectProviderByTier(ModelTier.Powerful);

        if (provider == null)
        {
            return AgentResponse.Fail("No suitable AI provider available for Backend Engineer");
        }

        var prompt = BuildBackendPrompt(input, context);
        var response = await provider.ChatMode(prompt, context.ProjectContext);

        return new AgentResponse
        {
            Success = true,
            Content = response,
            Model = $"{provider.ModelName} (Backend)",
            Metadata = new System.Collections.Generic.Dictionary<string, object>
            {
                ["agent"] = "Backend Engineer",
                ["role"] = "Server"
            }
        };
    }

    private string BuildBackendPrompt(string input, AgentContext context)
    {
        var prompt = $"{SystemPrompt}\n\n";

        if (!string.IsNullOrEmpty(context.ProjectContext))
        {
            prompt += $"=== Project Context ===\n{context.ProjectContext}\n\n";
        }

        if (context.RelevantFiles?.Count > 0)
        {
            prompt += $"=== Relevant Files ===\n{string.Join("\n", context.RelevantFiles)}\n\n";
        }

        prompt += $"=== Task ===\n{input}\n\n";
        prompt += "Please provide your implementation:";

        return prompt;
    }
}
