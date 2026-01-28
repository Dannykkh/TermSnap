using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;
using TermSnap.Services.Agents;
using TermSnap.Services.ExecutionStrategies;

namespace TermSnap.Services;

/// <summary>
/// 에이전트 오케스트레이터 - 에이전트 선택 및 협업 관리
/// </summary>
public class AgentOrchestrator : IDisposable
{
    private readonly SmartRouterService _router;
    private readonly Dictionary<AgentRole, ISubAgent> _agents;
    private readonly Dictionary<ExecutionMode, IExecutionStrategy> _strategies;
    private ExecutionMode _currentMode = ExecutionMode.Interactive;
    private bool _disposed;

    /// <summary>
    /// 진행 상황 변경 이벤트
    /// </summary>
    public event Action<ExecutionProgress>? ProgressChanged;

    /// <summary>
    /// 에이전트 선택 이벤트
    /// </summary>
    public event Action<AgentRole, string>? AgentSelected;

    /// <summary>
    /// 작업 완료 이벤트
    /// </summary>
    public event Action<AgentTask, AgentResponse>? TaskCompleted;

    public AgentOrchestrator(SmartRouterService? router = null)
    {
        _router = router ?? new SmartRouterService();

        // 에이전트 등록
        _agents = new Dictionary<AgentRole, ISubAgent>
        {
            [AgentRole.Oracle] = new OracleAgent(_router),
            [AgentRole.Librarian] = new LibrarianAgent(_router),
            [AgentRole.FrontendEngineer] = new FrontendEngineerAgent(_router),
            [AgentRole.BackendEngineer] = new BackendEngineerAgent(_router),
            [AgentRole.DevOps] = new DevOpsAgent(_router)
        };

        // 실행 전략 등록
        _strategies = new Dictionary<ExecutionMode, IExecutionStrategy>
        {
            [ExecutionMode.EcoMode] = new EcoModeStrategy(_router),
            [ExecutionMode.Pipeline] = new PipelineStrategy(_router),
            [ExecutionMode.Swarm] = new SwarmStrategy(3, _router),
            [ExecutionMode.Expert] = new ExpertStrategy(_router)
        };

        // 전략 이벤트 연결
        foreach (var strategy in _strategies.Values)
        {
            strategy.ProgressChanged += p => ProgressChanged?.Invoke(p);
            strategy.TaskCompleted += (t, r) => TaskCompleted?.Invoke(t, r);
        }
    }

    /// <summary>
    /// 현재 실행 모드
    /// </summary>
    public ExecutionMode CurrentMode
    {
        get => _currentMode;
        set => _currentMode = value;
    }

    /// <summary>
    /// 사용 가능한 에이전트 목록
    /// </summary>
    public IEnumerable<AgentRole> AvailableAgents => _agents.Keys;

    /// <summary>
    /// 사용 가능한 실행 모드 목록
    /// </summary>
    public IEnumerable<ExecutionMode> AvailableModes => _strategies.Keys;

    /// <summary>
    /// 작업에 최적의 에이전트 선택
    /// </summary>
    public (AgentRole Role, double Confidence) SelectBestAgent(string taskDescription)
    {
        var candidates = new List<(AgentRole Role, double Confidence)>();

        foreach (var (role, agent) in _agents)
        {
            var (canHandle, confidence) = agent.CanHandle(taskDescription);
            if (canHandle)
            {
                candidates.Add((role, confidence));
            }
        }

        if (candidates.Count == 0)
        {
            return (AgentRole.General, 0);
        }

        return candidates.OrderByDescending(c => c.Confidence).First();
    }

    /// <summary>
    /// 특정 에이전트로 작업 처리
    /// </summary>
    public async Task<AgentResponse> ProcessWithAgentAsync(
        AgentRole role,
        string input,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_agents.TryGetValue(role, out var agent))
        {
            // General 역할은 기본 처리
            if (role == AgentRole.General)
            {
                return await ProcessWithDefaultAsync(input, context, cancellationToken);
            }

            return AgentResponse.Fail($"Agent not found: {role}");
        }

