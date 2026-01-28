# ADR-003: Strategy Pattern for Execution Modes

## Status
Accepted

## Date
2026-01-26

## Context

GSD 워크플로우 Execute 단계에서 여러 실행 모드를 지원해야 합니다:

- Interactive: 단계별 확인
- EcoMode: 비용 절감 모드
- Pipeline: 순차 실행 (결과 전달)
- Swarm: 병렬 실행
- Expert: 전문 에이전트 자동 선택

각 모드는 작업 실행 방식이 다르지만, 공통 인터페이스를 통해 교체 가능해야 합니다.

## Decision

**Strategy Pattern**을 사용하여 실행 모드를 구현합니다.

### Interface

```csharp
public interface IExecutionStrategy
{
    string Name { get; }
    string Description { get; }
    ExecutionMode Mode { get; }

    Task<ExecutionResult> ExecuteAsync(
        IEnumerable<AgentTask> tasks,
        AgentContext context,
        CancellationToken cancellationToken = default);

    event Action<ExecutionProgress>? ProgressChanged;
    event Action<AgentTask, AgentResponse>? TaskCompleted;
}
```

### Implementations

| Strategy | Key Behavior |
|----------|--------------|
| EcoModeStrategy | Complex 작업도 Balanced 사용, 나머지 Fast |
| PipelineStrategy | 이전 결과를 StringBuilder에 누적하여 전달 |
| SwarmStrategy | SemaphoreSlim(3)으로 동시 실행 제한 |
| ExpertStrategy | 키워드 분석 → 에이전트 역할 자동 선택 |

### AgentOrchestrator에서의 사용

```csharp
private readonly Dictionary<ExecutionMode, IExecutionStrategy> _strategies = new()
{
    [ExecutionMode.EcoMode] = new EcoModeStrategy(_router),
    [ExecutionMode.Pipeline] = new PipelineStrategy(_router),
    [ExecutionMode.Swarm] = new SwarmStrategy(3, _router),
    [ExecutionMode.Expert] = new ExpertStrategy(_router)
};

public async Task<ExecutionResult> ExecuteTasksAsync(...)
{
    if (_strategies.TryGetValue(_currentMode, out var strategy))
    {
        return await strategy.ExecuteAsync(tasks, context, cancellationToken);
    }
    // fallback to sequential
}
```

## Alternatives Considered

### 1. Switch 문으로 분기
```csharp
switch (mode)
{
    case ExecutionMode.EcoMode:
        // 긴 코드...
    case ExecutionMode.Swarm:
        // 긴 코드...
}
```
- 단점: 코드 비대화, 테스트 어려움

### 2. Template Method Pattern
- 단점: 상속 기반으로 유연성 떨어짐

### 3. Strategy Pattern (채택)
- 장점: 전략 교체 용이, 테스트 용이, 확장 용이

## Consequences

### Positive
- 새 모드 추가 시 새 Strategy 클래스만 구현
- 각 전략 독립적으로 테스트 가능
- 런타임에 모드 변경 용이

### Negative
- 클래스 수 증가 (5개 전략)
- 공통 로직 중복 가능성 → SubAgentBase로 해결

### Extension Example

```csharp
// 새로운 실행 모드 추가 시
public class DebugStrategy : IExecutionStrategy
{
    public ExecutionMode Mode => ExecutionMode.Debug;

    public async Task<ExecutionResult> ExecuteAsync(...)
    {
        // 모든 작업을 로깅하며 실행
    }
}

// AgentOrchestrator에 등록
_strategies[ExecutionMode.Debug] = new DebugStrategy(_router);
```
