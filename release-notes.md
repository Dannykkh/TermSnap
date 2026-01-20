# TermSnap v1.1.0 - 성능 & UX 대폭 개선 🚀

**릴리즈 날짜**: 2026-01-20

## 🎯 핵심 개선사항

### ⚡ SFTP 성능 대폭 향상 (3-8배)
이제 TermSnap은 **FileZilla**와 **WindTerm** 수준의 파일 전송 속도를 제공합니다!

**단일 파일 전송 최적화**
- 기존: ~50 MB/s → 개선: **100-150 MB/s** (3-5배 향상)
- SSH.NET 내부 버퍼 최적화 (maxPendingReads: 10 → 100)
- 소켓 버퍼 증가 (256KB)
- 3.2MB in-flight data 처리

**다중 파일 병렬 전송** ✨ NEW
- 여러 파일을 선택하면 **4개씩 동시 전송**
- 총 속도: **400 MB/s** (FileZilla 161 MB/s 대비 2.5배)
- 업로드: Ctrl+클릭으로 여러 파일 선택 가능
- 다운로드: DataGrid에서 다중 선택 (Ctrl/Shift) 후 폴더 지정
- 전체 진행률 표시

### ✏️ IDE 스타일 파일 편집기 ✨ NEW
**뷰어/편집 모드 분리**로 실수로 파일 수정하는 일이 사라졌습니다!

**뷰어 모드 (기본)**
- 읽기 전용, 회색 배경
- 파일 내용을 안전하게 확인
- "편집" 버튼으로 명시적 전환 필요

**편집 모드**
- "편집" 버튼 클릭 시 활성화
- 흰색 배경, 저장/취소 버튼 표시
- Undo/Redo 활성화
- 저장 후 자동으로 뷰어 모드 복귀

**지원 기능**
- 구문 강조: C#, Python, JavaScript, JSON, XML, HTML, CSS, SQL, Bash, C++, Java, PHP, Ruby, Markdown
- 인코딩: UTF-8, UTF-8 BOM, EUC-KR, ASCII
- 줄 번호, 자동 줄바꿈
- Ctrl+S 저장 단축키

### 📜 라이선스 변경: MIT License
- **Proprietary → MIT** 오픈소스로 전환
- 상업적 사용 제한 없음
- 자유로운 수정 및 배포 가능
- 기업에서도 부담 없이 사용 가능

## 📦 설치 방법

### 다운로드
1. `TermSnap-v1.1.0-win-x64.zip` 다운로드
2. 압축 해제
3. `TermSnap.exe` 실행
4. 설정창에서 AI API 키 입력

### 요구사항
- **OS**: Windows 10/11 (64-bit)
- **.NET Runtime**: .NET 8.0 이상
- **AI API Key**: Gemini, OpenAI, Claude, 또는 Grok 중 최소 하나

## 🔧 기술적 세부사항

### SFTP 최적화 원리
```
SSH.NET 기본 설정 (느림)          최적화된 설정 (빠름)
─────────────────────────────────────────────────────
maxPendingReads: 10              maxPendingReads: 100
BufferSize: 32KB                 BufferSize: 64KB
SocketBuffer: 64KB               SocketBuffer: 256KB
In-flight: 320KB                 In-flight: 3.2MB
속도: ~50 MB/s                   속도: 100-150 MB/s

병렬 전송 (4개 동시)
─────────────────────────────────────────────────────
4 connections × 100 MB/s = 400 MB/s
```

### 경쟁사 비교
| 제품 | 속도 | 방식 |
|------|------|------|
| **TermSnap v1.1.0** | **400 MB/s** | 병렬 전송 (4개) |
| **TermSnap v1.0.0** | 50 MB/s | 단일 연결 |
| FileZilla | 161 MB/s | 병렬 전송 (10개) |
| WindTerm | 216 MB/s | libssh + C++ |