        AgentSelected?.Invoke(role, agent.AgentName);
        return await agent.ProcessAsync(input, context, cancellationToken);
    }

    /// <summary>
    /// 자동으로 최적 에이전트 선택 후 작업 처리
    /// </summary>
    public async Task<AgentResponse> ProcessAutoAsync(
        string input,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        var (role, confidence) = SelectBestAgent(input);

        if (confidence < 0.1 || role == AgentRole.General)
        {
            return await ProcessWithDefaultAsync(input, context, cancellationToken);
        }

        return await ProcessWithAgentAsync(role, input, context, cancellationToken);
    }

    /// <summary>
    /// 현재 모드로 작업 목록 실행
    /// </summary>
    public async Task<ExecutionResult> ExecuteTasksAsync(
        IEnumerable<AgentTask> tasks,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        // Interactive/Yolo 모드는 기본 순차 실행
        if (_currentMode == ExecutionMode.Interactive || _currentMode == ExecutionMode.Yolo)
        {
            return await ExecuteSequentialAsync(tasks, context, cancellationToken);
        }

        // 해당 모드의 전략 사용
        if (_strategies.TryGetValue(_currentMode, out var strategy))
        {
            return await strategy.ExecuteAsync(tasks, context, cancellationToken);
        }

        // 기본 순차 실행
        return await ExecuteSequentialAsync(tasks, context, cancellationToken);
    }

    /// <summary>
    /// 특정 모드로 작업 목록 실행
    /// </summary>
    public async Task<ExecutionResult> ExecuteWithModeAsync(
        ExecutionMode mode,
        IEnumerable<AgentTask> tasks,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        var previousMode = _currentMode;
        _currentMode = mode;

        try
        {
            return await ExecuteTasksAsync(tasks, context, cancellationToken);
        }
        finally
        {
            _currentMode = previousMode;
        }
    }

    /// <summary>
    /// 기본 처리 (전문 에이전트 없이)
    /// </summary>
    private async Task<AgentResponse> ProcessWithDefaultAsync(
        string input,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        // 복잡도 분석 후 적절한 모델 선택
        var complexity = _router.AnalyzeComplexity(input);
        var provider = _router.SelectProvider(input);

        if (provider == null)
        {
            return AgentResponse.Fail("No available AI provider");
        }

        var prompt = input;
        if (!string.IsNullOrEmpty(context.ProjectContext))
        {
            prompt = $"Context:\n{context.ProjectContext}\n\nTask:\n{input}";
        }

        var response = await provider.ChatMode(prompt, context.ProjectContext);

        return new AgentResponse
        {
            Success = true,
            Content = response,
            Model = provider.ModelName,
            Metadata = new Dictionary<string, object>
            {
                ["complexity"] = complexity.ToString(),
                ["agent"] = "General"
            }
        };
    }

    /// <summary>
    /// 순차 실행 (기본)
    /// </summary>
    private async Task<ExecutionResult> ExecuteSequentialAsync(
        IEnumerable<AgentTask> tasks,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        var taskList = tasks.ToList();
        var result = new ExecutionResult
        {
            TotalCount = taskList.Count
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < taskList.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var task = taskList[i];

            ProgressChanged?.Invoke(new ExecutionProgress
            {
                CurrentIndex = i + 1,
                TotalCount = taskList.Count,
                CurrentTask = task.Description,
                Status = "Processing..."
            });

            var response = await ProcessAutoAsync(task.Description, context, cancellationToken);

            var taskResult = new TaskResult
            {
                TaskId = task.Id,
                Success = response.Success,
                Response = response.Content,
                Error = response.Error,
                Model = response.Model,
                TokensUsed = response.TokensUsed
            };

            result.TaskResults.Add(taskResult);

            if (response.Success)
                result.CompletedCount++;
            else
                result.FailedCount++;

            task.Status = response.Success ? AgentTaskStatus.Completed : AgentTaskStatus.Failed;
            task.Result = response.Content;
            task.Error = response.Error;

            TaskCompleted?.Invoke(task, response);
        }

        stopwatch.Stop();
        result.TotalDuration = stopwatch.Elapsed;
        result.Success = result.FailedCount == 0;

        return result;
    }

    /// <summary>
    /// 에이전트 정보 조회
    /// </summary>
    public (string Name, string SystemPrompt, ModelTier Tier)? GetAgentInfo(AgentRole role)
    {
        if (_agents.TryGetValue(role, out var agent))
        {
            return (agent.AgentName, agent.SystemPrompt, agent.RecommendedTier);
        }
        return null;
    }

    /// <summary>
    /// 실행 모드 정보 조회
    /// </summary>
    public (string Name, string Description)? GetModeInfo(ExecutionMode mode)
    {
        if (_strategies.TryGetValue(mode, out var strategy))
        {
            return (strategy.Name, strategy.Description);
        }

        return mode switch
        {
            ExecutionMode.Interactive => ("Interactive", "Step-by-step execution with user confirmation"),
            ExecutionMode.Yolo => ("YOLO", "Automatic execution without confirmation"),
            _ => null
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

/// <summary>
/// 오케스트레이터 확장 메서드
/// </summary>
public static class AgentOrchestratorExtensions
{
    /// <summary>
    /// ExecutionMode를 표시 문자열로 변환
    /// </summary>
    public static string ToDisplayString(this ExecutionMode mode) => mode switch
    {
        ExecutionMode.Interactive => "Interactive",
        ExecutionMode.Yolo => "YOLO (Auto)",
        ExecutionMode.EcoMode => "Eco (Cost-saving)",
        ExecutionMode.Pipeline => "Pipeline",
        ExecutionMode.Swarm => "Swarm (Parallel)",
        ExecutionMode.Expert => "Expert (Auto-select)",
        _ => mode.ToString()
    };

    /// <summary>
    /// ExecutionMode 설명
    /// </summary>
    public static string GetDescription(this ExecutionMode mode) => mode switch
    {
        ExecutionMode.Interactive => "Step-by-step with user confirmation",
        ExecutionMode.Yolo => "Automatic execution without confirmation",
        ExecutionMode.EcoMode => "Use fast/cheap models when possible",
        ExecutionMode.Pipeline => "Sequential with context passing",
        ExecutionMode.Swarm => "Parallel execution (3-5 concurrent)",
        ExecutionMode.Expert => "Auto-select specialized agents",
        _ => ""
    };
}
