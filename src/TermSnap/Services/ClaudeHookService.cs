using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace TermSnap.Services;

/// <summary>
/// Claude Code 장기기억 시스템 자동 설정 서비스
/// - hooks 폴더 생성 (save-conversation.ps1, update-memory.ps1)
/// - .claude/settings.local.json 생성
/// - .claude/conversations 폴더 생성
/// - MEMORY.md, CLAUDE.md 생성
/// </summary>
public static class ClaudeHookService
{
    private const string ClaudeFolderName = ".claude";
    private const string HooksFolderName = "hooks";
    private const string ConversationsFolderName = "conversations";
    private const string SettingsFileName = "settings.local.json";
    private const string MemoryFileName = "MEMORY.md";

    /// <summary>
    /// 장기기억 시스템 전체 설치
    /// Claude Code 실행 전 호출하여 모든 필요 파일/폴더 생성
    /// </summary>
    public static bool InstallMemorySystem(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            return false;

        try
        {
            var anyCreated = false;

            // 1. hooks 폴더 및 스크립트 생성
            anyCreated |= EnsureHooksFolder(workingDirectory);

            // 2. .claude 폴더 및 conversations 폴더 생성
            anyCreated |= EnsureClaudeFolders(workingDirectory);

            // 3. settings.local.json 생성/업데이트
            anyCreated |= EnsureSettingsJson(workingDirectory);

            // 4. MEMORY.md 생성
            anyCreated |= EnsureMemoryMd(workingDirectory);

            // 5. CLAUDE.md에 @MEMORY.md import 추가
            anyCreated |= EnsureClaudeMd(workingDirectory);

            if (anyCreated)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] 장기기억 시스템 설치 완료: {workingDirectory}");
            }

            return anyCreated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] 장기기억 시스템 설치 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 기존 메서드 호환성 유지
    /// </summary>
    public static bool EnsureMemoryHooks(string workingDirectory) => InstallMemorySystem(workingDirectory);

    /// <summary>
    /// 기존 메서드 호환성 유지
    /// </summary>
    public static bool EnsureMemoryReference(string workingDirectory) => EnsureClaudeMd(workingDirectory);

    #region Hooks Folder

    /// <summary>
    /// hooks 폴더 및 스크립트 생성
    /// </summary>
    private static bool EnsureHooksFolder(string workingDirectory)
    {
        var hooksFolder = Path.Combine(workingDirectory, HooksFolderName);
        var created = false;

        // hooks 폴더 생성
        if (!Directory.Exists(hooksFolder))
        {
            Directory.CreateDirectory(hooksFolder);
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] hooks 폴더 생성: {hooksFolder}");
            created = true;
        }

        // save-conversation.ps1 생성
        var saveConversationPath = Path.Combine(hooksFolder, "save-conversation.ps1");
        if (!File.Exists(saveConversationPath))
        {
            File.WriteAllText(saveConversationPath, GenerateSaveConversationScript(), Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] save-conversation.ps1 생성");
            created = true;
        }

        // update-memory.ps1 생성
        var updateMemoryPath = Path.Combine(hooksFolder, "update-memory.ps1");
        if (!File.Exists(updateMemoryPath))
        {
            File.WriteAllText(updateMemoryPath, GenerateUpdateMemoryScript(), Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] update-memory.ps1 생성");
            created = true;
        }

        return created;
    }

    /// <summary>
    /// save-conversation.ps1 스크립트 생성
    /// 매 프롬프트마다 대화를 .claude/conversations/YYYY-MM-DD.md에 저장
    /// </summary>
    private static string GenerateSaveConversationScript()
    {
        return @"# save-conversation.ps1
# 대화 내용을 날짜별 마크다운 파일로 저장
# 사용법: powershell -File hooks/save-conversation.ps1 ""$PROMPT""

param(
    [string]$Prompt
)

# 프로젝트 디렉토리 (스크립트 위치 기준)
$ProjectDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ProjectDir) { $ProjectDir = Get-Location }

# 대화 저장 폴더
$ConversationsDir = Join-Path $ProjectDir "".claude/conversations""
if (-not (Test-Path $ConversationsDir)) {
    New-Item -ItemType Directory -Path $ConversationsDir -Force | Out-Null
}

# 오늘 날짜 파일
$Today = Get-Date -Format ""yyyy-MM-dd""
$LogFile = Join-Path $ConversationsDir ""$Today.md""

