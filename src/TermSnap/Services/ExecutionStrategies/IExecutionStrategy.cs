using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services.ExecutionStrategies;

/// <summary>
/// 실행 전략 인터페이스
/// </summary>
public interface IExecutionStrategy
{
    /// <summary>
    /// 전략 이름
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 전략 설명
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 대응되는 실행 모드
    /// </summary>
    ExecutionMode Mode { get; }

    /// <summary>
    /// 작업 실행
    /// </summary>
    /// <param name="tasks">실행할 작업 목록</param>
    /// <param name="context">실행 컨텍스트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>실행 결과</returns>
    Task<ExecutionResult> ExecuteAsync(
        IEnumerable<AgentTask> tasks,
        AgentContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 단일 작업 실행
    /// </summary>
    Task<AgentResponse> ExecuteTaskAsync(
        AgentTask task,
        AgentContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 진행 상황 업데이트 이벤트
    /// </summary>
    event Action<ExecutionProgress>? ProgressChanged;

    /// <summary>
    /// 작업 완료 이벤트
    /// </summary>
    event Action<AgentTask, AgentResponse>? TaskCompleted;
}

/// <summary>
/// 실행 결과
/// </summary>
public class ExecutionResult
{
    /// <summary>
    /// 전체 성공 여부
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 완료된 작업 수
    /// </summary>
    public int CompletedCount { get; set; }

    /// <summary>
    /// 실패한 작업 수
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 총 작업 수
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 총 실행 시간
    /// </summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>
    /// 총 토큰 사용량
    /// </summary>
    public int TotalTokensUsed { get; set; }

    /// <summary>
    /// 작업별 결과
    /// </summary>
    public List<TaskResult> TaskResults { get; set; } = new();

    /// <summary>
    /// 오류 메시지 (있을 경우)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 결과 요약
    /// </summary>
    public string Summary => $"{CompletedCount}/{TotalCount} completed, {FailedCount} failed, {TotalDuration.TotalSeconds:F1}s";
}

/// <summary>
/// 개별 작업 결과
/// </summary>
public class TaskResult
{
    /// <summary>
    /// 작업 ID
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 응답 내용
    /// </summary>
    public string? Response { get; set; }

    /// <summary>
    /// 오류 메시지
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// 실행 시간
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 사용된 모델
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 토큰 사용량
    /// </summary>
    public int? TokensUsed { get; set; }
}

/// <summary>
/// 실행 진행 상황
/// </summary>
public class ExecutionProgress
{
    /// <summary>
    /// 현재 작업 인덱스
    /// </summary>
    public int CurrentIndex { get; set; }

    /// <summary>
    /// 총 작업 수
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 현재 작업 설명
    /// </summary>
    public string CurrentTask { get; set; } = string.Empty;

    /// <summary>
    /// 진행률 (0-100)
    /// </summary>
    public int ProgressPercent => TotalCount > 0 ? CurrentIndex * 100 / TotalCount : 0;

    /// <summary>
    /// 현재 상태
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 현재 사용 중인 모델
    /// </summary>
    public string? CurrentModel { get; set; }

    /// <summary>
    /// 동시 실행 중인 작업 수 (Swarm 모드)
    /// </summary>
    public int? ConcurrentTasks { get; set; }
}
