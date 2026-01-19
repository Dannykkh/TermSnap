using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TermSnap.Models;
using TermSnap.Services;
using TermSnap.ViewModels;

namespace TermSnap.Mcp;

/// <summary>
/// MCP에서 생성된 SSH 세션을 관리
/// </summary>
public class McpSessionManager
{
    private readonly ConcurrentDictionary<string, McpManagedSession> _sessions = new();

    public static McpSessionManager Instance { get; } = new();

    /// <summary>
    /// MCP 관리 세션 정보
    /// </summary>
    private class McpManagedSession
    {
        public string SessionId { get; set; } = string.Empty;
        public ServerSessionViewModel ViewModel { get; set; } = null!;
        public DateTime ConnectedAt { get; set; }
    }

    /// <summary>
    /// 저장된 서버 프로필 목록 반환
    /// </summary>
    public List<ServerProfileInfo> GetServerProfiles()
    {
        var config = ConfigService.Load();
        return config.ServerProfiles.Select(p => new ServerProfileInfo
        {
            ProfileName = p.ProfileName,
            Host = p.Host,
            Port = p.Port,
            Username = p.Username,
            LastConnected = p.LastConnected,
            Memo = p.Notes
        }).ToList();
    }

    /// <summary>
    /// 세션 생성 결과
    /// </summary>
    public class CreateSessionResult
    {
        public bool Success { get; set; }
        public string? SessionId { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// 새 SSH 세션 생성 (UI 스레드에서 호출됨)
    /// </summary>
    public async Task<CreateSessionResult> CreateSessionAsync(MainViewModel mainViewModel, string profileName)
    {
        try
        {
            var config = ConfigService.Load();
            var profile = config.ServerProfiles.FirstOrDefault(p =>
                p.ProfileName.Equals(profileName, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                return new CreateSessionResult
                {
                    Success = false,
                    Message = $"Profile not found: {profileName}"
                };
            }

            // 세션 ID 생성
            var sessionId = $"mcp-{Guid.NewGuid():N}".Substring(0, 20);

            // 새 SSH 세션 뷰모델 생성
            var sessionVm = new ServerSessionViewModel(config)
            {
                // MCP 제어 세션은 AI 추천 기본 OFF
                UseAISuggestion = false
            };

            // 탭에 추가
            Application.Current.Dispatcher.Invoke(() =>
            {
                mainViewModel.Sessions.Add(sessionVm);
                mainViewModel.SelectedSession = sessionVm;
            });

            // SSH 연결
            await sessionVm.ConnectAsync(profile);

            // 관리 목록에 추가
            var managedSession = new McpManagedSession
            {
                SessionId = sessionId,
                ViewModel = sessionVm,
                ConnectedAt = DateTime.Now
            };
            _sessions[sessionId] = managedSession;

            // 탭 헤더에 MCP 표시
            Application.Current.Dispatcher.Invoke(() =>
            {
                sessionVm.TabHeader = $"[MCP] {profile.ProfileName}";
            });

            Debug.WriteLine($"[MCP] 세션 생성됨: {sessionId} -> {profileName}");

            return new CreateSessionResult
            {
                Success = true,
                SessionId = sessionId,
                Message = $"Connected to {profileName}"
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MCP] 세션 생성 실패: {ex.Message}");
            return new CreateSessionResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    /// <summary>
    /// 세션 연결 해제
    /// </summary>
    public bool DisconnectSession(MainViewModel mainViewModel, string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
            return false;

        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                session.ViewModel.Dispose();
                mainViewModel.Sessions.Remove(session.ViewModel);
            });

            Debug.WriteLine($"[MCP] 세션 연결 해제: {sessionId}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MCP] 세션 연결 해제 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 세션 상태 조회
    /// </summary>
    public SessionStatusInfo? GetSessionStatus(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;

        return new SessionStatusInfo
        {
            SessionId = sessionId,
            ProfileName = session.ViewModel.ServerProfile?.ProfileName ?? "",
            IsConnected = session.ViewModel.IsConnected,
            CurrentDirectory = session.ViewModel.CurrentDirectory,
            ConnectedAt = session.ConnectedAt,
            IsMcpControlled = true
        };
    }

    /// <summary>
    /// 모든 MCP 관리 세션 목록
    /// </summary>
    public List<SessionStatusInfo> GetAllSessions()
    {
        return _sessions.Values.Select(s => new SessionStatusInfo
        {
            SessionId = s.SessionId,
            ProfileName = s.ViewModel.ServerProfile?.ProfileName ?? "",
            IsConnected = s.ViewModel.IsConnected,
            CurrentDirectory = s.ViewModel.CurrentDirectory,
            ConnectedAt = s.ConnectedAt,
            IsMcpControlled = true
        }).ToList();
    }

    /// <summary>
    /// 명령어 실행
    /// </summary>
    public async Task<CommandExecuteResult?> ExecuteCommandAsync(string sessionId, string command)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;

        if (!session.ViewModel.IsConnected || session.ViewModel.SshService == null)
            return null;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // UI에 명령어 표시
            Application.Current.Dispatcher.Invoke(() =>
            {
                session.ViewModel.AddMessage($"[MCP] {command}", MessageType.Command);
            });

            // 명령어 실행
            var result = await session.ViewModel.SshService.ExecuteCommandAsync(command);

            stopwatch.Stop();

            // UI에 결과 표시
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (result.IsSuccess)
                {
                    session.ViewModel.AddMessage($"✓ 성공", MessageType.Success);
                    if (!string.IsNullOrWhiteSpace(result.Output))
                        session.ViewModel.AddMessage(result.Output, MessageType.Normal);
                }
                else
                {
                    session.ViewModel.AddMessage($"✗ 실패", MessageType.Error);
                    if (!string.IsNullOrWhiteSpace(result.Error))
                        session.ViewModel.AddMessage($"오류: {result.Error}", MessageType.Error);
                }
            });

            return new CommandExecuteResult
            {
                Command = command,
                IsSuccess = result.IsSuccess,
                Output = result.Output ?? "",
                Error = result.Error,
                CurrentDirectory = result.CurrentDirectory ?? "~",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            Application.Current.Dispatcher.Invoke(() =>
            {
                session.ViewModel.AddMessage($"오류: {ex.Message}", MessageType.Error);
            });

            return new CommandExecuteResult
            {
                Command = command,
                IsSuccess = false,
                Error = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// 서버 통계 조회
    /// </summary>
    public async Task<ServerStatsInfo?> GetServerStatsAsync(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;

        if (!session.ViewModel.IsConnected || session.ViewModel.SshService == null)
            return null;

        try
        {
            var ssh = session.ViewModel.SshService;

            // CPU 사용량
            var cpuResult = await ssh.ExecuteCommandAsync("top -bn1 | grep 'Cpu(s)' | awk '{print $2}' | cut -d'%' -f1");
            double.TryParse(cpuResult.Output?.Trim(), out var cpuUsage);

            // 메모리 사용량
            var memResult = await ssh.ExecuteCommandAsync("free -m | grep Mem | awk '{print $2,$3}'");
            var memParts = memResult.Output?.Trim().Split(' ') ?? new string[0];
            double.TryParse(memParts.ElementAtOrDefault(0), out var memTotal);
            double.TryParse(memParts.ElementAtOrDefault(1), out var memUsed);

            // 디스크 사용량
            var diskResult = await ssh.ExecuteCommandAsync("df -h --output=target,fstype,size,used,avail,pcent | tail -n +2");
            var diskLines = diskResult.Output?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
            var disks = new List<DiskUsageInfo>();

            foreach (var line in diskLines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 6)
                {
                    disks.Add(new DiskUsageInfo
                    {
                        MountPoint = parts[0],
                        FileSystem = parts[1],
                        TotalGB = ParseSize(parts[2]),
                        UsedGB = ParseSize(parts[3]),
                        AvailableGB = ParseSize(parts[4]),
                        UsagePercent = double.TryParse(parts[5].TrimEnd('%'), out var pct) ? pct : 0
                    });
                }
            }

            // Uptime
            var uptimeResult = await ssh.ExecuteCommandAsync("cat /proc/uptime | awk '{print $1}'");
            double.TryParse(uptimeResult.Output?.Trim(), out var uptimeSeconds);

            return new ServerStatsInfo
            {
                CpuUsage = cpuUsage,
                MemoryTotal = memTotal,
                MemoryUsed = memUsed,
                MemoryUsage = memTotal > 0 ? (memUsed / memTotal) * 100 : 0,
                Disks = disks,
                Uptime = TimeSpan.FromSeconds(uptimeSeconds)
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MCP] 서버 통계 조회 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// SFTP 파일 목록 조회
    /// </summary>
    public async Task<List<SftpFileInfo>?> SftpListAsync(string sessionId, string path)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;

        if (session.ViewModel.ServerProfile == null)
            return null;

        try
        {
            using var sftpService = new SftpService(session.ViewModel.ServerProfile);
            await sftpService.ConnectAsync();

            var items = await sftpService.ListDirectoryAsync(path);
            return items.Select(item => new SftpFileInfo
            {
                Name = item.Name,
                FullPath = item.FullPath,
                IsDirectory = item.IsDirectory,
                Size = item.Size,
                LastModified = item.LastModified,
                Permissions = item.Permissions ?? ""
            }).ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MCP] SFTP 목록 조회 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// SFTP 파일 다운로드
    /// </summary>
    public async Task<bool> SftpDownloadAsync(string sessionId, string remotePath, string localPath)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return false;

        if (session.ViewModel.ServerProfile == null)
            return false;

        try
        {
            using var sftpService = new SftpService(session.ViewModel.ServerProfile);
            await sftpService.ConnectAsync();
            await sftpService.DownloadFileAsync(remotePath, localPath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MCP] SFTP 다운로드 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// SFTP 파일 업로드
    /// </summary>
    public async Task<bool> SftpUploadAsync(string sessionId, string localPath, string remotePath)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return false;

        if (session.ViewModel.ServerProfile == null)
            return false;

        try
        {
            using var sftpService = new SftpService(session.ViewModel.ServerProfile);
            await sftpService.ConnectAsync();
            await sftpService.UploadFileAsync(localPath, remotePath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MCP] SFTP 업로드 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 사이즈 문자열 파싱 (예: "10G", "500M")
    /// </summary>
    private static double ParseSize(string sizeStr)
    {
        if (string.IsNullOrEmpty(sizeStr))
            return 0;

        sizeStr = sizeStr.Trim().ToUpperInvariant();

        if (sizeStr.EndsWith("G"))
        {
            if (double.TryParse(sizeStr[..^1], out var gb))
                return gb;
        }
        else if (sizeStr.EndsWith("M"))
        {
            if (double.TryParse(sizeStr[..^1], out var mb))
                return mb / 1024;
        }
        else if (sizeStr.EndsWith("K"))
        {
            if (double.TryParse(sizeStr[..^1], out var kb))
                return kb / 1024 / 1024;
        }
        else if (sizeStr.EndsWith("T"))
        {
            if (double.TryParse(sizeStr[..^1], out var tb))
                return tb * 1024;
        }

        return 0;
    }
}
