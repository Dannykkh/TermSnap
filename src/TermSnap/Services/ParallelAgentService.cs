using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;
using TermSnap.Services.ExecutionStrategies;

namespace TermSnap.Services;

/// <summary>
/// 병렬 에이전트 서비스 - 여러 작업을 동시에 실행
/// </summary>
public class ParallelAgentService : IDisposable
{
    private readonly SmartRouterService _router;
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private readonly ConcurrentDictionary<string, AgentTask> _activeTasks = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;

    /// <summary>
    /// 작업 완료 이벤트
    /// </summary>
    public event Action<AgentTask, AgentResponse>? TaskCompleted;

    /// <summary>
    /// 진행 상황 변경 이벤트
    /// </summary>
    public event Action<ParallelExecutionProgress>? ProgressChanged;

    /// <summary>
    /// 오류 발생 이벤트
    /// </summary>
    public event Action<AgentTask, Exception>? TaskError;

    /// <summary>
    /// 병렬 에이전트 서비스 생성
    /// </summary>
    /// <param name="maxConcurrency">최대 동시 실행 수 (기본: 3)</param>
    /// <param name="router">스마트 라우터</param>
    public ParallelAgentService(int maxConcurrency = 3, SmartRouterService? router = null)
    {
        _maxConcurrency = Math.Clamp(maxConcurrency, 1, 10);
        _semaphore = new SemaphoreSlim(_maxConcurrency);
        _router = router ?? new SmartRouterService();
    }

    /// <summary>
    /// 현재 동시 실행 중인 작업 수
    /// </summary>
    public int ActiveTaskCount => _activeTasks.Count;

    /// <summary>
    /// 최대 동시 실행 수
    /// </summary>
    public int MaxConcurrency => _maxConcurrency;

    /// <summary>
    /// 실행 중 여부
    /// </summary>
    public bool IsRunning => _cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested;

