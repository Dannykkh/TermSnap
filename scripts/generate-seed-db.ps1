# 시드 데이터베이스 생성 스크립트
# 실행: .\scripts\generate-seed-db.ps1

Write-Host "=== TermSnap 시드 데이터베이스 생성 ===" -ForegroundColor Cyan

$projectRoot = Split-Path -Parent $PSScriptRoot
$seedDbPath = Join-Path $projectRoot "src\TermSnap\Resources\seed-history.db"
$jsonPath = Join-Path $projectRoot "src\TermSnap\Resources\linux-commands.json"

# 출력 디렉토리 생성
$resourcesDir = Split-Path -Parent $seedDbPath
if (!(Test-Path $resourcesDir)) {
    New-Item -ItemType Directory -Path $resourcesDir | Out-Null
    Write-Host "Resources 폴더 생성: $resourcesDir" -ForegroundColor Green
}

# 기존 시드 DB 삭제
if (Test-Path $seedDbPath) {
    Remove-Item $seedDbPath -Force
    Write-Host "기존 시드 DB 삭제" -ForegroundColor Yellow
}

# JSON 파일 확인
if (!(Test-Path $jsonPath)) {
    Write-Host "오류: JSON 파일을 찾을 수 없습니다: $jsonPath" -ForegroundColor Red
    Write-Host "linux-commands.json 파일을 먼저 생성해주세요." -ForegroundColor Red
    exit 1
}

Write-Host "JSON 파일 발견: $jsonPath" -ForegroundColor Green

# 시드 생성기 도구 빌드
Write-Host "`n시드 생성기 도구 빌드 중..." -ForegroundColor Cyan
$toolBuildOutput = dotnet build "$projectRoot\tools\GenerateSeedDb\GenerateSeedDb.csproj" -c Release 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "도구 빌드 실패!" -ForegroundColor Red
    Write-Host $toolBuildOutput
    exit 1
}

Write-Host "도구 빌드 성공!" -ForegroundColor Green

# 시드 데이터베이스 생성
Write-Host "`n시드 데이터베이스 생성 중..." -ForegroundColor Cyan

$toolPath = Join-Path $projectRoot "tools\GenerateSeedDb\bin\Release\net8.0-windows\GenerateSeedDb.exe"

# 도구 실행
& $toolPath $jsonPath $seedDbPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n시드 DB 생성 실패!" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== 완료! ===" -ForegroundColor Green
Write-Host "시드 DB 위치: $seedDbPath" -ForegroundColor Cyan
Write-Host "이 파일은 프로젝트에 포함되어 배포됩니다." -ForegroundColor Cyan
