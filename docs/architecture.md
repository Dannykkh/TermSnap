# TermSnap Architecture

## Overview

TermSnap은 WPF (.NET 8.0) 기반의 AI 터미널 도우미입니다.

```
┌─────────────────────────────────────────────────────────────────┐
│                        MainWindow (TabControl)                   │
├─────────────────────────────────────────────────────────────────┤
│  Tab 1: SSH Session    │  Tab 2: Local Terminal  │  Tab 3: ... │
│  ┌───────────────────┐ │ ┌────────────────────┐  │             │
│  │ServerSessionView  │ │ │LocalTerminalView   │  │             │
│  │                   │ │ │                    │  │             │
│  │ + GsdWorkflowPanel│ │ │+ GsdWorkflowPanel  │  │             │
│  └───────────────────┘ │ └────────────────────┘  │             │
└─────────────────────────────────────────────────────────────────┘
```

## Layer Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Presentation Layer                        │
│  Views: MainWindow, ServerSessionView, LocalTerminalView        │
│  ViewModels: MainViewModel, ServerSessionViewModel              │
└──────────────────────────────┬──────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────┐
│                        Service Layer                             │
│  AI: AIProviderFactory, IAIProvider implementations             │
│  Agent: AgentOrchestrator, SubAgents, ExecutionStrategies       │
│  Core: SshService, SftpService, ConfigService                   │
└──────────────────────────────┬──────────────────────────────────┘
                               │
┌──────────────────────────────▼──────────────────────────────────┐
│                        Data Layer                                │
│  Models: AppConfig, ServerConfig, AIModelConfig, AgentTask      │
│  Storage: JSON files (%APPDATA%/TermSnap/)                      │
└─────────────────────────────────────────────────────────────────┘
```

## Agent System Architecture (v1.6.0+)

### Singleton Pattern

```
┌─────────────────────────────────────────────────────────────────┐
│                 AgentServiceManager.Instance                     │
│                    (앱 전역 싱글톤)                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────┐   ┌────────────────────────────────┐  │
│  │  SmartRouterService │   │     AgentRegistry              │  │
│  │  (앱 전역 싱글톤)    │   │    (앱 전역 싱글톤)             │  │
│  │                     │   │                                │  │
│  │  • 설정 읽기        │   │  • OracleAgent         (1개)  │  │
│  │  • 복잡도 분석      │   │  • LibrarianAgent      (1개)  │  │
│  │  • 모델 선택        │   │  • FrontendEngineerAgent(1개) │  │
│  └─────────────────────┘   │  • BackendEngineerAgent (1개) │  │
│                            │  • DevOpsAgent         (1개)  │  │
│                            └────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│                     탭별 인스턴스 관리                            │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  ConcurrentDictionary<tabId, AgentOrchestrator>          │  │
│  │  ConcurrentDictionary<tabId, ParallelAgentService>       │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Execution Modes

| Mode | Description | Use Case |
|------|-------------|----------|
| Interactive | 단계별 사용자 확인 | 기본값, 안전한 실행 |
| Eco | Fast 모델 우선 | 비용 절감 |
| Pipeline | 순차 실행 (결과 전달) | 의존성 있는 작업 |
| Swarm | 병렬 실행 (3개 동시) | 독립적인 작업들 |
| Expert | 전문 에이전트 자동 선택 | 다양한 분야 작업 |

### Smart Routing

```
사용자 입력
     │
     ▼
┌─────────────────────┐
│  복잡도 분석        │  키워드 기반 분석
│  (SmartRouter)      │  • Simple: format, list, show...
└──────────┬──────────┘  • Medium: implement, fix, bug...
           │             • Complex: architecture, security...
           ▼
┌─────────────────────┐
│  티어 선택          │  Simple → Fast (Haiku, GPT-4o-mini)
│                     │  Medium → Balanced (Sonnet, GPT-4o)
│                     │  Complex → Powerful (Opus)
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  Provider 선택      │  설정된 모델 중 해당 티어 선택
│  (AIProviderFactory)│
└─────────────────────┘
```

## Key Design Decisions

- [ADR-001: Singleton Pattern for Agent Services](adr/001-singleton-pattern.md)
- [ADR-002: Tab-based Orchestrator Instances](adr/002-tab-based-orchestrator.md)
- [ADR-003: Strategy Pattern for Execution Modes](adr/003-execution-strategy-pattern.md)

## File Structure

```
src/TermSnap/
├── Controls/           # Custom WPF controls
├── Core/               # Core abstractions
│   └── Sessions/       # Session management
├── Models/             # Data models
│   ├── AgentTask.cs
│   ├── AgentRole.cs
│   ├── ModelTier.cs
│   └── ...
├── Services/           # Business logic
│   ├── Agents/         # Specialized agents
│   │   ├── ISubAgent.cs
│   │   ├── OracleAgent.cs
│   │   └── ...
│   ├── ExecutionStrategies/
│   │   ├── IExecutionStrategy.cs
│   │   ├── EcoModeStrategy.cs
│   │   └── ...
│   ├── AgentOrchestrator.cs
│   ├── AgentServiceManager.cs
│   ├── SmartRouterService.cs
│   └── ...
├── ViewModels/         # MVVM ViewModels
├── Views/              # WPF Views
└── Resources/          # Localization, themes
```