    /// <summary>
    /// 작업 목록 병렬 실행
    /// </summary>
    public async Task<ParallelExecutionResult> ExecuteAsync(
        IEnumerable<AgentTask> tasks,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = _cancellationTokenSource.Token;

        var taskList = tasks.ToList();
        var result = new ParallelExecutionResult
        {
            TotalCount = taskList.Count,
            MaxConcurrency = _maxConcurrency
        };

        var stopwatch = Stopwatch.StartNew();
        var completedCount = 0;
        var lockObj = new object();

        // 의존성 그래프 구축
        var dependencyGraph = BuildDependencyGraph(taskList);

        // 준비된 작업 (의존성 없음)
        var readyTasks = new ConcurrentQueue<AgentTask>(
            taskList.Where(t => t.Dependencies.Count == 0));

        // 대기 중인 작업 (의존성 있음)
        var waitingTasks = new ConcurrentDictionary<string, AgentTask>(
            taskList.Where(t => t.Dependencies.Count > 0)
                    .ToDictionary(t => t.Id, t => t));

        // 완료된 작업 ID
        var completedTaskIds = new ConcurrentBag<string>();

        // 작업 실행 함수
        async Task ProcessTaskAsync(AgentTask task)
        {
            await _semaphore.WaitAsync(linkedToken);

            try
            {
                _activeTasks.TryAdd(task.Id, task);

                // 진행 상황 업데이트
                lock (lockObj)
                {
                    ProgressChanged?.Invoke(new ParallelExecutionProgress
                    {
                        CompletedCount = completedCount,
                        TotalCount = taskList.Count,
                        ActiveCount = _activeTasks.Count,
                        MaxConcurrency = _maxConcurrency,
                        CurrentTasks = _activeTasks.Values.Select(t => t.Description).ToList()
                    });
                }

                var response = await ExecuteTaskAsync(task, context, linkedToken);

                lock (lockObj)
                {
                    if (response.Success)
                    {
                        result.CompletedCount++;
                        completedTaskIds.Add(task.Id);
                    }
                    else
                    {
                        result.FailedCount++;
                    }

                    result.TaskResults.Add(new TaskResult
                    {
                        TaskId = task.Id,
                        Success = response.Success,
                        Response = response.Content,
                        Error = response.Error,
                        Model = response.Model,
                        TokensUsed = response.TokensUsed,
                        Duration = TimeSpan.FromMilliseconds(response.ExecutionTimeMs ?? 0)
                    });

                    if (response.TokensUsed.HasValue)
                        result.TotalTokensUsed += response.TokensUsed.Value;

                    completedCount++;
                }

                TaskCompleted?.Invoke(task, response);

                // 이 작업에 의존하는 작업들 체크
                if (response.Success)
                {
                    foreach (var waitingTask in waitingTasks.Values.ToList())
                    {
                        // 모든 의존성이 완료되었는지 확인
                        if (waitingTask.Dependencies.All(d => completedTaskIds.Contains(d)))
                        {
                            if (waitingTasks.TryRemove(waitingTask.Id, out var readyTask))
                            {
                                readyTasks.Enqueue(readyTask);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                task.Status = AgentTaskStatus.Failed;
                task.Error = ex.Message;
                TaskError?.Invoke(task, ex);

                lock (lockObj)
                {
                    result.FailedCount++;
                }
            }
            finally
            {
                _activeTasks.TryRemove(task.Id, out _);
                _semaphore.Release();
            }
        }

        // 병렬 실행
        var runningTasks = new List<Task>();

        while (!linkedToken.IsCancellationRequested)
        {
            // 준비된 작업 가져오기
            while (readyTasks.TryDequeue(out var task))
            {
                var workerTask = ProcessTaskAsync(task);
                runningTasks.Add(workerTask);

                // 동시 실행 제한 확인
                if (runningTasks.Count(t => !t.IsCompleted) >= _maxConcurrency)
                {
                    // 하나라도 완료될 때까지 대기
                    await Task.WhenAny(runningTasks.Where(t => !t.IsCompleted));
                }
            }

            // 모든 작업 완료 확인
            if (runningTasks.All(t => t.IsCompleted) &&
                readyTasks.IsEmpty &&
                waitingTasks.IsEmpty)
            {
                break;
            }

            // 아직 실행 중인 작업이 있으면 대기
            if (runningTasks.Any(t => !t.IsCompleted))
            {
                await Task.WhenAny(runningTasks.Where(t => !t.IsCompleted));
            }
            else if (!readyTasks.IsEmpty || !waitingTasks.IsEmpty)
            {
                // 짧은 대기 후 재시도
                await Task.Delay(50, linkedToken);
            }
        }

        // 남은 작업들 완료 대기
        await Task.WhenAll(runningTasks);

        stopwatch.Stop();
        result.TotalDuration = stopwatch.Elapsed;
        result.Success = result.FailedCount == 0;

        return result;
    }

    /// <summary>
    /// 단일 작업 실행
    /// </summary>
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

            // 최적 티어 선택
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

    /// <summary>
    /// 실행 취소
    /// </summary>
    public void Cancel()
    {
        _cancellationTokenSource?.Cancel();
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

    private Dictionary<string, List<string>> BuildDependencyGraph(List<AgentTask> tasks)
    {
        var graph = new Dictionary<string, List<string>>();

        foreach (var task in tasks)
        {
            graph[task.Id] = task.Dependencies.ToList();
        }

        return graph;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _semaphore.Dispose();

        _disposed = true;
    }
}

/// <summary>
/// 병렬 실행 결과
/// </summary>
public class ParallelExecutionResult : ExecutionResult
{
    /// <summary>
    /// 최대 동시 실행 수
    /// </summary>
    public int MaxConcurrency { get; set; }

    /// <summary>
    /// 평균 작업 시간
    /// </summary>
    public TimeSpan AverageTaskDuration =>
        TaskResults.Count > 0
            ? TimeSpan.FromMilliseconds(TaskResults.Average(r => r.Duration.TotalMilliseconds))
            : TimeSpan.Zero;
}

/// <summary>
/// 병렬 실행 진행 상황
/// </summary>
public class ParallelExecutionProgress
{
    /// <summary>
    /// 완료된 작업 수
    /// </summary>
    public int CompletedCount { get; set; }

    /// <summary>
    /// 총 작업 수
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 현재 실행 중인 작업 수
    /// </summary>
    public int ActiveCount { get; set; }

    /// <summary>
    /// 최대 동시 실행 수
    /// </summary>
    public int MaxConcurrency { get; set; }

    /// <summary>
    /// 현재 실행 중인 작업 설명들
    /// </summary>
    public List<string> CurrentTasks { get; set; } = new();

    /// <summary>
    /// 진행률 (0-100)
    /// </summary>
    public int ProgressPercent => TotalCount > 0 ? CompletedCount * 100 / TotalCount : 0;
}
