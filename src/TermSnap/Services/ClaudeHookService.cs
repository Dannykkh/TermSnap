using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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
    /// 설치 진행 상태 콜백
    /// </summary>
    public delegate void InstallProgressCallback(string step, int progress, int total);

    /// <summary>
    /// 장기기억 시스템 전체 설치 (동기 버전 - 호환성 유지)
    /// Claude Code 실행 전 호출하여 모든 필요 파일/폴더 생성
    /// </summary>
    public static bool InstallMemorySystem(string workingDirectory)
    {
        return InstallMemorySystemAsync(workingDirectory, null).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 장기기억 시스템 전체 설치 (비동기 버전)
    /// 진행 상태 콜백을 통해 UI에 설치 과정 표시 가능
    /// </summary>
    public static async Task<bool> InstallMemorySystemAsync(string workingDirectory, InstallProgressCallback? onProgress)
    {
        if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            return false;

        try
        {
            var anyCreated = false;
            var totalSteps = 5;
            var currentStep = 0;

            // 1. .claude 폴더 및 conversations 폴더 생성
            onProgress?.Invoke("폴더 구조 생성 중...", ++currentStep, totalSteps);
            await Task.Run(() => { anyCreated |= EnsureClaudeFolders(workingDirectory); });

            // 2. settings.local.json 생성/업데이트
            onProgress?.Invoke("설정 파일 생성 중...", ++currentStep, totalSteps);
            await Task.Run(() => { anyCreated |= EnsureSettingsJson(workingDirectory); });

            // Note: MEMORY.md, CLAUDE.md는 Claude Code가 자동 생성하므로 제거

            // 3. Gepetto 스킬 설치 (구현 계획 생성)
            onProgress?.Invoke("Gepetto 스킬 설치 중...", ++currentStep, totalSteps);
            anyCreated |= await InstallGepettoSkillAsync(workingDirectory);

            // 4. 오케스트라 MCP 서버 설치 (시간이 가장 오래 걸림)
            onProgress?.Invoke("Orchestrator MCP 설치 중... (npm install)", ++currentStep, totalSteps);
            anyCreated |= await InstallOrchestratorMCPAsync(workingDirectory);

            // 5. 오케스트라 커맨드 + Mnemo 글로벌 설치
            onProgress?.Invoke("커맨드 및 Mnemo 설치 중...", ++currentStep, totalSteps);
            await Task.Run(() =>
            {
                anyCreated |= InstallOrchestratorCommands(workingDirectory);
                anyCreated |= InstallMnemoGlobal();
            });

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
                var prefix = content.EndsWith("\n") ? "" : "\n";
                var addition = $"{prefix}\n# Claude conversation logs (auto-generated)\n.claude/conversations/\n";
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
    /// 메모리 훅이 완전한지 확인 (UserPromptSubmit + Stop 훅 모두 확인)
    /// </summary>
    private static bool HasCompleteMemoryHooks(JsonObject settings)
    {
        if (!settings.ContainsKey("hooks"))
            return false;

        var hooks = settings["hooks"]?.AsObject();
        if (hooks == null)
            return false;

        var hasSaveConversation = false;
        var hasSaveResponse = false;

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
                        hasSaveConversation = true;
                        break;
                    }
                }
            }
        }

        // Stop에 save-response 훅 확인
        if (hooks.ContainsKey("Stop"))
        {
            var stopArray = hooks["Stop"]?.AsArray();
            if (stopArray != null)
            {
                foreach (var hook in stopArray)
                {
                    var command = hook?["hooks"]?[0]?["command"]?.ToString();
                    if (command != null && command.Contains("save-response"))
                    {
                        hasSaveResponse = true;
                        break;
                    }
                }
            }
        }

        return hasSaveConversation && hasSaveResponse;
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
                ["matcher"] = "",  // 빈문자열 = 모든 프롬프트에 대해 실행
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

        // 2. Stop 훅 - AI 응답 완료 후 save-response.ps1 실행
        var stopHooks = hooks["Stop"]?.AsArray() ?? new JsonArray();

        var hasSaveResponseHook = false;
        foreach (var hook in stopHooks)
        {
            var command = hook?["hooks"]?[0]?["command"]?.ToString();
            if (command != null && command.Contains("save-response"))
            {
                hasSaveResponseHook = true;
                break;
            }
        }

        if (!hasSaveResponseHook)
        {
            stopHooks.Add(new JsonObject
            {
                ["matcher"] = "",  // 빈문자열 = 모든 응답에 대해 실행
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = "powershell -ExecutionPolicy Bypass -File hooks/save-response.ps1"
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

        // 5. pmworker 훅 - Worker 모드 시동어
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

    #region Gepetto Skill

    /// <summary>
    /// Gepetto 스킬 - 19단계 구현 계획 생성 워크플로우
    /// 리서치 → 이해관계자 인터뷰 → 멀티 LLM 리뷰 → 구현 검증
    /// </summary>
    private static string GetGepettoSkill()
    {
        return @"---
name: gepetto
description: ""상세한 구현 계획을 생성합니다. '계획을 세워보자', '계획을 세우고 싶어', 'plan' 등의 요청 시 자동 호출됩니다. 리서치, 이해관계자 인터뷰, 멀티 LLM 리뷰를 통해 실행 가능한 계획을 만들고, 구현 후 검증합니다.""
trigger: /gepetto
auto_trigger:
  - ""계획 세워줘""
  - ""계획을 세워보자""
  - ""계획을 세우고 싶어""
  - ""계획 만들어줘""
  - ""계획을 세워""
  - ""계획 세우자""
  - ""기획해줘""
  - ""설계해줘""
  - ""구현 계획""
  - ""plan this""
  - ""plan 세우자""
  - ""implementation plan""
  - ""let's plan""
---

# Gepetto: Implementation Planning Skill

Research → Interview → Spec Synthesis → Plan → External Review → Sections → Verify

## 사용법

```
/gepetto @docs/plan/my-feature-spec.md
```

- spec 파일 경로를 지정하면 **planning_dir** = 해당 파일의 부모 폴더
- 폴더와 spec 파일이 없으면 자동 생성
- 이미 계획 파일이 있으면 자동으로 Resume

## 19단계 워크플로우

### Setup (Step 1-3)
1. Print Intro - 배너 출력
2. Validate Spec File - @file 경로 확인
3. Setup Planning Session - 폴더/파일 생성, Resume 포인트 결정

### Research (Step 4-5)
4. Research Decision - 코드베이스/웹 리서치 여부 결정
5. Execute Research - 병렬 서브에이전트로 리서치 실행 → `claude-research.md`

### Interview (Step 6-7)
6. Detailed Interview - 사용자 인터뷰 (AskUserQuestion)
7. Save Interview - `claude-interview.md`

### Spec Synthesis (Step 8-9)
8. Write Initial Spec - 리서치+인터뷰 종합 → `claude-spec.md`
9. Generate Plan - 상세 구현 계획 → `claude-plan.md`

### External Review (Step 10-12)
10. External Review - Gemini/Codex 병렬 리뷰 → `reviews/`
11. Integrate Feedback - 피드백 반영 → `claude-integration-notes.md`
12. User Review - 사용자 확인

### Sections (Step 13-15)
13. Create Section Index - `sections/index.md` (SECTION_MANIFEST)
14. Write Section Files - 병렬 서브에이전트 → `sections/section-*.md`
15. Generate Execution Files → `claude-ralph-loop-prompt.md`, `claude-ralphy-prd.md`

### Output (Step 16-17)
16. Final Status - 파일 검증
17. Output Summary - 요약 및 다음 단계 안내

### Verification (Step 18-19)
18. Verify Implementation - 구현 검증 (병렬 서브에이전트)
19. Verification Report → `claude-verify-report.md`

## Resume 포인트

| 존재하는 파일 | Resume 단계 |
|--------------|------------|
| 없음 | Step 4 (새로 시작) |
| research만 | Step 6 (인터뷰) |
| research + interview | Step 8 (스펙 종합) |
| + spec | Step 9 (계획) |
| + plan | Step 10 (외부 리뷰) |
| + reviews | Step 11 (피드백 통합) |
| + integration-notes | Step 12 (사용자 확인) |
| + sections/index.md | Step 14 (섹션 작성) |
| 모든 섹션 완료 | Step 15 (실행 파일) |
| + ralph-loop + ralphy-prd | Step 18 (검증) |
| + verify-report | 완료 |

## 출력 파일 구조

```
<planning_dir>/              # spec 파일의 부모 폴더
├── your-spec.md             # 사용자 spec 파일 (자동 생성 가능)
├── claude-research.md       # 리서치 결과
├── claude-interview.md      # 인터뷰 Q&A
├── claude-spec.md           # 종합 명세서
├── claude-plan.md           # 구현 계획
├── claude-integration-notes.md  # 피드백 반영 기록
├── claude-ralph-loop-prompt.md  # ralph-loop용
├── claude-ralphy-prd.md     # Ralphy CLI용
├── claude-verify-report.md  # 검증 보고서
├── reviews/                 # 외부 LLM 리뷰
│   ├── gemini-review.md
│   └── codex-review.md
└── sections/                # 구현 단위
    ├── index.md             # SECTION_MANIFEST
    ├── section-01-*.md
    └── section-02-*.md
```

## 구현 옵션

**Option A - Manual:**
섹션 파일을 순서대로 직접 구현

**Option B - ralph-loop:**
```
/ralph-loop @<planning_dir>/claude-ralph-loop-prompt.md
```

**Option C - Ralphy CLI:**
```
ralphy --prd <planning_dir>/claude-ralphy-prd.md
```

**Option D - Verify:**
구현 완료 후 같은 spec으로 재실행하면 자동 검증 모드
```
/gepetto @<planning_dir>/your-spec.md
```
";
    }

    #endregion

    #region Orchestrator MCP

    /// <summary>
    /// 오케스트라 MCP 서버 설치 (GitHub에서 최신 소스 다운로드)
    /// - 폴더가 없으면: GitHub에서 다운로드하여 설치
    /// - 폴더가 있으면: 백그라운드에서 업데이트 확인
    /// </summary>
    private static bool InstallOrchestratorMCP(string workingDirectory)
    {
        try
        {
            var mcpServersFolder = Path.Combine(workingDirectory, "mcp-servers", "claude-orchestrator-mcp");
            var distIndexPath = Path.Combine(mcpServersFolder, "dist", "index.js");

            // 이미 설치되어 있는지 확인
            if (File.Exists(distIndexPath))
            {
                // 백그라운드에서 버전 확인 및 업데이트 (비동기)
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var (localVersion, remoteVersion, needsUpdate) = await CheckOrchestratorVersionAsync(workingDirectory);
                        if (needsUpdate)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Orchestrator 업데이트 필요: {localVersion} → {remoteVersion}");
                            await UpdateOrchestratorFromGitHubAsync(workingDirectory);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Orchestrator 업데이트 확인 실패: {ex.Message}");
                    }
                });

                // settings.local.json에 MCP 서버 설정 확인/추가
                AddMCPServerToSettings(workingDirectory);
                return false;
            }

            // 새로 설치 필요 - GitHub에서 다운로드 (동기 대기)
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Orchestrator 새로 설치: GitHub에서 다운로드 중...");

            try
            {
                // 동기적으로 GitHub에서 다운로드 및 빌드
                var task = UpdateOrchestratorFromGitHubAsync(workingDirectory);
                task.Wait(TimeSpan.FromMinutes(3)); // 최대 3분 대기

                if (task.Result.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Orchestrator 설치 완료");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Orchestrator 설치 실패: {task.Result.Message}");
                    // 실패 시 하드코딩 폴백
                    return InstallOrchestratorMCPFallback(workingDirectory);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] GitHub 다운로드 실패, 폴백 설치: {ex.Message}");
                // GitHub 실패 시 하드코딩 폴백
                return InstallOrchestratorMCPFallback(workingDirectory);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] 오케스트라 MCP 설치 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 오케스트라 MCP 서버 설치 (비동기 버전)
    /// </summary>
    private static async Task<bool> InstallOrchestratorMCPAsync(string workingDirectory)
    {
        try
        {
            var mcpServersFolder = Path.Combine(workingDirectory, "mcp-servers", "claude-orchestrator-mcp");
            var distIndexPath = Path.Combine(mcpServersFolder, "dist", "index.js");

            // 이미 설치되어 있으면 백그라운드에서 업데이트 확인
            if (File.Exists(distIndexPath))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var (_, _, needsUpdate) = await CheckOrchestratorVersionAsync(workingDirectory);
                        if (needsUpdate) await UpdateOrchestratorFromGitHubAsync(workingDirectory);
                    }
                    catch { /* 무시 */ }
                });

                AddMCPServerToSettings(workingDirectory);
                return false;
            }

            // 새로 설치 - GitHub에서 다운로드
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Orchestrator 새로 설치: GitHub에서 다운로드 중...");

            var result = await UpdateOrchestratorFromGitHubAsync(workingDirectory);
            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Orchestrator 설치 완료");
                return true;
            }

            // 실패 시 하드코딩 폴백
            return await Task.Run(() => InstallOrchestratorMCPFallback(workingDirectory));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Orchestrator 설치 실패: {ex.Message}");
            return await Task.Run(() => InstallOrchestratorMCPFallback(workingDirectory));
        }
    }

    /// <summary>
    /// Orchestrator 폴백 설치 (GitHub 실패 시 하드코딩 버전 사용)
    /// </summary>
    private static bool InstallOrchestratorMCPFallback(string workingDirectory)
    {
        try
        {
            var anyCreated = false;

            var mcpServersFolder = Path.Combine(workingDirectory, "mcp-servers", "claude-orchestrator-mcp");
            var distFolder = Path.Combine(mcpServersFolder, "dist");
            var servicesFolder = Path.Combine(distFolder, "services");

            if (!Directory.Exists(servicesFolder))
            {
                Directory.CreateDirectory(servicesFolder);
                anyCreated = true;
            }

            anyCreated |= CreateMCPFile(Path.Combine(mcpServersFolder, "package.json"), GetMCPPackageJson());
            anyCreated |= CreateMCPFile(Path.Combine(distFolder, "index.js"), GetMCPIndexJs());
            anyCreated |= CreateMCPFile(Path.Combine(servicesFolder, "ai-detector.js"), GetMCPAIDetectorJs());
            anyCreated |= CreateMCPFile(Path.Combine(servicesFolder, "state-manager.js"), GetMCPStateManagerJs());

            AddMCPServerToSettings(workingDirectory);
            EnsureOrchestratorGitIgnore(workingDirectory);

            var nodeModulesPath = Path.Combine(mcpServersFolder, "node_modules");
            if (!Directory.Exists(nodeModulesPath))
            {
                RunNpmInstall(mcpServersFolder);
                anyCreated = true;
            }

            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Orchestrator 폴백 설치 완료");
            return anyCreated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Orchestrator 폴백 설치 실패: {ex.Message}");
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
    /// npm install 실행 (백그라운드, UI 블로킹 없음)
    /// MCP 서버의 런타임 의존성(@modelcontextprotocol/sdk, zod, glob) 설치
    /// </summary>
    private static void RunNpmInstall(string mcpServersFolder)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c npm install --production",
                WorkingDirectory = mcpServersFolder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                // 백그라운드에서 완료 대기 (UI 블로킹 방지)
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        process.WaitForExit(60000); // 60초 타임아웃
                        if (process.ExitCode == 0)
                        {
                            System.Diagnostics.Debug.WriteLine("[ClaudeHook] npm install 완료");
                        }
                        else
                        {
                            var error = process.StandardError.ReadToEnd();
                            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] npm install 실패 (exit {process.ExitCode}): {error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ClaudeHook] npm install 대기 중 오류: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                });
            }
        }
        catch (Exception ex)
        {
            // npm이 설치되지 않은 경우 등
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] npm install 실행 불가: {ex.Message}");
        }
    }

    /// <summary>
    /// settings.local.json에 MCP 서버 설정 추가
    /// 주의: EnsureSettingsJson()과 같은 파일을 다루므로 InstallMemorySystem() 내에서 순차 호출 필수
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

                // orchestrator MCP 설정 확인 - 경로가 현재 프로젝트와 일치하는지 검증
                if (settings.ContainsKey("mcpServers"))
                {
                    var mcpServers = settings["mcpServers"]?.AsObject();
                    if (mcpServers != null && mcpServers.ContainsKey("orchestrator"))
                    {
                        // 경로 검증: 현재 프로젝트 경로와 일치하면 스킵
                        var expectedPath = Path.Combine(workingDirectory, "mcp-servers", "claude-orchestrator-mcp", "dist", "index.js")
                            .Replace("\\", "/");
                        var existingArgs = mcpServers["orchestrator"]?["args"]?.AsArray();
                        var existingPath = existingArgs?.Count > 0 ? existingArgs[0]?.ToString() : null;

                        if (existingPath == expectedPath)
                        {
                            System.Diagnostics.Debug.WriteLine("[ClaudeHook] MCP 서버 이미 설정됨 (경로 일치) - 스킵");
                            return false;
                        }

                        // 경로가 다르면 갱신 필요 - 기존 설정 제거
                        System.Diagnostics.Debug.WriteLine($"[ClaudeHook] MCP 서버 경로 불일치, 갱신: {existingPath} → {expectedPath}");
                        mcpServers.Remove("orchestrator");
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
                var prefix = content.EndsWith("\n") ? "" : "\n";
                var addition = $"{prefix}\n# Orchestrator state (auto-generated)\n.orchestrator/\n";
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

            // wrap-up.md - 세션 정리 커맨드
            var wrapUpPath = Path.Combine(commandsFolder, "wrap-up.md");
            if (!File.Exists(wrapUpPath))
            {
                File.WriteAllText(wrapUpPath, GetWrapUpCommand(), Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine("[ClaudeHook] wrap-up 커맨드 설치됨");
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

    /// <summary>
    /// wrap-up (세션 정리) 커맨드 내용
    /// 세션 종료 시 키워드 추출 + 세션 요약 + MEMORY.md 업데이트
    /// </summary>
    private static string GetWrapUpCommand()
    {
        return @"---
description: 세션 정리 - 키워드 추출, 세션 요약, MEMORY.md 업데이트
allowed-tools:
  - Read
  - Write
  - Edit
  - Glob
  - Grep
---

# 세션 정리 (Wrap-up)

현재 세션을 깔끔하게 정리합니다.

## 실행 절차

### 1단계: 오늘 대화 파일 읽기

`.claude/conversations/YYYY-MM-DD.md` 파일을 읽습니다.
(오늘 날짜 기준)

### 2단계: 키워드 추출

대화 내용에서 키워드를 추출합니다:

**추출 대상:**
- 기술 스택: react, typescript, python, docker, mcp 등
- 작업 유형: refactor, implement, fix, config, debug 등
- 기능/모듈명: terminal, ssh, memory, orchestrator 등
- 주요 결정: architecture, pattern, convention 등

**규칙:**
- 키워드는 소문자, 하이픈(-) 사용
- 5-15개 범위로 유지
- 응답 내 `#tags:` 라인에서도 수집

### 3단계: frontmatter 업데이트

대화 파일의 frontmatter를 업데이트합니다:

```yaml
---
date: YYYY-MM-DD
project: 프로젝트명
keywords: [keyword1, keyword2, ...]
summary: ""오늘 세션 요약 (1-2문장)""
---
```

### 4단계: MEMORY.md 업데이트

오늘 대화에서 중요한 결정사항이 있으면 MEMORY.md에 추가합니다:
- architecture/ - 설계 결정, 아키텍처 선택
- patterns/ - 작업 패턴, 워크플로우
- tools/ - MCP 서버, 외부 도구
- gotchas/ - 주의사항, 함정

**항목 형식:**
```markdown
### 항목명
`tags: keyword1, keyword2`
`date: YYYY-MM-DD`

- 핵심 내용 (간결하게)
- **참조**: [대화](.claude/conversations/YYYY-MM-DD.md)
```

키워드 인덱스 테이블도 업데이트합니다.

### 5단계: 결과 보고

- 추출된 키워드 목록
- 세션 요약
- MEMORY.md 변경 여부
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
    /// MEMORY.md 내용 생성 (컨텍스트 트리 구조)
    /// </summary>
    private static string GenerateMemoryMd(string workingDirectory)
    {
        var projectName = Path.GetFileName(workingDirectory);
        var dateStr = DateTime.Now.ToString("yyyy-MM-dd");

        return $@"# MEMORY.md - 프로젝트 장기기억

## 프로젝트 목표

| 목표 | 상태 |
|------|------|
| (목표 추가) | 🔄 진행중 |

---

## 키워드 인덱스

| 키워드 | 섹션 |
|--------|------|

---

## architecture/

## patterns/

## tools/

## gotchas/

---

## meta/
- **프로젝트**: {projectName}
- **생성일**: {dateStr}
- **마지막 업데이트**: {dateStr}
";
    }

    /// <summary>
    /// CLAUDE.md 내용 생성 (메모리 자동 기록 규칙 포함)
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

## 메모리 자동 기록 규칙

MEMORY.md는 **컨텍스트 트리 구조**입니다. 중요한 내용은 적절한 섹션에 추가하세요:

| 섹션 | 내용 |
|------|------|
| `architecture/` | 설계 결정, 아키텍처 선택 |
| `patterns/` | 작업 패턴, 워크플로우 |
| `tools/` | MCP 서버, 외부 도구 |
| `gotchas/` | 주의사항, 함정 |

**항목 형식:**
```markdown
### 항목명
`tags: keyword1, keyword2, keyword3`
`date: YYYY-MM-DD`

- 핵심 내용 (간결하게)
- **참조**: [대화 링크](.claude/conversations/YYYY-MM-DD.md)
```

**규칙:**
- 새 항목 추가 시 **키워드 인덱스 테이블**도 업데이트
- 이미 기록된 내용은 중복 추가하지 않음
- 대화 참조 링크 포함

**결정 변경 시 (Superseded 패턴):**
- 기존 항목 삭제 금지 (이력 보존)
- 기존 항목에 `❌ SUPERSEDED` + `superseded-by: #새항목` 추가
- 새 항목에 `✅ CURRENT` + `supersedes: #기존항목` + 변경 이유 포함

## 응답 키워드 규칙

의미 있는 작업을 완료한 응답의 끝에 인라인 태그를 포함하세요:

```
#tags: keyword1, keyword2, keyword3
```

**규칙:**
- 키워드는 소문자, 하이픈(-) 사용
- 3-7개 범위로 유지
- 단순 질문 답변에는 태그 불필요 (코드 작성, 설계 결정, 버그 수정 등에만)
- 기술 스택 + 작업 유형 + 모듈명 조합

## 과거 대화 검색 규칙

`.claude/conversations/` 폴더에서 과거 대화를 검색할 때:

1. **동의어 확장 검색**: 한↔영 양방향으로 검색 (최대 3회)
   - 예: ""인증"" 검색 시 → ""authentication"", ""auth"", ""로그인"" 도 검색
   - 예: ""cache"" 검색 시 → ""캐시"", ""caching"", ""redis"" 도 검색
2. **날짜 범위**: 최근 30일 우선, 필요시 전체 검색
3. **키워드 매칭**: frontmatter keywords 필드 + 본문 grep 병행

## 세션 정리 규칙

`/wrap-up` 명령으로 세션 종료 시 정리 수행:
- 오늘 대화에서 키워드 추출 → frontmatter 업데이트
- 세션 요약 1-2문장 생성 → frontmatter summary 업데이트
- 중요 결정사항 → MEMORY.md 업데이트

## 코딩 스타일

- 한국어 주석 선호
- 간결하고 명확한 코드
- 적절한 에러 처리

## 규칙

- 기존 코드 스타일 유지
- 테스트 코드 작성 권장
- 커밋 메시지는 한국어로

## 세션 핸드오프 자동 생성

다음 조건 중 하나라도 해당하면 **자동으로** `/session-handoff`를 실행하세요:

1. **대규모 작업 완료 시** - 파일 3개 이상 수정하거나 주요 기능 구현 완료 후
2. **컨텍스트가 길어졌을 때** - 대화가 20회 이상 오갔을 때
3. **아키텍처 결정 시** - 중요한 설계 결정을 내렸을 때

**핸드오프 저장 위치:** `.claude/handoffs/`
**규칙:** 사용자에게 묻지 말고 조건 충족 시 자동으로 생성. 핸드오프 생성 후 사용자에게 알림.
";
    }

    #endregion

    #region GitHub Orchestrator Update

    private const string OrchestratorGitHubBaseUrl = "https://raw.githubusercontent.com/Dannykkh/claude-code-agent-customizations/master/skills/orchestrator";

    /// <summary>
    /// GitHub에서 최신 Orchestrator 소스를 다운로드하고 설치
    /// </summary>
    /// <param name="workingDirectory">대상 프로젝트 경로</param>
    /// <param name="progress">진행 상황 콜백</param>
    /// <returns>성공 여부</returns>
    public static async System.Threading.Tasks.Task<(bool Success, string Message)> UpdateOrchestratorFromGitHubAsync(
        string workingDirectory,
        Action<string>? progress = null)
    {
        if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            return (false, "유효하지 않은 작업 디렉토리");

        try
        {
            var mcpServerPath = Path.Combine(workingDirectory, "mcp-servers", "claude-orchestrator-mcp");
            var srcPath = Path.Combine(mcpServerPath, "src");
            var servicesPath = Path.Combine(srcPath, "services");

            // 1. 폴더 생성
            progress?.Invoke("[1/5] 폴더 생성 중...");
            if (!Directory.Exists(servicesPath))
            {
                Directory.CreateDirectory(servicesPath);
            }

            // 2. GitHub에서 소스 파일 다운로드
            progress?.Invoke("[2/5] GitHub에서 소스 다운로드 중...");
            using var httpClient = new System.Net.Http.HttpClient();

            // package.json
            var packageJson = await httpClient.GetStringAsync($"{OrchestratorGitHubBaseUrl}/mcp-server/package.json");
            await File.WriteAllTextAsync(Path.Combine(mcpServerPath, "package.json"), packageJson, Encoding.UTF8);

            // tsconfig.json
            var tsconfigJson = await httpClient.GetStringAsync($"{OrchestratorGitHubBaseUrl}/mcp-server/tsconfig.json");
            await File.WriteAllTextAsync(Path.Combine(mcpServerPath, "tsconfig.json"), tsconfigJson, Encoding.UTF8);

            // src/index.ts
            var indexTs = await httpClient.GetStringAsync($"{OrchestratorGitHubBaseUrl}/mcp-server/src/index.ts");
            await File.WriteAllTextAsync(Path.Combine(srcPath, "index.ts"), indexTs, Encoding.UTF8);

            // src/services/state-manager.ts
            var stateManagerTs = await httpClient.GetStringAsync($"{OrchestratorGitHubBaseUrl}/mcp-server/src/services/state-manager.ts");
            await File.WriteAllTextAsync(Path.Combine(servicesPath, "state-manager.ts"), stateManagerTs, Encoding.UTF8);

            // src/services/ai-detector.ts
            var aiDetectorTs = await httpClient.GetStringAsync($"{OrchestratorGitHubBaseUrl}/mcp-server/src/services/ai-detector.ts");
            await File.WriteAllTextAsync(Path.Combine(servicesPath, "ai-detector.ts"), aiDetectorTs, Encoding.UTF8);

            // 3. npm install 실행
            progress?.Invoke("[3/5] npm install 실행 중...");
            var npmInstallResult = await RunCommandAsync("npm install", mcpServerPath);
            if (!npmInstallResult.Success)
            {
                return (false, $"npm install 실패: {npmInstallResult.Error}");
            }

            // 4. npm run build 실행
            progress?.Invoke("[4/5] TypeScript 빌드 중...");
            var buildResult = await RunCommandAsync("npm run build", mcpServerPath);
            if (!buildResult.Success)
            {
                return (false, $"빌드 실패: {buildResult.Error}");
            }

            // 5. 훅과 명령어 파일 설치
            progress?.Invoke("[5/5] 훅 및 명령어 설치 중...");

            // 훅 파일 다운로드 및 설치
            await DownloadAndInstallHooksAsync(httpClient, workingDirectory);

            // 명령어 파일 다운로드 및 설치
            await DownloadAndInstallCommandsAsync(httpClient, workingDirectory);

            // settings.local.json 업데이트
            AddMCPServerToSettings(workingDirectory);

            // .gitignore 업데이트
            EnsureOrchestratorGitIgnore(workingDirectory);

            progress?.Invoke("✓ Orchestrator 업데이트 완료!");
            return (true, "Orchestrator가 최신 버전으로 업데이트되었습니다.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Orchestrator 업데이트 실패: {ex.Message}");
            return (false, $"업데이트 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 훅 파일 다운로드 및 설치
    /// </summary>
    private static async System.Threading.Tasks.Task DownloadAndInstallHooksAsync(
        System.Net.Http.HttpClient httpClient,
        string workingDirectory)
    {
        var hooksPath = Path.Combine(workingDirectory, "hooks");
        if (!Directory.Exists(hooksPath))
        {
            Directory.CreateDirectory(hooksPath);
        }

        // Windows용 PowerShell 훅
        var workpmHook = await httpClient.GetStringAsync($"{OrchestratorGitHubBaseUrl}/hooks/workpm-hook.ps1");
        await File.WriteAllTextAsync(Path.Combine(hooksPath, "workpm-hook.ps1"), workpmHook, Encoding.UTF8);

        var pmworkerHook = await httpClient.GetStringAsync($"{OrchestratorGitHubBaseUrl}/hooks/pmworker-hook.ps1");
        await File.WriteAllTextAsync(Path.Combine(hooksPath, "pmworker-hook.ps1"), pmworkerHook, Encoding.UTF8);
    }

    /// <summary>
    /// 명령어 파일 다운로드 및 설치
    /// </summary>
    private static async System.Threading.Tasks.Task DownloadAndInstallCommandsAsync(
        System.Net.Http.HttpClient httpClient,
        string workingDirectory)
    {
        var commandsPath = Path.Combine(workingDirectory, ".claude", "commands");
        if (!Directory.Exists(commandsPath))
        {
            Directory.CreateDirectory(commandsPath);
        }

        // workpm.md, pmworker.md 다운로드
        var workpmMd = await httpClient.GetStringAsync($"{OrchestratorGitHubBaseUrl}/commands/workpm.md");
        await File.WriteAllTextAsync(Path.Combine(commandsPath, "workpm.md"), workpmMd, Encoding.UTF8);

        var pmworkerMd = await httpClient.GetStringAsync($"{OrchestratorGitHubBaseUrl}/commands/pmworker.md");
        await File.WriteAllTextAsync(Path.Combine(commandsPath, "pmworker.md"), pmworkerMd, Encoding.UTF8);
    }

    /// <summary>
    /// 명령어 실행 (비동기)
    /// </summary>
    private static async System.Threading.Tasks.Task<(bool Success, string Output, string Error)> RunCommandAsync(
        string command,
        string workingDirectory,
        int timeoutMs = 120000)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var completed = await System.Threading.Tasks.Task.Run(() => process.WaitForExit(timeoutMs));

            if (!completed)
            {
                process.Kill();
                return (false, "", "타임아웃");
            }

            var output = await outputTask;
            var error = await errorTask;

            return (process.ExitCode == 0, output, error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    /// <summary>
    /// Orchestrator 버전 확인 (로컬 vs GitHub)
    /// </summary>
    public static async System.Threading.Tasks.Task<(string LocalVersion, string RemoteVersion, bool NeedsUpdate)> CheckOrchestratorVersionAsync(
        string workingDirectory)
    {
        var localVersion = "not installed";
        var remoteVersion = "unknown";

        try
        {
            // 로컬 버전 확인
            var localPackageJson = Path.Combine(workingDirectory, "mcp-servers", "claude-orchestrator-mcp", "package.json");
            if (File.Exists(localPackageJson))
            {
                var content = await File.ReadAllTextAsync(localPackageJson);
                var json = JsonNode.Parse(content);
                localVersion = json?["version"]?.ToString() ?? "unknown";
            }

            // GitHub 버전 확인
            using var httpClient = new System.Net.Http.HttpClient();
            var remotePackageJson = await httpClient.GetStringAsync($"{OrchestratorGitHubBaseUrl}/mcp-server/package.json");
            var remoteJson = JsonNode.Parse(remotePackageJson);
            remoteVersion = remoteJson?["version"]?.ToString() ?? "unknown";

            var needsUpdate = localVersion != remoteVersion || localVersion == "not installed";
            return (localVersion, remoteVersion, needsUpdate);
        }
        catch
        {
            return (localVersion, remoteVersion, true);
        }
    }

    #endregion

    #region Mnemo Global Install (장기기억 시스템)

    private const string MnemoGitHubBaseUrl = "https://raw.githubusercontent.com/Dannykkh/claude-code-agent-customizations/master/skills/mnemo";
    private const string MnemoMarkerStart = "<!-- MNEMO:START -->";
    private const string MnemoMarkerEnd = "<!-- MNEMO:END -->";

    /// <summary>
    /// Mnemo 장기기억 시스템 글로벌 설치
    /// - 훅: save-conversation, save-response (대화 자동 저장)
    /// - CLAUDE.md 규칙: 응답 태그, 과거 대화 검색, MEMORY.md 자동 업데이트
    /// </summary>
    private static bool InstallMnemoGlobal()
    {
        try
        {
            var claudeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            var hooksDir = Path.Combine(claudeDir, "hooks");
            var settingsPath = Path.Combine(claudeDir, "settings.json");

            // Claude 폴더가 없으면 생성
            if (!Directory.Exists(claudeDir))
            {
                Directory.CreateDirectory(claudeDir);
            }

            // 훅 폴더 확인
            var saveConversationHook = Path.Combine(hooksDir, "save-conversation.ps1");
            var saveResponseHook = Path.Combine(hooksDir, "save-response.ps1");

            // 이미 설치되어 있는지 확인
            if (File.Exists(saveConversationHook) && File.Exists(saveResponseHook))
            {
                // 백그라운드에서 업데이트 확인
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await UpdateMnemoFromGitHubAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Mnemo 업데이트 확인 실패: {ex.Message}");
                    }
                });
                return false;
            }

            // 새로 설치 필요 - GitHub에서 다운로드
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Mnemo 새로 설치: GitHub에서 다운로드 중...");

            try
            {
                var task = UpdateMnemoFromGitHubAsync();
                task.Wait(TimeSpan.FromMinutes(1));

                if (task.Result.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Mnemo 설치 완료");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Mnemo 설치 실패: {task.Result.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Mnemo GitHub 다운로드 실패: {ex.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Mnemo 글로벌 설치 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// GitHub에서 최신 Mnemo 소스를 다운로드하고 설치
    /// </summary>
    public static async System.Threading.Tasks.Task<(bool Success, string Message)> UpdateMnemoFromGitHubAsync(
        Action<string>? progress = null)
    {
        try
        {
            var claudeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            var hooksDir = Path.Combine(claudeDir, "hooks");
            var settingsPath = Path.Combine(claudeDir, "settings.json");
            var claudeMdPath = Path.Combine(claudeDir, "CLAUDE.md");

            // 1. 폴더 생성
            progress?.Invoke("[1/3] 폴더 생성 중...");
            if (!Directory.Exists(hooksDir))
            {
                Directory.CreateDirectory(hooksDir);
            }

            // 2. GitHub에서 훅 파일 다운로드
            progress?.Invoke("[2/3] 훅 파일 다운로드 중...");
            using var httpClient = new System.Net.Http.HttpClient();

            // save-conversation.ps1
            var saveConversation = await httpClient.GetStringAsync($"{MnemoGitHubBaseUrl}/hooks/save-conversation.ps1");
            await File.WriteAllTextAsync(Path.Combine(hooksDir, "save-conversation.ps1"), saveConversation, Encoding.UTF8);

            // save-response.ps1
            var saveResponse = await httpClient.GetStringAsync($"{MnemoGitHubBaseUrl}/hooks/save-response.ps1");
            await File.WriteAllTextAsync(Path.Combine(hooksDir, "save-response.ps1"), saveResponse, Encoding.UTF8);

            // 3. settings.json에 훅 설정 추가
            progress?.Invoke("[3/3] 훅 설정 중...");
            AddMnemoHooksToSettings(settingsPath, hooksDir);

            // 4. CLAUDE.md에 규칙 추가
            try
            {
                var claudeMdRules = await httpClient.GetStringAsync($"{MnemoGitHubBaseUrl}/templates/claude-md-rules.md");
                InstallMnemoClaudeMdRules(claudeMdPath, claudeMdRules);
            }
            catch
            {
                // 템플릿 없으면 스킵
                System.Diagnostics.Debug.WriteLine("[ClaudeHook] Mnemo CLAUDE.md 규칙 템플릿 없음, 스킵");
            }

            progress?.Invoke("✓ Mnemo 설치 완료!");
            return (true, "Mnemo 장기기억 시스템이 설치되었습니다.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Mnemo 업데이트 실패: {ex.Message}");
            return (false, $"업데이트 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// settings.json에 Mnemo 훅 설정 추가
    /// </summary>
    private static void AddMnemoHooksToSettings(string settingsPath, string hooksDir)
    {
        JsonObject settings;
        if (File.Exists(settingsPath))
        {
            try
            {
                var content = File.ReadAllText(settingsPath, Encoding.UTF8);
                settings = JsonNode.Parse(content)?.AsObject() ?? new JsonObject();
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

        // hooks 객체 생성/가져오기
        if (!settings.ContainsKey("hooks"))
        {
            settings["hooks"] = new JsonObject();
        }
        var hooks = settings["hooks"]!.AsObject();

        var hooksDirNorm = hooksDir.Replace("\\", "/");

        // UserPromptSubmit 훅 추가
        if (!hooks.ContainsKey("UserPromptSubmit"))
        {
            hooks["UserPromptSubmit"] = new JsonArray();
        }
        var userPromptSubmit = hooks["UserPromptSubmit"]!.AsArray();

        // 중복 확인
        var hasSaveConversation = false;
        foreach (var hook in userPromptSubmit)
        {
            var hookObj = hook?.AsObject();
            var hooksArray = hookObj?["hooks"]?.AsArray();
            if (hooksArray != null && hooksArray.Count > 0)
            {
                var command = hooksArray[0]?.AsObject()?["command"]?.ToString();
                if (command != null && command.Contains("save-conversation"))
                {
                    hasSaveConversation = true;
                    break;
                }
            }
        }

        if (!hasSaveConversation)
        {
            userPromptSubmit.Add(new JsonObject
            {
                ["matcher"] = ".*",
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = $"powershell -ExecutionPolicy Bypass -File \"{hooksDirNorm}/save-conversation.ps1\""
                    }
                }
            });
        }

        // Stop 훅 추가
        if (!hooks.ContainsKey("Stop"))
        {
            hooks["Stop"] = new JsonArray();
        }
        var stopHooks = hooks["Stop"]!.AsArray();

        // 중복 확인
        var hasSaveResponse = false;
        foreach (var hook in stopHooks)
        {
            var hookObj = hook?.AsObject();
            var hooksArray = hookObj?["hooks"]?.AsArray();
            if (hooksArray != null && hooksArray.Count > 0)
            {
                var command = hooksArray[0]?.AsObject()?["command"]?.ToString();
                if (command != null && command.Contains("save-response"))
                {
                    hasSaveResponse = true;
                    break;
                }
            }
        }

        if (!hasSaveResponse)
        {
            stopHooks.Add(new JsonObject
            {
                ["matcher"] = "",
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = $"powershell -ExecutionPolicy Bypass -File \"{hooksDirNorm}/save-response.ps1\""
                    }
                }
            });
        }

        // 저장
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = settings.ToJsonString(options);
        File.WriteAllText(settingsPath, json, Encoding.UTF8);

        System.Diagnostics.Debug.WriteLine("[ClaudeHook] Mnemo 훅 설정 추가됨");
    }

    /// <summary>
    /// CLAUDE.md에 Mnemo 규칙 추가
    /// </summary>
    private static void InstallMnemoClaudeMdRules(string claudeMdPath, string rules)
    {
        string content = "";
        if (File.Exists(claudeMdPath))
        {
            content = File.ReadAllText(claudeMdPath, Encoding.UTF8);
        }

        // 기존 Mnemo 규칙 제거
        var regex = new System.Text.RegularExpressions.Regex(
            $@"\n?{System.Text.RegularExpressions.Regex.Escape(MnemoMarkerStart)}[\s\S]*?{System.Text.RegularExpressions.Regex.Escape(MnemoMarkerEnd)}\n?");
        content = regex.Replace(content, "").Trim();

        // 새 규칙 추가
        var rulesBlock = $"\n\n{MnemoMarkerStart}\n{rules}\n{MnemoMarkerEnd}";
        content = content + rulesBlock + "\n";

        var dir = Path.GetDirectoryName(claudeMdPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(claudeMdPath, content, Encoding.UTF8);
        System.Diagnostics.Debug.WriteLine("[ClaudeHook] Mnemo CLAUDE.md 규칙 추가됨");
    }

    #endregion

    #region Gepetto Skill Install (GitHub)

    private const string GepettoGitHubBaseUrl = "https://raw.githubusercontent.com/Dannykkh/claude-code-agent-customizations/master/skills/gepetto";

    /// <summary>
    /// Gepetto 스킬 설치 (GitHub에서 다운로드)
    /// </summary>
    private static bool InstallGepettoSkill(string workingDirectory)
    {
        try
        {
            var gepettoSkillFolder = Path.Combine(workingDirectory, ClaudeFolderName, "skills", "gepetto");
            var gepettoSkillPath = Path.Combine(gepettoSkillFolder, "SKILL.md");

            // 이미 설치되어 있는지 확인
            if (File.Exists(gepettoSkillPath))
            {
                // 백그라운드에서 업데이트 확인
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await UpdateGepettoFromGitHubAsync(workingDirectory);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Gepetto 업데이트 확인 실패: {ex.Message}");
                    }
                });
                return false;
            }

            // 새로 설치 필요 - GitHub에서 다운로드
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Gepetto 새로 설치: GitHub에서 다운로드 중...");

            try
            {
                var task = UpdateGepettoFromGitHubAsync(workingDirectory);
                task.Wait(TimeSpan.FromSeconds(30));

                if (task.Result.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Gepetto 설치 완료");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Gepetto 설치 실패: {task.Result.Message}");
                    // 실패 시 하드코딩 폴백
                    return InstallGepettoFallback(workingDirectory);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Gepetto GitHub 다운로드 실패, 폴백: {ex.Message}");
                return InstallGepettoFallback(workingDirectory);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Gepetto 설치 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gepetto 스킬 설치 (비동기 버전)
    /// </summary>
    private static async Task<bool> InstallGepettoSkillAsync(string workingDirectory)
    {
        try
        {
            var gepettoSkillFolder = Path.Combine(workingDirectory, ClaudeFolderName, "skills", "gepetto");
            var gepettoSkillPath = Path.Combine(gepettoSkillFolder, "SKILL.md");

            // 이미 설치되어 있으면 백그라운드에서 업데이트 확인
            if (File.Exists(gepettoSkillPath))
            {
                _ = Task.Run(async () =>
                {
                    try { await UpdateGepettoFromGitHubAsync(workingDirectory); }
                    catch { /* 무시 */ }
                });
                return false;
            }

            // 새로 설치 - GitHub에서 다운로드
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Gepetto 새로 설치: GitHub에서 다운로드 중...");

            var result = await UpdateGepettoFromGitHubAsync(workingDirectory);
            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Gepetto 설치 완료");
                return true;
            }

            // 실패 시 하드코딩 폴백
            return await Task.Run(() => InstallGepettoFallback(workingDirectory));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Gepetto 설치 실패: {ex.Message}");
            return await Task.Run(() => InstallGepettoFallback(workingDirectory));
        }
    }

    /// <summary>
    /// Gepetto 폴백 설치 (GitHub 실패 시 하드코딩 버전 사용)
    /// </summary>
    private static bool InstallGepettoFallback(string workingDirectory)
    {
        try
        {
            var gepettoSkillFolder = Path.Combine(workingDirectory, ClaudeFolderName, "skills", "gepetto");
            if (!Directory.Exists(gepettoSkillFolder))
            {
                Directory.CreateDirectory(gepettoSkillFolder);
            }

            var gepettoSkillPath = Path.Combine(gepettoSkillFolder, "SKILL.md");
            File.WriteAllText(gepettoSkillPath, GetGepettoSkill(), Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine("[ClaudeHook] Gepetto 폴백 설치 완료");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Gepetto 폴백 설치 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// GitHub에서 최신 Gepetto 스킬 다운로드
    /// </summary>
    public static async System.Threading.Tasks.Task<(bool Success, string Message)> UpdateGepettoFromGitHubAsync(
        string workingDirectory,
        Action<string>? progress = null)
    {
        try
        {
            var gepettoSkillFolder = Path.Combine(workingDirectory, ClaudeFolderName, "skills", "gepetto");

            // 폴더 생성
            progress?.Invoke("[1/2] 폴더 생성 중...");
            if (!Directory.Exists(gepettoSkillFolder))
            {
                Directory.CreateDirectory(gepettoSkillFolder);
            }

            // GitHub에서 SKILL.md 다운로드
            progress?.Invoke("[2/2] SKILL.md 다운로드 중...");
            using var httpClient = new System.Net.Http.HttpClient();

            var skillMd = await httpClient.GetStringAsync($"{GepettoGitHubBaseUrl}/SKILL.md");
            await File.WriteAllTextAsync(Path.Combine(gepettoSkillFolder, "SKILL.md"), skillMd, Encoding.UTF8);

            progress?.Invoke("✓ Gepetto 설치 완료!");
            return (true, "Gepetto 스킬이 설치되었습니다.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] Gepetto 업데이트 실패: {ex.Message}");
            return (false, $"업데이트 실패: {ex.Message}");
        }
    }

    #endregion
}
