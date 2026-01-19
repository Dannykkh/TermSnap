# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# 빌드
dotnet build

# 실행
dotnet run --project src/Nebula Terminal/Nebula Terminal.csproj

# 릴리스 빌드
dotnet build -c Release

# 게시 (Windows 단일 파일)
dotnet publish src/Nebula Terminal/Nebula Terminal.csproj -c Release -r win-x64 --self-contained
```

## Project Vision

**"PuTTY를 더 편하게"** - AI 기반 AI 기반 터미널 도우미

### 개발 동기
1. 서버 세팅/제어가 어렵다 → Linux 명령어를 매번 검색해야 함
2. 명령어 입력이 번거롭다 → 자주 쓰는 명령어 저장 필요
3. AI 도움이 필요하다 → 자연어로 명령어 생성
4. 터미널을 통합하고 싶다 → 서버 접속 + 로컬 개발을 하나의 도구에서

### 두 가지 세션 타입
- **SSH 서버 세션**: 원격 서버 연결, AI 명령어 생성, SFTP 파일 전송
- **로컬 터미널 세션**: PowerShell, CMD, WSL, Git Bash (Warp 스타일)

## Architecture Overview

WPF (.NET 8.0) 기반, MVVM 패턴 사용.

### Core Flow: SSH 서버 세션

```
사용자 질문 "nginx 재시작해줘"
         ↓
┌─────────────────────────────────────┐
│ 1. Q&A 벡터 검색 (RAGService)        │
│    → 등록된 Q&A에서 유사 질문 검색    │
│    → 있으면 저장된 답변 반환 (토큰 절약)│
└─────────────────────────────────────┘
         ↓ (없으면)
┌─────────────────────────────────────┐
│ 2. AI API 호출 (IAIProvider)         │
│    → 자연어 → Linux 명령어 변환       │
│    → 결과를 Q&A에 저장 (재사용)       │
└─────────────────────────────────────┘
         ↓
┌─────────────────────────────────────┐
│ 3. SSH 실행 (SshService)             │
│    → 명령어 실행                      │
│    → 오류 시 ErrorHandler가 분석/재시도│
└─────────────────────────────────────┘
```

### Core Flow: 로컬 터미널 세션

```
새 탭 생성 → 세션 선택 화면 → "로컬 터미널" 선택
         ↓
┌─────────────────────────────────────┐
│ WelcomePanel (Warp 스타일)           │
│ - 폴더 열기                          │
│ - Git 저장소 복제                    │
│ - 최근 폴더 목록                     │
└─────────────────────────────────────┘
         ↓
┌─────────────────────────────────────┐
│ LocalSession                         │
│ - 선택된 쉘 실행 (PowerShell/CMD/WSL)│
│ - UTF-8 인코딩 설정                  │
│ - 작업 디렉토리 설정                  │
└─────────────────────────────────────┘
```

## Key Components

### ViewModels
- `MainViewModel` - 다중 탭 관리자, 전체 세션 관리
- `ServerSessionViewModel` - SSH 서버 세션 (AI 명령어, 채팅)
- `LocalTerminalViewModel` - 로컬 터미널 세션 (웰컴 패널, 쉘 선택)
- `NewSessionSelectorViewModel` - 새 탭 세션 타입 선택

### Services - AI & 검색
- `IAIProvider` - AI 제공자 공통 인터페이스
- `AIProviderFactory` - AI 제공자 팩토리 (Gemini, OpenAI, Claude, Grok)
- `RAGService` - Q&A 벡터 검색 (토큰 절약)
- `EmbeddingService` - 텍스트 임베딩 생성
- `QADatabaseService` - Q&A 데이터 관리

### Services - 서버 연결
- `SshService` - SSH 연결 및 명령어 실행 (.ppk, .pem 지원)
- `SftpService` - SFTP 파일 전송
- `ServerMonitorService` - 서버 리소스 모니터링

### Services - 로컬 터미널
- `LocalSession` (Core/Sessions/) - 로컬 쉘 프로세스 관리
  - PowerShell, CMD, WSL, Git Bash 지원
  - UTF-8 인코딩 설정 (`chcp 65001`, 환경 변수)

### Services - 유틸리티
- `ErrorHandler` - 오류 분석 및 자동 재시도, 위험 명령어 차단
- `ConfigService` - JSON 설정 파일 관리
- `EncryptionService` - API 키/비밀번호 암호화 (DPAPI)
- `HistoryDatabaseService` - 명령어 실행 이력

### Models
- `AppConfig` - 전체 앱 설정 (서버 프로필, AI 모델, 스니펫, Q&A)
- `ServerConfig` - 서버 연결 정보 (호스트, 인증 방식, 메모)
- `AIModelConfig` - AI 모델별 설정 및 API 키
- `QAEntry` - Q&A 항목 (질문, 답변, 임베딩)
- `CommandSnippet` - 저장된 명령어 스니펫
- `CommandHistory` - 명령어 실행 이력
- `RecentFolder` - 최근 폴더 목록

### Views (주요 창)
- `MainWindow` - 메인 창 (다중 탭 컨테이너)
- `ServerSessionView` - SSH 세션 UI
- `LocalTerminalView` - 로컬 터미널 UI
- `NewSessionSelectorView` - 세션 타입 선택 화면
- `WelcomePanel` - Warp 스타일 웰컴 화면
- `SettingsWindow` - 설정 창
- `FileTransferWindow` - SFTP 파일 전송 창
- `QAManagerWindow` - Q&A 관리 창
- `SnippetManagerWindow` - 스니펫 관리

## Development Notes

- UI 프레임워크: WPF + MaterialDesignThemes
- 설정 파일: `%APPDATA%/Nebula Terminal/config.json`
- 비밀번호/API 키는 Windows DPAPI로 암호화 저장
- 위험한 명령어 (rm -rf /, dd 등) 자동 차단 (`ErrorHandler.IsDangerousCommand`)
- SSH 키: .ppk (PuTTY), .pem (OpenSSH) 모두 지원
- 로컬 터미널: `chcp 65001` 및 환경 변수로 UTF-8 인코딩 보장
