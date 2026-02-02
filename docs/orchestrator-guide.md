# Claude Code 오케스트레이터 가이드

## 개요

오케스트레이터는 Claude Code에서 여러 에이전트를 조율하여 복잡한 작업을 수행하는 시스템입니다. oh-my-claude-code 플러그인을 통해 32개 이상의 전문 에이전트와 40개 이상의 스킬을 활용할 수 있습니다.

## 설치

### 1. oh-my-claude-code 설치

Claude Code에서 다음 명령어를 실행합니다:

```
/plugin install oh-my-claude-code
```

### 2. 초기 설정

설치 후 설정 마법사를 실행합니다:

```
/oh-my-claudecode:omc-setup
```

### 3. MCP 서버 설정 (선택사항)

고급 오케스트레이션을 위해 MCP 서버를 설정합니다.

**`.claude/settings.local.json`:**
```json
{
  "mcpServers": {
    "claude-orchestrator-mcp": {
      "command": "node",
      "args": ["mcp-servers/claude-orchestrator-mcp/index.js"]
    }
  }
}
```

## 실행 모드

### Autopilot 모드
기본 자동 실행 모드. 단순한 작업에 적합.

```
/oh-my-claudecode:autopilot "버그 수정해줘"
```

### Ultrapilot 모드 (Ultrawork)
집중적인 작업 모드. 복잡한 개발 작업에 적합.

```
/oh-my-claudecode:ultrawork "새 기능 구현해줘"
```

### Swarm 모드
여러 에이전트가 동시에 협업. 대규모 리팩토링에 적합.

```
/oh-my-claudecode:swarm "전체 코드베이스 리팩토링"
```

### Pipeline 모드
순차적 작업 처리. CI/CD 스타일 작업에 적합.

```
/oh-my-claudecode:pipeline "빌드 → 테스트 → 배포"
```

### Ecomode
토큰 절약 모드. 간단한 작업에 효율적.

```
/oh-my-claudecode:ecomode "간단한 수정"
```

## 주요 에이전트

| 에이전트 | 용도 |
|----------|------|
| orchestrator | 메인 오케스트레이터, 작업 분배 |
| code-reviewer | 코드 리뷰 |
| frontend-react | React 프론트엔드 개발 |
| backend-spring | Spring Boot 백엔드 개발 |
| database-mysql | MySQL 데이터베이스 작업 |
| api-tester | API 테스트 |
| documentation | 문서화 |
| qa-engineer | QA 테스트 |
| explore-agent | 코드베이스 탐색 |
| memory-writer | 장기기억 작성 |

## 스킬 사용

### 스킬 목록 보기
```
/skills list
```

### 스킬 검색
```
/find-skills "코드 리뷰"
```

### 스킬 실행
```
/code-reviewer
/react-best-practices
/docker-deploy
```

## 파일 구조

```
.claude/
├── settings.local.json    # MCP 서버 설정
├── agents/                # 에이전트 정의
│   ├── orchestrator.md
│   ├── code-reviewer.md
│   └── ...
├── skills/                # 스킬 정의
│   ├── code-reviewer/
│   ├── react-dev/
│   └── ...
└── hooks/                 # 자동 실행 훅
    ├── update-memory.ps1
    └── ...
```

## 설정 예시

### settings.local.json 전체 예시

```json
{
  "mcpServers": {
    "claude-orchestrator-mcp": {
      "command": "node",
      "args": ["mcp-servers/claude-orchestrator-mcp/index.js"]
    },
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@anthropic-ai/mcp-server-filesystem", "."]
    },
    "memory": {
      "command": "npx",
      "args": ["-y", "@anthropic-ai/mcp-server-memory"]
    }
  }
}
```

## 모범 사례

1. **작업 분해**: 큰 작업은 작은 단위로 분해하여 에이전트에 할당
2. **적절한 모드 선택**: 작업 복잡도에 맞는 실행 모드 선택
3. **에이전트 특화**: 각 에이전트의 전문 분야에 맞는 작업 할당
4. **피드백 반영**: 에이전트 결과를 검토하고 필요시 재작업 요청

## 문제 해결

### MCP 서버 연결 실패
1. Node.js 설치 확인
2. 경로 확인
3. Claude Code 재시작

### 에이전트 응답 없음
1. 작업 설명을 더 구체적으로
2. 다른 에이전트 시도
3. 작업 범위 축소

## 참고

- oh-my-claude-code: https://github.com/anthropics/claude-code
- 에이전트 정의: https://github.com/Dannykkh/claude-code-agent-customizations/tree/master/agents
- 스킬 정의: https://github.com/Dannykkh/claude-code-agent-customizations/tree/master/skills
