# TermSnap

> **"PuTTY를 더 편하게"** - AI 기반 AI 기반 터미널 도우미

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)
![License](https://img.shields.io/badge/License-MIT-green)

## 프로젝트 개요

### 왜 만들었나?

1. **서버 세팅/제어가 어렵다** - Linux 명령어를 매번 검색해야 함
2. **명령어 입력이 번거롭다** - 자주 쓰는 명령어를 저장하고 싶음
3. **AI 도움이 필요하다** - 자연어로 명령어 생성
4. **터미널을 통합하고 싶다** - 서버 접속 + 로컬 개발을 하나의 도구에서

### 핵심 개념

```
┌───────────────────────────────────────────────────────────────────────┐
│                         TermSnap                                   │
├───────────────────────────────────────────────────────────────────────┤
│  [탭 1]  [탭 2]  [탭 3]  [+]                                            │
├───────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   [SSH 서버 세션]                    [로컬 터미널 세션]                   │
│   ┌──────────────────────┐          ┌──────────────────────┐           │
│   │ AI 명령어 생성        │          │ PowerShell / CMD     │           │
│   │ SSH 연결/실행         │          │ WSL / Git Bash       │           │
│   │ 파일 전송 (SFTP)      │          │                      │           │
│   │ 서버 모니터링         │          │ ┌──────────────────┐ │           │
│   └──────────────────────┘          │ │  AI CLI 통합      │ │           │
│                                      │ │  - Claude Code   │ │           │
│                                      │ │  - Codex CLI     │ │           │
│                                      │ │  - Gemini CLI    │ │           │
│                                      │ │  - Aider         │ │           │
│                                      │ └──────────────────┘ │           │
│                                      └──────────────────────┘           │
│                     │                           │                       │
│                     └───────────┬───────────────┘                       │
│                                 ↓                                       │
│                     ┌─────────────────────┐                             │
│                     │  공통 기능           │                             │
│                     │ - 명령어 스니펫 저장  │                             │
│                     │ - Q&A 벡터 검색      │                             │
│                     │ - 실행 이력 기록     │                             │
│                     │ - 다크/라이트 테마   │                             │
│                     └─────────────────────┘                             │
│                                                                         │
└───────────────────────────────────────────────────────────────────────┘
```

## 스크린샷

### 세션 선택 화면

![세션 선택](docs/images/session-selector.png)

새 탭을 생성할 때 다음 중 선택:
- **로컬 터미널**: PowerShell, CMD, WSL, Git Bash 등으로 로컬 작업
- **SSH 서버 연결**: 원격 Linux 서버에 SSH로 연결

**단축키**: `Ctrl+L` 로컬 터미널, `Ctrl+T` SSH 연결

### 로컬 터미널 - 웰컴 패널

![로컬 터미널 웰컴](docs/images/local-terminal-welcome.png)

웰컴 패널 기능:
- **쉘 선택**: PowerShell (기본), Windows PowerShell, CMD, Git Bash, WSL (Ubuntu, Docker)
- **AI CLI 통합**: Claude Code, Codex, Gemini CLI, Aider를 체크박스 하나로 활성화
  - 설치 여부 자동 감지
  - 미설치 시 원클릭 설치 버튼 제공
  - 자동 모드 토글 (Claude Code의 경우 `--dangerously-skip-permissions`)
- **현재 경로 표시**: 작업 디렉토리 표시
- **실행 버튼**: 선택한 쉘을 AI CLI와 함께 시작

**참고**: Claude Code CLI는 Node.js 18+ 및 `npm install -g @anthropic-ai/claude-code` 필요

### SSH 서버 연결

![SSH 연결](docs/images/ssh-connection.png)

저장된 서버에 연결하거나 새 서버 추가:
- **저장된 서버**: 자주 사용하는 서버 빠른 접속
- **새 서버 추가**: 호스트, 포트, 사용자명, 비밀번호/SSH 키 설정
- **설정**: 서버 프로필 관리 (편집, 삭제, 정리)

**기능**:
- SSH 키 인증 (.pem, .ppk 파일 지원)
- Windows DPAPI 암호화로 비밀번호 보호
- 커스텀 이름 및 메모가 포함된 서버 프로필

## 주요 기능

### 1. SSH 서버 연결 (PuTTY 대체)
- 다중 서버 프로필 관리
- SSH 키 인증 (.pem, .ppk 지원)
- **고성능 파일 전송 (SFTP)**
  - 단일 파일: 최대 **100-150 MB/s** (일반 대비 3-5배 향상)
  - 다중 파일: **병렬 전송** 지원 (4개씩 동시 전송)
  - 예상 총 속도: **400 MB/s** (FileZilla 161 MB/s 대비 2.5배)
  - 다중 파일 선택: Ctrl+클릭, Shift+클릭
- **IDE 스타일 파일 편집기**
  - 뷰어/편집 모드 분리 (실수로 파일 수정 방지)
  - 구문 강조 (C#, Python, JavaScript, JSON, XML 등)
  - Undo/Redo, 줄 번호, 인코딩 변경 (UTF-8, EUC-KR 등)
- 서버 모니터링 (CPU, 메모리, 디스크)

### 2. AI 명령어 생성
- 자연어 → Linux 명령어 변환
- 다중 AI 제공자: Gemini, OpenAI, Claude, Grok
- 오류 발생 시 AI 분석 및 자동 재시도

### 3. Q&A 벡터 검색 (토큰 절약)
```
┌─────────────────────────────────────────────────────────────┐
│  사용자 질문: "nginx 재시작 방법"                             │
│                        ↓                                     │
│  1단계: 등록된 Q&A에서 벡터 검색                              │
│         → 일치하는 답변 있으면 즉시 반환 (API 호출 없음)       │
│                        ↓                                     │
│  2단계: 일치 없으면 AI API 호출                               │
│         → 결과를 Q&A에 저장하여 재사용                        │
└─────────────────────────────────────────────────────────────┘
```
- 자주 묻는 질문/답변 미리 등록
- 임베딩 기반 유사도 검색
- API 토큰 사용량 절감

### 4. 로컬 터미널 (Warp 스타일)
- PowerShell, CMD, WSL, Git Bash 지원
- 웰컴 화면 (폴더 열기, Git Clone, 최근 폴더)
- AI CLI 도구 통합

### 5. AI CLI 통합
다양한 AI 코딩 어시스턴트 CLI를 원클릭으로 실행:

| CLI | 설명 | 자동 모드 플래그 |
|-----|------|-----------------|
| **Claude Code** | Anthropic의 AI 코딩 어시스턴트 | `--dangerously-skip-permissions` |
| **Codex CLI** | OpenAI의 코드 생성 CLI | `--full-auto` |
| **Gemini CLI** | Google의 Gemini AI CLI | `-y` |
| **Aider** | 오픈소스 AI 페어 프로그래밍 | `--yes` |
| **커스텀** | 사용자 정의 AI CLI | 직접 설정 |

주요 기능:
- 설치 상태 자동 감지 (설치됨/설치 안됨)
- 원클릭 설치 버튼 (터미널에서 설치 명령 실행)
- CLI별 옵션 설정 (Claude: `--print`, `--verbose`, `--resume` 등)
- 마지막 선택 저장 (다음 실행 시 자동 복원)
- 초기 프롬프트 입력 가능

#### Claude 오케스트레이션 (Claude 전용)
MCP(Model Context Protocol) 기반 멀티 에이전트 실행 기능:

```
┌─────────────────────────────────────────────────────────────┐
│  오케스트레이션 모드                                          │
│                                                              │
│  메인 Claude ──┬── 서브 에이전트 1 (파일 분석)                 │
│                ├── 서브 에이전트 2 (코드 작성)                 │
│                ├── 서브 에이전트 3 (테스트)                    │
│                └── 서브 에이전트 4 (리팩토링)                  │
│                                                              │
│  → 복잡한 작업을 여러 에이전트가 병렬로 처리                    │
└─────────────────────────────────────────────────────────────┘
```

- 하나의 작업을 여러 Claude 에이전트가 분담
- MCP 프로토콜을 통한 에이전트 간 통신
- Claude Code에서만 사용 가능 (다른 AI CLI 미지원)

### 6. 명령어 스니펫
- 자주 사용하는 명령어 저장
- 카테고리/태그 분류
- 빠른 검색 및 실행

### 7. 다중 탭 지원
- 여러 세션을 탭으로 관리
- SSH 서버 세션과 로컬 터미널 세션 혼합 가능
- 탭별 독립적인 작업 환경
- 새 탭 생성 시 세션 타입 선택

## 기술 스택

| 영역 | 기술 |
|------|------|
| 프레임워크 | .NET 8.0 / WPF |
| AI | Gemini, OpenAI, Claude, Grok (다중 제공자) |
| SSH/SFTP | SSH.NET, SshNet.PuttyKeyFile |
| 벡터 검색 | 로컬 임베딩 (sentence-transformers) |
| UI | XAML, Material Design In XAML |
| 보안 | Windows DPAPI (암호화) |

## 시작하기

### 요구사항
- **OS**: Windows 10/11 (64-bit)
- **.NET Runtime**: .NET 8.0 이상
- **AI API Key**: Gemini, OpenAI, Claude, 또는 Grok 중 **최소 하나**
- **선택사항**: Node.js 18+ (AI CLI 사용 시), Python 3.9+ (Aider 사용 시)

### 설치 방법

#### 방법 1: 설치 파일 사용 (권장)
1. [Releases](https://github.com/Dannykkh/TermSnap/releases) 페이지에서 최신 `.exe` 다운로드
2. 설치 마법사 실행
3. 프로그램 실행
4. 설정창에서 AI API 키 입력

#### 방법 2: 소스코드 빌드
```bash
# 1. 저장소 클론
git clone https://github.com/Dannykkh/TermSnap.git
cd TermSnap

# 2. 빌드
dotnet build

# 3. 실행
dotnet run --project src/TermSnap/TermSnap.csproj

# 4. (선택) 릴리스 빌드
dotnet publish src/TermSnap/TermSnap.csproj -c Release -r win-x64 --self-contained
```

### 빠른 시작 (5분 안에)

1. **프로그램 실행**
   ```
   TermSnap.exe 실행
   ```

2. **AI API 키 설정**
   - 설정 ⚙️ → AI 모델 → API 키 입력
   - [Gemini API 키 발급](https://ai.google.dev/) (무료, 추천)
   - [OpenAI API 키 발급](https://platform.openai.com/api-keys)
   - [Anthropic API 키 발급](https://console.anthropic.com/)

3. **첫 서버 연결** (SSH 세션)
   - "새 탭" (+) → "SSH 서버" 선택
   - 서버 정보 입력 (호스트, 사용자명, 비밀번호/SSH 키)
   - 연결 → AI에게 명령어 요청!
   - 예: "nginx 상태 확인해줘"

4. **로컬 터미널 사용** (선택)
   - "새 탭" (+) → "로컬 터미널" 선택
   - PowerShell, CMD, WSL, Git Bash 선택
   - 폴더 열기 → AI CLI 실행 (Claude Code, Aider 등)

### 초기 설정 예제

프로그램 실행 시 자동으로 생성되는 설정 파일 위치:
```
%APPDATA%\TermSnap\config.json
```

설정 파일 예제는 [`config.example.json`](config.example.json) 참고

## 사용 예시

### SSH 서버 세션
```
사용자: "nginx 상태 확인해줘"
AI: systemctl status nginx
결과: [nginx 상태 출력]

사용자: "디스크 80% 이상인 파티션 찾아줘"
AI: df -h | awk '$5 > 80 {print}'
결과: [파티션 목록]
```

### 로컬 터미널 세션
```
# AI CLI 빠른 실행 (웰컴 패널에서 버튼 클릭)
> claude                              # Claude Code 실행
> claude --dangerously-skip-permissions  # 자동 모드로 실행
> codex --full-auto                   # Codex 자동 모드

# 일반 터미널 명령어
> npm run build
> git push
```

### SFTP 파일 전송 (고성능)

#### 단일 파일 업로드/다운로드
```
1. SSH 세션에서 "파일 전송" 버튼 클릭
2. "업로드" → 파일 선택 (최대 100-150 MB/s)
3. "다운로드" → 파일 선택 후 저장 위치 선택
```

#### 다중 파일 병렬 전송 (400 MB/s)
```
[업로드]
1. "업로드" 버튼 클릭
2. Ctrl+클릭으로 여러 파일 선택 (예: 10개 파일)
3. 자동으로 4개씩 병렬 전송
   → 100 MB/s × 4 = 400 MB/s 속도

[다운로드]
1. 파일 목록에서 Ctrl+클릭 또는 Shift+클릭으로 여러 파일 선택
2. "다운로드" 버튼 → 폴더 선택
3. 선택한 파일들을 4개씩 병렬 다운로드
```

### 파일 편집 (IDE 스타일)

#### 뷰어 모드 (기본)
```
1. 파일 목록에서 텍스트 파일 더블클릭
2. [뷰어 모드] 파일 편집기 열림
   - 읽기 전용 (회색 배경)
   - "편집" 버튼만 표시
   - 실수로 파일 수정 불가
```

#### 편집 모드
```
1. "편집" 버튼 클릭 (✏️)
2. [편집 모드] 전환
   - 편집 가능 (흰색 배경)
   - "저장" / "취소" 버튼 표시
   - Undo/Redo 활성화
3. 파일 수정
4. "저장" (Ctrl+S) → 자동으로 뷰어 모드로 복귀
   또는 "취소" → 변경 사항 버리고 뷰어 모드로 복귀
```

#### 지원 기능
- **구문 강조**: C#, Python, JavaScript, JSON, XML, HTML, CSS, SQL, Bash, C++, Java, PHP, Ruby, Markdown
- **인코딩**: UTF-8, UTF-8 BOM, EUC-KR, ASCII
- **편집**: Undo/Redo, 줄 번호, 자동 줄바꿈
- **상태바**: 현재 줄/열, 줄 끝 문자 (CRLF/LF), 인코딩
- **코드 접기**: `{}` 블록 및 XML 태그 접기/펼치기
- **현재 줄 하이라이트**: 커서 위치 줄 강조
- **다크 테마 최적화**: VS Code 스타일 구문 강조 색상

### 파일 뷰어

![파일 뷰어](docs/images/file-viewer.png)

통합 파일 뷰어 기능:
- **구문 강조**: 20개 이상의 언어 지원
- **줄 번호** 및 **코드 접기**
- **편집 모드**: 저장/실행 취소/다시 실행
- **다크 테마** 최적화 색상
- **상태바**: 커서 위치, 인코딩, 줄 끝 문자

## 키보드 단축키

### 전역 단축키

| 단축키 | 기능 |
|--------|------|
| `Ctrl+L` | 새 로컬 터미널 탭 |
| `Ctrl+T` | 새 SSH 연결 탭 |
| `Ctrl+Tab` | 다음 탭으로 이동 |
| `Ctrl+W` | 현재 탭 닫기 |

### 파일 에디터 단축키

| 단축키 | 기능 |
|--------|------|
| `Ctrl+E` | 편집/뷰어 모드 토글 |
| `Ctrl+S` | 파일 저장 (편집 모드) |
| `Ctrl+F` | 텍스트 검색 |
| `Ctrl+H` | 찾아 바꾸기 (편집 모드) |
| `Ctrl+G` | 줄 이동 |
| `Ctrl+Z` | 실행 취소 |
| `Ctrl+Y` | 다시 실행 |
| `Ctrl+A` | 전체 선택 |
| `Ctrl+C/V/X` | 복사/붙여넣기/잘라내기 |
| `Ctrl+휠` | 글꼴 크기 조절 (확대/축소) |

### 터미널 단축키

| 단축키 | 기능 |
|--------|------|
| `Ctrl+C` | 현재 명령 취소 |
| `Ctrl+L` | 터미널 지우기 |
| `↑/↓` | 명령어 히스토리 |
| `Tab` | 자동 완성 |

## 설정

설정 파일: `%APPDATA%/TermSnap/config.json`

```json
{
  "ServerProfiles": [...],
  "AIModels": [
    { "Provider": "Gemini", "ModelId": "gemini-2.0-flash", "EncryptedApiKey": "..." },
    { "Provider": "OpenAI", "ModelId": "gpt-4o", "EncryptedApiKey": "..." }
  ],
  "SelectedProvider": "Gemini",
  "SelectedModelId": "gemini-2.0-flash",
  "AICLISettings": {
    "SelectedCLI": "claude",
    "LastCommand": "claude",
    "LastAutoMode": false,
    "IsExpanded": false
  },
  "QAEntries": [...],
  "CommandSnippets": [...],
  "RecentFolders": [...]
}
```

## AI CLI 설치

TermSnap에서 AI CLI를 사용하려면 먼저 해당 CLI를 설치해야 합니다.
앱에서 설치 상태를 자동으로 감지하며, 설치 버튼을 클릭하면 터미널에서 설치를 진행합니다.

### Claude Code
```bash
npm install -g @anthropic-ai/claude-code
```
- 요구사항: Node.js 18+
- [공식 GitHub](https://github.com/anthropics/claude-code)

### Codex CLI
```bash
npm install -g @openai/codex
```
- 요구사항: Node.js 22+
- [공식 GitHub](https://github.com/openai/codex)

### Gemini CLI
```bash
npm install -g @google/gemini-cli
```
- 요구사항: Node.js 18+
- [공식 GitHub](https://github.com/google/gemini-cli)

### Aider
```bash
pip install aider-chat
```
- 요구사항: Python 3.9+
- [공식 GitHub](https://github.com/paul-gauthier/aider)

## 기여하기

이 프로젝트에 기여하고 싶으신가요? 환영합니다! 🎉

1. **버그 리포트**: [Issues](https://github.com/Dannykkh/TermSnap/issues)에서 버그 보고
2. **기능 제안**: [Issues](https://github.com/Dannykkh/TermSnap/issues)에서 새 기능 제안
3. **코드 기여**:
   ```bash
   # Fork → Clone → Branch → Commit → Push → Pull Request
   git checkout -b feature/amazing-feature
   git commit -m "Add amazing feature"
   git push origin feature/amazing-feature
   ```
4. **문서 개선**: README, 주석, 가이드 개선
5. **번역**: 다른 언어로 번역 기여

자세한 내용은 [CONTRIBUTING.md](CONTRIBUTING.md) 참고

## FAQ

### Q: API 키 없이 사용할 수 있나요?
**A**: AI 명령어 생성 기능은 API 키가 필요하지만, SSH 연결/파일 전송/로컬 터미널은 API 키 없이 사용 가능합니다.

### Q: SSH 키 인증은 어떻게 설정하나요?
**A**: 서버 프로필 생성 시 "인증 방식" → "SSH 키" 선택 → .pem 또는 .ppk 파일 선택

### Q: 무료 AI API는 없나요?
**A**: Gemini API는 무료 티어가 있으며, 일일 한도 내에서 무료로 사용 가능합니다. ([Google AI Studio](https://ai.google.dev/)에서 발급)

### Q: Claude Code가 설치되었는데 감지가 안 돼요
**A**:
1. PowerShell에서 `claude --version` 실행 확인
2. PATH 환경 변수에 `npm global bin` 경로 추가
3. 프로그램 재시작

### Q: 다크 모드만 지원하나요?
**A**: 설정에서 라이트/다크 테마 전환 가능합니다.

### Q: SFTP 전송 속도가 왜 빠른가요?
**A**:
- **단일 파일**: SSH.NET 라이브러리의 내부 버퍼 최적화 (maxPendingReads 100, 소켓 버퍼 256KB)로 3-5배 향상
- **다중 파일**: 4개 파일을 동시에 병렬 전송하여 총 400 MB/s 달성
- FileZilla (161 MB/s), WindTerm (216 MB/s)와 경쟁 가능한 수준

### Q: 파일 편집 중 실수로 저장할까봐 걱정돼요
**A**: 기본은 **뷰어 모드**(읽기 전용)입니다. 명시적으로 "편집" 버튼을 눌러야 수정 가능하므로 실수로 파일이 변경될 걱정이 없습니다. IDE처럼 안전하게 사용하세요!

## 문제 해결

### 빌드 오류
- .NET 8.0 SDK가 설치되어 있는지 확인: `dotnet --version`
- 종속성 복원: `dotnet restore`

### 실행 오류
- .NET 8.0 Runtime이 설치되어 있는지 확인
- [.NET 8.0 다운로드](https://dotnet.microsoft.com/download/dotnet/8.0)

### SSH 연결 실패
- 방화벽 확인 (포트 22 허용)
- SSH 서비스 실행 확인: `sudo systemctl status sshd`
- 호스트/포트/인증 정보 확인

## 로드맵

- [ ] macOS/Linux 지원 (Avalonia UI 전환)
- [ ] 영어 UI 지원
- [ ] 플러그인 시스템
- [ ] 클라우드 설정 동기화
- [ ] 원격 데스크톱 통합
- [ ] 터미널 녹화/재생 기능

## 라이선스

**MIT License**

TermSnap은 MIT 라이센스 하에 배포되는 오픈소스 프로젝트입니다.

```
Copyright (c) 2026 Dannykkh

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
```

### MIT 라이센스란?
- ✅ **상업적 사용 가능**: 회사에서 자유롭게 사용 가능
- ✅ **수정 가능**: 코드를 자유롭게 수정 및 개선 가능
- ✅ **배포 가능**: 수정한 버전을 자유롭게 배포 가능
- ✅ **사유 소프트웨어 통합 가능**: 사유 제품에 포함 가능
- ⚠️ **라이센스 고지 필수**: 저작권 표시와 라이센스 사본 포함 필요

자세한 내용은 [LICENSE](LICENSE) 파일을 참고하세요.

## 감사의 말

이 프로젝트는 다음 오픈소스 라이브러리를 사용합니다:
- [SSH.NET](https://github.com/sshnet/SSH.NET) - SSH/SFTP 연결
- [Material Design In XAML](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) - UI 디자인
- [sentence-transformers](https://www.sbert.net/) - 임베딩 모델

그리고 다음 AI 제공자들에게 감사드립니다:
- Google Gemini
- OpenAI
- Anthropic Claude
- xAI Grok

## 지원 및 문의

- 🐛 버그 리포트: [Issues](https://github.com/Dannykkh/TermSnap/issues)
- 💡 기능 제안: [Issues](https://github.com/Dannykkh/TermSnap/issues)
- 💬 토론: [Discussions](https://github.com/Dannykkh/TermSnap/discussions)

---

⭐ 이 프로젝트가 도움이 되셨다면 Star를 눌러주세요!
