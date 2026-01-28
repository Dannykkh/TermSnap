using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services.ExecutionStrategies;

/// <summary>
/// Pipeline 모드 전략 - 순차 실행 (이전 결과를 다음 단계로 전달)
/// </summary>
public class PipelineStrategy : IExecutionStrategy
{
    private readonly SmartRouterService _router;

    public string Name => "Pipeline Mode";
    public string Description => "Sequential execution passing results to next step";
    public ExecutionMode Mode => ExecutionMode.Pipeline;

    public event Action<ExecutionProgress>? ProgressChanged;
    public event Action<AgentTask, AgentResponse>? TaskCompleted;

    public PipelineStrategy(SmartRouterService? router = null)
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
        var pipelineContext = new StringBuilder();

        // 초기 컨텍스트 추가
        if (!string.IsNullOrEmpty(context.ProjectContext))
        {
            pipelineContext.AppendLine("=== Initial Context ===");
            pipelineContext.AppendLine(context.ProjectContext);
            pipelineContext.AppendLine();
        }

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
                Status = $"Pipeline step {i + 1}/{taskList.Count}..."
            });

            // 파이프라인 컨텍스트를 현재 컨텍스트에 추가
            var currentContext = new AgentContext
            {
                WorkingDirectory = context.WorkingDirectory,
                ProjectContext = pipelineContext.ToString(),
                RelevantFiles = context.RelevantFiles,
                Parameters = context.Parameters
            };

            var response = await ExecuteTaskAsync(task, currentContext, cancellationToken);

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
            {
                result.CompletedCount++;

                // 결과를 파이프라인 컨텍스트에 추가
                pipelineContext.AppendLine($"=== Step {i + 1} Result ({task.Description.Substring(0, Math.Min(50, task.Description.Length))}...) ===");
                pipelineContext.AppendLine(response.Content);
                pipelineContext.AppendLine();
            }
            else
            {
                result.FailedCount++;

                // 파이프라인 중단 (실패 시)
                result.ErrorMessage = $"Pipeline failed at step {i + 1}: {response.Error}";
                break;
            }

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

            // AI 호출 (파이프라인 컨텍스트 포함)
            var prompt = BuildPipelinePrompt(task, context);
            var response = await provider.ChatMode(prompt);

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

    private string BuildPipelinePrompt(AgentTask task, AgentContext context)
    {
        var sb = new StringBuilder();

        // 파이프라인 모드임을 명시
        sb.AppendLine("You are part of a pipeline workflow. Use the previous results as context for your task.");
        sb.AppendLine();

        // 이전 단계 결과 (파이프라인 컨텍스트)
        if (!string.IsNullOrEmpty(context.ProjectContext))
        {
            sb.AppendLine("=== Previous Pipeline Context ===");
            sb.AppendLine(context.ProjectContext);
            sb.AppendLine();
        }

        // 현재 작업
        sb.AppendLine("=== Current Task ===");
        sb.AppendLine(task.Description);

        // 관련 파일
        if (context.RelevantFiles?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("=== Relevant Files ===");
            sb.AppendLine(string.Join("\n", context.RelevantFiles));
        }

        return sb.ToString();
    }
}
