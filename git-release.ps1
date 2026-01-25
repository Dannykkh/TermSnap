# TermSnap Git Release 스크립트
# 버전 업데이트, 빌드, GitHub 릴리즈 생성, 브랜치 머지를 자동화합니다

param(
    [Parameter(Mandatory=$false)]
    [string]$NewVersion,

    [Parameter(Mandatory=$false)]
    [switch]$SkipBuild,

    [Parameter(Mandatory=$false)]
    [switch]$SkipTests,

    [Parameter(Mandatory=$false)]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# 색상 출력 함수
function Write-Step {
    param([string]$Message)
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "  ✓ $Message" -ForegroundColor Green
}

function Write-Error {
    param([string]$Message)
    Write-Host "  ✗ $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host "  → $Message" -ForegroundColor Yellow
}

# 프로젝트 경로
$ProjectRoot = $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "src\TermSnap\TermSnap.csproj"
$BuildScript = Join-Path $ProjectRoot "build-installer.ps1"
$InstallerScript = Join-Path $ProjectRoot "installer.iss"
$ReleaseNotes = Join-Path $ProjectRoot "RELEASE_NOTES.md"
$InstallerDir = Join-Path $ProjectRoot "installer"

# Git 브랜치 확인
Write-Step "1. Git 상태 확인"

$currentBranch = git rev-parse --abbrev-ref HEAD
Write-Info "현재 브랜치: $currentBranch"

if ($currentBranch -ne "develop") {
    Write-Error "develop 브랜치에서 실행해야 합니다"
    Write-Info "실행: git checkout develop"
    exit 1
}

$gitStatus = git status --porcelain
if ($gitStatus) {
    Write-Error "커밋되지 않은 변경사항이 있습니다"
    Write-Info "먼저 변경사항을 커밋하거나 stash 하세요"
    git status --short
    exit 1
}

Write-Success "Git 상태 확인 완료"

# 현재 버전 읽기
Write-Step "2. 현재 버전 확인"

$csprojContent = Get-Content $ProjectFile -Raw
if ($csprojContent -match '<Version>([\d\.]+)</Version>') {
    $currentVersion = $matches[1]
    Write-Info "현재 버전: $currentVersion"
} else {
    Write-Error "프로젝트 파일에서 버전을 찾을 수 없습니다"
    exit 1
}

# 새 버전 입력
if (-not $NewVersion) {
    Write-Host "`n새 버전 번호를 입력하세요 (예: 1.4.0): " -ForegroundColor Yellow -NoNewline
    $NewVersion = Read-Host
}

if (-not $NewVersion -match '^\d+\.\d+\.\d+$') {
    Write-Error "올바른 버전 형식이 아닙니다 (예: 1.4.0)"
    exit 1
}

Write-Success "새 버전: $NewVersion"

# 버전 비교
$cv = [version]$currentVersion
$nv = [version]$NewVersion
if ($nv -le $cv) {
    Write-Error "새 버전($NewVersion)은 현재 버전($currentVersion)보다 높아야 합니다"
    exit 1
}

# Dry Run 체크
if ($DryRun) {
    Write-Info "DRY RUN 모드: 실제 변경사항 없이 시뮬레이션만 수행합니다"
    Write-Host ""
}

# 버전 업데이트
Write-Step "3. 버전 업데이트"

if (-not $DryRun) {
    # csproj 업데이트
    $csprojContent = $csprojContent -replace '<Version>[\d\.]+</Version>', "<Version>$NewVersion</Version>"
    $csprojContent = $csprojContent -replace '<AssemblyVersion>[\d\.]+\.0</AssemblyVersion>', "<AssemblyVersion>$NewVersion.0</AssemblyVersion>"
    $csprojContent = $csprojContent -replace '<FileVersion>[\d\.]+\.0</FileVersion>', "<FileVersion>$NewVersion.0</FileVersion>"
    Set-Content -Path $ProjectFile -Value $csprojContent -Encoding UTF8
    Write-Success "TermSnap.csproj 업데이트 완료"

    # build-installer.ps1 업데이트
    $buildContent = Get-Content $BuildScript -Raw
    $buildContent = $buildContent -replace '\[string\]\$Version = "[\d\.]+"', "[string]`$Version = `"$NewVersion`""
    Set-Content -Path $BuildScript -Value $buildContent -Encoding UTF8
    Write-Success "build-installer.ps1 업데이트 완료"

    # installer.iss 업데이트
    $issContent = Get-Content $InstallerScript -Raw
    $issContent = $issContent -replace '#define MyAppVersion "[\d\.]+"', "#define MyAppVersion `"$NewVersion`""
    Set-Content -Path $InstallerScript -Value $issContent -Encoding UTF8
    Write-Success "installer.iss 업데이트 완료"
} else {
    Write-Info "[DRY RUN] 버전 업데이트 건너뜀"
}

# 릴리즈 노트 자동 생성
Write-Step "4. 릴리즈 노트 생성"

$lastTag = git describe --tags --abbrev=0 2>$null
if (-not $lastTag) {
    $lastTag = git rev-list --max-parents=0 HEAD
    Write-Info "이전 태그를 찾을 수 없어 첫 커밋부터 생성합니다"
} else {
    Write-Info "이전 태그: $lastTag"
}

# git log에서 커밋 메시지 추출
$commits = git log "$lastTag..HEAD" --pretty=format:"%s" 2>$null
if (-not $commits) {
    Write-Error "새로운 커밋이 없습니다"
    exit 1
}

# 커밋 분류
$features = @()
$fixes = @()
$chores = @()
$others = @()

foreach ($commit in $commits) {
    if ($commit -match '^feat(\(.+\))?:\s*(.+)') {
        $features += $matches[2]
    } elseif ($commit -match '^fix(\(.+\))?:\s*(.+)') {
        $fixes += $matches[2]
    } elseif ($commit -match '^chore(\(.+\))?:\s*(.+)') {
        $chores += $matches[2]
    } else {
        $others += $commit
    }
}

# 릴리즈 노트 생성
$date = Get-Date -Format "yyyy-MM-dd"
$releaseNoteContent = @"
# Release Notes

## v$NewVersion - $date

"@

if ($features.Count -gt 0 -or $fixes.Count -gt 0 -or $others.Count -gt 0) {
    if ($features.Count -gt 0) {
        $releaseNoteContent += @"

### 🎉 New Features

"@
        foreach ($feature in $features) {
            $releaseNoteContent += "- $feature`n"
        }
    }

    if ($fixes.Count -gt 0) {
        $releaseNoteContent += @"

### 🐛 Bug Fixes

"@
        foreach ($fix in $fixes) {
            $releaseNoteContent += "- $fix`n"
        }
    }

    if ($others.Count -gt 0) {
        $releaseNoteContent += @"

### 📝 Other Changes

"@
        foreach ($other in $others) {
            $releaseNoteContent += "- $other`n"
        }
    }
}

$releaseNoteContent += @"

---

"@

# 기존 릴리즈 노트가 있으면 추가
if (Test-Path $ReleaseNotes) {
    $existingContent = Get-Content $ReleaseNotes -Raw
    # 기존 내용에서 "# Release Notes" 헤더 제거
    $existingContent = $existingContent -replace '^# Release Notes\s*\n', ''
    $releaseNoteContent += $existingContent
}

if (-not $DryRun) {
    Set-Content -Path $ReleaseNotes -Value $releaseNoteContent -Encoding UTF8
    Write-Success "RELEASE_NOTES.md 생성 완료"
    Write-Info "생성된 항목: Features($($features.Count)), Fixes($($fixes.Count)), Others($($others.Count))"
} else {
    Write-Info "[DRY RUN] 릴리즈 노트 생성 건너뜀"
    Write-Host "`n--- 생성될 릴리즈 노트 미리보기 ---" -ForegroundColor Gray
    Write-Host $releaseNoteContent -ForegroundColor Gray
    Write-Host "--- 미리보기 끝 ---`n" -ForegroundColor Gray
}

# 빌드 및 인스톨러 생성
if (-not $SkipBuild) {
    Write-Step "5. 빌드 및 인스톨러 생성"

    if (-not $DryRun) {
        Write-Info "릴리즈 빌드 실행 중..."
        & dotnet build -c Release
        if ($LASTEXITCODE -ne 0) {
            Write-Error "빌드 실패"
            exit 1
        }
        Write-Success "빌드 완료"

        Write-Info "인스톨러 생성 중..."
        & powershell -ExecutionPolicy Bypass -File $BuildScript
        if ($LASTEXITCODE -ne 0) {
            Write-Error "인스톨러 생성 실패"
            exit 1
        }
        Write-Success "인스톨러 생성 완료"
    } else {
        Write-Info "[DRY RUN] 빌드 및 인스톨러 생성 건너뜀"
    }
} else {
    Write-Info "빌드 건너뜀 (--SkipBuild)"
}

# Git 커밋
Write-Step "6. Git 커밋 및 푸시"

if (-not $DryRun) {
    git add -A

    $commitMessage = @"
chore: bump version to v$NewVersion

Release v$NewVersion with the following changes:
- Features: $($features.Count) items
- Bug Fixes: $($fixes.Count) items
- Other Changes: $($others.Count) items

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
"@

    git commit -m $commitMessage
    Write-Success "커밋 완료"

    Write-Info "develop 브랜치에 푸시 중..."
    git push origin develop
    Write-Success "develop 푸시 완료"
} else {
    Write-Info "[DRY RUN] Git 커밋 및 푸시 건너뜀"
}

# GitHub 릴리즈 생성
Write-Step "7. GitHub 릴리즈 생성"

$installerPath = Join-Path $InstallerDir "TermSnap-Setup-v$NewVersion.exe"

if (-not $DryRun) {
    if (-not (Test-Path $installerPath)) {
        Write-Error "인스톨러 파일을 찾을 수 없습니다: $installerPath"
        exit 1
    }

    # 릴리즈 노트에서 현재 버전 섹션만 추출
    $releaseNotesForGH = $releaseNoteContent -split "---" | Select-Object -First 1
    $releaseNotesForGH = $releaseNotesForGH -replace "# Release Notes\s*\n", ""
    $releaseNotesForGH = $releaseNotesForGH.Trim()

    Write-Info "GitHub 릴리즈 생성 중..."
    $ghOutput = gh release create "v$NewVersion" `
        $installerPath `
        --title "v$NewVersion" `
        --notes $releaseNotesForGH

    if ($LASTEXITCODE -ne 0) {
        Write-Error "GitHub 릴리즈 생성 실패"
        exit 1
    }

    Write-Success "GitHub 릴리즈 생성 완료"
    Write-Info "릴리즈 URL: $ghOutput"
} else {
    Write-Info "[DRY RUN] GitHub 릴리즈 생성 건너뜀"
}

# master 브랜치 머지
Write-Step "8. master 브랜치에 머지"

if (-not $DryRun) {
    git checkout master
    git pull origin master
    Write-Success "master 브랜치로 전환 완료"

    $mergeMessage = @"
chore: merge develop into master for v$NewVersion release

Release v$NewVersion

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
"@

    git merge develop --no-ff -m $mergeMessage
    Write-Success "develop → master 머지 완료"

    Write-Info "master 브랜치에 푸시 중..."
    git push origin master
    Write-Success "master 푸시 완료"

    git checkout develop
    Write-Success "develop 브랜치로 복귀"
} else {
    Write-Info "[DRY RUN] master 머지 건너뜀"
}

# 완료
Write-Step "✨ 릴리즈 완료!"

Write-Host ""
Write-Success "버전: $currentVersion → $NewVersion"
Write-Success "브랜치: develop, master 모두 업데이트됨"
if (-not $DryRun) {
    Write-Success "GitHub 릴리즈: https://github.com/Dannykkh/TermSnap/releases/tag/v$NewVersion"
    Write-Success "인스톨러: $installerPath"
}
Write-Host ""

if ($DryRun) {
    Write-Host "이것은 DRY RUN이었습니다. 실제로 실행하려면 -DryRun 플래그를 제거하세요." -ForegroundColor Yellow
}