# 파일이 없으면 헤더 추가
if (-not (Test-Path $LogFile)) {
    $ProjectName = Split-Path -Leaf $ProjectDir
    $Header = @""
# 대화 기록: $Today
프로젝트: $ProjectName

---

""@
    $Header | Out-File -FilePath $LogFile -Encoding UTF8
}

# 타임스탬프와 함께 프롬프트 추가
$Timestamp = Get-Date -Format ""HH:mm:ss""
$Entry = @""

## [$Timestamp] User
$Prompt

""@

$Entry | Out-File -FilePath $LogFile -Encoding UTF8 -Append

Write-Host ""[Memory] Conversation saved to $LogFile""
";
    }

    /// <summary>
    /// update-memory.ps1 스크립트 생성
    /// 세션 종료 시 Claude를 호출하여 MEMORY.md 자동 업데이트
    /// </summary>
    private static string GenerateUpdateMemoryScript()
    {
        return @"# update-memory.ps1
# 세션 종료 시 오늘 대화를 분석하여 MEMORY.md 업데이트
# 사용법: powershell -File hooks/update-memory.ps1

# 프로젝트 디렉토리
$ProjectDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ProjectDir) { $ProjectDir = Get-Location }

# 오늘 대화 로그 파일
$Today = Get-Date -Format ""yyyy-MM-dd""
$LogFile = Join-Path $ProjectDir "".claude/conversations/$Today.md""
$MemoryFile = Join-Path $ProjectDir ""MEMORY.md""

# 대화 로그와 MEMORY.md 둘 다 있어야 실행
if (-not (Test-Path $LogFile)) {
    Write-Host ""[Memory] No conversation log for today. Skipping memory update.""
    exit 0
}

if (-not (Test-Path $MemoryFile)) {
    Write-Host ""[Memory] MEMORY.md not found. Skipping memory update.""
    exit 0
}

Write-Host ""[Memory] Analyzing today's conversation and updating MEMORY.md...""

# Claude CLI를 사용하여 메모리 업데이트
# --print: 비대화형 모드
# --max-turns 3: 빠른 완료를 위해 턴 수 제한
$Prompt = @""
You are a memory-writer agent. Analyze today's conversation log and update MEMORY.md.

Instructions:
1. Read the conversation log: $LogFile
2. Extract important information (decisions, preferences, lessons learned, technical choices)
3. Update MEMORY.md with new entries under appropriate sections
4. Do NOT duplicate existing entries
5. Keep entries concise (1-2 sentences each)
6. Add timestamp to new entries

Sections in MEMORY.md:
- 프로젝트: Project-level information
- 사실: Facts about the user/project
- 선호도: User preferences
- 기술 스택: Technology choices
- 작업 패턴: Work patterns
- 지침: Instructions/rules
- 학습된 교훈: Lessons learned

Please update MEMORY.md now.
""@