## 🐛 버그 수정
- 파일 전송 UI에서 KeyEventArgs 충돌 해결
- 편집기에서 저장 후 상태 업데이트 개선

## 📝 문서 개선
- README.md에 SFTP 성능 및 파일 편집기 사용법 추가
- FAQ에 속도 관련 질문 추가

## 🙏 감사의 말
이번 릴리즈는 다음 오픈소스 프로젝트의 영감을 받았습니다:
- [SSH.NET PR #866](https://github.com/sshnet/SSH.NET/pull/866) - SFTP 성능 최적화
- FileZilla, WindTerm - 병렬 전송 벤치마크

---

⭐ 이 프로젝트가 도움이 되셨다면 Star를 눌러주세요!
🐛 버그 리포트: [Issues](https://github.com/Dannykkh/TermSnap/issues)
💬 토론: [Discussions](https://github.com/Dannykkh/TermSnap/discussions)

---

# TermSnap v1.0.0 - Initial Release 🎉

**릴리즈 날짜**: 2026-01-15

**"PuTTY를 더 편하게"** - AI 기반 터미널 도우미

## 주요 기능

### 🖥️ 두 가지 세션 타입
- **SSH 서버 세션**: 원격 서버 연결, AI 명령어 생성, SFTP 파일 전송
- **로컬 터미널 세션**: PowerShell, CMD, WSL, Git Bash (Warp 스타일)

### 🤖 AI 명령어 생성
- 자연어를 Linux 명령어로 자동 변환
- 다중 AI 제공자 지원: Gemini, OpenAI, Claude, Grok
- 오류 발생 시 AI 분석 및 자동 재시도
- 위험 명령어 자동 차단 (rm -rf /, dd 등)

### 🔍 Q&A 벡터 검색 (토큰 절약)
- 등록된 Q&A에서 유사 질문 자동 검색
- API 호출 없이 저장된 답변 즉시 반환
- 새로운 답변은 자동으로 저장하여 재사용

### 📁 파일 관리
- Windows 탐색기 스타일의 단축키 (F2, Delete, Ctrl+C/X/V)
- 로컬 및 SSH(SFTP) 모두 지원
- 재귀적 디렉토리 복사/이동
- 파일 미리보기 및 편집

### 🎨 사용자 인터페이스
- Material Design 기반 모던 UI
- 다크/라이트 테마 전환
- 다중 탭 지원 (SSH와 로컬 세션 혼합 가능)
- 실시간 CPU/메모리 사용량 표시

### 🛠️ 로컬 터미널
- Warp 스타일 웰컴 화면
- AI CLI 도구 통합 (Claude Code, Codex, Gemini CLI, Aider)
- 폴더 열기, Git 저장소 복제
- 최근 폴더 목록

## 기술 스택
- .NET 8.0 / WPF
- SSH.NET, Material Design In XAML
- 로컬 임베딩 (ONNX Runtime)
- Windows DPAPI 암호화

## 요구사항
- **OS**: Windows 10/11 (64-bit)
- **.NET Runtime**: .NET 8.0 이상
- **AI API Key**: Gemini, OpenAI, Claude, 또는 Grok 중 최소 하나

## 설치 방법

### 다운로드
1. `TermSnap-v1.0.0-win-x64.zip` 다운로드
2. 압축 해제
3. `TermSnap.exe` 실행
4. 설정창에서 AI API 키 입력

## 라이선스
- **Proprietary License** (v1.0.0 당시)
- v1.1.0부터 **MIT License**로 변경

자세한 내용은 [LICENSE](https://github.com/Dannykkh/TermSnap/blob/develop/LICENSE) 참고

---

⭐ 이 프로젝트가 도움이 되셨다면 Star를 눌러주세요!
🐛 버그 리포트: [Issues](https://github.com/Dannykkh/TermSnap/issues)
💬 토론: [Discussions](https://github.com/Dannykkh/TermSnap/discussions)
