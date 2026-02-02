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

            // 6. 에이전트 설치 (memory-writer, keyword-extractor)
            anyCreated |= InstallMemoryAgents(workingDirectory);

            // 7. 스킬 설치 (long-term-memory)
            anyCreated |= InstallMemorySkills(workingDirectory);

            // 8. 가이드 문서 설치
            anyCreated |= InstallGuideDocuments(workingDirectory);

            // 9. 오케스트라 MCP 서버 설치
            anyCreated |= InstallOrchestratorMCP(workingDirectory);

            // 10. 오케스트라 커맨드 설치 (workpm, pmworker)
            anyCreated |= InstallOrchestratorCommands(workingDirectory);

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

        // workpm-hook.ps1 생성 (PM 모드 시동어 훅)
        var workpmHookPath = Path.Combine(hooksFolder, "workpm-hook.ps1");
        if (!File.Exists(workpmHookPath))
        {
            File.WriteAllText(workpmHookPath, GenerateWorkPMHookScript(), Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] workpm-hook.ps1 생성");
            created = true;
        }

        // pmworker-hook.ps1 생성 (Worker 모드 시동어 훅)
        var pmworkerHookPath = Path.Combine(hooksFolder, "pmworker-hook.ps1");
        if (!File.Exists(pmworkerHookPath))
        {
            File.WriteAllText(pmworkerHookPath, GeneratePMWorkerHookScript(), Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] pmworker-hook.ps1 생성");
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

    /// <summary>
    /// workpm-hook.ps1 스크립트 생성
    /// PM 모드 시동어 훅 - 프롬프트에 workpm이 포함되면 PM 모드 컨텍스트 주입
    /// </summary>
    private static string GenerateWorkPMHookScript()
    {
        return @"# workpm-hook.ps1
# PM 모드 시동어 훅 - workpm 입력 시 PM 모드 활성화
# 출력 내용은 Claude의 additional context로 주입됨

Write-Host @""
[PM MODE ACTIVATED]

당신은 Multi-AI Orchestrator의 PM(Project Manager)입니다.

## 시작 절차

1. **AI Provider 감지**
   orchestrator_detect_providers 도구로 설치된 AI CLI 확인

2. **플랜 파일 로드**
   orchestrator_get_latest_plan으로 최신 플랜 자동 로드
   플랜 파일을 분석하여 작업 목록 추출

3. **프로젝트 분석**
   orchestrator_analyze_codebase로 코드 구조 파악

4. **태스크 생성**
   orchestrator_create_task로 태스크 생성
   - 의존성(depends_on) 설정
   - scope 명시 (수정 가능 파일)
   - AI Provider 배정 (강점에 따라)

5. **모니터링**
   orchestrator_get_progress로 진행 상황 확인

## AI 배정 가이드

| 태스크 유형 | 추천 AI |
|------------|---------|
| 코드 생성 | codex |
| 리팩토링 | claude |
| 코드 리뷰 | gemini |
| 문서 작성 | claude |

## Worker 추가

다른 터미널에서 'pmworker'를 입력하면 Worker가 추가됩니다.

---
지금 바로 orchestrator_detect_providers를 호출하여 시작하세요.
""@
";
    }

    /// <summary>
    /// pmworker-hook.ps1 스크립트 생성
    /// Worker 모드 시동어 훅 - 프롬프트에 pmworker가 포함되면 Worker 모드 컨텍스트 주입
    /// </summary>
    private static string GeneratePMWorkerHookScript()
    {
        return @"# pmworker-hook.ps1
# Worker 모드 시동어 훅 - pmworker 입력 시 Worker 모드 활성화
# 출력 내용은 Claude의 additional context로 주입됨

Write-Host @""
[WORKER MODE ACTIVATED]

당신은 Multi-AI Orchestrator의 Worker입니다.
PM이 생성한 태스크를 가져와 처리합니다.

## 작업 루프

```
while (태스크가 있는 동안) {
    1. orchestrator_get_available_tasks
    2. orchestrator_claim_task(첫번째_태스크)
    3. orchestrator_lock_file(수정할_파일들)
    4. 작업_수행
    5. orchestrator_complete_task 또는 orchestrator_fail_task
}
```

## 중요 규칙

1. **반드시 claim 먼저**: 작업 전 반드시 claim_task 호출
2. **lock 먼저**: 파일 수정 전 반드시 lock_file 호출
3. **scope 준수**: 태스크 scope 외의 파일 수정 금지
4. **완료 보고**: 작업 후 반드시 complete_task 또는 fail_task

## 파일 락 충돌 시

- 다른 Worker가 락 보유 중이면 잠시 대기
- 다른 가용 태스크가 있으면 먼저 처리

---
지금 바로 orchestrator_get_available_tasks를 호출하여 시작하세요.
""@
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

        // 3. workpm 훅 - PM 모드 시동어
        var hasWorkpmHook = false;
        foreach (var hook in userPromptHooks)
        {
            var matcher = hook?["matcher"]?.ToString();
            if (matcher != null && matcher.Contains("workpm"))
            {
                hasWorkpmHook = true;
                break;
            }
        }

        if (!hasWorkpmHook)
        {
            userPromptHooks.Add(new JsonObject
            {
                ["matcher"] = "(?i)^\\s*workpm",  // workpm으로 시작하는 프롬프트
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = "powershell -ExecutionPolicy Bypass -File hooks/workpm-hook.ps1"
                    }
                }
            });
        }

        // 4. pmworker 훅 - Worker 모드 시동어
        var hasPmworkerHook = false;
        foreach (var hook in userPromptHooks)
        {
            var matcher = hook?["matcher"]?.ToString();
            if (matcher != null && matcher.Contains("pmworker"))
            {
                hasPmworkerHook = true;
                break;
            }
        }

        if (!hasPmworkerHook)
        {
            userPromptHooks.Add(new JsonObject
            {
                ["matcher"] = "(?i)^\\s*pmworker",  // pmworker로 시작하는 프롬프트
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = "powershell -ExecutionPolicy Bypass -File hooks/pmworker-hook.ps1"
                    }
                }
            });
        }

        hooks["UserPromptSubmit"] = userPromptHooks;

        // 메타 정보 추가
        settings["_termsnap_memory_system"] = "v1.0 - Auto-installed by TermSnap";
        settings["_termsnap_installed_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    #endregion

    #region Memory Agents & Skills

    /// <summary>
    /// 장기기억 관련 에이전트 설치
    /// - memory-writer.md: 메모리 정리 에이전트
    /// - keyword-extractor.md: 키워드 추출 에이전트
    /// </summary>
    private static bool InstallMemoryAgents(string workingDirectory)
    {
        var agentsFolder = Path.Combine(workingDirectory, ClaudeFolderName, "agents");
        if (!Directory.Exists(agentsFolder))
        {
            Directory.CreateDirectory(agentsFolder);
        }

        var anyCreated = false;

        // memory-writer.md
        var memoryWriterPath = Path.Combine(agentsFolder, "memory-writer.md");
        if (!File.Exists(memoryWriterPath))
        {
            File.WriteAllText(memoryWriterPath, GetMemoryWriterAgent(), Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine("[ClaudeHook] memory-writer.md 에이전트 설치됨");
            anyCreated = true;
        }

        // keyword-extractor.md
        var keywordExtractorPath = Path.Combine(agentsFolder, "keyword-extractor.md");
        if (!File.Exists(keywordExtractorPath))
        {
            File.WriteAllText(keywordExtractorPath, GetKeywordExtractorAgent(), Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine("[ClaudeHook] keyword-extractor.md 에이전트 설치됨");
            anyCreated = true;
        }

        return anyCreated;
    }

    /// <summary>
    /// 장기기억 관련 스킬 설치
    /// - long-term-memory: 메모리 관리 스킬
    /// </summary>
    private static bool InstallMemorySkills(string workingDirectory)
    {
        var skillsFolder = Path.Combine(workingDirectory, ClaudeFolderName, "skills", "long-term-memory");
        if (!Directory.Exists(skillsFolder))
        {
            Directory.CreateDirectory(skillsFolder);
        }

        var anyCreated = false;

        // SKILL.md
        var skillPath = Path.Combine(skillsFolder, "SKILL.md");
        if (!File.Exists(skillPath))
        {
            File.WriteAllText(skillPath, GetLongTermMemorySkill(), Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine("[ClaudeHook] long-term-memory 스킬 설치됨");
            anyCreated = true;
        }

        return anyCreated;
    }

    private static string GetMemoryWriterAgent()
    {
        return @"# Memory Writer Agent

세션 종료 시 대화를 분석하여 MEMORY.md를 자동 업데이트하는 에이전트입니다.

## 역할

- 오늘 대화에서 중요한 결정사항 추출
- MEMORY.md의 적절한 섹션에 정보 추가
- 중복 방지 및 간결한 정리

## 사용 시점

- Stop 훅에서 자동 호출
- 수동: ""memory-writer 에이전트로 MEMORY.md 업데이트해줘""

## 동작

1. `.claude/conversations/YYYY-MM-DD.md` 읽기
2. 중요 정보 추출:
   - 아키텍처/설계 결정
   - 버그 원인과 해결책
   - 기술 스택 선택 이유
   - 반복되는 작업 패턴
3. MEMORY.md 적절한 섹션에 추가
4. 타임스탬프 포함

## 추출 대상

| 섹션 | 추출 내용 |
|------|----------|
| 사실 | 프로젝트/사용자 관련 사실 |
| 선호도 | 코딩 스타일, 도구 선호 |
| 기술 스택 | 사용 중인 기술 |
| 작업 패턴 | 반복 작업 방식 |
| 지침 | 규칙, 주의사항 |
| 학습된 교훈 | 문제 해결 경험 |

## 주의사항

- 기존 내용과 중복 금지
- 1-2문장으로 간결하게
- 날짜 타임스탬프 필수
";
    }

    private static string GetKeywordExtractorAgent()
    {
        return @"# Keyword Extractor Agent

대화 로그에서 키워드를 추출하고 인덱스를 업데이트하는 에이전트입니다.

## 역할

- 대화에서 핵심 키워드 10-20개 추출
- frontmatter keywords 필드 업데이트
- index.json 인덱스 업데이트
- 1-2문장 요약 생성

## 사용 시점

- Stop 훅에서 자동 호출
- 수동: ""keyword-extractor 에이전트로 키워드 추출해줘""

## 추출 대상

| 유형 | 예시 |
|------|------|
| 기술 스택 | react, typescript, python, docker |
| 기능/모듈 | orchestrator, authentication, caching |
| 파일/경로 | state-manager.ts, launch.ps1 |
| 작업 유형 | refactor, implement, fix, config |
| 주요 결정 | jwt-선택, redis-도입 |

## 출력 형식

**frontmatter 업데이트:**
```yaml
---
date: 2026-02-02
project: my-project
keywords: [react, hooks, authentication, jwt, refactor]
summary: ""React 훅 최적화 및 JWT 인증 구현""
---
```

**index.json 업데이트:**
```json
{
  ""conversations"": [...],
  ""keywordIndex"": {
    ""react"": [""2026-02-02""],
    ""jwt"": [""2026-02-02""]
  }
}
```
";
    }

    private static string GetLongTermMemorySkill()
    {
        return @"---
name: long-term-memory
description: 장기기억 관리 스킬 - 기억 추가, 검색, 조회
trigger: /memory
auto_trigger:
  - ""기억해""
  - ""remember""
  - ""기억 찾아""
  - ""이전에 뭘 했""
---

# Long-term Memory Skill

세션 간 컨텍스트 유지를 위한 장기기억 관리 스킬입니다.

## 명령어

### /memory add <내용>
정보를 MEMORY.md에 저장합니다.

```
/memory add Redis TTL은 항상 1시간으로 설정
기억해: API 키는 환경변수로 관리
```

### /memory search <키워드>
MEMORY.md에서 키워드 검색합니다.

```
/memory search redis
redis 관련 기억 찾아줘
```

### /memory find <키워드>
대화 로그 인덱스에서 검색합니다 (RAG 스타일).

```
/memory find orchestrator
이전에 orchestrator 구현한 적 있어?
```

### /memory read <날짜>
특정 날짜의 대화 로그를 읽습니다.

```
/memory read 2026-02-02
어제 대화 보여줘
```

### /memory tag <키워드들>
오늘 대화에 키워드를 수동 추가합니다.

```
/memory tag oauth, jwt, authentication
```

### /memory list
MEMORY.md 전체 내용을 표시합니다.

```
/memory list
장기기억 보여줘
```

## 파일 구조

```
프로젝트/
├── MEMORY.md                    # 구조화된 장기기억
├── CLAUDE.md                    # @MEMORY.md 참조
└── .claude/
    └── conversations/           # 대화 로그
        ├── 2026-02-02.md
        ├── 2026-02-01.md
        └── index.json           # 키워드 인덱스
```

## 자동 동작

1. **UserPromptSubmit**: 모든 대화 자동 저장
2. **Stop**: 키워드 추출 + MEMORY.md 업데이트
";
    }

    #endregion

    #region Guide Documents

    /// <summary>
    /// 가이드 문서 설치 (.claude/docs/)
    /// - memory-system.md: 장기기억 시스템 가이드
    /// - orchestrator-guide.md: 오케스트레이터 가이드
    /// </summary>
    public static bool InstallGuideDocuments(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            return false;

        try
        {
            var claudeFolder = Path.Combine(workingDirectory, ClaudeFolderName);
            var docsFolder = Path.Combine(claudeFolder, "docs");

            // .claude/docs 폴더 생성
            if (!Directory.Exists(docsFolder))
            {
                Directory.CreateDirectory(docsFolder);
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] .claude/docs 폴더 생성: {docsFolder}");
            }

            var anyCreated = false;

            // memory-system.md 설치
            var memorySystemPath = Path.Combine(docsFolder, "memory-system.md");
            if (!File.Exists(memorySystemPath))
            {
                File.WriteAllText(memorySystemPath, GetMemorySystemGuide(), Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] memory-system.md 설치됨");
                anyCreated = true;
            }

            // orchestrator-guide.md 설치
            var orchestratorGuidePath = Path.Combine(docsFolder, "orchestrator-guide.md");
            if (!File.Exists(orchestratorGuidePath))
            {
                File.WriteAllText(orchestratorGuidePath, GetOrchestratorGuide(), Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] orchestrator-guide.md 설치됨");
                anyCreated = true;
            }

            if (anyCreated)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] 가이드 문서 설치 완료: {docsFolder}");
            }

            return anyCreated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] 가이드 문서 설치 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 장기기억 시스템 가이드 문서
    /// 출처: https://github.com/Dannykkh/claude-code-agent-customizations/blob/master/docs/memory-system.md
    /// </summary>
    private static string GetMemorySystemGuide()
    {
        return @"# 장기기억 시스템 (Long-term Memory System)

세션 간 컨텍스트 유지 및 키워드 기반 대화 검색을 위한 메모리 시스템입니다.

---

## 개요

### 문제
- Claude Code 세션은 컨텍스트 제한이 있음
- 이전 세션에서 무엇을 했는지 기억하지 못함
- ""이전에 어떻게 구현했지?"" 질문에 답할 수 없음

### 해결

| 구성요소 | 역할 |
|---------|------|
| **MEMORY.md** | 중요한 결정사항, 교훈 저장 (항상 로드됨) |
| **대화 로그** | 모든 대화 원시 저장 + 키워드 태깅 |
| **index.json** | 키워드 인덱스 (RAG 스타일 검색용) |
| **검색 명령어** | `/memory find`, `/memory search` |

---

## 시스템 구조

```
프로젝트/
├── MEMORY.md                          # 구조화된 장기기억 (Git 추적)
├── CLAUDE.md                          # @MEMORY.md 참조 → 항상 로드
└── .claude/
    ├── conversations/                 # 대화 로그 (Git 제외)
    │   ├── 2026-02-02.md              # 오늘 대화 (frontmatter + 키워드)
    │   ├── 2026-02-01.md              # 어제 대화
    │   └── index.json                 # 키워드 인덱스
    ├── agents/                        # 에이전트 정의
    │   ├── memory-writer.md           # 메모리 정리 에이전트
    │   └── keyword-extractor.md       # 키워드 추출 에이전트
    └── skills/                        # 스킬 정의
        └── long-term-memory/          # 메모리 관리 스킬
            └── SKILL.md
```

---

## 자동 동작

### 세션 중 (UserPromptSubmit 훅)
1. 사용자 입력 → save-conversation.ps1 실행
2. `.claude/conversations/YYYY-MM-DD.md`에 대화 저장
3. 해시태그(#keyword) 자동 추출

### 세션 종료 (Stop 훅)
1. keyword-extractor 에이전트: 키워드 추출 + index.json 업데이트
2. memory-writer 에이전트: MEMORY.md 업데이트

---

## 명령어 가이드

### /memory add - 정보 기억하기
```
/memory add Redis TTL은 항상 1시간으로 설정
기억해: API 키는 환경변수로 관리
```

### /memory search - MEMORY.md 검색
```
/memory search redis
redis 관련 기억 찾아줘
```

### /memory find - 대화 키워드 검색 (RAG 스타일)
```
/memory find orchestrator
이전에 orchestrator 구현한 적 있어?
```

### /memory read - 특정 대화 읽기
```
/memory read 2026-02-02
어제 대화 보여줘
```

### /memory tag - 수동 키워드 태깅
```
/memory tag oauth, jwt, authentication
```

### /memory list - 전체 기억 보기
```
/memory list
장기기억 보여줘
```

---

## 해시태그로 키워드 추가

대화 중 해시태그를 사용하면 자동으로 키워드에 추가됩니다:

```
jwt 인증 구현해줘 #authentication #security
→ keywords: [authentication, security] 자동 추가
```

---

## 훅 설정

`.claude/settings.local.json`:

```json
{
  ""hooks"": {
    ""UserPromptSubmit"": [
      {""hooks"": [""powershell -ExecutionPolicy Bypass -File hooks/save-conversation.ps1 \""$PROMPT\""""]}
    ],
    ""Stop"": [
      {""hooks"": [""powershell -ExecutionPolicy Bypass -File hooks/update-memory.ps1""]}
    ]
  }
}
```

---

## 참고

- 원본: https://github.com/Dannykkh/claude-code-agent-customizations/blob/master/docs/memory-system.md
- TermSnap에서 Claude Code 시작 시 자동 설치됩니다.
";
    }

    /// <summary>
    /// 오케스트레이터 가이드 문서
    /// 출처: https://github.com/Dannykkh/claude-code-agent-customizations/blob/master/docs/orchestrator-guide.md
    /// </summary>
    private static string GetOrchestratorGuide()
    {
        return @"# Multi-AI Orchestrator 상세 가이드

PM + Multi-AI Worker 병렬 처리 시스템의 완전한 사용 가이드입니다.

---

## 개요

### 무엇인가?

Multi-AI Orchestrator는 여러 AI CLI (Claude, Codex, Gemini)를 동시에 활용하여 대규모 작업을 병렬로 처리하는 시스템입니다.

### 언제 사용하나?

| 상황 | 권장 |
|------|------|
| 단일 파일 수정 | 일반 Claude Code |
| 다중 모듈 동시 작업 | **Orchestrator** |
| 대규모 리팩토링 | **Orchestrator** |
| 여러 관점의 코드 리뷰 | **Orchestrator** (Multi-AI) |

### 핵심 기능

- **파일 락킹**: 다중 Worker 간 파일 충돌 방지
- **태스크 의존성**: 선행 작업 완료 후 자동 언블록
- **Multi-AI**: Claude + Codex + Gemini 병렬 실행
- **자동 Fallback**: 설치된 AI만 자동 감지

---

## 아키텍처

```
┌─────────────────────────────────────────────────────────────┐
│                         PM (Claude)                          │
│  workpm 입력 → AI 감지 → 프로젝트 분석 → 태스크 생성         │
└─────────────────────────────────────────────────────────────┘
                              ↓
              ┌───────────────┼───────────────┐
              ↓               ↓               ↓
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│   Worker-1      │ │   Worker-2      │ │   Worker-3      │
│   (Claude)      │ │   (Codex)       │ │   (Gemini)      │
│ claim → work    │ │ claim → work    │ │ claim → work    │
│ → complete      │ │ → complete      │ │ → complete      │
└─────────────────┘ └─────────────────┘ └─────────────────┘
```

---

## PM 모드 (workpm)

PM 터미널에서 `workpm` 입력 후:

1. **AI Provider 감지** - 설치된 CLI 자동 감지
2. **프로젝트 분석** - 모듈, 파일 구조 분석
3. **태스크 분해 및 생성** - scope, 의존성, AI 배정
4. **진행 상황 모니터링**

### 태스크 설계 원칙

| 원칙 | 설명 |
|------|------|
| **단일 책임** | 하나의 태스크 = 하나의 목표 |
| **명확한 범위** | scope로 수정 가능 파일 명시 |
| **적절한 크기** | 1-2시간 내 완료 가능 |
| **의존성 명시** | depends_on으로 순서 지정 |

### AI 배정 가이드

| 태스크 유형 | 추천 AI | 이유 |
|------------|---------|------|
| 코드 생성/구현 | `codex` | 빠른 코드 생성 |
| 리팩토링 | `claude` | 복잡한 추론 |
| 코드 리뷰/보안 | `gemini` | 대용량 컨텍스트 |
| 문서 작성 | `claude` | 자연어 품질 |

---

## Worker 모드 (pmworker)

Worker 터미널에서 `pmworker` 입력 후:

1. **가용 태스크 확인** - get_available_tasks
2. **태스크 담당 선언** - claim_task
3. **파일 락 획득** - lock_file
4. **작업 수행** - 세부 TODO 작성 및 진행
5. **완료/실패 보고** - complete_task / fail_task

### 파일 락 규칙

- 다른 Worker가 같은 경로를 락하면 충돌
- 상위/하위 경로도 충돌로 처리
- 태스크 완료 시 자동 해제

---

## 설치 및 설정

### 1. MCP 서버 빌드

```powershell
cd mcp-servers/claude-orchestrator-mcp
npm install && npm run build
```

### 2. settings.local.json

```json
{
  ""mcpServers"": {
    ""orchestrator"": {
      ""command"": ""node"",
      ""args"": [""path/to/claude-orchestrator-mcp/dist/index.js""],
      ""env"": {
        ""ORCHESTRATOR_PROJECT_ROOT"": ""${workspaceFolder}"",
        ""ORCHESTRATOR_WORKER_ID"": ""pm""
      }
    }
  }
}
```

### 3. 다중 터미널 실행

```powershell
.\scripts\launch.ps1 -ProjectPath ""C:\project"" -MultiAI
```

---

## MCP 도구 레퍼런스

### PM 전용

| 도구 | 설명 |
|------|------|
| `orchestrator_detect_providers` | AI CLI 감지 |
| `orchestrator_analyze_codebase` | 프로젝트 분석 |
| `orchestrator_create_task` | 태스크 생성 |
| `orchestrator_get_progress` | 진행 상황 |

### Worker 전용

| 도구 | 설명 |
|------|------|
| `orchestrator_get_available_tasks` | 가용 태스크 |
| `orchestrator_claim_task` | 태스크 담당 |
| `orchestrator_lock_file` | 파일 락 |
| `orchestrator_complete_task` | 완료 보고 |
| `orchestrator_fail_task` | 실패 보고 |

---

## 트러블슈팅

### ""Task has unmet dependencies""
→ 선행 태스크 완료 대기

### ""Path is locked by another worker""
→ get_file_locks()로 확인, 해당 Worker 완료 대기

### AI Provider 감지 안됨
→ CLI 설치 및 PATH 확인 (`claude --version`)

---

## 참고

- 원본: https://github.com/Dannykkh/claude-code-agent-customizations/blob/master/docs/orchestrator-guide.md
- TermSnap에서 Claude Code 시작 시 자동 설치됩니다.
";
    }

    #endregion

    #region Orchestrator MCP

    /// <summary>
    /// 오케스트라 MCP 서버 설치
    /// TermSnap 리소스에서 대상 프로젝트로 MCP 서버 파일 복사 및 설정 추가
    /// </summary>
    private static bool InstallOrchestratorMCP(string workingDirectory)
    {
        try
        {
            var anyCreated = false;

            // 1. mcp-servers 폴더 생성
            var mcpServersFolder = Path.Combine(workingDirectory, "mcp-servers", "claude-orchestrator-mcp");
            var distFolder = Path.Combine(mcpServersFolder, "dist");
            var servicesFolder = Path.Combine(distFolder, "services");

            if (!Directory.Exists(servicesFolder))
            {
                Directory.CreateDirectory(servicesFolder);
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] MCP 서버 폴더 생성: {mcpServersFolder}");
                anyCreated = true;
            }

            // 2. MCP 서버 파일들 생성
            anyCreated |= CreateMCPFile(Path.Combine(mcpServersFolder, "package.json"), GetMCPPackageJson());
            anyCreated |= CreateMCPFile(Path.Combine(distFolder, "index.js"), GetMCPIndexJs());
            anyCreated |= CreateMCPFile(Path.Combine(servicesFolder, "ai-detector.js"), GetMCPAIDetectorJs());
            anyCreated |= CreateMCPFile(Path.Combine(servicesFolder, "state-manager.js"), GetMCPStateManagerJs());

            // 3. settings.local.json에 MCP 서버 설정 추가
            anyCreated |= AddMCPServerToSettings(workingDirectory);

            // 4. .gitignore에 .orchestrator 폴더 추가
            EnsureOrchestratorGitIgnore(workingDirectory);

            if (anyCreated)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] 오케스트라 MCP 서버 설치 완료: {mcpServersFolder}");
            }

            return anyCreated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] 오케스트라 MCP 설치 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// MCP 파일 생성 (이미 존재하면 스킵)
    /// </summary>
    private static bool CreateMCPFile(string filePath, string content)
    {
        if (File.Exists(filePath))
            return false;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, content, Encoding.UTF8);
        System.Diagnostics.Debug.WriteLine($"[ClaudeHook] MCP 파일 생성: {Path.GetFileName(filePath)}");
        return true;
    }

    /// <summary>
    /// settings.local.json에 MCP 서버 설정 추가
    /// </summary>
    private static bool AddMCPServerToSettings(string workingDirectory)
    {
        var claudeFolder = Path.Combine(workingDirectory, ClaudeFolderName);
        var settingsPath = Path.Combine(claudeFolder, SettingsFileName);

        if (!Directory.Exists(claudeFolder))
        {
            Directory.CreateDirectory(claudeFolder);
        }

        JsonObject settings;
        if (File.Exists(settingsPath))
        {
            try
            {
                var existingContent = File.ReadAllText(settingsPath, Encoding.UTF8);
                settings = JsonNode.Parse(existingContent)?.AsObject() ?? new JsonObject();

                // 이미 orchestrator MCP가 설정되어 있으면 스킵
                if (settings.ContainsKey("mcpServers"))
                {
                    var mcpServers = settings["mcpServers"]?.AsObject();
                    if (mcpServers != null && mcpServers.ContainsKey("orchestrator"))
                    {
                        System.Diagnostics.Debug.WriteLine("[ClaudeHook] MCP 서버 이미 설정됨 - 스킵");
                        return false;
                    }
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

        // mcpServers 객체 생성/가져오기
        if (!settings.ContainsKey("mcpServers"))
        {
            settings["mcpServers"] = new JsonObject();
        }
        var mcpServersObj = settings["mcpServers"]!.AsObject();

        // orchestrator MCP 서버 설정 추가
        // Windows에서 경로 형식 주의
        var mcpPath = Path.Combine(workingDirectory, "mcp-servers", "claude-orchestrator-mcp", "dist", "index.js");
        mcpPath = mcpPath.Replace("\\", "/"); // JSON에서는 슬래시 사용

        mcpServersObj["orchestrator"] = new JsonObject
        {
            ["command"] = "node",
            ["args"] = new JsonArray { mcpPath },
            ["env"] = new JsonObject
            {
                ["ORCHESTRATOR_PROJECT_ROOT"] = workingDirectory.Replace("\\", "/"),
                ["ORCHESTRATOR_WORKER_ID"] = "pm"
            }
        };

        // 저장
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = settings.ToJsonString(options);
        File.WriteAllText(settingsPath, json, Encoding.UTF8);

        System.Diagnostics.Debug.WriteLine("[ClaudeHook] settings.local.json에 MCP 서버 설정 추가됨");
        return true;
    }

    /// <summary>
    /// .gitignore에 .orchestrator 폴더 추가
    /// </summary>
    private static void EnsureOrchestratorGitIgnore(string workingDirectory)
    {
        var gitignorePath = Path.Combine(workingDirectory, ".gitignore");
        if (!File.Exists(gitignorePath)) return;

        try
        {
            var content = File.ReadAllText(gitignorePath, Encoding.UTF8);
            if (!content.Contains(".orchestrator"))
            {
                var addition = "\n# Orchestrator state (auto-generated)\n.orchestrator/\n";
                File.AppendAllText(gitignorePath, addition, Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine("[ClaudeHook] .gitignore에 .orchestrator 추가");
            }
        }
        catch { }
    }

    #region MCP Server Files Content

    private static string GetMCPPackageJson()
    {
        return @"{
  ""name"": ""claude-orchestrator-mcp"",
  ""version"": ""1.0.0"",
  ""description"": ""MCP server for Claude Code orchestration - PM + Worker parallel execution"",
  ""main"": ""dist/index.js"",
  ""type"": ""module"",
  ""scripts"": {
    ""build"": ""tsc"",
    ""start"": ""node dist/index.js"",
    ""dev"": ""tsc && node dist/index.js""
  },
  ""dependencies"": {
    ""@modelcontextprotocol/sdk"": ""^1.0.0"",
    ""zod"": ""^3.22.4"",
    ""glob"": ""^10.3.10""
  },
  ""devDependencies"": {
    ""@types/node"": ""^20.10.0"",
    ""typescript"": ""^5.3.0""
  },
  ""engines"": {
    ""node"": "">=18.0.0""
  }
}
";
    }

    private static string GetMCPAIDetectorJs()
    {
        return @"import { execSync } from 'child_process';
// ============================================================================
// AI CLI 감지 함수
// ============================================================================
/**
 * 특정 CLI가 시스템에 설치되어 있는지 확인
 */
function checkCLI(command, versionFlag = '--version') {
    try {
        const result = execSync(`${command} ${versionFlag}`, {
            encoding: 'utf-8',
            timeout: 5000,
            stdio: ['pipe', 'pipe', 'pipe']
        });
        // 버전 정보 추출 (첫 줄에서 숫자 패턴 찾기)
        const versionMatch = result.match(/[\d]+\.[\d]+\.[\d]+/);
        return {
            available: true,
            version: versionMatch ? versionMatch[0] : 'unknown'
        };
    }
    catch {
        return { available: false };
    }
}
/**
 * 모든 AI Provider 감지
 */
export function detectAIProviders() {
    const providers = [
        {
            name: 'claude',
            ...checkCLI('claude', '--version'),
            command: 'claude',
            description: 'Anthropic Claude Code CLI'
        },
        {
            name: 'codex',
            ...checkCLI('codex', '--version'),
            command: 'codex',
            description: 'OpenAI Codex CLI (GPT-5.2)'
        },
        {
            name: 'gemini',
            ...checkCLI('gemini', '--version'),
            command: 'gemini',
            description: 'Google Gemini CLI (Gemini 3 Pro)'
        }
    ];
    const availableCount = providers.filter(p => p.available).length;
    let mode;
    let modeDescription;
    if (availableCount >= 3) {
        mode = 'full';
        modeDescription = 'Full Mode: Claude + Codex + Gemini (3개 AI 병렬 처리)';
    }
    else if (availableCount === 2) {
        mode = 'dual';
        const available = providers.filter(p => p.available).map(p => p.name);
        modeDescription = `Dual Mode: ${available.join(' + ')} (2개 AI 병렬 처리)`;
    }
    else {
        mode = 'single';
        modeDescription = 'Single Mode: Claude만 사용 (기본 모드)';
    }
    return {
        providers,
        availableCount,
        mode,
        modeDescription
    };
}
/**
 * 특정 Provider가 사용 가능한지 확인
 */
export function isProviderAvailable(provider) {
    const result = detectAIProviders();
    const providerInfo = result.providers.find(p => p.name === provider);
    return providerInfo?.available ?? false;
}
/**
 * 사용 가능한 Provider 목록 반환
 */
export function getAvailableProviders() {
    const result = detectAIProviders();
    return result.providers
        .filter(p => p.available)
        .map(p => p.name);
}
/**
 * Provider 실행 명령어 생성
 */
export function getProviderCommand(provider, options = {}) {
    const { autoMode = true, workDir } = options;
    let command;
    switch (provider) {
        case 'claude':
            command = autoMode
                ? 'claude --dangerously-skip-permissions'
                : 'claude';
            break;
        case 'codex':
            // Codex CLI 옵션
            command = autoMode
                ? 'codex --full-auto --approval-mode full-auto'
                : 'codex';
            break;
        case 'gemini':
            // Gemini CLI 옵션
            command = autoMode
                ? 'gemini --approval-mode yolo'
                : 'gemini';
            break;
        default:
            command = 'claude';
    }
    if (workDir) {
        command = `cd ""${workDir}"" && ${command}`;
    }
    return command;
}
/**
 * AI Provider별 최적 용도 반환
 */
export function getProviderStrengths(provider) {
    switch (provider) {
        case 'claude':
            return [
                '복잡한 추론 및 분석',
                '코드 리팩토링',
                '문서 작성',
                '아키텍처 설계'
            ];
        case 'codex':
            return [
                '코드 생성 및 자동화',
                '테스트 케이스 작성',
                '반복적인 코드 수정',
                '빠른 프로토타이핑'
            ];
        case 'gemini':
            return [
                '대용량 컨텍스트 분석 (1M 토큰)',
                '전체 코드베이스 리뷰',
                '보안 취약점 분석',
                '멀티파일 이해'
            ];
        default:
            return [];
    }
}
//# sourceMappingURL=ai-detector.js.map
";
    }

    private static string GetMCPStateManagerJs()
    {
        return @"import * as fs from 'fs';
import * as path from 'path';
// ============================================================================
// StateManager 클래스
// ============================================================================
export class StateManager {
    state;
    stateFilePath;
    workerId;
    constructor(projectRoot, workerId) {
        this.workerId = workerId;
        this.stateFilePath = path.join(projectRoot, '.orchestrator', 'state.json');
        // 상태 디렉토리 생성
        const stateDir = path.dirname(this.stateFilePath);
        if (!fs.existsSync(stateDir)) {
            fs.mkdirSync(stateDir, { recursive: true });
        }
        // 기존 상태 로드 또는 새로 생성
        if (fs.existsSync(this.stateFilePath)) {
            this.state = this.loadState();
        }
        else {
            this.state = this.createInitialState(projectRoot);
            this.saveState();
        }
        // 워커 등록
        this.registerWorker();
    }
    // --------------------------------------------------------------------------
    // 상태 파일 관리
    // --------------------------------------------------------------------------
    loadState() {
        const content = fs.readFileSync(this.stateFilePath, 'utf-8');
        return JSON.parse(content);
    }
    saveState() {
        fs.writeFileSync(this.stateFilePath, JSON.stringify(this.state, null, 2), 'utf-8');
    }
    createInitialState(projectRoot) {
        return {
            tasks: [],
            fileLocks: [],
            workers: [],
            projectRoot,
            startedAt: new Date().toISOString(),
            version: '1.0.0'
        };
    }
    // 상태 다시 로드 (다른 프로세스가 변경했을 수 있음)
    reloadState() {
        if (fs.existsSync(this.stateFilePath)) {
            this.state = this.loadState();
        }
    }
    // --------------------------------------------------------------------------
    // 워커 관리
    // --------------------------------------------------------------------------
    registerWorker() {
        this.reloadState();
        const existingWorker = this.state.workers.find(w => w.id === this.workerId);
        if (existingWorker) {
            existingWorker.status = 'idle';
            existingWorker.lastHeartbeat = new Date().toISOString();
        }
        else {
            this.state.workers.push({
                id: this.workerId,
                status: 'idle',
                lastHeartbeat: new Date().toISOString(),
                completedTasks: 0
            });
        }
        this.saveState();
    }
    updateHeartbeat() {
        this.reloadState();
        const worker = this.state.workers.find(w => w.id === this.workerId);
        if (worker) {
            worker.lastHeartbeat = new Date().toISOString();
            this.saveState();
        }
    }
    getWorkers() {
        this.reloadState();
        return [...this.state.workers];
    }
    // --------------------------------------------------------------------------
    // 태스크 관리 - PM 전용
    // --------------------------------------------------------------------------
    createTask(id, prompt, options = {}) {
        this.reloadState();
        // ID 중복 확인
        if (this.state.tasks.find(t => t.id === id)) {
            return { success: false, message: `Task with id '${id}' already exists` };
        }
        // 의존성 태스크 존재 확인
        const dependsOn = options.dependsOn || [];
        for (const depId of dependsOn) {
            if (!this.state.tasks.find(t => t.id === depId)) {
                return { success: false, message: `Dependency task '${depId}' not found` };
            }
        }
        const task = {
            id,
            prompt,
            status: 'pending',
            dependsOn,
            scope: options.scope,
            priority: options.priority ?? 1,
            aiProvider: options.aiProvider,
            createdAt: new Date().toISOString()
        };
        this.state.tasks.push(task);
        this.saveState();
        return { success: true, message: `Task '${id}' created successfully`, task };
    }
    getProgress() {
        this.reloadState();
        const tasks = this.state.tasks;
        const completed = tasks.filter(t => t.status === 'completed').length;
        const failed = tasks.filter(t => t.status === 'failed').length;
        const inProgress = tasks.filter(t => t.status === 'in_progress').length;
        const pending = tasks.filter(t => t.status === 'pending').length;
        const total = tasks.length;
        // 의존성 때문에 블로킹된 태스크
        const blockedTasks = tasks
            .filter(t => t.status === 'pending')
            .filter(t => {
            return t.dependsOn.some(depId => {
                const depTask = tasks.find(dt => dt.id === depId);
                return depTask && depTask.status !== 'completed';
            });
        })
            .map(t => t.id);
        // 현재 진행 중인 태스크
        const activeTasks = tasks
            .filter(t => t.status === 'in_progress')
            .map(t => ({
            id: t.id,
            owner: t.owner || 'unknown',
            startedAt: t.startedAt || ''
        }));
        return {
            total,
            completed,
            failed,
            inProgress,
            pending,
            percentComplete: total > 0 ? Math.round((completed / total) * 100) : 0,
            blockedTasks,
            activeTasks
        };
    }
    // --------------------------------------------------------------------------
    // 태스크 관리 - Worker 전용
    // --------------------------------------------------------------------------
    getAvailableTasks() {
        this.reloadState();
        const availableTasks = this.state.tasks
            .filter(t => t.status === 'pending')
            .filter(t => {
            // 모든 의존성이 완료되었는지 확인
            return t.dependsOn.every(depId => {
                const depTask = this.state.tasks.find(dt => dt.id === depId);
                return depTask && depTask.status === 'completed';
            });
        })
            .filter(t => {
            // scope가 락된 파일과 충돌하지 않는지 확인
            if (!t.scope || t.scope.length === 0)
                return true;
            return !t.scope.some(scopePath => this.isPathLocked(scopePath));
        })
            .sort((a, b) => b.priority - a.priority)
            .map(t => ({
            id: t.id,
            prompt: t.prompt,
            priority: t.priority,
            scope: t.scope
        }));
        return {
            workerId: this.workerId,
            availableTasks,
            message: availableTasks.length > 0
                ? `${availableTasks.length} task(s) available`
                : 'No tasks available'
        };
    }
    claimTask(taskId) {
        this.reloadState();
        const task = this.state.tasks.find(t => t.id === taskId);
        if (!task) {
            return { success: false, message: `Task '${taskId}' not found` };
        }
        if (task.status !== 'pending') {
            return { success: false, message: `Task '${taskId}' is not pending (status: ${task.status})` };
        }
        // 의존성 확인
        const unmetDeps = task.dependsOn.filter(depId => {
            const depTask = this.state.tasks.find(dt => dt.id === depId);
            return !depTask || depTask.status !== 'completed';
        });
        if (unmetDeps.length > 0) {
            return {
                success: false,
                message: `Task '${taskId}' has unmet dependencies: ${unmetDeps.join(', ')}`
            };
        }
        // 태스크 담당
        task.status = 'in_progress';
        task.owner = this.workerId;
        task.startedAt = new Date().toISOString();
        // 워커 상태 업데이트
        const worker = this.state.workers.find(w => w.id === this.workerId);
        if (worker) {
            worker.status = 'working';
            worker.currentTask = taskId;
        }
        this.saveState();
        return { success: true, message: `Task '${taskId}' claimed by ${this.workerId}`, task };
    }
    completeTask(taskId, result) {
        this.reloadState();
        const task = this.state.tasks.find(t => t.id === taskId);
        if (!task) {
            return { success: false, message: `Task '${taskId}' not found` };
        }
        if (task.owner !== this.workerId) {
            return { success: false, message: `Task '${taskId}' is owned by ${task.owner}, not ${this.workerId}` };
        }
        // 태스크 완료
        task.status = 'completed';
        task.completedAt = new Date().toISOString();
        if (result)
            task.result = result;
        // 워커의 모든 락 해제
        this.state.fileLocks = this.state.fileLocks.filter(lock => lock.owner !== this.workerId);
        // 워커 상태 업데이트
        const worker = this.state.workers.find(w => w.id === this.workerId);
        if (worker) {
            worker.status = 'idle';
            worker.currentTask = undefined;
            worker.completedTasks++;
        }
        // 의존성이 해소된 태스크 찾기
        const unlockedDependents = this.state.tasks
            .filter(t => t.status === 'pending' && t.dependsOn.includes(taskId))
            .filter(t => {
            return t.dependsOn.every(depId => {
                const depTask = this.state.tasks.find(dt => dt.id === depId);
                return depTask && depTask.status === 'completed';
            });
        })
            .map(t => t.id);
        this.saveState();
        return {
            success: true,
            message: `Task '${taskId}' completed`,
            unlockedDependents: unlockedDependents.length > 0 ? unlockedDependents : undefined
        };
    }
    failTask(taskId, error) {
        this.reloadState();
        const task = this.state.tasks.find(t => t.id === taskId);
        if (!task) {
            return { success: false, message: `Task '${taskId}' not found` };
        }
        if (task.owner !== this.workerId) {
            return { success: false, message: `Task '${taskId}' is owned by ${task.owner}, not ${this.workerId}` };
        }
        task.status = 'failed';
        task.completedAt = new Date().toISOString();
        task.error = error;
        // 워커의 모든 락 해제
        this.state.fileLocks = this.state.fileLocks.filter(lock => lock.owner !== this.workerId);
        // 워커 상태 업데이트
        const worker = this.state.workers.find(w => w.id === this.workerId);
        if (worker) {
            worker.status = 'idle';
            worker.currentTask = undefined;
        }
        this.saveState();
        return { success: true, message: `Task '${taskId}' marked as failed` };
    }
    // --------------------------------------------------------------------------
    // 파일 락 관리
    // --------------------------------------------------------------------------
    isPathLocked(targetPath) {
        const normalizedTarget = this.normalizePath(targetPath);
        return this.state.fileLocks.some(lock => {
            const normalizedLock = this.normalizePath(lock.path);
            // 경로가 같거나, 상위/하위 관계인지 확인
            return normalizedTarget === normalizedLock ||
                normalizedTarget.startsWith(normalizedLock + '/') ||
                normalizedLock.startsWith(normalizedTarget + '/');
        });
    }
    normalizePath(p) {
        return p.replace(/\\/g, '/').replace(/\/+$/, '');
    }
    lockFile(filePath, reason) {
        this.reloadState();
        const normalizedPath = this.normalizePath(filePath);
        // 기존 락 확인
        const existingLock = this.state.fileLocks.find(lock => {
            const normalizedLock = this.normalizePath(lock.path);
            return normalizedPath === normalizedLock ||
                normalizedPath.startsWith(normalizedLock + '/') ||
                normalizedLock.startsWith(normalizedPath + '/');
        });
        if (existingLock) {
            if (existingLock.owner === this.workerId) {
                return { success: true, message: `Path '${filePath}' is already locked by you` };
            }
            return {
                success: false,
                message: `Path '${filePath}' is locked by ${existingLock.owner} (locked: ${existingLock.path})`
            };
        }
        // 새 락 생성
        this.state.fileLocks.push({
            path: filePath,
            owner: this.workerId,
            lockedAt: new Date().toISOString(),
            reason
        });
        this.saveState();
        return { success: true, message: `Path '${filePath}' locked successfully` };
    }
    unlockFile(filePath) {
        this.reloadState();
        const normalizedPath = this.normalizePath(filePath);
        const lockIndex = this.state.fileLocks.findIndex(lock => {
            return this.normalizePath(lock.path) === normalizedPath && lock.owner === this.workerId;
        });
        if (lockIndex === -1) {
            return { success: false, message: `No lock found for '${filePath}' owned by you` };
        }
        this.state.fileLocks.splice(lockIndex, 1);
        this.saveState();
        return { success: true, message: `Path '${filePath}' unlocked successfully` };
    }
    getFileLocks() {
        this.reloadState();
        return [...this.state.fileLocks];
    }
    // --------------------------------------------------------------------------
    // 공통 조회
    // --------------------------------------------------------------------------
    getTask(taskId) {
        this.reloadState();
        return this.state.tasks.find(t => t.id === taskId);
    }
    getAllTasks() {
        this.reloadState();
        return [...this.state.tasks];
    }
    getStatus() {
        this.reloadState();
        return { ...this.state };
    }
    getProjectRoot() {
        return this.state.projectRoot;
    }
    // --------------------------------------------------------------------------
    // 관리 기능
    // --------------------------------------------------------------------------
    resetState() {
        this.state = this.createInitialState(this.state.projectRoot);
        this.registerWorker();
        this.saveState();
    }
    deleteTask(taskId) {
        this.reloadState();
        const taskIndex = this.state.tasks.findIndex(t => t.id === taskId);
        if (taskIndex === -1) {
            return { success: false, message: `Task '${taskId}' not found` };
        }
        // 이 태스크에 의존하는 다른 태스크가 있는지 확인
        const dependentTasks = this.state.tasks.filter(t => t.dependsOn.includes(taskId));
        if (dependentTasks.length > 0) {
            return {
                success: false,
                message: `Cannot delete task '${taskId}': other tasks depend on it (${dependentTasks.map(t => t.id).join(', ')})`
            };
        }
        this.state.tasks.splice(taskIndex, 1);
        this.saveState();
        return { success: true, message: `Task '${taskId}' deleted successfully` };
    }
}
//# sourceMappingURL=state-manager.js.map
";
    }

    private static string GetMCPIndexJs()
    {
        return @"#!/usr/bin/env node
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { CallToolRequestSchema, ListToolsRequestSchema, } from '@modelcontextprotocol/sdk/types.js';
import { z } from 'zod';
import { glob } from 'glob';
import * as fs from 'fs';
import * as path from 'path';
import { StateManager } from './services/state-manager.js';
import { detectAIProviders, getProviderStrengths } from './services/ai-detector.js';
// ============================================================================
// 환경변수에서 설정 읽기
// ============================================================================
const PROJECT_ROOT = process.env.ORCHESTRATOR_PROJECT_ROOT || process.cwd();
const WORKER_ID = process.env.ORCHESTRATOR_WORKER_ID || 'worker-' + Date.now();
// StateManager 인스턴스 생성
const stateManager = new StateManager(PROJECT_ROOT, WORKER_ID);
// ============================================================================
// MCP 서버 설정
// ============================================================================
const server = new Server({
    name: 'claude-orchestrator-mcp',
    version: '1.0.0',
}, {
    capabilities: {
        tools: {},
    },
});
// ============================================================================
// 도구 정의
// ============================================================================
server.setRequestHandler(ListToolsRequestSchema, async () => {
    return {
        tools: [
            // PM 전용 도구들
            {
                name: 'orchestrator_detect_providers',
                description: '[PM] Detect installed AI CLI providers (Claude, Codex, Gemini)',
                inputSchema: {
                    type: 'object',
                    properties: {},
                },
            },
            {
                name: 'orchestrator_analyze_codebase',
                description: '[PM] Analyze project structure for task planning',
                inputSchema: {
                    type: 'object',
                    properties: {
                        patterns: {
                            type: 'array',
                            items: { type: 'string' },
                            description: 'Glob patterns to analyze (default: src/**/*)',
                        },
                    },
                },
            },
            {
                name: 'orchestrator_create_task',
                description: '[PM] Create a new task for workers',
                inputSchema: {
                    type: 'object',
                    properties: {
                        id: { type: 'string', description: 'Unique task ID' },
                        prompt: { type: 'string', description: 'Task description/prompt' },
                        scope: {
                            type: 'array',
                            items: { type: 'string' },
                            description: 'Files/folders this task can modify',
                        },
                        dependsOn: {
                            type: 'array',
                            items: { type: 'string' },
                            description: 'Task IDs that must complete first',
                        },
                        priority: {
                            type: 'number',
                            description: 'Task priority (higher = more important)',
                        },
                        aiProvider: {
                            type: 'string',
                            enum: ['claude', 'codex', 'gemini'],
                            description: 'Preferred AI provider for this task',
                        },
                    },
                    required: ['id', 'prompt'],
                },
            },
            {
                name: 'orchestrator_get_progress',
                description: '[PM] Get overall progress of all tasks',
                inputSchema: {
                    type: 'object',
                    properties: {},
                },
            },
            {
                name: 'orchestrator_delete_task',
                description: '[PM] Delete a task (only if no other tasks depend on it)',
                inputSchema: {
                    type: 'object',
                    properties: {
                        taskId: { type: 'string', description: 'Task ID to delete' },
                    },
                    required: ['taskId'],
                },
            },
            // Worker 전용 도구들
            {
                name: 'orchestrator_get_available_tasks',
                description: '[Worker] Get list of available tasks to claim',
                inputSchema: {
                    type: 'object',
                    properties: {},
                },
            },
            {
                name: 'orchestrator_claim_task',
                description: '[Worker] Claim a task to work on',
                inputSchema: {
                    type: 'object',
                    properties: {
                        taskId: { type: 'string', description: 'Task ID to claim' },
                    },
                    required: ['taskId'],
                },
            },
            {
                name: 'orchestrator_complete_task',
                description: '[Worker] Mark a task as completed',
                inputSchema: {
                    type: 'object',
                    properties: {
                        taskId: { type: 'string', description: 'Task ID to complete' },
                        result: { type: 'string', description: 'Optional result/summary' },
                    },
                    required: ['taskId'],
                },
            },
            {
                name: 'orchestrator_fail_task',
                description: '[Worker] Mark a task as failed',
                inputSchema: {
                    type: 'object',
                    properties: {
                        taskId: { type: 'string', description: 'Task ID that failed' },
                        error: { type: 'string', description: 'Error description' },
                    },
                    required: ['taskId', 'error'],
                },
            },
            {
                name: 'orchestrator_lock_file',
                description: '[Worker] Lock a file/folder to prevent conflicts',
                inputSchema: {
                    type: 'object',
                    properties: {
                        path: { type: 'string', description: 'File or folder path to lock' },
                        reason: { type: 'string', description: 'Reason for locking' },
                    },
                    required: ['path'],
                },
            },
            {
                name: 'orchestrator_unlock_file',
                description: '[Worker] Unlock a file/folder',
                inputSchema: {
                    type: 'object',
                    properties: {
                        path: { type: 'string', description: 'File or folder path to unlock' },
                    },
                    required: ['path'],
                },
            },
            // 공용 도구들
            {
                name: 'orchestrator_get_task',
                description: 'Get details of a specific task',
                inputSchema: {
                    type: 'object',
                    properties: {
                        taskId: { type: 'string', description: 'Task ID to get' },
                    },
                    required: ['taskId'],
                },
            },
            {
                name: 'orchestrator_get_all_tasks',
                description: 'Get all tasks',
                inputSchema: {
                    type: 'object',
                    properties: {},
                },
            },
            {
                name: 'orchestrator_get_file_locks',
                description: 'Get all current file locks',
                inputSchema: {
                    type: 'object',
                    properties: {},
                },
            },
            {
                name: 'orchestrator_get_workers',
                description: 'Get all registered workers',
                inputSchema: {
                    type: 'object',
                    properties: {},
                },
            },
            {
                name: 'orchestrator_reset',
                description: '[Admin] Reset all state (tasks, locks, workers)',
                inputSchema: {
                    type: 'object',
                    properties: {
                        confirm: { type: 'boolean', description: 'Must be true to reset' },
                    },
                    required: ['confirm'],
                },
            },
            // 플랜 파일 도구
            {
                name: 'orchestrator_get_latest_plan',
                description: '[PM] Get the most recently modified plan file (PLAN.md, PRD.md)',
                inputSchema: {
                    type: 'object',
                    properties: {},
                },
            },
            {
                name: 'orchestrator_list_plan_files',
                description: '[PM] List all plan files in the project',
                inputSchema: {
                    type: 'object',
                    properties: {},
                },
            },
            {
                name: 'orchestrator_read_plan',
                description: '[PM] Read contents of a specific plan file',
                inputSchema: {
                    type: 'object',
                    properties: {
                        path: { type: 'string', description: 'Relative path to the plan file' },
                    },
                    required: ['path'],
                },
            },
        ],
    };
});
// ============================================================================
// 도구 실행
// ============================================================================
server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args } = request.params;
    try {
        switch (name) {
            // PM 전용
            case 'orchestrator_detect_providers': {
                const result = detectAIProviders();
                return {
                    content: [
                        {
                            type: 'text',
                            text: JSON.stringify({
                                ...result,
                                providerStrengths: result.providers
                                    .filter(p => p.available)
                                    .reduce((acc, p) => {
                                    acc[p.name] = getProviderStrengths(p.name);
                                    return acc;
                                }, {})
                            }, null, 2),
                        },
                    ],
                };
            }
            case 'orchestrator_analyze_codebase': {
                const patterns = args?.patterns || ['src/**/*'];
                const analysis = await analyzeCodebase(patterns);
                return {
                    content: [{ type: 'text', text: JSON.stringify(analysis, null, 2) }],
                };
            }
            case 'orchestrator_create_task': {
                const result = stateManager.createTask(args.id, args.prompt, {
                    scope: args.scope,
                    dependsOn: args.dependsOn,
                    priority: args.priority,
                    aiProvider: args.aiProvider,
                });
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            case 'orchestrator_get_progress': {
                const result = stateManager.getProgress();
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            case 'orchestrator_delete_task': {
                const result = stateManager.deleteTask(args.taskId);
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            // Worker 전용
            case 'orchestrator_get_available_tasks': {
                const result = stateManager.getAvailableTasks();
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            case 'orchestrator_claim_task': {
                const result = stateManager.claimTask(args.taskId);
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            case 'orchestrator_complete_task': {
                const result = stateManager.completeTask(args.taskId, args.result);
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            case 'orchestrator_fail_task': {
                const result = stateManager.failTask(args.taskId, args.error);
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            case 'orchestrator_lock_file': {
                const result = stateManager.lockFile(args.path, args.reason);
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            case 'orchestrator_unlock_file': {
                const result = stateManager.unlockFile(args.path);
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            // 공용
            case 'orchestrator_get_task': {
                const task = stateManager.getTask(args.taskId);
                return {
                    content: [{
                            type: 'text',
                            text: task
                                ? JSON.stringify(task, null, 2)
                                : JSON.stringify({ error: `Task '${args.taskId}' not found` })
                        }],
                };
            }
            case 'orchestrator_get_all_tasks': {
                const tasks = stateManager.getAllTasks();
                return {
                    content: [{ type: 'text', text: JSON.stringify(tasks, null, 2) }],
                };
            }
            case 'orchestrator_get_file_locks': {
                const locks = stateManager.getFileLocks();
                return {
                    content: [{ type: 'text', text: JSON.stringify(locks, null, 2) }],
                };
            }
            case 'orchestrator_get_workers': {
                const workers = stateManager.getWorkers();
                return {
                    content: [{ type: 'text', text: JSON.stringify(workers, null, 2) }],
                };
            }
            case 'orchestrator_reset': {
                if (args.confirm === true) {
                    stateManager.resetState();
                    return {
                        content: [{ type: 'text', text: JSON.stringify({ success: true, message: 'State reset successfully' }) }],
                    };
                }
                return {
                    content: [{ type: 'text', text: JSON.stringify({ success: false, message: 'Confirm must be true to reset' }) }],
                };
            }
            // 플랜 파일 도구
            case 'orchestrator_get_latest_plan': {
                const result = await getLatestPlanFile();
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            case 'orchestrator_list_plan_files': {
                const result = await listPlanFiles();
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            case 'orchestrator_read_plan': {
                const result = await readPlanFile(args.path);
                return {
                    content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
                };
            }
            default:
                return {
                    content: [{ type: 'text', text: `Unknown tool: ${name}` }],
                    isError: true,
                };
        }
    }
    catch (error) {
        return {
            content: [{
                    type: 'text',
                    text: `Error: ${error instanceof Error ? error.message : String(error)}`
                }],
            isError: true,
        };
    }
});
// ============================================================================
// 플랜 파일 관련 함수
// ============================================================================
const PLAN_FILE_PATTERNS = [
    'PLAN.md', 'PRD.md', 'plan.md', 'prd.md',
    '.claude/plan.md', '.claude/plans/*.md',
    'docs/PLAN.md', 'docs/PRD.md', 'docs/plan.md', 'docs/prd.md'
];

async function listPlanFiles() {
    const projectRoot = stateManager.getProjectRoot();
    const planFiles = [];

    for (const pattern of PLAN_FILE_PATTERNS) {
        try {
            const files = await glob(pattern, {
                cwd: projectRoot,
                nodir: true,
                absolute: false
            });
            for (const file of files) {
                const fullPath = path.join(projectRoot, file);
                try {
                    const stat = fs.statSync(fullPath);
                    if (!planFiles.find(p => p.path === file)) {
                        planFiles.push({
                            path: file,
                            fullPath,
                            modifiedTime: stat.mtime.toISOString(),
                            size: stat.size
                        });
                    }
                } catch (e) { /* ignore */ }
            }
        } catch (e) { /* ignore */ }
    }

    // 수정 시간 기준 정렬 (최신 우선)
    planFiles.sort((a, b) => new Date(b.modifiedTime).getTime() - new Date(a.modifiedTime).getTime());

    return { projectRoot, planFiles, count: planFiles.length };
}

async function getLatestPlanFile() {
    const { planFiles, projectRoot } = await listPlanFiles();

    if (planFiles.length === 0) {
        return {
            found: false,
            message: '플랜 파일을 찾을 수 없습니다. PLAN.md 또는 PRD.md 파일을 생성해주세요.',
            searchedPatterns: PLAN_FILE_PATTERNS
        };
    }

    const latestFile = planFiles[0];
    try {
        const content = fs.readFileSync(latestFile.fullPath, 'utf-8');
        return {
            found: true,
            path: latestFile.path,
            fullPath: latestFile.fullPath,
            modifiedTime: latestFile.modifiedTime,
            size: latestFile.size,
            content,
            otherPlanFiles: planFiles.slice(1).map(f => f.path)
        };
    } catch (error) {
        return {
            found: false,
            path: latestFile.path,
            error: `파일을 읽을 수 없습니다: ${error.message}`
        };
    }
}

async function readPlanFile(relativePath) {
    const projectRoot = stateManager.getProjectRoot();
    const fullPath = path.join(projectRoot, relativePath);

    if (!fs.existsSync(fullPath)) {
        return {
            found: false,
            path: relativePath,
            error: '파일이 존재하지 않습니다.'
        };
    }

    try {
        const stat = fs.statSync(fullPath);
        const content = fs.readFileSync(fullPath, 'utf-8');
        return {
            found: true,
            path: relativePath,
            fullPath,
            modifiedTime: stat.mtime.toISOString(),
            size: stat.size,
            content
        };
    } catch (error) {
        return {
            found: false,
            path: relativePath,
            error: `파일을 읽을 수 없습니다: ${error.message}`
        };
    }
}

// ============================================================================
// 코드베이스 분석 함수
// ============================================================================
async function analyzeCodebase(patterns) {
    const projectRoot = stateManager.getProjectRoot();
    const allFiles = [];
    // 패턴별로 파일 수집
    for (const pattern of patterns) {
        const files = await glob(pattern, {
            cwd: projectRoot,
            nodir: true,
            ignore: ['**/node_modules/**', '**/.git/**', '**/dist/**', '**/build/**']
        });
        allFiles.push(...files);
    }
    // 중복 제거
    const uniqueFiles = [...new Set(allFiles)];
    // 디렉토리별 파일 그룹화
    const byDirectory = {};
    for (const file of uniqueFiles) {
        const dir = path.dirname(file);
        if (!byDirectory[dir]) {
            byDirectory[dir] = [];
        }
        byDirectory[dir].push(path.basename(file));
    }
    // 파일 타입별 분류
    const byExtension = {};
    for (const file of uniqueFiles) {
        const ext = path.extname(file) || 'no-ext';
        if (!byExtension[ext]) {
            byExtension[ext] = 0;
        }
        byExtension[ext]++;
    }
    // 대략적인 모듈 감지 (폴더 기반)
    const modules = Object.keys(byDirectory)
        .filter(dir => dir.includes('/') || dir.includes('\\'))
        .map(dir => dir.split(/[/\\]/)[0])
        .filter((v, i, a) => a.indexOf(v) === i);
    return {
        totalFiles: uniqueFiles.length,
        byDirectory,
        byExtension,
        suggestedModules: modules,
        projectRoot,
    };
}
// ============================================================================
// 서버 시작
// ============================================================================
async function main() {
    const transport = new StdioServerTransport();
    await server.connect(transport);
    console.error(`Claude Orchestrator MCP Server started (Worker ID: ${WORKER_ID})`);
    console.error(`Project Root: ${PROJECT_ROOT}`);
}
main().catch(console.error);
//# sourceMappingURL=index.js.map
";
    }

    #endregion

    #region Orchestrator Commands

    /// <summary>
    /// 오케스트라 커맨드 설치 (workpm, pmworker)
    /// .claude/commands/ 폴더에 커맨드 파일 생성
    /// </summary>
    private static bool InstallOrchestratorCommands(string workingDirectory)
    {
        try
        {
            var commandsFolder = Path.Combine(workingDirectory, ClaudeFolderName, "commands");
            if (!Directory.Exists(commandsFolder))
            {
                Directory.CreateDirectory(commandsFolder);
            }

            var anyCreated = false;

            // workpm.md - PM 모드 시작 커맨드
            var workpmPath = Path.Combine(commandsFolder, "workpm.md");
            if (!File.Exists(workpmPath))
            {
                File.WriteAllText(workpmPath, GetWorkPMCommand(), Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine("[ClaudeHook] workpm 커맨드 설치됨");
                anyCreated = true;
            }

            // pmworker.md - Worker 모드 시작 커맨드
            var pmworkerPath = Path.Combine(commandsFolder, "pmworker.md");
            if (!File.Exists(pmworkerPath))
            {
                File.WriteAllText(pmworkerPath, GetPMWorkerCommand(), Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine("[ClaudeHook] pmworker 커맨드 설치됨");
                anyCreated = true;
            }

            return anyCreated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] 오케스트라 커맨드 설치 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// workpm (PM 모드) 커맨드 내용
    /// </summary>
    private static string GetWorkPMCommand()
    {
        return @"---
description: PM 모드로 오케스트레이터 시작. 플랜 파일을 분석하고 태스크를 생성합니다.
allowed-tools:
  - orchestrator_detect_providers
  - orchestrator_analyze_codebase
  - orchestrator_create_task
  - orchestrator_get_progress
  - orchestrator_get_latest_plan
  - orchestrator_list_plan_files
  - orchestrator_read_plan
  - orchestrator_get_status
  - orchestrator_delete_task
  - Read
  - Glob
  - Grep
---

# PM (Project Manager) 모드

당신은 Multi-AI Orchestrator의 PM(Project Manager)입니다.

## 시작 절차

1. **AI Provider 감지**
   - `orchestrator_detect_providers` 도구로 설치된 AI CLI 확인
   - 사용 가능한 AI: Claude, Codex, Gemini

2. **플랜 파일 로드**
   $ARGUMENTS (플랜 파일 경로가 주어진 경우 해당 파일 사용)
   - 경로가 없으면 `orchestrator_get_latest_plan`으로 최신 플랜 자동 로드
   - 플랜 파일을 분석하여 작업 목록 추출

3. **프로젝트 분석**
   - `orchestrator_analyze_codebase`로 코드 구조 파악
   - 모듈/파일별 의존성 분석

4. **태스크 생성**
   - 플랜의 각 항목을 태스크로 분해
   - `orchestrator_create_task`로 태스크 생성
   - 의존성(depends_on) 설정
   - AI Provider 배정 (강점에 따라)

5. **모니터링**
   - `orchestrator_get_progress`로 진행 상황 확인
   - 블로킹된 태스크 확인 및 해결

## 태스크 설계 원칙

| 원칙 | 설명 |
|------|------|
| 단일 책임 | 하나의 태스크 = 하나의 목표 |
| 명확한 범위 | scope로 수정 가능 파일 명시 |
| 적절한 크기 | 하나의 기능/모듈 단위 |
| 의존성 명시 | depends_on으로 순서 지정 |

## AI 배정 가이드

| 태스크 유형 | 추천 AI | 이유 |
|------------|---------|------|
| 코드 생성 | codex | 빠른 코드 생성 |
| 리팩토링 | claude | 복잡한 추론 |
| 코드 리뷰 | gemini | 대용량 컨텍스트 |
| 문서 작성 | claude | 자연어 품질 |

## Worker 추가 안내

다른 터미널에서 `pmworker`를 실행하면 Worker가 추가됩니다.
Worker는 자동으로 태스크를 가져와 처리합니다.
";
    }

    /// <summary>
    /// pmworker (Worker 모드) 커맨드 내용
    /// </summary>
    private static string GetPMWorkerCommand()
    {
        return @"---
description: Worker 모드로 오케스트레이터 참여. 태스크를 가져와 작업을 수행합니다.
allowed-tools:
  - orchestrator_get_available_tasks
  - orchestrator_claim_task
  - orchestrator_lock_file
  - orchestrator_unlock_file
  - orchestrator_complete_task
  - orchestrator_fail_task
  - orchestrator_get_task
  - orchestrator_get_status
  - orchestrator_get_file_locks
  - Read
  - Write
  - Edit
  - Glob
  - Grep
  - Bash
---

# Worker 모드

당신은 Multi-AI Orchestrator의 Worker입니다.
PM이 생성한 태스크를 가져와 처리합니다.

## 작업 절차

1. **태스크 확인**
   ```
   orchestrator_get_available_tasks
   ```
   - 수행 가능한 태스크 목록 확인
   - 의존성이 해소된 태스크만 표시됨

2. **태스크 담당**
   ```
   orchestrator_claim_task { ""taskId"": ""task-1"" }
   ```
   - 태스크 담당 선언
   - 다른 Worker와 충돌 방지

3. **파일 락**
   ```
   orchestrator_lock_file { ""path"": ""src/service/UserService.java"" }
   ```
   - 수정할 파일 락 획득
   - 상위/하위 경로도 충돌로 처리됨

4. **작업 수행**
   - 태스크의 prompt에 따라 작업 수행
   - scope 내의 파일만 수정
   - 필요시 테스트 작성

5. **완료 보고**
   ```
   orchestrator_complete_task { ""taskId"": ""task-1"", ""result"": ""구현 완료"" }
   ```
   - 태스크 완료 및 락 자동 해제

6. **실패 보고** (에러 발생 시)
   ```
   orchestrator_fail_task { ""taskId"": ""task-1"", ""error"": ""의존성 충돌"" }
   ```

## 중요 규칙

1. **반드시 claim 먼저**: 작업 전 반드시 claim_task 호출
2. **lock 먼저**: 파일 수정 전 반드시 lock_file 호출
3. **scope 준수**: 태스크 scope 외의 파일 수정 금지
4. **완료 보고**: 작업 후 반드시 complete_task 또는 fail_task

## 작업 루프

```
while (태스크가_있는_동안) {
    1. get_available_tasks()
    2. claim_task(첫번째_태스크)
    3. lock_file(수정할_파일들)
    4. 작업_수행()
    5. complete_task() 또는 fail_task()
}
```

## 다른 Worker와 협업

- 파일 락 충돌 시: 잠시 대기 후 다른 태스크 선택
- 의존성 대기: 선행 태스크 완료까지 다른 태스크 처리
";
    }

    #endregion

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