try {
    claude --print --max-turns 3 $Prompt
    Write-Host ""[Memory] MEMORY.md updated successfully.""
} catch {
    Write-Host ""[Memory] Failed to update MEMORY.md: $_""
    Write-Host ""[Memory] You can manually run: claude 'MEMORY.md를 오늘 대화 기반으로 업데이트해줘'""
}
";
    }

    #endregion

    #region Claude Folders

    /// <summary>
    /// .claude 폴더 및 conversations 폴더 생성
    /// </summary>
    private static bool EnsureClaudeFolders(string workingDirectory)
    {
        var created = false;

        // .claude 폴더
        var claudeFolder = Path.Combine(workingDirectory, ClaudeFolderName);
        if (!Directory.Exists(claudeFolder))
        {
            Directory.CreateDirectory(claudeFolder);
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] .claude 폴더 생성: {claudeFolder}");
            created = true;
        }

        // .claude/conversations 폴더
        var conversationsFolder = Path.Combine(claudeFolder, ConversationsFolderName);
        if (!Directory.Exists(conversationsFolder))
        {
            Directory.CreateDirectory(conversationsFolder);
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] conversations 폴더 생성: {conversationsFolder}");
            created = true;
        }

        // .gitignore에 conversations 추가 (있으면)
        EnsureGitIgnore(workingDirectory);

        return created;
    }

    /// <summary>
    /// .gitignore에 conversations 폴더 추가
    /// </summary>
    private static void EnsureGitIgnore(string workingDirectory)
    {
        var gitignorePath = Path.Combine(workingDirectory, ".gitignore");
        if (!File.Exists(gitignorePath)) return;

        try
        {
            var content = File.ReadAllText(gitignorePath, Encoding.UTF8);
            if (!content.Contains(".claude/conversations"))
            {
                var addition = "\n# Claude conversation logs (auto-generated)\n.claude/conversations/\n";
                File.AppendAllText(gitignorePath, addition, Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine("[ClaudeHook] .gitignore에 conversations 추가");
            }
        }
        catch { }
    }

    #endregion

    #region Settings JSON

    /// <summary>
    /// settings.local.json 생성/업데이트
    /// </summary>
    private static bool EnsureSettingsJson(string workingDirectory)
    {
        var claudeFolder = Path.Combine(workingDirectory, ClaudeFolderName);
        var settingsPath = Path.Combine(claudeFolder, SettingsFileName);

        // 기존 설정 로드 또는 새로 생성
        JsonObject settings;
        if (File.Exists(settingsPath))
        {
            try
            {
                var existingContent = File.ReadAllText(settingsPath, Encoding.UTF8);
                settings = JsonNode.Parse(existingContent)?.AsObject() ?? new JsonObject();

                // 이미 완전한 메모리 훅이 있으면 스킵
                if (HasCompleteMemoryHooks(settings))
                {
                    System.Diagnostics.Debug.WriteLine("[ClaudeHook] 메모리 훅이 이미 완전히 설정됨 - 스킵");
                    return false;
                }
            }
            catch
            {
                settings = new JsonObject();
            }
        }
        else
        {
            settings = new JsonObject();
        }

        // 훅 설정 추가
        AddMemoryHooks(settings);

        // 저장
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = settings.ToJsonString(options);
        File.WriteAllText(settingsPath, json, Encoding.UTF8);

        System.Diagnostics.Debug.WriteLine($"[ClaudeHook] settings.local.json 저장: {settingsPath}");
        return true;
    }

    /// <summary>
    /// 완전한 메모리 훅이 있는지 확인 (UserPromptSubmit + Stop)
    /// </summary>
    private static bool HasCompleteMemoryHooks(JsonObject settings)
    {
        if (!settings.ContainsKey("hooks"))
            return false;

        var hooks = settings["hooks"]?.AsObject();
        if (hooks == null)
            return false;

        var hasUserPromptHook = false;
        var hasStopHook = false;

        // UserPromptSubmit에 save-conversation 훅 확인
        if (hooks.ContainsKey("UserPromptSubmit"))
        {
            var hookArray = hooks["UserPromptSubmit"]?.AsArray();
            if (hookArray != null)
            {
                foreach (var hook in hookArray)
                {
                    var command = hook?["hooks"]?[0]?["command"]?.ToString();
                    if (command != null && command.Contains("save-conversation"))
                    {
                        hasUserPromptHook = true;
                        break;
                    }
                }
            }
        }

        // Stop에 update-memory 훅 확인
        if (hooks.ContainsKey("Stop"))
        {
            var hookArray = hooks["Stop"]?.AsArray();
            if (hookArray != null)
            {
                foreach (var hook in hookArray)
                {
                    var command = hook?["hooks"]?[0]?["command"]?.ToString();
                    if (command != null && command.Contains("update-memory"))
                    {
                        hasStopHook = true;
                        break;
                    }
                }
            }
        }

        return hasUserPromptHook && hasStopHook;
    }

    /// <summary>
    /// 메모리 훅 추가
    /// </summary>
    private static void AddMemoryHooks(JsonObject settings)
    {
        // hooks 객체 가져오기 또는 생성
        if (!settings.ContainsKey("hooks"))
        {
            settings["hooks"] = new JsonObject();
        }
        var hooks = settings["hooks"]!.AsObject();

        // 1. UserPromptSubmit 훅 - 매 프롬프트마다 대화 저장
        var userPromptHooks = hooks["UserPromptSubmit"]?.AsArray() ?? new JsonArray();

        // 기존에 save-conversation 훅이 없으면 추가
        var hasSaveHook = false;
        foreach (var hook in userPromptHooks)
        {
            var command = hook?["hooks"]?[0]?["command"]?.ToString();
            if (command != null && command.Contains("save-conversation"))
            {
                hasSaveHook = true;
                break;
            }
        }

        if (!hasSaveHook)
        {
            userPromptHooks.Add(new JsonObject
            {
                ["matcher"] = ".*",  // 모든 프롬프트에 대해 실행
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = "powershell -ExecutionPolicy Bypass -File hooks/save-conversation.ps1 \"$PROMPT\""
                    }
                }
            });
        }

        hooks["UserPromptSubmit"] = userPromptHooks;

        // 2. Stop 훅 - 세션 종료 시 MEMORY.md 업데이트
        var stopHooks = hooks["Stop"]?.AsArray() ?? new JsonArray();

        // 기존에 update-memory 훅이 없으면 추가
        var hasUpdateHook = false;
        foreach (var hook in stopHooks)
        {
            var command = hook?["hooks"]?[0]?["command"]?.ToString();
            if (command != null && command.Contains("update-memory"))
            {
                hasUpdateHook = true;
                break;
            }
        }

        if (!hasUpdateHook)
        {
            stopHooks.Add(new JsonObject
            {
                ["matcher"] = ".*",  // 모든 종료에 대해 실행
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = "powershell -ExecutionPolicy Bypass -File hooks/update-memory.ps1"
                    }
                }
            });
        }

        hooks["Stop"] = stopHooks;

        // 메타 정보 추가
        settings["_termsnap_memory_system"] = "v1.0 - Auto-installed by TermSnap";
        settings["_termsnap_installed_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    #endregion

    #region MEMORY.md & CLAUDE.md

    /// <summary>
    /// MEMORY.md 생성
    /// </summary>
    private static bool EnsureMemoryMd(string workingDirectory)
    {
        var memoryPath = Path.Combine(workingDirectory, MemoryFileName);
        if (File.Exists(memoryPath))
            return false;

        var content = GenerateMemoryMd(workingDirectory);
        File.WriteAllText(memoryPath, content, Encoding.UTF8);
        System.Diagnostics.Debug.WriteLine($"[ClaudeHook] MEMORY.md 생성: {memoryPath}");
        return true;
    }

    /// <summary>
    /// CLAUDE.md에 @MEMORY.md import 추가
    /// </summary>
    private static bool EnsureClaudeMd(string workingDirectory)
    {
        var claudeMdPath = Path.Combine(workingDirectory, "CLAUDE.md");

        // CLAUDE.md가 없으면 생성
        if (!File.Exists(claudeMdPath))
        {
            var content = GenerateClaudeMd(workingDirectory);
            File.WriteAllText(claudeMdPath, content, Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] CLAUDE.md 생성: {claudeMdPath}");
            return true;
        }

        // 이미 @MEMORY.md import가 있는지 확인
        var existingContent = File.ReadAllText(claudeMdPath, Encoding.UTF8);
        if (existingContent.Contains("@MEMORY.md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // @MEMORY.md import 구문 추가 (파일 상단에)
        var importLine = "@MEMORY.md\n\n";
        var newContent = importLine + existingContent;
        File.WriteAllText(claudeMdPath, newContent, Encoding.UTF8);
        System.Diagnostics.Debug.WriteLine("[ClaudeHook] CLAUDE.md에 @MEMORY.md import 추가");
        return true;
    }

    /// <summary>
    /// MEMORY.md 내용 생성
    /// </summary>
    private static string GenerateMemoryMd(string workingDirectory)
    {
        var projectName = Path.GetFileName(workingDirectory);
        var dateStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        return $@"# AI 장기기억 (MEMORY.md)

> 이 파일은 AI가 참조하는 장기기억입니다. CLAUDE.md에서 이 파일을 참조합니다.
> 세션 종료 시 자동으로 업데이트되며, 직접 편집해도 됩니다.

## 프로젝트

- 프로젝트: {projectName}
- 생성일: {dateStr}

## 사실

## 선호도

## 기술 스택

## 작업 패턴

## 지침

## 학습된 교훈

---
*마지막 업데이트: {dateStr}*
";
    }

    /// <summary>
    /// CLAUDE.md 내용 생성
    /// </summary>
    private static string GenerateClaudeMd(string workingDirectory)
    {
        var projectName = Path.GetFileName(workingDirectory);
        var dateStr = DateTime.Now.ToString("yyyy-MM-dd");

        return $@"@MEMORY.md

# CLAUDE.md

이 프로젝트의 Claude Code 설정 파일입니다.

## 프로젝트 정보

- 프로젝트: {projectName}
- 생성일: {dateStr}

## 장기기억 시스템

이 프로젝트는 TermSnap의 장기기억 시스템을 사용합니다:
- 매 대화는 `.claude/conversations/YYYY-MM-DD.md`에 자동 저장됩니다
- 세션 종료 시 중요 내용이 `MEMORY.md`에 자동 정리됩니다
- ""기억해"" 또는 ""remember"" 명령으로 즉시 저장할 수 있습니다

## 코딩 스타일

- 한국어 주석 선호
- 간결하고 명확한 코드
- 적절한 에러 처리

## 규칙

- 기존 코드 스타일 유지
- 테스트 코드 작성 권장
- 커밋 메시지는 한국어로
";
    }

    #endregion
}
