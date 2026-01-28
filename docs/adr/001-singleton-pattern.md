# ADR-001: Singleton Pattern for Agent Services

## Status
Accepted

## Date
2026-01-26

## Context

에이전트 시스템을 구현하면서 여러 서비스들의 인스턴스 관리 방식을 결정해야 했습니다.

초기 구현에서는 모든 곳에서 `new SmartRouterService()`로 직접 생성했습니다:

```csharp
// 문제: 매번 새 인스턴스 생성
var router = new SmartRouterService();  // GsdWorkflowPanel
var router = new SmartRouterService();  // AgentOrchestrator
var router = new SmartRouterService();  // EcoModeStrategy
var router = new SmartRouterService();  // 각 Agent에서도...
```

**문제점:**
1. 설정 변경 시 모든 인스턴스에 동기화 안 됨
2. 메모리 낭비 (동일한 설정을 여러 번 로드)
3. 테스트 어려움 (DI 불가)

## Decision

**앱 전역 싱글톤**과 **탭별 인스턴스**를 구분하여 관리합니다.

### 앱 전역 싱글톤 (상태 없음, 공유 가능)

| 서비스 | 이유 |
|--------|------|
| SmartRouterService | 설정 기반, 상태 없음 |
| AgentRegistry | 에이전트들은 상태 없음 |
| 각 SubAgent | 입력→출력만, 상태 없음 |

### 탭별 인스턴스 (실행 상태 있음)

| 서비스 | 이유 |
|--------|------|
| AgentOrchestrator | 실행 모드, 진행 상태가 탭마다 다름 |
| ParallelAgentService | 취소 토큰, 실행 상태가 탭별 |
| ExecutionStrategies | 진행 상황이 탭별 |

### 구현

```csharp
public class AgentServiceManager : IDisposable
{
    // 앱 전역 싱글톤
    private static readonly Lazy<AgentServiceManager> _instance =
        new(() => new AgentServiceManager());
    public static AgentServiceManager Instance => _instance.Value;

    // 탭별 인스턴스 관리
    private readonly ConcurrentDictionary<string, AgentOrchestrator> _orchestrators = new();

    public AgentOrchestrator GetOrchestrator(string tabId)
    {
        return _orchestrators.GetOrAdd(tabId, _ =>
            new AgentOrchestrator(Router));
    }
}
```

## Consequences

### Positive
- 설정 변경 시 `RefreshConfiguration()` 한 번으로 동기화
- 메모리 효율성 (에이전트 인스턴스 공유)
- 탭 간 실행 상태 독립성 보장
- 테스트 시 `Instance`를 mock으로 대체 가능

### Negative
- 싱글톤 의존성으로 단위 테스트가 약간 복잡해짐
- 앱 전역 상태에 대한 주의 필요

### Neutral
- 탭 닫힐 때 `ReleaseTab()` 호출 필요
