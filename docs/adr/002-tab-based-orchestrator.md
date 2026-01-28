# ADR-002: Tab-based Orchestrator Instances

## Status
Accepted

## Date
2026-01-26

## Context

TermSnap은 다중 탭 인터페이스를 지원합니다. 각 탭에서 독립적으로 GSD 워크플로우를 실행할 수 있어야 합니다.

**고려 사항:**
1. 탭 A에서 Swarm 모드로 실행 중일 때, 탭 B는 Interactive 모드일 수 있음
2. 탭 A에서 실행 취소해도 탭 B는 영향 받으면 안 됨
3. 탭별 진행 상황 표시가 독립적이어야 함

## Decision

`AgentOrchestrator`와 `ParallelAgentService`는 **탭별 인스턴스**로 관리합니다.

```csharp
// 탭 ID 기반으로 인스턴스 관리
private readonly ConcurrentDictionary<string, AgentOrchestrator> _orchestrators = new();

public AgentOrchestrator GetOrchestrator(string tabId)
{
    return _orchestrators.GetOrAdd(tabId, _ =>
        new AgentOrchestrator(Router));  // Router는 공유
}
```

### GsdWorkflowPanel에서의 사용

```csharp
public partial class GsdWorkflowPanel : UserControl
{
    // 탭 ID (탭별 싱글톤용)
    private string _tabId = Guid.NewGuid().ToString("N")[..8];

    // 프로퍼티로 접근 시 자동 생성/가져오기
    private AgentOrchestrator Orchestrator =>
        AgentServiceManager.Instance.GetOrchestrator(_tabId);

    // 외부에서 탭 ID 설정 가능 (MainViewModel에서)
    public string TabId
    {
        get => _tabId;
        set => _tabId = value;
    }
}
```

## Alternatives Considered

### 1. 모든 인스턴스 탭별
- 장점: 완전한 격리
- 단점: 메모리 낭비, 설정 동기화 어려움

### 2. 모두 싱글톤
- 장점: 간단함
- 단점: 탭 간 상태 충돌

### 3. 선택적 공유 (채택)
- 장점: 효율성 + 독립성 균형
- 단점: 관리 복잡도 증가 (수용 가능)

## Consequences

### Positive
- 탭별 독립적인 실행 모드/상태
- 공유 가능한 서비스는 효율적으로 재사용
- 탭 닫힘 시 해당 리소스만 정리

### Negative
- 탭 ID 관리 필요
- `ReleaseTab()` 호출 잊으면 메모리 누수 가능

### Implementation Notes

```csharp
// 탭 닫힐 때 반드시 호출
public void ReleaseTab(string tabId)
{
    if (_orchestrators.TryRemove(tabId, out var orchestrator))
    {
        orchestrator.Dispose();
    }
    if (_parallelServices.TryRemove(tabId, out var service))
    {
        service.Dispose();
    }
}
```
