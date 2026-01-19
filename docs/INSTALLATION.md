# 설치 가이드

## Windows에서 실행하기

### 1. 필수 요구사항

- **Windows 10/11** (64비트)
- **.NET 8.0 Runtime** 이상
  - [다운로드 링크](https://dotnet.microsoft.com/download/dotnet/8.0)
  - "Download .NET Desktop Runtime" 선택

### 2. 설치 방법

#### 방법 A: 릴리스 버전 사용 (권장)

1. [Releases 페이지](https://github.com/yourusername/linuxserverai/releases)에서 최신 버전 다운로드
2. ZIP 파일 압축 해제
3. `LinuxServerAI.exe` 실행

#### 방법 B: 소스에서 빌드

1. **Visual Studio 2022** 설치 (Community Edition 무료)
   - Workload: ".NET desktop development" 선택
   - [다운로드](https://visualstudio.microsoft.com/downloads/)

2. **저장소 클론**
   ```bash
   git clone https://github.com/yourusername/linuxserverai.git
   cd linuxserverai
   ```

3. **Visual Studio에서 빌드**
   - `LinuxServerAI.sln` 파일 열기
   - 메뉴: Build > Build Solution (Ctrl+Shift+B)
   - 실행: Debug > Start (F5)

4. **명령줄에서 빌드 (선택사항)**
   ```bash
   dotnet build
   dotnet run --project src/LinuxServerAI/LinuxServerAI.csproj
   ```

### 3. 초기 설정

첫 실행 시 다음 정보를 입력해야 합니다:

#### Gemini API 키 발급

1. [Google AI Studio](https://aistudio.google.com/app/apikey) 접속
2. Google 계정으로 로그인
3. "Create API Key" 버튼 클릭
4. 생성된 API 키 복사

#### SSH 서버 정보

- **호스트**: 서버 IP 주소 또는 도메인
- **포트**: 기본값 22
- **사용자명**: SSH 접속 계정
- **인증 방식**:
  - 비밀번호: 계정 비밀번호 입력
  - 개인 키: SSH 키 파일 경로 지정 (.pem, id_rsa 등)

### 4. 연결 테스트

1. 프로그램 실행
2. "설정" 버튼 클릭
3. 모든 정보 입력 후 "연결 테스트" 버튼 클릭
4. 성공 메시지 확인 후 "저장"
5. "연결" 버튼으로 서버 연결

## 문제 해결

### .NET 런타임 오류
```
Could not load file or assembly...
```
**해결**: .NET 8.0 Desktop Runtime 설치

### SSH 연결 실패
```
SSH 연결 실패: Connection refused
```
**확인 사항**:
- 서버 IP/포트가 올바른지 확인
- 방화벽에서 SSH 포트(22) 허용 확인
- 서버에서 SSH 서비스 실행 중인지 확인: `sudo systemctl status sshd`

### API 키 오류
```
Gemini API 오류: 403
```
**해결**: API 키가 올바른지 확인, 필요시 새 키 생성

### 권한 오류
```
Permission denied
```
**해결**: SSH 계정에 적절한 권한이 있는지 확인

## 추가 리소스

- [사용 가이드](USAGE.md)
- [FAQ](FAQ.md)
- [GitHub Issues](https://github.com/yourusername/linuxserverai/issues)
