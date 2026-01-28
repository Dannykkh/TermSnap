using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services.ExecutionStrategies;

/// <summary>
/// Swarm 모드 전략 - 병렬 실행 (3-5개 에이전트 동시)
/// </summary>
public class SwarmStrategy : IExecutionStrategy
{
    private readonly SmartRouterService _router;
    private readonly int _maxConcurrency;
    private readonly SemaphoreSlim _semaphore;

    public string Name => "Swarm Mode";
    public string Description => "Parallel execution with multiple agents";
    public ExecutionMode Mode => ExecutionMode.Swarm;

    public event Action<ExecutionProgress>? ProgressChanged;
    public event Action<AgentTask, AgentResponse>? TaskCompleted;

    /// <summary>
    /// Swarm 전략 생성
    /// </summary>
    /// <param name="maxConcurrency">최대 동시 실행 수 (기본: 3)</param>
    /// <param name="router">스마트 라우터</param>
    public SwarmStrategy(int maxConcurrency = 3, SmartRouterService? router = null)
    {
        _maxConcurrency = Math.Clamp(maxConcurrency, 1, 5);
        _semaphore = new SemaphoreSlim(_maxConcurrency);
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
        var completedCount = 0;
        var lockObj = new object();

        // 의존성이 없는 작업들 먼저 실행
        var independentTasks = taskList.Where(t => t.Dependencies.Count == 0).ToList();
        var dependentTasks = taskList.Where(t => t.Dependencies.Count > 0).ToList();

        // 병렬 실행
        var parallelTasks = independentTasks.Select(async task =>
        {
            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                // 진행 상황 업데이트
                lock (lockObj)
                {
                    ProgressChanged?.Invoke(new ExecutionProgress
                    {
                        CurrentIndex = completedCount + 1,
                        TotalCount = taskList.Count,
                        CurrentTask = task.Description,
                        Status = "Running in parallel...",
                        ConcurrentTasks = _maxConcurrency - _semaphore.CurrentCount
                    });
                }

                var response = await ExecuteTaskAsync(task, context, cancellationToken);

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

                lock (lockObj)
                {
                    result.TaskResults.Add(taskResult);

                    if (response.Success)
                        result.CompletedCount++;
                    else
                        result.FailedCount++;

                    if (response.TokensUsed.HasValue)
                        result.TotalTokensUsed += response.TokensUsed.Value;

                    completedCount++;
                }

                TaskCompleted?.Invoke(task, response);

                return (task, response);
            }
            finally
            {
                _semaphore.Release();
            }
        });

        await Task.WhenAll(parallelTasks);

        // 의존성 있는 작업들 순차 실행
        foreach (var task in dependentTasks)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // 의존성 확인
            var allDependenciesComplete = task.Dependencies.All(depId =>
                result.TaskResults.Any(r => r.TaskId == depId && r.Success));

            if (!allDependenciesComplete)
            {
                task.Status = AgentTaskStatus.Failed;
                task.Error = "Dependencies not met";
                result.FailedCount++;
                continue;
            }

            var response = await ExecuteTaskAsync(task, context, cancellationToken);

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

    public async Task<AgentResponse> ExecuteTaskAsync(
        AgentTask task,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 복잡도 분석
            var complexity = _router.AnalyzeComplexity(task.Description);
            task.Complexity = complexity;

            // 복잡도에 맞는 티어 선택
            var tier = _router.GetRecommendedTier(complexity);
            task.AssignedTier = tier;

            // Provider 선택
            var provider = _router.SelectProviderByTier(tier);
            if (provider == null)
            {
                return AgentResponse.Fail("No available AI provider");
            }

            task.Status = AgentTaskStatus.Running;
            task.StartedAt = DateTime.Now;
            task.AssignedAgent = provider.ProviderName;

            // AI 호출
            var prompt = BuildPrompt(task, context);
            var response = await provider.ChatMode(prompt, context.ProjectContext);

            task.Status = AgentTaskStatus.Completed;
            task.CompletedAt = DateTime.Now;
            task.Result = response;

            stopwatch.Stop();

            return new AgentResponse
            {
                Success = true,
                Content = response,
                Model = provider.ModelName,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
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

    private string BuildPrompt(AgentTask task, AgentContext context)
    {
        var prompt = task.Description;

        if (!string.IsNullOrEmpty(context.ProjectContext))
        {
            prompt = $"Context:\n{context.ProjectContext}\n\nTask:\n{prompt}";
        }

        if (context.RelevantFiles?.Count > 0)
        {
            prompt += $"\n\nRelevant files:\n{string.Join("\n", context.RelevantFiles)}";
        }

        return prompt;
    }
}
