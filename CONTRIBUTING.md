# 기여 가이드

Nebula Terminal Assistant 프로젝트에 관심을 가져주셔서 감사합니다!

## 기여 방법

### 1. 이슈 리포트

버그를 발견하거나 개선 아이디어가 있다면:

1. [Issues](https://github.com/Dannykkh/nebula-terminal/issues)에서 중복된 이슈가 없는지 확인
2. 새 이슈 생성
3. 다음 정보 포함:
   - 명확한 제목
   - 재현 방법 (버그인 경우)
   - 예상 동작 vs 실제 동작
   - 환경 정보 (Windows 버전, .NET 버전 등)
   - 스크린샷 (해당되는 경우)

### 2. 코드 기여

#### 준비 사항

- Visual Studio 2022 이상
- .NET 8.0 SDK
- Git

#### 개발 프로세스

1. **Fork 및 Clone**
   ```bash
   git clone https://github.com/your-username/nebula-terminal.git
   cd nebula-terminal
   ```

2. **브랜치 생성**
   ```bash
   git checkout -b feature/your-feature-name
   # 또는
   git checkout -b fix/bug-description
   ```

3. **개발**
   - 코드 스타일 가이드 준수
   - 의미있는 커밋 메시지 작성
   - 변경사항 테스트

4. **커밋**
   ```bash
   git add .
   git commit -m "feat: Add new feature description"
   ```

5. **Push 및 Pull Request**
   ```bash
   git push origin feature/your-feature-name
   ```
   - GitHub에서 Pull Request 생성
   - 변경 사항 상세히 설명
   - 관련 이슈 번호 참조 (#123)

## 코드 스타일

### C# 코딩 규칙

```csharp
// ✅ 좋은 예
public class GeminiService
{
    private readonly string _apiKey;

    public async Task<string> ConvertToLinuxCommand(string userRequest)
    {
        if (string.IsNullOrWhiteSpace(userRequest))
        {
            throw new ArgumentException("User request cannot be empty", nameof(userRequest));
        }

        // 로직...
    }
}

// ❌ 나쁜 예
public class geminiservice
{
    public string apikey;

    public string convert(string s)
    {
        return ""; // 오류 처리 없음
    }
}
```

### 규칙

- **네이밍**:
  - 클래스/메서드: PascalCase
  - 변수/파라미터: camelCase
  - Private 필드: _camelCase
  - 상수: UPPER_CASE

- **포맷팅**:
  - 들여쓰기: 4 스페이스
  - 중괄호: 새 줄에 시작
  - 한 줄 최대 길이: 120자

- **주석**:
  - XML 문서 주석 사용
  - 복잡한 로직은 설명 추가
  - TODO 주석에는 이슈 번호 포함

```csharp
/// <summary>
/// Gemini API를 사용하여 자연어를 리눅스 명령어로 변환
/// </summary>
/// <param name="userRequest">사용자의 자연어 요청</param>
/// <returns>생성된 리눅스 명령어</returns>
public async Task<string> ConvertToLinuxCommand(string userRequest)
{
    // TODO: #42 - 캐싱 기능 추가
}
```

## 커밋 메시지 규칙

```
<type>: <subject>

<body>

<footer>
```

### Type

- `feat`: 새로운 기능
- `fix`: 버그 수정
- `docs`: 문서 변경
- `style`: 코드 포맷팅 (기능 변경 없음)
- `refactor`: 리팩토링
- `test`: 테스트 추가/수정
- `chore`: 빌드/설정 변경

### 예시

```
feat: Add command history feature

- Add CommandHistory class
- Implement history navigation with up/down arrows
- Save history to config file

Closes #42
```

## Pull Request 가이드라인

### PR 제목

- 명확하고 간결하게
- 커밋 메시지 규칙 따르기
- 예: `feat: Add SSH key authentication support`

### PR 설명

다음 템플릿 사용:

```markdown
## 변경 사항
- 변경된 내용 요약

## 동기
- 왜 이 변경이 필요한가?

## 테스트
- 어떻게 테스트했는가?

## 스크린샷 (해당되는 경우)
- UI 변경사항 스크린샷

## 체크리스트
- [ ] 코드가 빌드됨
- [ ] 스타일 가이드 준수
- [ ] 문서 업데이트 (필요시)
- [ ] 테스트 통과
```

### 리뷰 프로세스

1. 자동 빌드 통과 확인
2. 최소 1명의 리뷰어 승인 필요
3. 변경 요청 사항 반영
4. Squash and merge

## 개발 환경 설정

### 권장 도구

- **IDE**: Visual Studio 2022 Community
- **Extensions**:
  - ReSharper (선택사항)
  - XAML Styler
  - EditorConfig

### 빌드 및 실행

```bash
# 빌드
dotnet build

# 실행
dotnet run --project src/Nebula Terminal/Nebula Terminal.csproj

# 테스트
dotnet test
```

## 프로젝트 구조

```
nebula-terminal/
├── src/Nebula Terminal/
│   ├── Models/          # 데이터 모델
│   ├── Services/        # 비즈니스 로직
│   ├── ViewModels/      # MVVM 뷰모델
│   └── Views/           # UI (XAML)
├── tests/               # 단위 테스트
└── docs/                # 문서
```

## 우선순위 기능

다음 기능들에 대한 기여를 환영합니다:

- [ ] 여러 서버 프로필 관리
- [ ] 명령어 실행 이력 저장/검색
- [ ] 즐겨찾기 명령어 기능
- [ ] 다크 모드 지원
- [ ] 다국어 지원 (영어, 일본어 등)
- [ ] 명령어 자동완성
- [ ] 서버 모니터링 대시보드
- [ ] 스크립트 생성 및 저장 기능

## 질문이나 도움이 필요한 경우

- [GitHub Discussions](https://github.com/Dannykkh/nebula-terminal/discussions)
- [Issues](https://github.com/Dannykkh/nebula-terminal/issues)

## 행동 강령

- 존중하고 포용적인 태도
- 건설적인 피드백
- 다양한 관점 환영
- 협력적인 문제 해결

## 라이선스

기여한 코드는 프로젝트의 MIT 라이선스를 따릅니다.

---

다시 한 번 기여해주셔서 감사합니다! 🎉
