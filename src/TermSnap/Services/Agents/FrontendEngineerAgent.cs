using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services.Agents;

/// <summary>
/// Frontend Engineer 에이전트 - UI/UX, React, CSS, 웹 개발
/// </summary>
public class FrontendEngineerAgent : SubAgentBase
{
    private static readonly string[] Keywords =
    {
        "react", "vue", "angular", "svelte", "css", "scss", "tailwind",
        "html", "ui", "ux", "component", "frontend", "style", "layout",
        "responsive", "animation", "button", "form", "modal", "dialog",
        "hook", "state", "props", "jsx", "tsx", "dom", "event",
        "프론트엔드", "컴포넌트", "스타일", "레이아웃", "반응형"
    };

    public override string AgentName => "Frontend Engineer";
    public override AgentRole Role => AgentRole.FrontendEngineer;
    public override ModelTier RecommendedTier => ModelTier.Balanced;

    public override string SystemPrompt => @"You are an expert Frontend Engineer specializing in modern web development.

Your expertise includes:
- React, Vue, Angular, and modern JavaScript frameworks
- CSS, SCSS, Tailwind, and styling best practices
- Component architecture and state management
- Responsive design and accessibility
- Performance optimization and bundle size
- User experience and interaction design

Guidelines:
- Write clean, reusable component code
- Follow framework-specific best practices
- Consider accessibility (a11y) in all solutions
- Optimize for performance and user experience
- Use TypeScript when applicable
- Include proper error handling and loading states

When implementing UI:
1. Consider the user flow and experience
2. Ensure responsive behavior across devices
3. Follow consistent styling patterns
4. Handle edge cases (empty states, errors, loading)
5. Write semantic, accessible HTML";

    public FrontendEngineerAgent(SmartRouterService? router = null) : base(router) { }

    public override (bool CanHandle, double Confidence) CanHandle(string taskDescription)
    {
        var confidence = CalculateConfidence(taskDescription, Keywords);

        // UI 관련 단어가 있으면 추가 신뢰도
        if (taskDescription.ToLower().Contains("button") ||
            taskDescription.ToLower().Contains("component") ||
            taskDescription.ToLower().Contains("page") ||
            taskDescription.ToLower().Contains("screen"))
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
            return AgentResponse.Fail("No suitable AI provider available for Frontend Engineer");
        }

        var prompt = BuildFrontendPrompt(input, context);
        var response = await provider.ChatMode(prompt, context.ProjectContext);

        return new AgentResponse
        {
            Success = true,
            Content = response,
            Model = $"{provider.ModelName} (Frontend)",
            Metadata = new System.Collections.Generic.Dictionary<string, object>
            {
                ["agent"] = "Frontend Engineer",
                ["role"] = "UI/UX"
            }
        };
    }

    private string BuildFrontendPrompt(string input, AgentContext context)
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
