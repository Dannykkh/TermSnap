using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services.ExecutionStrategies;

/// <summary>
/// Expert 모드 전략 - 전문 에이전트 자동 선택
/// 작업 내용을 분석해서 적절한 에이전트 역할 할당
/// </summary>
public class ExpertStrategy : IExecutionStrategy
{
    private readonly SmartRouterService _router;

    public string Name => "Expert Mode";
    public string Description => "Automatically selects specialized agents based on task analysis";
    public ExecutionMode Mode => ExecutionMode.Expert;

    public event Action<ExecutionProgress>? ProgressChanged;
    public event Action<AgentTask, AgentResponse>? TaskCompleted;

    // 역할 판단 키워드
    private static readonly Dictionary<AgentRole, HashSet<string>> RoleKeywords = new()
    {
        [AgentRole.FrontendEngineer] = new(StringComparer.OrdinalIgnoreCase)
        {
            "react", "vue", "angular", "css", "html", "ui", "ux", "component", "frontend",
            "style", "layout", "responsive", "animation", "button", "form", "modal",
            "프론트엔드", "컴포넌트", "스타일", "레이아웃"
        },
        [AgentRole.BackendEngineer] = new(StringComparer.OrdinalIgnoreCase)
        {
            "api", "rest", "graphql", "database", "sql", "backend", "server", "endpoint",
            "controller", "service", "repository", "crud", "authentication", "authorization",
            "백엔드", "서버", "데이터베이스", "인증"
        },
        [AgentRole.DevOps] = new(StringComparer.OrdinalIgnoreCase)
        {
            "docker", "kubernetes", "k8s", "ci", "cd", "pipeline", "deploy", "infrastructure",
            "aws", "azure", "gcp", "nginx", "container", "helm", "terraform",
            "배포", "인프라", "컨테이너"
        },
        [AgentRole.SecurityExpert] = new(StringComparer.OrdinalIgnoreCase)
        {
            "security", "vulnerability", "xss", "csrf", "injection", "authentication",
            "encryption", "ssl", "tls", "certificate", "penetration", "audit",
            "보안", "취약점", "암호화", "인증"
        },
        [AgentRole.TestEngineer] = new(StringComparer.OrdinalIgnoreCase)
        {
            "test", "unit", "integration", "e2e", "jest", "mocha", "cypress", "selenium",
            "coverage", "assertion", "mock", "stub", "fixture",
            "테스트", "단위", "통합"
        },
        [AgentRole.Oracle] = new(StringComparer.OrdinalIgnoreCase)
        {
            "architecture", "design", "pattern", "strategy", "refactor", "optimize",
            "best practice", "decision", "trade-off", "scalability",
            "아키텍처", "설계", "패턴", "전략", "리팩토링"
        },
        [AgentRole.Librarian] = new(StringComparer.OrdinalIgnoreCase)
        {
            "documentation", "docs", "api reference", "how to", "example", "tutorial",
            "library", "package", "npm", "nuget", "dependency",
            "문서", "레퍼런스", "사용법", "라이브러리"
        },
        [AgentRole.CodeReviewer] = new(StringComparer.OrdinalIgnoreCase)
        {
            "review", "code quality", "lint", "standard", "convention", "readable",
            "maintainable", "clean code", "solid",
            "리뷰", "코드 품질", "컨벤션"
        }
    };

    public ExpertStrategy(SmartRouterService? router = null)
    {
        _router = router ?? new SmartRouterService();
    }

    public async Task<ExecutionResult> ExecuteAsync(
        IEnumerable<AgentTask> tasks,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        var taskList = tasks.ToList();
        var result = new ExecutionResult
        {
            TotalCount = taskList.Count
        };

        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < taskList.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var task = taskList[i];

            // 역할 분석
            var role = AnalyzeTaskRole(task.Description);

            // 진행 상황 업데이트
            ProgressChanged?.Invoke(new ExecutionProgress
            {
                CurrentIndex = i + 1,
                TotalCount = taskList.Count,
                CurrentTask = task.Description,
                Status = $"Assigned to {AgentRoleInfo.GetDisplayName(role)}...",
                CurrentModel = role.ToString()
            });

            var response = await ExecuteTaskWithRoleAsync(task, role, context, cancellationToken);

            var taskResult = new TaskResult
            {
                TaskId = task.Id,
                Success = response.Success,
                Response = response.Content,
                Error = response.Error,
                Model = response.Model,
                TokensUsed = response.TokensUsed,
                Duration = TimeSpan.FromMilliseconds(response.ExecutionTimeMs ?? 0)
            };

            result.TaskResults.Add(taskResult);

            if (response.Success)
                result.CompletedCount++;
            else
                result.FailedCount++;

            if (response.TokensUsed.HasValue)
                result.TotalTokensUsed += response.TokensUsed.Value;

            TaskCompleted?.Invoke(task, response);
        }

