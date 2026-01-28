# Changelog

All notable changes to TermSnap will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

---

## [1.6.0] - 2026-01-28

### Added
- **Agent System**: oh-my-claudecode 스타일의 에이전트 시스템
  - `SmartRouterService`: 복잡도 분석 및 최적 모델 선택
  - 5가지 실행 모드: Interactive, Eco, Pipeline, Swarm, Expert
  - 5개 전문 에이전트: Oracle, Librarian, Frontend, Backend, DevOps
  - `AgentOrchestrator`: 에이전트 선택 및 협업 관리
  - `ParallelAgentService`: 병렬 실행 (SemaphoreSlim 기반)
- **Model Tier System**: Fast/Balanced/Powerful 티어로 모델 분류
- **GSD Workflow Mode Selection**: Execute 단계에서 실행 모드 선택 UI
- **AI Model Icon Display**: 인터랙티브 모드에서 현재 AI 모델 로고 표시 (Claude, OpenAI, Gemini)
- **Terminal Scrollbar**: 터미널 컨트롤에 스크롤바 추가로 탐색 편의성 향상
- **Memory Panel**: 장기기억 관리 패널
- **Ralph Loop Panel**: 자동 작업 반복 실행 패널
- **Sub-Process Manager**: 서브 프로세스 추적 및 관리

### Changed
- `AIModelConfig`에 `Tier` 필드 추가
- `GsdWorkflowPanel`에 Mode 선택 ComboBox 추가
- `AgentServiceManager`로 싱글톤 관리 중앙화
- 로컬 터미널 UI 개선: 경로 표시 영역에 AI 모델 아이콘 통합

---

## [1.5.0] - 2026-01-25

### Added
- IDE 스타일 파일 에디터 (AvalonEdit)
- Ollama 로컬 LLM 지원

## [1.4.0] - 2025-XX-XX

### Added
- GSD Workflow 패널
- Ralph Loop (자동 실행)
- 다국어 지원 (한국어, 영어)

## [1.3.0] - 2025-XX-XX

### Added
- 로컬 터미널 세션 (PowerShell, CMD, WSL, Git Bash)
- Warp 스타일 웰컴 패널
- 최근 폴더 기록

## [1.2.0] - 2025-XX-XX

### Added
- Q&A 벡터 검색 (RAG)
- 명령어 스니펫 관리
- 서버 모니터링

## [1.1.0] - 2025-XX-XX

### Added
- AI 제공자 선택 (Gemini, OpenAI, Claude, Grok)
- SFTP 파일 전송
- 테마 지원 (Light/Dark)

## [1.0.0] - 2025-XX-XX

### Added
- 초기 릴리스
- SSH 서버 연결
- AI 명령어 생성
- .ppk, .pem 키 지원
