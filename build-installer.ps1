# Nebula Terminal 설치 파일 빌드 스크립트
# Inno Setup이 설치되어 있어야 합니다

param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Nebula Terminal 설치 파일 빌드" -ForegroundColor Cyan
Write-Host "버전: $Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 프로젝트 루트 디렉토리
$ProjectRoot = $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "src\LinuxServerAI\Nebula.csproj"
$PublishDir = Join-Path $ProjectRoot "src\LinuxServerAI\bin\Release\net8.0-windows\win-x64\publish"
$InstallerScript = Join-Path $ProjectRoot "installer.iss"
$OutputDir = Join-Path $ProjectRoot "installer_output"

# 1. 이전 빌드 정리
Write-Host "[1/4] 이전 빌드 정리 중..." -ForegroundColor Yellow
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
    Write-Host "  ✓ 이전 빌드 삭제 완료" -ForegroundColor Green
}

if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
    Write-Host "  ✓ 이전 설치 파일 삭제 완료" -ForegroundColor Green
}

# 2. 프로젝트 게시 (Self-Contained, Single File)
Write-Host ""
Write-Host "[2/4] 프로젝트 게시 중..." -ForegroundColor Yellow
Write-Host "  - 구성: $Configuration" -ForegroundColor Gray
Write-Host "  - 런타임: win-x64" -ForegroundColor Gray
Write-Host "  - Self-Contained: Yes" -ForegroundColor Gray
Write-Host ""

$publishArgs = @(
    "publish",
    $ProjectFile,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "true",
    "/p:PublishSingleFile=false",  # 단일 파일이 아닌 폴더 형태로 게시
    "/p:DebugType=None",
    "/p:DebugSymbols=false"
)

try {
    $output = & dotnet $publishArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "게시 실패!" -ForegroundColor Red
        Write-Host $output -ForegroundColor Red
        exit 1
    }
    Write-Host "  ✓ 프로젝트 게시 완료" -ForegroundColor Green
}
catch {
    Write-Host "게시 중 오류 발생: $_" -ForegroundColor Red
    exit 1
}

# 3. 게시 결과 확인
Write-Host ""
Write-Host "[3/4] 게시 결과 확인 중..." -ForegroundColor Yellow

if (-not (Test-Path $PublishDir)) {
    Write-Host "  ✗ 게시 디렉토리를 찾을 수 없습니다: $PublishDir" -ForegroundColor Red
    exit 1
}

$exePath = Join-Path $PublishDir "Nebula.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "  ✗ 실행 파일을 찾을 수 없습니다: $exePath" -ForegroundColor Red
    exit 1
}

$fileCount = (Get-ChildItem -Path $PublishDir -Recurse -File).Count
$dirSize = [math]::Round((Get-ChildItem -Path $PublishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 2)

Write-Host "  ✓ 실행 파일: Nebula.exe" -ForegroundColor Green
Write-Host "  ✓ 파일 수: $fileCount" -ForegroundColor Green
Write-Host "  ✓ 총 크기: $dirSize MB" -ForegroundColor Green

# 4. Inno Setup으로 설치 파일 생성
Write-Host ""
Write-Host "[4/4] Inno Setup으로 설치 파일 생성 중..." -ForegroundColor Yellow

# Inno Setup 컴파일러 경로 찾기
$InnoSetupPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 5\ISCC.exe"
)

$ISCC = $null
foreach ($path in $InnoSetupPaths) {
    if (Test-Path $path) {
        $ISCC = $path
        break
    }
}

if (-not $ISCC) {
    Write-Host "  ✗ Inno Setup 컴파일러(ISCC.exe)를 찾을 수 없습니다!" -ForegroundColor Red
    Write-Host "  Inno Setup을 다음 경로 중 하나에 설치해주세요:" -ForegroundColor Yellow
    foreach ($path in $InnoSetupPaths) {
        Write-Host "    - $path" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "  Inno Setup 다운로드: https://jrsoftware.org/isdl.php" -ForegroundColor Cyan
    exit 1
}

Write-Host "  - Inno Setup 컴파일러: $ISCC" -ForegroundColor Gray

try {
    $isccArgs = @(
        "/Q",  # Quiet mode
        "/DMyAppVersion=$Version",
        $InstallerScript
    )

    $output = & $ISCC $isccArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ✗ Inno Setup 컴파일 실패!" -ForegroundColor Red
        Write-Host $output -ForegroundColor Red
        exit 1
    }

    Write-Host "  ✓ 설치 파일 생성 완료" -ForegroundColor Green
}
catch {
    Write-Host "  ✗ Inno Setup 실행 중 오류 발생: $_" -ForegroundColor Red
    exit 1
}

# 5. 완료 메시지
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "빌드 완료!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (Test-Path $OutputDir) {
    $installerFiles = Get-ChildItem -Path $OutputDir -Filter "*.exe"
    if ($installerFiles) {
        Write-Host "설치 파일 위치:" -ForegroundColor Yellow
        foreach ($file in $installerFiles) {
            $fileSize = [math]::Round($file.Length / 1MB, 2)
            Write-Host "  📦 $($file.Name) ($fileSize MB)" -ForegroundColor Cyan
            Write-Host "     $($file.FullName)" -ForegroundColor Gray
        }
    }
}

Write-Host ""
Write-Host "설치 파일을 테스트하려면:" -ForegroundColor Yellow
Write-Host "  1. installer_output 폴더에서 .exe 파일 실행" -ForegroundColor Gray
Write-Host "  2. 설치 마법사 따라 진행" -ForegroundColor Gray
Write-Host ""
