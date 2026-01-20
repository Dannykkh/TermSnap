# TermSnap Terminal v1.0.0 - Initial Release 🎉

> **"PuTTY를 더 편하게"** - AI 기반 AI 기반 터미널 도우미

## 🌟 주요 기능

### 1. SSH 서버 관리
- 🔐 SSH 키 인증 지원 (.pem, .ppk)
- 📁 SFTP 파일 전송
- 📊 서버 모니터링 (CPU, 메모리, 디스크)
- 💾 다중 서버 프로필 관리

### 2. AI 명령어 생성
- 🤖 자연어 → Linux 명령어 변환
- 🔄 다중 AI 제공자 지원:
  - Google Gemini
  - OpenAI GPT-4
  - Anthropic Claude
  - xAI Grok
- 🔍 오류 분석 및 자동 재시도
- ⚠️ 위험한 명령어 자동 차단

### 3. Q&A 벡터 검색 (토큰 절약)
- 💡 자주 묻는 질문 자동 응답
- 🎯 임베딩 기반 유사도 검색
- 💰 API 토큰 사용량 최소화

### 4. 로컬 터미널 (Warp 스타일)
- 🖥️ 다중 쉘 지원:
  - PowerShell
  - CMD
  - WSL (Windows Subsystem for Linux)
  - Git Bash
- 📂 폴더 열기, Git Clone
- 📋 최근 폴더 목록

### 5. AI CLI 통합
- ⚡ 원클릭 실행:
  - **Claude Code** - Anthropic AI 코딩 어시스턴트
  - **Codex CLI** - OpenAI 코드 생성
  - **Gemini CLI** - Google AI
  - **Aider** - AI 페어 프로그래밍
- 🔧 자동 설치 감지
- ⚙️ 자동 모드 플래그 지원
- 🎛️ 커스텀 CLI 추가 가능

### 6. 추가 기능
- 📝 명령어 스니펫 저장 및 관리
- 📊 명령어 실행 이력
- 🌿 Git 브랜치 자동 표시
- 🎨 다크/라이트 테마
- 🔒 DPAPI 암호화 (API 키, 비밀번호)

## 📋 요구사항

### 필수
- **OS**: Windows 10/11 (64-bit)
- **.NET Runtime**: .NET 8.0 이상
- **AI API Key**: Gemini, OpenAI, Claude, 또는 Grok 중 최소 하나

### 선택 (AI CLI 사용 시)
- **Node.js**: 18+ (Claude Code, Codex, Gemini CLI)
- **Python**: 3.9+ (Aider)

## 🚀 빠른 시작

### 1. 설치
1. 아래 설치 파일 다운로드
2. 설치 마법사 실행
3. 프로그램 실행

### 2. AI API 키 설정
1. 설정 ⚙️ → AI 모델
2. API 키 입력:
   - [Gemini API](https://ai.google.dev/) (무료 티어 있음, 추천)
   - [OpenAI API](https://platform.openai.com/api-keys)
   - [Anthropic API](https://console.anthropic.com/)
   - [xAI Grok API](https://x.ai/)

### 3. 첫 서버 연결 (SSH 세션)
1. "새 탭" (+) → "SSH 서버" 선택
2. 서버 정보 입력
3. 연결 → AI에게 명령어 요청!
   - 예: "nginx 상태 확인해줘"
   - 예: "디스크 사용량 보여줘"

### 4. 로컬 터미널 사용
1. "새 탭" (+) → "로컬 터미널" 선택
2. PowerShell/CMD/WSL/Git Bash 선택
3. 폴더 열기 → AI CLI 실행

## 📦 다운로드

아래 Assets에서 다운로드:
- **Nebula Terminal-Setup-v1.0.0.exe** (약 58 MB)

## 🔧 소스코드 빌드

```bash
git clone https://github.com/Dannykkh/nebula-terminal.git
cd nebula-terminal
dotnet restore
dotnet build
dotnet run --project src/Nebula Terminal/Nebula Terminal.csproj
```

## 📖 문서

- [README](https://github.com/Dannykkh/nebula-terminal#readme)
- [기여 가이드](https://github.com/Dannykkh/nebula-terminal/blob/master/CONTRIBUTING.md)
- [설치 파일 빌드 가이드](https://github.com/Dannykkh/nebula-terminal/blob/master/BUILD_INSTALLER_README.md)

## 🐛 알려진 이슈

현재 알려진 큰 이슈는 없습니다.

버그를 발견하셨다면 [Issues](https://github.com/Dannykkh/nebula-terminal/issues)에 보고해주세요!

## 🙏 감사의 말

이 프로젝트는 다음 오픈소스 라이브러리를 사용합니다:
- [SSH.NET](https://github.com/sshnet/SSH.NET) - SSH/SFTP
- [Material Design In XAML](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) - UI
- [sentence-transformers](https://www.sbert.net/) - 임베딩

그리고 AI 제공자들:
- Google Gemini
- OpenAI
- Anthropic Claude
- xAI Grok

## 📝 체인지로그

### [1.0.0] - 2025-01-18

#### Added
- 초기 릴리즈
- SSH 서버 연결 및 관리
- AI 명령어 생성 (다중 제공자)
- Q&A 벡터 검색 시스템
- 로컬 터미널 (PowerShell, CMD, WSL, Git Bash)
- AI CLI 통합 (Claude Code, Codex, Gemini CLI, Aider)
- 명령어 스니펫 관리
- Git 브랜치 표시
- 다크/라이트 테마
- 명령어 실행 이력
- SFTP 파일 전송
- 서버 모니터링

## 📬 지원 및 문의

- 🐛 버그 리포트: [Issues](https://github.com/Dannykkh/nebula-terminal/issues)
- 💡 기능 제안: [Issues](https://github.com/Dannykkh/nebula-terminal/issues)
- 💬 토론: [Discussions](https://github.com/Dannykkh/nebula-terminal/discussions)

---

⭐ **이 프로젝트가 도움이 되셨다면 Star를 눌러주세요!**

MIT License © 2025