        stopwatch.Stop();
        result.TotalDuration = stopwatch.Elapsed;
        result.Success = result.FailedCount == 0;

        return result;
    }

    public Task<AgentResponse> ExecuteTaskAsync(
        AgentTask task,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        var role = AnalyzeTaskRole(task.Description);
        return ExecuteTaskWithRoleAsync(task, role, context, cancellationToken);
    }

    /// <summary>
    /// 작업 내용을 분석하여 적절한 역할 결정
    /// </summary>
    public AgentRole AnalyzeTaskRole(string taskDescription)
    {
        if (string.IsNullOrWhiteSpace(taskDescription))
            return AgentRole.General;

        var lowerTask = taskDescription.ToLower();

        // 각 역할별 점수 계산
        var roleScores = new Dictionary<AgentRole, int>();

        foreach (var (role, keywords) in RoleKeywords)
        {
            var score = keywords.Count(k => lowerTask.Contains(k.ToLower()));
            if (score > 0)
                roleScores[role] = score;
        }

        // 가장 높은 점수의 역할 반환
        if (roleScores.Count > 0)
        {
            return roleScores.OrderByDescending(x => x.Value).First().Key;
        }

        return AgentRole.General;
    }

    private async Task<AgentResponse> ExecuteTaskWithRoleAsync(
        AgentTask task,
        AgentRole role,
        AgentContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 역할에 맞는 티어 선택
            var tier = AgentRoleInfo.GetRecommendedTier(role);
            task.AssignedTier = tier;
            task.AssignedAgent = AgentRoleInfo.GetDisplayName(role);

            // Provider 선택
            var provider = _router.SelectProviderByTier(tier);
            if (provider == null)
            {
                return AgentResponse.Fail("No available AI provider for the role");
            }

            task.Status = AgentTaskStatus.Running;
            task.StartedAt = DateTime.Now;

            // 역할별 시스템 프롬프트를 사용한 AI 호출
            var prompt = BuildExpertPrompt(task, role, context);
            var response = await provider.ChatMode(prompt, context.ProjectContext);

            task.Status = AgentTaskStatus.Completed;
            task.CompletedAt = DateTime.Now;
            task.Result = response;

            stopwatch.Stop();

            return new AgentResponse
            {
                Success = true,
                Content = response,
                Model = $"{provider.ModelName} ({AgentRoleInfo.GetDisplayName(role)})",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                Metadata = new Dictionary<string, object>
                {
                    ["role"] = role.ToString(),
                    ["tier"] = tier.ToString()
                }
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            task.Status = AgentTaskStatus.Failed;
            task.Error = ex.Message;

            return new AgentResponse
            {
                Success = false,
                Error = ex.Message,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private string BuildExpertPrompt(AgentTask task, AgentRole role, AgentContext context)
    {
        var systemPrompt = GetRoleSystemPrompt(role);

        var prompt = $"{systemPrompt}\n\n";

        if (!string.IsNullOrEmpty(context.ProjectContext))
        {
            prompt += $"=== Project Context ===\n{context.ProjectContext}\n\n";
        }

        prompt += $"=== Task ===\n{task.Description}";

        if (context.RelevantFiles?.Count > 0)
        {
            prompt += $"\n\n=== Relevant Files ===\n{string.Join("\n", context.RelevantFiles)}";
        }

        return prompt;
    }

    private string GetRoleSystemPrompt(AgentRole role) => role switch
    {
        AgentRole.Oracle => "You are an expert software architect and technical advisor. Provide high-level strategic guidance, architectural recommendations, and best practices. Focus on scalability, maintainability, and long-term impact.",

        AgentRole.Librarian => "You are a documentation specialist and library expert. Help find relevant documentation, API references, and usage examples. Provide clear, accurate information about libraries and frameworks.",

        AgentRole.FrontendEngineer => "You are an expert frontend engineer specializing in UI/UX, React, CSS, and web development. Focus on user experience, performance, accessibility, and modern frontend best practices.",

        AgentRole.BackendEngineer => "You are an expert backend engineer specializing in API design, databases, and server-side development. Focus on clean architecture, performance, security, and maintainable code.",

        AgentRole.DevOps => "You are an expert DevOps engineer specializing in deployment, CI/CD, infrastructure, and containerization. Focus on automation, reliability, monitoring, and infrastructure as code.",

        AgentRole.SecurityExpert => "You are a cybersecurity expert specializing in vulnerability assessment, secure coding, and security best practices. Focus on identifying risks and providing secure solutions.",

        AgentRole.TestEngineer => "You are an expert QA/test engineer specializing in test strategy, automation, and quality assurance. Focus on comprehensive test coverage, edge cases, and test maintainability.",

        AgentRole.CodeReviewer => "You are an expert code reviewer focusing on code quality, readability, and best practices. Provide constructive feedback on code structure, naming, patterns, and potential improvements.",

        _ => "You are a helpful AI assistant. Provide clear, accurate, and helpful responses."
    };
}
