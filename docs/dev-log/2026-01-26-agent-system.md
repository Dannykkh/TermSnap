# Development Log: 2026-01-26

## Agent System Implementation

### Overview
oh-my-claudecode/oh-my-opencode의 핵심 기능을 TermSnap에 통합하는 작업 수행.

### Implemented Features

#### 1. Smart Model Routing
- `SmartRouterService` 구현
- 키워드 기반 복잡도 분석 (Simple/Medium/Complex)
- 복잡도 → 티어 → Provider 자동 선택
- `AIModelConfig`에 `Tier` 필드 추가

#### 2. Execution Modes
- `ExecutionMode` enum 확장 (Interactive, Yolo, EcoMode, Pipeline, Swarm, Expert)
- `IExecutionStrategy` 인터페이스 정의
- 4가지 전략 구현:
  - `EcoModeStrategy` - 저비용 모델 우선
  - `PipelineStrategy` - 순차 실행, 결과 전달
  - `SwarmStrategy` - 병렬 실행 (SemaphoreSlim)
  - `ExpertStrategy` - 전문 에이전트 자동 선택

#### 3. Specialized Agents
- `ISubAgent` 인터페이스 + `SubAgentBase` 추상 클래스
- 5개 전문 에이전트:
  - OracleAgent (전략/아키텍처)
  - LibrarianAgent (문서/레퍼런스)
  - FrontendEngineerAgent (UI/React)
  - BackendEngineerAgent (API/DB)
  - DevOpsAgent (인프라/배포)

#### 4. Agent Orchestrator
- `AgentOrchestrator` - 에이전트 선택 및 협업
- `ParallelAgentService` - 병렬 실행 관리 (의존성 그래프 지원)

#### 5. UI Integration
- `GsdWorkflowPanel.xaml` - Mode 선택 ComboBox 추가
- Mode 변경 시 설명 텍스트 업데이트

### Architecture Decisions

#### 싱글톤 패턴 적용 (리팩토링)
초기 구현에서 발견된 문제:
```csharp
// 문제: 모든 곳에서 new SmartRouterService()
var router = new SmartRouterService();  // 8곳에서 중복 생성
```

해결:
```csharp
// AgentServiceManager로 중앙 관리
public class AgentServiceManager
{
    public static AgentServiceManager Instance => _instance.Value;

    // 앱 전역 싱글톤
    public SmartRouterService Router => _router.Value;
    public AgentRegistry Agents => _agentRegistry.Value;

    // 탭별 인스턴스
    public AgentOrchestrator GetOrchestrator(string tabId);
}
```

구분 기준:
- **앱 전역**: 설정 기반, 상태 없음 (Router, Agents)
- **탭별**: 실행 상태 있음 (Orchestrator, ParallelService)

### Files Created (15)

| File | Lines | Description |
|------|-------|-------------|
| Models/ModelTier.cs | 48 | ModelTier, TaskComplexity enum |
| Models/AgentTask.cs | 197 | AgentTask, AgentResponse, AgentContext |
| Models/AgentRole.cs | 96 | AgentRole enum + AgentRoleInfo |
| Services/SmartRouterService.cs | 188 | 복잡도 분석, 모델 선택 |
| Services/ParallelAgentService.cs | 280 | 병렬 실행 관리 |
| Services/AgentOrchestrator.cs | 310 | 에이전트 선택/협업 |
| Services/AgentServiceManager.cs | 165 | 싱글톤 관리 |
| Services/Agents/ISubAgent.cs | 95 | 서브에이전트 인터페이스 |
| Services/Agents/OracleAgent.cs | 95 | 전략/아키텍처 에이전트 |
| Services/Agents/LibrarianAgent.cs | 90 | 문서 검색 에이전트 |
| Services/Agents/FrontendEngineerAgent.cs | 105 | UI/React 에이전트 |
| Services/Agents/BackendEngineerAgent.cs | 100 | API/DB 에이전트 |
| Services/Agents/DevOpsAgent.cs | 100 | 인프라/배포 에이전트 |
| Services/ExecutionStrategies/IExecutionStrategy.cs | 140 | 실행 전략 인터페이스 |
| Services/ExecutionStrategies/*.cs | 4 files | 각 전략 구현 |

### Files Modified (6)

| File | Changes |
|------|---------|
| Models/AIModelConfig.cs | `Tier` 필드 추가 |
| Models/GsdProject.cs | `ExecutionMode` enum 확장 |
| Services/AIProviderFactory.cs | `GetProviderByTier()` 추가 |
| Services/GsdWorkflowService.cs | 모드별 실행 로직 |
| Views/GsdWorkflowPanel.xaml | Mode 선택 UI |
| Views/GsdWorkflowPanel.xaml.cs | 핸들러 + 싱글톤 적용 |

### Build Result
```
빌드했습니다.
    경고 0개 (새 코드)
    오류 0개
```

### Next Steps
- [ ] 실제 사용 테스트
- [ ] 토큰 사용량 트래킹
- [ ] 에이전트 응답 캐싱 검토
- [ ] 추가 에이전트 (SecurityExpert, TestEngineer) 구현
