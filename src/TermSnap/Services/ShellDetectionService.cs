using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TermSnap.Core.Sessions;
using Microsoft.Win32;

namespace TermSnap.Services;

/// <summary>
/// 시스템에 설치된 쉘 프로그램을 감지하는 서비스
/// </summary>
public class ShellDetectionService
{
    /// <summary>
    /// 감지된 쉘 정보
    /// </summary>
    public class DetectedShell
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string IconKind { get; set; } = "Console";
        public LocalSession.LocalShellType ShellType { get; set; }
        public bool IsDefault { get; set; }
    }

    private static readonly Lazy<ShellDetectionService> _instance = new(() => new ShellDetectionService());
    public static ShellDetectionService Instance => _instance.Value;

    private List<DetectedShell>? _cachedShells;

    private ShellDetectionService() { }

    /// <summary>
    /// 설치된 모든 쉘 감지
    /// </summary>
    public List<DetectedShell> DetectInstalledShells(bool forceRefresh = false)
    {
        if (_cachedShells != null && !forceRefresh)
            return _cachedShells;

        var shells = new List<DetectedShell>();

        // PowerShell Core (pwsh.exe) - 최신 버전
        var pwshPath = FindPowerShellCore();
        if (!string.IsNullOrEmpty(pwshPath))
        {
            shells.Add(new DetectedShell
            {
                Name = "pwsh",
                DisplayName = "PowerShell",
                Path = pwshPath,
                Arguments = "-NoLogo",
                IconKind = "Powershell",
                ShellType = LocalSession.LocalShellType.PowerShell,
                IsDefault = true
            });
        }

        // Windows PowerShell (powershell.exe) - 기본 설치
        var windowsPowerShellPath = FindWindowsPowerShell();
        if (!string.IsNullOrEmpty(windowsPowerShellPath))
        {
            shells.Add(new DetectedShell
            {
                Name = "powershell",
                DisplayName = "Windows PowerShell",
                Path = windowsPowerShellPath,
                Arguments = "-NoLogo",
                IconKind = "Powershell",
                ShellType = LocalSession.LocalShellType.PowerShell,
                IsDefault = shells.Count == 0 // pwsh가 없으면 기본
            });
        }

        // CMD
        var cmdPath = FindCmd();
        if (!string.IsNullOrEmpty(cmdPath))
        {
            shells.Add(new DetectedShell
            {
                Name = "cmd",
                DisplayName = "명령 프롬프트",
                Path = cmdPath,
                Arguments = "/K chcp 65001 >nul",
                IconKind = "Console",
                ShellType = LocalSession.LocalShellType.Cmd
            });
        }

        // Git Bash
        var gitBashPath = FindGitBash();
        if (!string.IsNullOrEmpty(gitBashPath))
        {
            shells.Add(new DetectedShell
            {
                Name = "bash",
                DisplayName = "Git Bash",
                Path = gitBashPath,
                Arguments = "--login -i",
                IconKind = "Git",
                ShellType = LocalSession.LocalShellType.GitBash
            });
        }

        // WSL 배포판 감지
        var wslDistros = FindWslDistributions();
        foreach (var distro in wslDistros)
        {
            shells.Add(distro);
        }

        _cachedShells = shells;
        return shells;
    }

    /// <summary>
    /// PowerShell Core (pwsh.exe) 찾기
    /// </summary>
    private static string? FindPowerShellCore()
    {
        var possiblePaths = new[]
        {
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files\PowerShell\7-preview\pwsh.exe",
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\PowerShell\7\pwsh.exe"),
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\PowerShell\pwsh.exe")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        // PATH에서 찾기
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';'))
        {
            var pwshPath = Path.Combine(dir, "pwsh.exe");
            if (File.Exists(pwshPath))
                return pwshPath;
        }

        return null;
    }

    /// <summary>
    /// Windows PowerShell 찾기
    /// </summary>
    private static string? FindWindowsPowerShell()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");

        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// CMD 찾기
    /// </summary>
    private static string? FindCmd()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");

        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Git Bash 찾기
    /// </summary>
    private static string? FindGitBash()
    {
        var possiblePaths = new[]
        {
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Git\bin\bash.exe"),
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\Git\bin\bash.exe")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        // 레지스트리에서 찾기
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GitForWindows");
            if (key != null)
            {
                var installPath = key.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(installPath))
                {
                    var bashPath = Path.Combine(installPath, "bin", "bash.exe");
                    if (File.Exists(bashPath))
                        return bashPath;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// WSL 배포판 찾기
    /// </summary>
    private static List<DetectedShell> FindWslDistributions()
    {
        var distros = new List<DetectedShell>();

        // WSL이 설치되어 있는지 확인
        var wslPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "wsl.exe");

        if (!File.Exists(wslPath))
            return distros;

        try
        {
            // wsl --list --quiet 실행하여 설치된 배포판 목록 가져오기
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = wslPath,
                Arguments = "--list --quiet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.Unicode
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var distroName = line.Trim().Replace("\0", "");
                    if (string.IsNullOrWhiteSpace(distroName))
                        continue;

                    // 기본 배포판인지 확인
                    var isDefault = distroName.EndsWith(" (Default)") || distroName.EndsWith("(기본값)");
                    distroName = distroName.Replace(" (Default)", "").Replace("(기본값)", "").Trim();

                    distros.Add(new DetectedShell
                    {
                        Name = $"wsl-{distroName.ToLower()}",
                        DisplayName = distroName,
                        Path = wslPath,
                        Arguments = $"-d {distroName}",
                        IconKind = GetWslIcon(distroName),
                        ShellType = LocalSession.LocalShellType.WSL,
                        IsDefault = false
                    });
                }
            }
        }
        catch { }

        return distros;
    }

    /// <summary>
    /// WSL 배포판에 맞는 아이콘 반환
    /// </summary>
    private static string GetWslIcon(string distroName)
    {
        var lower = distroName.ToLower();

        if (lower.Contains("ubuntu"))
            return "Ubuntu";
        if (lower.Contains("debian"))
            return "Debian";
        if (lower.Contains("kali"))
            return "Linux";
        if (lower.Contains("opensuse") || lower.Contains("suse"))
            return "Linux";
        if (lower.Contains("alpine"))
            return "Linux";

        return "Linux";
    }

    /// <summary>
    /// 기본 쉘 반환
    /// </summary>
    public DetectedShell? GetDefaultShell()
    {
        var shells = DetectInstalledShells();
        return shells.FirstOrDefault(s => s.IsDefault) ?? shells.FirstOrDefault();
    }
}
