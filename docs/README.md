# TermSnap Documentation

## Overview

TermSnap 개발자 문서입니다.

## Contents

### Architecture
- [Architecture Overview](architecture.md) - 전체 아키텍처
- [Agent System](agent-system.md) - 에이전트 시스템 상세

### Architecture Decision Records (ADR)
- [ADR-001: Singleton Pattern](adr/001-singleton-pattern.md) - 싱글톤 패턴 적용
- [ADR-002: Tab-based Orchestrator](adr/002-tab-based-orchestrator.md) - 탭별 인스턴스 관리
- [ADR-003: Execution Strategy Pattern](adr/003-execution-strategy-pattern.md) - 실행 전략 패턴

### Development Log
- [2026-01-26: Agent System](dev-log/2026-01-26-agent-system.md) - 에이전트 시스템 구현

## Quick Links

| Topic | Description |
|-------|-------------|
| [README.md](../README.md) | 사용자 가이드 |
| [CHANGELOG.md](../CHANGELOG.md) | 버전별 변경 사항 |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | 기여 가이드 |

## Document Structure

```
docs/
├── README.md              # 이 파일
├── architecture.md        # 전체 아키텍처
├── agent-system.md        # 에이전트 시스템 상세
├── adr/                   # Architecture Decision Records
│   ├── 001-singleton-pattern.md
│   ├── 002-tab-based-orchestrator.md
│   └── 003-execution-strategy-pattern.md
└── dev-log/               # 개발 일지
    └── 2026-01-26-agent-system.md
```

## Writing Guidelines

### ADR Format
```markdown
# ADR-XXX: Title

## Status
Accepted | Proposed | Deprecated | Superseded

## Date
YYYY-MM-DD

## Context
왜 이 결정이 필요했는지

## Decision
무엇을 결정했는지

## Consequences
결정의 영향 (Positive, Negative, Neutral)
```

### Dev Log Format
```markdown
# Development Log: YYYY-MM-DD

## Topic Name

### Overview
작업 개요

### Implemented Features
구현한 기능 목록

### Files Created/Modified
변경된 파일 목록

### Next Steps
다음 단계
```
