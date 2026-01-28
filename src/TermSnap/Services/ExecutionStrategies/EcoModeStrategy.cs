using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services.ExecutionStrategies;

/// <summary>
/// Eco 모드 전략 - 저비용 모델 우선 사용
/// 단순 작업은 Fast 모델, 복잡한 작업만 Powerful 모델 사용
/// </summary>
public class EcoModeStrategy : IExecutionStrategy
{
    private readonly SmartRouterService _router;

    public string Name => "Eco Mode";
    public string Description => "Cost-effective execution using fast models for simple tasks";
    public ExecutionMode Mode => ExecutionMode.EcoMode;

    public event Action<ExecutionProgress>? ProgressChanged;
    public event Action<AgentTask, AgentResponse>? TaskCompleted;

    public EcoModeStrategy(SmartRouterService? router = null)
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

            // 진행 상황 업데이트
            ProgressChanged?.Invoke(new ExecutionProgress
            {
                CurrentIndex = i + 1,
                TotalCount = taskList.Count,
                CurrentTask = task.Description,
                Status = "Analyzing complexity..."
            });

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

            // Eco 모드: 기본적으로 Fast, 복잡한 것만 Balanced 사용
            var tier = complexity switch
            {
                TaskComplexity.Complex => ModelTier.Balanced, // Eco에서는 Powerful 대신 Balanced
                _ => ModelTier.Fast
            };

            task.AssignedTier = tier;

            // 진행 상황 업데이트
            ProgressChanged?.Invoke(new ExecutionProgress
            {
                CurrentTask = task.Description,
                Status = $"Using {tier} model...",
                CurrentModel = tier.ToString()
            });

            // Provider 선택 및 실행
            var provider = _router.SelectProviderByTier(tier);
            if (provider == null)
            {
                return AgentResponse.Fail("No available AI provider for the selected tier");
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
