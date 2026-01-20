# TermSnap Terminal 설치 파일 빌드 가이드

## 준비 사항

### 1. Inno Setup 설치
- 다운로드: https://jrsoftware.org/isdl.php
- **Inno Setup 6** 이상 설치 (권장)
- 설치 시 기본 경로 사용 권장

### 2. 아이콘 준비 (선택 사항)
아이콘을 설정하려면:

1. `assets/icon.svg`를 `assets/icon.ico`로 변환
   - 온라인 도구 사용: https://cloudconvert.com/svg-to-ico
   - 또는 `assets/ICON_CONVERSION_GUIDE.md` 참고

2. `installer.iss` 파일 수정:
   ```iss
   ; SetupIconFile=assets\icon.ico  <- 주석 제거
   SetupIconFile=assets\icon.ico
   ```

3. `src/TermSnap/TermSnap.csproj` 파일에 아이콘 추가:
   ```xml
   <PropertyGroup>
     <ApplicationIcon>..\..\assets\icon.ico</ApplicationIcon>
   </PropertyGroup>
   ```

## 빌드 방법

### 자동 빌드 (PowerShell 스크립트)

가장 간단한 방법입니다:

```powershell
# 기본 빌드 (버전 1.0.0)
.\build-installer.ps1

# 버전 지정
.\build-installer.ps1 -Version "1.2.3"
```

스크립트가 자동으로:
1. 이전 빌드 정리
2. 프로젝트 게시 (Self-Contained)
3. Inno Setup으로 설치 파일 생성

### 수동 빌드

#### 1단계: 프로젝트 게시

```bash
dotnet publish src/TermSnap/TermSnap.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  /p:PublishSingleFile=false
```

#### 2단계: Inno Setup 컴파일

```bash
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
```

## 출력 파일

빌드가 완료되면 `installer_output` 폴더에 설치 파일이 생성됩니다:
- `TermSnap-Setup-v1.1.0.exe` (또는 지정한 버전)

## 설치 파일 테스트

1. `installer_output` 폴더의 `.exe` 파일 실행
2. 설치 마법사 따라 진행
3. 설치된 프로그램 실행 테스트
4. 제거 테스트 (제어판 → 프로그램 제거)

## 문제 해결

### "Inno Setup 컴파일러를 찾을 수 없습니다"

- Inno Setup이 설치되어 있는지 확인
- 다음 경로 중 하나에 ISCC.exe가 있는지 확인:
  - `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
  - `C:\Program Files\Inno Setup 6\ISCC.exe`

### "게시 디렉토리를 찾을 수 없습니다"

- .NET 8.0 SDK가 설치되어 있는지 확인
- `dotnet --version` 명령어로 확인 (8.0 이상)

### 설치 파일 크기가 너무 큼

Self-Contained 게시는 .NET 런타임을 포함하므로 파일 크기가 큽니다 (약 100-150MB).
크기를 줄이려면:

1. ReadyToRun 컴파일 비활성화:
   ```xml
   <PublishReadyToRun>false</PublishReadyToRun>
   ```

2. 트리밍 활성화 (주의: 일부 기능이 동작하지 않을 수 있음):
   ```xml
   <PublishTrimmed>true</PublishTrimmed>
   ```

## 배포

생성된 `.exe` 파일을:
- GitHub Releases에 업로드
- 다운로드 서버에 호스팅
- 사용자에게 직접 배포

## 버전 관리

버전 번호는 다음 규칙을 따릅니다:
- **Major.Minor.Patch** (예: 1.2.3)
- Major: 주요 변경 사항
- Minor: 새로운 기능 추가
- Patch: 버그 수정

버전 업데이트 시:
1. `build-installer.ps1 -Version "x.y.z"` 실행
2. 또는 `installer.iss`의 `MyAppVersion` 수정
