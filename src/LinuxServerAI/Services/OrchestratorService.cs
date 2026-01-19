using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Nebula.Services;

/// <summary>
/// Claude Code Orchestrator 상태 관리 및 실행 서비스
/// </summary>
public class OrchestratorService
{
    private readonly string _mcpServerPath;
    private FileSystemWatcher? _stateWatcher;

    public event EventHandler<OrchestratorState>? StateChanged;

    public OrchestratorService()
    {
        // MCP 서버 경로 (앱 실행 경로 기준)
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _mcpServerPath = Path.Combine(appDir, "..", "..", "..", "..", "claude-orchestrator-mcp");

        // 개발 환경에서 경로 조정
        if (!Directory.Exists(_mcpServerPath))
        {
            _mcpServerPath = Path.Combine(appDir, "claude-orchestrator-mcp");
        }
    }

    /// <summary>
    /// MCP 서버 경로
    /// </summary>
    public string McpServerPath => Path.GetFullPath(_mcpServerPath);

    /// <summary>
    /// MCP 서버가 빌드되어 있는지 확인
    /// </summary>
    public bool IsMcpServerBuilt => File.Exists(Path.Combine(_mcpServerPath, "dist", "index.js"));

    /// <summary>
    /// MCP 서버 빌드
    /// </summary>
    public async Task<(bool Success, string Output)> BuildMcpServerAsync()
    {
        if (!Directory.Exists(_mcpServerPath))
        {
            return (false, $"MCP 서버 디렉토리를 찾을 수 없습니다: {_mcpServerPath}");
        }

        try
        {
            // npm install
            var installResult = await RunCommandAsync("npm", "install", _mcpServerPath);
            if (!installResult.Success)
            {
                return (false, $"npm install 실패: {installResult.Output}");
            }

            // npm run build
            var buildResult = await RunCommandAsync("npm", "run build", _mcpServerPath);
            if (!buildResult.Success)
            {
                return (false, $"npm run build 실패: {buildResult.Output}");
            }

            return (true, "빌드 완료");
        }
        catch (Exception ex)
        {
            return (false, $"빌드 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 오케스트레이터 실행 (PowerShell 스크립트)
    /// </summary>
    public async Task<(bool Success, string Output)> LaunchOrchestratorAsync(
        string projectPath,
        int workerCount = 3,
        bool cleanStart = false)
    {
        var scriptPath = Path.Combine(_mcpServerPath, "scripts", "launch.ps1");
        if (!File.Exists(scriptPath))
        {
            return (false, $"실행 스크립트를 찾을 수 없습니다: {scriptPath}");
        }

        try
        {
            var args = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -ProjectPath \"{projectPath}\" -WorkerCount {workerCount}";
            if (cleanStart)
            {
                args += " -CleanStart";
            }

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = projectPath
            };

            Process.Start(psi);
            return (true, "오케스트레이터가 실행되었습니다.");
        }
        catch (Exception ex)
        {
            return (false, $"실행 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 오케스트레이터 상태 조회
    /// </summary>
    public OrchestratorState? GetState(string projectPath)
    {
        var statePath = Path.Combine(projectPath, ".orchestrator", "state.json");
        if (!File.Exists(statePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize<OrchestratorState>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 상태 파일 감시 시작
    /// </summary>
    public void StartWatching(string projectPath)
    {
        StopWatching();

        var orchestratorDir = Path.Combine(projectPath, ".orchestrator");
        if (!Directory.Exists(orchestratorDir))
        {
            return;
        }

        _stateWatcher = new FileSystemWatcher(orchestratorDir, "state.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _stateWatcher.Changed += (s, e) =>
        {
            try
            {
                // 디바운스를 위해 약간 대기
                System.Threading.Thread.Sleep(100);
                var state = GetState(projectPath);
                if (state != null)
                {
                    StateChanged?.Invoke(this, state);
                }
            }
            catch { /* 무시 */ }
        };
    }

    /// <summary>
    /// 상태 파일 감시 중지
    /// </summary>
    public void StopWatching()
    {
        if (_stateWatcher != null)
        {
            _stateWatcher.EnableRaisingEvents = false;
            _stateWatcher.Dispose();
            _stateWatcher = null;
        }
    }

    /// <summary>
    /// 진행률 계산
    /// </summary>
    public OrchestratorProgress GetProgress(OrchestratorState state)
    {
        var total = state.Tasks.Count;
        var completed = state.Tasks.Count(t => t.Status == "completed");
        var failed = state.Tasks.Count(t => t.Status == "failed");
        var inProgress = state.Tasks.Count(t => t.Status == "in_progress");
        var pending = state.Tasks.Count(t => t.Status == "pending");

        return new OrchestratorProgress
        {
            Total = total,
            Completed = completed,
            Failed = failed,
            InProgress = inProgress,
            Pending = pending,
            PercentComplete = total > 0 ? (int)Math.Round((double)completed / total * 100) : 0
        };
    }

    private async Task<(bool Success, string Output)> RunCommandAsync(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            return (false, string.IsNullOrEmpty(error) ? output : error);
        }

        return (true, output);
    }
}

// ============================================================================
// 데이터 모델
// ============================================================================

public class OrchestratorState
{
    [JsonPropertyName("tasks")]
    public List<OrchestratorTask> Tasks { get; set; } = new();

    [JsonPropertyName("fileLocks")]
    public List<FileLock> FileLocks { get; set; } = new();

    [JsonPropertyName("workers")]
    public List<WorkerInfo> Workers { get; set; } = new();

    [JsonPropertyName("projectRoot")]
    public string ProjectRoot { get; set; } = "";

    [JsonPropertyName("startedAt")]
    public string StartedAt { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}

public class OrchestratorTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("dependsOn")]
    public List<string> DependsOn { get; set; } = new();

    [JsonPropertyName("scope")]
    public List<string>? Scope { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("startedAt")]
    public string? StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public string? CompletedAt { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class FileLock
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = "";

    [JsonPropertyName("lockedAt")]
    public string LockedAt { get; set; } = "";

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public class WorkerInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("currentTask")]
    public string? CurrentTask { get; set; }

    [JsonPropertyName("lastHeartbeat")]
    public string LastHeartbeat { get; set; } = "";

    [JsonPropertyName("completedTasks")]
    public int CompletedTasks { get; set; }
}

public class OrchestratorProgress
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int InProgress { get; set; }
    public int Pending { get; set; }
    public int PercentComplete { get; set; }
}
