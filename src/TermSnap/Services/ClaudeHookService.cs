using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace TermSnap.Services;

/// <summary>
/// Claude Code 훅 자동 설정 서비스
/// - .claude/settings.local.json 생성
/// - 장기기억 훅 설정
/// </summary>
public static class ClaudeHookService
{
    private const string ClaudeFolderName = ".claude";
    private const string SettingsFileName = "settings.local.json";
    private const string MemoryFileName = "MEMORY.md";

    /// <summary>
    /// Claude Code 실행 전 훅 설정 확인 및 생성
    /// </summary>
    /// <param name="workingDirectory">프로젝트 작업 디렉토리</param>
    /// <returns>훅이 생성/업데이트 되었는지 여부</returns>
    public static bool EnsureMemoryHooks(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            return false;

        try
        {
            // .claude 폴더 생성
            var claudeFolder = Path.Combine(workingDirectory, ClaudeFolderName);
            if (!Directory.Exists(claudeFolder))
            {
                Directory.CreateDirectory(claudeFolder);
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] .claude 폴더 생성: {claudeFolder}");
            }

            // settings.local.json 경로
            var settingsPath = Path.Combine(claudeFolder, SettingsFileName);

            // 기존 설정 로드 또는 새로 생성
            JsonObject settings;
            if (File.Exists(settingsPath))
            {
                var existingContent = File.ReadAllText(settingsPath, Encoding.UTF8);
                settings = JsonNode.Parse(existingContent)?.AsObject() ?? new JsonObject();

                // 이미 hooks가 있고 memory 관련 훅이 있으면 스킵
                if (HasMemoryHooks(settings))
                {
                    System.Diagnostics.Debug.WriteLine("[ClaudeHook] 메모리 훅이 이미 존재함 - 스킵");
                    return false;
                }
            }
            else
            {
                settings = new JsonObject();
            }

            // 훅 추가
            AddMemoryHooks(settings, workingDirectory);

            // 저장
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = settings.ToJsonString(options);
            File.WriteAllText(settingsPath, json, Encoding.UTF8);

            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] 메모리 훅 설정 완료: {settingsPath}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] 훅 설정 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 메모리 훅이 이미 있는지 확인
    /// </summary>
    private static bool HasMemoryHooks(JsonObject settings)
    {
        if (!settings.ContainsKey("hooks"))
            return false;

        var hooks = settings["hooks"]?.AsObject();
        if (hooks == null)
            return false;

        // PreToolUse나 UserPromptSubmit에 memory 관련 훅이 있는지 확인
        foreach (var hookType in new[] { "PreToolUse", "UserPromptSubmit" })
        {
            if (hooks.ContainsKey(hookType))
            {
                var hookArray = hooks[hookType]?.AsArray();
                if (hookArray != null)
                {
                    foreach (var hook in hookArray)
                    {
                        var matcher = hook?["matcher"]?.ToString();
                        if (matcher != null && matcher.Contains("MEMORY", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 메모리 훅 추가
    /// </summary>
    private static void AddMemoryHooks(JsonObject settings, string workingDirectory)
    {
        // hooks 객체 가져오기 또는 생성
        if (!settings.ContainsKey("hooks"))
        {
            settings["hooks"] = new JsonObject();
        }
        var hooks = settings["hooks"]!.AsObject();

        // MEMORY.md 경로
        var memoryPath = Path.Combine(workingDirectory, MemoryFileName).Replace("\\", "/");

        // 1. UserPromptSubmit 훅 - "기억해" 패턴 감지
        var userPromptHooks = hooks["UserPromptSubmit"]?.AsArray() ?? new JsonArray();

        // 기억 저장 훅 추가
        var memorySaveMsg = GetLocalizedString("ClaudeHook.MemorySaveDetected");
        userPromptHooks.Add(new JsonObject
        {
            ["matcher"] = "(?i)(기억해|remember|기억 ?저장|save.*memory)",
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = $"echo '{memorySaveMsg}'"
                }
            }
        });

        hooks["UserPromptSubmit"] = userPromptHooks;

        // 2. Notification 훅 - ULTRAWORK 모드 알림
        var notificationHooks = hooks["Notification"]?.AsArray() ?? new JsonArray();

        var taskCompleteMsg = GetLocalizedString("ClaudeHook.TaskCompleteNotify");
        notificationHooks.Add(new JsonObject
        {
            ["matcher"] = "(?i)(task.*complete|작업.*완료|ULTRAWORK.*complete)",
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = $"echo '{taskCompleteMsg}'"
                }
            }
        });

        hooks["Notification"] = notificationHooks;

        // 설명 주석 추가 (실제로는 JSON에 주석 불가하지만 별도 키로)
        settings["_termsnap_info"] = GetLocalizedString("ClaudeHook.AutoGenerated");
    }

    /// <summary>
    /// 리소스에서 문자열 가져오기 (Application.Current가 있을 때만)
    /// </summary>
    private static string GetLocalizedString(string key)
    {
        try
        {
            if (Application.Current != null)
            {
                return LocalizationService.Instance.GetString(key);
            }
        }
        catch { }

        // 폴백: 키 반환
        return key;
    }

    /// <summary>
    /// MEMORY.md 파일 참조를 CLAUDE.md에 추가 (import 구문 사용)
    /// </summary>
    public static bool EnsureMemoryReference(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            return false;

        try
        {
            var claudeMdPath = Path.Combine(workingDirectory, "CLAUDE.md");
            var memoryMdPath = Path.Combine(workingDirectory, MemoryFileName);
            var created = false;

            // 1. MEMORY.md가 없으면 생성
            if (!File.Exists(memoryMdPath))
            {
                var memoryContent = GenerateMemoryMd(workingDirectory);
                File.WriteAllText(memoryMdPath, memoryContent, Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] MEMORY.md 생성: {memoryMdPath}");
                created = true;
            }

            // 2. CLAUDE.md가 없으면 생성
            if (!File.Exists(claudeMdPath))
            {
                var content = GenerateClaudeMd(workingDirectory);
                File.WriteAllText(claudeMdPath, content, Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine($"[ClaudeHook] CLAUDE.md 생성: {claudeMdPath}");
                return true;
            }

            // 3. 이미 @MEMORY.md import가 있는지 확인
            var existingContent = File.ReadAllText(claudeMdPath, Encoding.UTF8);
            if (existingContent.Contains("@MEMORY.md", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine("[ClaudeHook] CLAUDE.md에 이미 @MEMORY.md import 있음");
                return created;
            }

            // 4. @MEMORY.md import 구문 추가 (파일 상단에)
            var importLine = "@MEMORY.md\n\n";
            var newContent = importLine + existingContent;
            File.WriteAllText(claudeMdPath, newContent, Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine("[ClaudeHook] CLAUDE.md에 @MEMORY.md import 추가");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeHook] CLAUDE.md 업데이트 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 기본 CLAUDE.md 생성 (@MEMORY.md import 포함)
    /// </summary>
    private static string GenerateClaudeMd(string workingDirectory)
    {
        var projectName = Path.GetFileName(workingDirectory);
        var dateStr = DateTime.Now.ToString("yyyy-MM-dd");

        var title = GetLocalizedString("ClaudeHook.ClaudeMd.Title");
        var projectInfo = string.Format(GetLocalizedString("ClaudeHook.ClaudeMd.ProjectInfo"), projectName, dateStr);
        var codingStyle = GetLocalizedString("ClaudeHook.ClaudeMd.CodingStyle");
        var memoryRules = GetLocalizedString("ClaudeHook.ClaudeMd.MemoryRules");

        return $@"@MEMORY.md

{title}

{projectInfo}

{codingStyle}

{memoryRules}
";
    }

    /// <summary>
    /// 기본 MEMORY.md 생성 (장기기억 저장 파일)
    /// </summary>
    private static string GenerateMemoryMd(string workingDirectory)
    {
        var projectName = Path.GetFileName(workingDirectory);
        var dateStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        return $@"# AI 장기기억 (MEMORY.md)

> 이 파일은 AI가 참조하는 장기기억입니다. CLAUDE.md에서 이 파일을 참조합니다.
> TermSnap에서 자동 관리되며, 직접 편집해도 됩니다.

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
}
