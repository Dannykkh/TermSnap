# MEMORY.md - 프로젝트 장기기억

## 프로젝트 목표

| 목표 | 상태 |
|------|------|
| PuTTY를 더 편하게 - AI 기반 터미널 도우미 | 🔄 진행중 |
| SSH 서버 세션 (원격 연결, AI 명령어, SFTP) | ✅ 완성 |
| 로컬 터미널 세션 (PowerShell, CMD, WSL, Git Bash) | ✅ 완성 |
| GPU 가속 최적화 | 🔄 진행중 |
| 세션 자동 복원 | ✅ 완성 |
| 링크 클릭 팝업 (Warp 스타일) | ✅ 완성 |
| 4분할 그리드 뷰 | ❌ 롤백 (불필요 판단) |
| 프로젝트 서브탭 시스템 | ✅ 완성 |
| 서브탭 추가 선택기 (쉘/CLI 선택) | ✅ 완성 |
| 에디터 실행 (VS Code/Cursor) | ✅ 완성 |

---

## 키워드 인덱스

| 키워드 | 섹션 |
|--------|------|
| wpf, dotnet, csharp | #architecture/core |
| ssh, sftp, putty | #architecture/ssh |
| terminal, conpty, powershell | #architecture/terminal |
| session-restore, session-state | #architecture/session-restore |
| link-popup, warp-style | #architecture/link-popup |
| quad-split, split-view | #architecture/split-view |
| ai, gemini, openai, claude | #architecture/ai |
| gpu, rendering, drawingvisual | #gotchas/gpu-rendering |
| memory, hook, skill | #tools/claude-code |
| sub-tab, project-session, editor | #architecture/sub-tab |

---

## architecture/

### core
`tags: wpf, dotnet, csharp, mvvm`
`date: 2026-02-02`

- WPF .NET 8.0 기반, MVVM 패턴
- UI: MaterialDesignThemes
- 설정: `%APPDATA%/TermSnap/config.json`
- 암호화: Windows DPAPI

### ssh
`tags: ssh, sftp, putty, renci`
`date: 2026-02-02`

- Renci.SshNet 라이브러리 사용
- .ppk (PuTTY), .pem (OpenSSH) 키 지원
- SFTP 파일 전송 지원

### terminal
`tags: terminal, conpty, powershell, cmd, wsl`
`date: 2026-02-02`

- DrawingVisual 기반 라인별 캐싱 렌더링
- ConPTY로 로컬 쉘 실행
- UTF-8 인코딩: `chcp 65001` + 환경변수

### ai
`tags: ai, gemini, openai, claude, grok, ollama`
`date: 2026-02-02`

- AIProviderFactory로 다중 AI 제공자 지원
- RAGService로 Q&A 벡터 검색 (토큰 절약)
- 자연어 → Linux 명령어 변환

### sub-tab
`tags: sub-tab, project-session, editor, vscode, cursor`
`date: 2026-02-07`

- ProjectSessionViewModel: 서브탭 컨테이너 (ISessionViewModel 구현)
- 로컬 터미널 생성 시 자동으로 ProjectSession으로 감싸기
- 서브탭별 View 캐싱 (ProjectSessionView.xaml.cs)
- 파일 탐색기: 프로젝트 레벨에 고정 (서브탭 전환해도 유지)
- 에디터 실행: FileTreePanel 헤더에 VS Code/Cursor 버튼
- 설치 감지: `where code` / `where cursor` (미설치시 숨김)
- 세션 저장/복원: SubSessionState 리스트로 서브탭 구조 보존

---

## patterns/

### build
`tags: dotnet, build, run`
`date: 2026-02-02`

```bash
dotnet build src/TermSnap/TermSnap.csproj
dotnet run --project src/TermSnap/TermSnap.csproj
```

### coding-conventions
`tags: coding, convention, korean`
`date: 2026-02-02`

- 한국어 주석 선호
- MVVM 패턴 준수
- async/await 사용

---

## tools/

### claude-code
`tags: claude-code, mcp, hook, skill, agent`
`date: 2026-02-03`

- 장기기억 시스템 (컨텍스트 트리 구조)
- AIToolsPanel에서 스킬/에이전트 설치 UI
- settings.local.json MCP 서버 관리
- **Stop 훅 없음** - 추가 AI 호출 방지

---

## gotchas/

### build-lock
`tags: build, lock, termsnap`
`date: 2026-02-02`

- TermSnap.exe 실행 중 빌드 불가
- **해결**: 앱 종료 후 빌드

### dangerous-commands
`tags: security, command, block`
`date: 2026-02-02`

- `ErrorHandler.IsDangerousCommand`로 위험 명령어 차단
- rm -rf /, dd 등 자동 차단

### gpu-rendering
`tags: gpu, rendering, bitmap-cache, drawingvisual`
`date: 2026-02-03`

- WPF는 기본적으로 DirectX GPU 가속 사용
- `RenderCapability.Tier`로 GPU 지원 확인
- **BitmapCache 주의**: TextBox 입력 차단 가능
- **스냅샷 캐싱 주의**: Visual 트리 충돌로 화면 가림 발생
- DrawingVisual 라인별 캐싱이 안정적
- **참조**: [대화](.claude/conversations/2026-02-03.md)

---

## meta/
- **프로젝트**: TermSnap (linuxserverai)
- **유형**: WPF .NET 8.0 애플리케이션
- **생성일**: 2026-02-02
- **마지막 업데이트**: 2026-02-07
