# Agent System Documentation

## Overview

TermSnap Agent System은 oh-my-claudecode/oh-my-opencode에서 영감을 받아 구현된 AI 에이전트 시스템입니다.

## Components

### 1. SmartRouterService

복잡도 분석 및 최적 모델 선택을 담당합니다.

```csharp
// 사용 예시
var router = AgentServiceManager.Instance.Router;

// 복잡도 분석
var complexity = router.AnalyzeComplexity("nginx 설정 최적화해줘");
// → TaskComplexity.Medium

// 최적 Provider 선택
var provider = router.SelectProvider("아키텍처 리뷰해줘");
// → Powerful tier model
```

**복잡도 판단 기준:**

| Complexity | Keywords | Recommended Tier |
|------------|----------|------------------|
| Simple | format, list, show, convert | Fast |
| Medium | implement, fix, bug, test | Balanced |
| Complex | architecture, security, optimize | Powerful |

### 2. AgentOrchestrator

에이전트 선택 및 협업을 관리합니다.

```csharp
// 탭별 오케스트레이터 가져오기
var orchestrator = AgentServiceManager.Instance.GetOrchestrator(tabId);

// 실행 모드 설정
orchestrator.CurrentMode = ExecutionMode.Swarm;

// 자동 에이전트 선택 후 처리
var response = await orchestrator.ProcessAutoAsync(
    "React 컴포넌트 최적화해줘",
    context
);
// → FrontendEngineerAgent가 자동 선택됨

// 특정 에이전트 지정
var response = await orchestrator.ProcessWithAgentAsync(
    AgentRole.Oracle,
    "마이크로서비스 전환 전략 조언해줘",
    context
);
```

### 3. Specialized Agents

| Agent | Role | Tier | Specialization |
|-------|------|------|----------------|
| OracleAgent | 전략/아키텍처 | Powerful | 설계 결정, 리팩토링 전략 |
| LibrarianAgent | 문서/레퍼런스 | Fast | API 문서, 사용법 안내 |
| FrontendEngineerAgent | UI/UX | Balanced | React, CSS, 컴포넌트 |
| BackendEngineerAgent | API/DB | Balanced | REST API, 데이터베이스 |
| DevOpsAgent | 인프라/배포 | Balanced | Docker, CI/CD, 클라우드 |

### 4. Execution Strategies

```csharp
// EcoMode - 비용 절감
// Complex 작업도 Balanced 모델 사용, 나머지는 Fast
var ecoStrategy = new EcoModeStrategy(router);

// Pipeline - 순차 실행, 결과 전달
// 이전 단계 결과가 다음 단계 컨텍스트에 추가됨
var pipelineStrategy = new PipelineStrategy(router);

// Swarm - 병렬 실행
// SemaphoreSlim으로 동시 실행 수 제한 (기본 3개)
var swarmStrategy = new SwarmStrategy(maxConcurrency: 3, router);

// Expert - 전문 에이전트 자동 선택
// 작업 내용 분석 → 적절한 에이전트 선택
var expertStrategy = new ExpertStrategy(router);
```

## Usage Examples

### Basic Usage

```csharp
// 1. 서비스 매니저에서 오케스트레이터 가져오기
var orchestrator = AgentServiceManager.Instance.GetOrchestrator("tab-123");

// 2. 컨텍스트 설정
var context = new AgentContext
{
    WorkingDirectory = @"C:\MyProject",
    ProjectContext = "React + TypeScript 프로젝트",
    RelevantFiles = new List<string> { "src/App.tsx", "src/components/" }
};

// 3. 작업 처리
var response = await orchestrator.ProcessAutoAsync(
    "버튼 컴포넌트에 로딩 상태 추가해줘",
    context
);

Console.WriteLine(response.Content);
Console.WriteLine($"Model: {response.Model}"); // Sonnet (Frontend)
```

### Parallel Execution

```csharp
var tasks = new List<AgentTask>
{
    new() { Description = "README.md 업데이트" },
    new() { Description = "CHANGELOG.md 작성" },
    new() { Description = "API 문서 생성" }
};

orchestrator.CurrentMode = ExecutionMode.Swarm;
var result = await orchestrator.ExecuteTasksAsync(tasks, context);

Console.WriteLine(result.Summary);
// "3/3 completed, 0 failed, 5.2s"
```

### Pipeline Execution

```csharp
var tasks = new List<AgentTask>
{
    new() { Description = "현재 코드 분석" },
    new() { Description = "리팩토링 계획 수립" },
    new() { Description = "리팩토링 실행" }
};

orchestrator.CurrentMode = ExecutionMode.Pipeline;
var result = await orchestrator.ExecuteTasksAsync(tasks, context);
// 각 단계 결과가 다음 단계로 전달됨
```

## Configuration

### Model Tier 설정

`%APPDATA%/TermSnap/config.json`:

```json
{
  "AIModels": [
    {
      "Provider": "Claude",
      "ModelId": "claude-3-5-haiku-20241022",
      "Tier": "Fast"
    },
    {
      "Provider": "Claude",
      "ModelId": "claude-sonnet-4-20250514",
      "Tier": "Balanced"
    },
    {
      "Provider": "Claude",
      "ModelId": "claude-opus-4-5-20251101",
      "Tier": "Powerful"
    }
  ]
}
```

### Tier 우선순위

설정된 티어가 없을 경우 인접 티어에서 대체:

- Fast 없음 → Balanced → Powerful
- Powerful 없음 → Balanced → Fast

## Events

```csharp
// 진행 상황 모니터링
orchestrator.ProgressChanged += (progress) =>
{
    Console.WriteLine($"[{progress.CurrentIndex}/{progress.TotalCount}] {progress.CurrentTask}");
};

// 작업 완료 알림
orchestrator.TaskCompleted += (task, response) =>
{
    Console.WriteLine($"Completed: {task.Description}");
    Console.WriteLine($"Model: {response.Model}");
};

// 에이전트 선택 알림
orchestrator.AgentSelected += (role, name) =>
{
    Console.WriteLine($"Selected: {name} ({role})");
};
```

## Cleanup

```csharp
// 탭 닫힐 때 리소스 해제
AgentServiceManager.Instance.ReleaseTab("tab-123");

// 앱 종료 시
AgentServiceManager.Instance.Dispose();
```
