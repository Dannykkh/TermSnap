using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services.Agents;

/// <summary>
/// DevOps 에이전트 - 배포, CI/CD, 인프라, Docker
/// </summary>
public class DevOpsAgent : SubAgentBase
{
    private static readonly string[] Keywords =
    {
        "docker", "kubernetes", "k8s", "ci", "cd", "pipeline", "deploy",
        "infrastructure", "aws", "azure", "gcp", "cloud", "nginx", "apache",
        "container", "helm", "terraform", "ansible", "jenkins", "github actions",
        "monitoring", "logging", "scaling", "load balancer", "ssl", "certificate",
        "배포", "인프라", "컨테이너", "파이프라인", "클라우드"
    };

    public override string AgentName => "DevOps Engineer";
    public override AgentRole Role => AgentRole.DevOps;
    public override ModelTier RecommendedTier => ModelTier.Balanced;

    public override string SystemPrompt => @"You are an expert DevOps Engineer specializing in infrastructure and deployment.

Your expertise includes:
- Docker and containerization
- Kubernetes and orchestration
- CI/CD pipelines (GitHub Actions, Jenkins, GitLab CI)
- Cloud platforms (AWS, Azure, GCP)
- Infrastructure as Code (Terraform, Ansible)
- Monitoring and logging
- Security and compliance

Guidelines:
- Follow infrastructure as code principles
- Prioritize automation and repeatability
- Consider security at every layer
- Design for scalability and reliability
- Use environment variables for configuration
- Implement proper logging and monitoring

When creating configurations:
1. Use best practices for the specific platform
2. Include proper error handling and rollback strategies
3. Document environment variables and dependencies
4. Consider both development and production environments
5. Implement health checks and monitoring";

    public DevOpsAgent(SmartRouterService? router = null) : base(router) { }

    public override (bool CanHandle, double Confidence) CanHandle(string taskDescription)
    {
        var confidence = CalculateConfidence(taskDescription, Keywords);

        // 배포/인프라 관련이면 추가 신뢰도
        if (taskDescription.ToLower().Contains("deploy") ||
            taskDescription.ToLower().Contains("server") ||
            taskDescription.ToLower().Contains("production"))
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
            return AgentResponse.Fail("No suitable AI provider available for DevOps Engineer");
        }

        var prompt = BuildDevOpsPrompt(input, context);
        var response = await provider.ChatMode(prompt, context.ProjectContext);

        return new AgentResponse
        {
            Success = true,
            Content = response,
            Model = $"{provider.ModelName} (DevOps)",
            Metadata = new System.Collections.Generic.Dictionary<string, object>
            {
                ["agent"] = "DevOps Engineer",
                ["role"] = "Infrastructure"
            }
        };
    }

    private string BuildDevOpsPrompt(string input, AgentContext context)
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
        prompt += "Please provide your solution:";

        return prompt;
    }
}
