# Claude Code 장기기억 시스템

## 개요

Claude Code의 장기기억 시스템은 프로젝트별로 중요한 정보를 `MEMORY.md` 파일에 저장하여 세션 간에 컨텍스트를 유지합니다.

## 설치

### 1. MEMORY.md 파일 생성

프로젝트 루트에 `MEMORY.md` 파일을 생성합니다:

```markdown
# 프로젝트 장기기억

## 프로젝트 정보
- 프로젝트명: [프로젝트 이름]
- 기술 스택: [사용하는 기술]
- 핵심 원칙: [프로젝트의 핵심 원칙]

## 아키텍처 결정
[중요한 아키텍처 결정 사항들]

## 코딩 컨벤션
[프로젝트의 코딩 규칙]

## 주요 이슈 & 해결책
[발견된 문제와 해결 방법]
```

### 2. CLAUDE.md에 참조 추가

`CLAUDE.md` 파일에 다음을 추가하여 MEMORY.md를 자동으로 로드합니다:

```markdown
<!-- #include MEMORY.md -->
```

### 3. 메모리 훅 설치 (선택사항)

세션 종료 시 자동으로 기억을 업데이트하려면 훅을 설치합니다:

**Windows (`.claude/hooks/update-memory.ps1`):**
```powershell
# Claude Code 세션 종료 시 MEMORY.md 업데이트 요청
Write-Host "세션 종료 - 기억 업데이트 확인 중..."
```

**Linux/Mac (`.claude/hooks/update-memory.sh`):**
```bash
#!/bin/bash
echo "세션 종료 - 기억 업데이트 확인 중..."
```

## 사용 방법

### 기억 추가
```
"이거 기억해: API 키는 환경변수로 관리해야 함"
"기억해줘: 테스트는 항상 pytest로 실행"
```

### 기억 조회
```
"API 관련해서 뭘 기억해?"
"테스트 방법에 대해 기억하는 거 있어?"
```

### 전체 기억 보기
```
"장기기억 보여줘"
"MEMORY.md 내용 보여줘"
```

### 기억 수정/삭제
```
"이 기억 삭제해: [기억 내용]"
"기억 수정해: [기존 내용] -> [새 내용]"
```

## 모범 사례

1. **구조화된 정보 저장**: 카테고리별로 정리하여 검색이 쉽게
2. **핵심만 저장**: 불필요한 정보는 저장하지 않음
3. **정기적 정리**: 오래된 정보나 더 이상 유효하지 않은 정보 삭제
4. **프로젝트 특화**: 프로젝트에 특화된 정보 위주로 저장

## 파일 위치

| 파일 | 위치 | 용도 |
|------|------|------|
| MEMORY.md | 프로젝트 루트 | 장기기억 저장 |
| CLAUDE.md | 프로젝트 루트 | 프로젝트 지침 (MEMORY.md 포함) |
| update-memory.ps1 | .claude/hooks/ | Windows 메모리 훅 |
| update-memory.sh | .claude/hooks/ | Linux/Mac 메모리 훅 |

## 참고

- 원본: https://github.com/Dannykkh/claude-code-agent-customizations
- MEMORY.md는 자동 생성되며 자유롭게 편집 가능합니다.
