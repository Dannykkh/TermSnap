# TermSnap - AI 터미널 도우미

> Windows용 최고의 AI 기반 SSH 클라이언트 + 로컬 터미널. 서버 관리가 처음이어도 자연어로 Linux 명령어를 생성하고, Claude Code와 같은 AI 코딩 도구를 한 번의 클릭으로 실행하세요.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)
![License](https://img.shields.io/badge/License-MIT-green)

한국어 | **[English](README.md)**

---

## 이런 분들을 위한 도구입니다

- **Linux 명령어가 어렵다면** - "nginx 재시작해줘"라고 입력하면 AI가 `sudo systemctl restart nginx` 명령어를 생성합니다
- **서버 관리를 자주 한다면** - SSH 연결, 파일 전송, 명령어 저장을 한 곳에서
- **AI 코딩 도구를 사용한다면** - Claude Code, Aider, Gemini CLI를 원클릭으로 실행
- **여러 서버를 관리한다면** - 탭으로 SSH와 로컬 터미널을 동시에

---

## 핵심 기능

### AI 명령어 생성
자연어를 Linux 명령어로 변환합니다. Gemini, OpenAI, Claude, Grok 중 선택하세요.

```
입력: "디스크 80% 넘는 파티션 찾아줘"
출력: df -h | awk '$5 > 80 {print}'
```

### 초고속 SFTP 전송
- 단일 파일: **100-150 MB/s** (일반 대비 3-5배)
- 병렬 전송: **400 MB/s** (4개 동시)
- FileZilla보다 2.5배 빠름

### AI CLI 원클릭 실행
| 도구 | 설명 |
|------|------|
| **Claude Code** | Anthropic의 AI 코딩 어시스턴트 |
| **Codex CLI** | OpenAI의 코드 생성기 |
| **Gemini CLI** | Google의 AI CLI |
| **Aider** | 오픈소스 AI 페어 프로그래밍 |

설치 여부 자동 감지, 미설치 시 원클릭 설치 버튼 제공

### AI 도구 패널
AI 기반 개발을 위한 통합 패널입니다.
- **기억 탭**: 장기 기억(MEMORY.md), 대화 로그, 통합 검색
- **오케스트레이터 탭**: 멀티 에이전트 PM 모드, 태스크 진행 추적
- **스킬 탭**: 프로젝트 기반 리소스 추천 (스킬, 에이전트, 훅, MCP)

자세한 내용: [AI 워크플로우 가이드](docs/AI_WORKFLOW_GUIDE.ko.md)

---

## 설치하기

### 요구 사항
- Windows 10/11 (64-bit)
- .NET 8.0 Runtime
- AI API 키 (Gemini 무료 추천)

### 방법 1: 설치 파일 (권장)

1. [Releases](https://github.com/Dannykkh/TermSnap/releases)에서 최신 버전 다운로드
2. 설치 마법사 실행
3. 프로그램 실행 후 설정에서 API 키 입력

### 방법 2: 소스 빌드

```bash
git clone https://github.com/Dannykkh/TermSnap.git
cd TermSnap
dotnet build
dotnet run --project src/TermSnap/TermSnap.csproj
```

---

## 5분 만에 시작하기

### 1단계: API 키 설정
- 설정(⚙️) → AI 모델 → API 키 입력
- [Gemini API 키 무료 발급](https://ai.google.dev/) (추천)

### 2단계: SSH 서버 연결
- 새 탭(+) → SSH 서버 선택
- 서버 정보 입력 (호스트, 사용자명, 비밀번호/SSH 키)
- AI에게 "메모리 사용량 확인해줘" 요청

### 3단계: 로컬 터미널 (선택)
- 새 탭(+) → 로컬 터미널 선택
- 쉘 선택 (PowerShell, CMD, WSL, Git Bash)
- AI CLI 체크 후 실행

---

## 주요 기능 상세

### SSH 서버 관리
- 다중 서버 프로필 저장
- SSH 키 인증 (.pem, .ppk 지원)
- 서버 모니터링 (CPU, 메모리, 디스크)
- AI 오류 분석 및 자동 재시도

### IDE 스타일 파일 편집기
- 뷰어/편집 모드 분리 (실수 방지)
- 20개 이상 언어 구문 강조
- 코드 접기, Undo/Redo
- UTF-8, EUC-KR 등 인코딩 지원

### Q&A 벡터 검색
자주 묻는 질문을 저장해 API 토큰을 절약하세요.
```
"nginx 재시작" → 저장된 답변 즉시 반환 (API 호출 없음)
```

### 명령어 스니펫
자주 쓰는 명령어를 저장하고 빠르게 실행하세요.
- 카테고리/태그 분류
- 검색 기능
- 11개 기본 제공

---

## 단축키

| 단축키 | 기능 |
|--------|------|
| `Ctrl+L` | 새 로컬 터미널 |
| `Ctrl+T` | 새 SSH 연결 |
| `Ctrl+Tab` | 다음 탭 |
| `Ctrl+W` | 탭 닫기 |
| `Ctrl+S` | 파일 저장 |

---

## 자주 묻는 질문

**Q: API 키 없이 사용할 수 있나요?**
A: SSH 연결, 파일 전송, 로컬 터미널은 API 키 없이 사용 가능합니다. AI 명령어 생성만 API 키가 필요합니다.

**Q: 무료 AI API가 있나요?**
A: [Gemini API](https://ai.google.dev/)가 무료 티어를 제공합니다.

**Q: Claude Code가 감지 안 돼요**
A: PowerShell에서 `claude --version` 확인 후, PATH에 npm 경로가 있는지 확인하세요.

**Q: SFTP가 왜 빠른가요?**
A: SSH.NET 버퍼 최적화(256KB)와 4개 병렬 전송으로 최대 400MB/s를 달성합니다.

더 많은 FAQ: [FAQ.md](docs/FAQ.md)

---

## 문서

- [AI 워크플로우 가이드](docs/AI_WORKFLOW_GUIDE.ko.md) - 새로운 AI 패널 사용법
- [사용자 가이드](docs/USER_GUIDE.md) - 전체 기능 설명
- [설치 가이드](docs/INSTALLATION.md) - 상세 설치 방법
- [FAQ](docs/FAQ.md) - 자주 묻는 질문

---

## 기술 스택

| 영역 | 기술 |
|------|------|
| 프레임워크 | .NET 8.0 / WPF |
| AI | Gemini, OpenAI, Claude, Grok |
| SSH/SFTP | SSH.NET |
| 에디터 | AvalonEdit |
| UI | Material Design In XAML |

---

## 기여하기

버그 리포트, 기능 제안, 코드 기여 모두 환영합니다!

1. [Issues](https://github.com/Dannykkh/TermSnap/issues)에서 버그/기능 제안
2. Fork → Branch → Commit → Pull Request

자세한 내용: [CONTRIBUTING.md](CONTRIBUTING.md)

---

## 라이선스

MIT License - 상업적 사용, 수정, 배포 모두 자유롭습니다.

---

## 지원

- [GitHub Issues](https://github.com/Dannykkh/TermSnap/issues) - 버그 리포트
- [GitHub Discussions](https://github.com/Dannykkh/TermSnap/discussions) - 질문 및 토론

---

**이 프로젝트가 도움이 되셨다면 ⭐ Star를 눌러주세요!**
