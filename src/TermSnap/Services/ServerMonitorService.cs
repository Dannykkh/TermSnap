using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// 서버 모니터링 서비스
/// </summary>
public class ServerMonitorService
{
    private readonly SshService _sshService;

    public ServerMonitorService(SshService sshService)
    {
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
    }

    /// <summary>
    /// 서버 통계 수집
    /// </summary>
    public async Task<ServerStats> GetServerStatsAsync()
    {
        var stats = new ServerStats();

        try
        {
            // CPU 사용률
            var cpuResult = await _sshService.ExecuteCommandAsync("top -bn1 | grep 'Cpu(s)' | awk '{print $2}' | cut -d'%' -f1");
            if (cpuResult.IsSuccess && double.TryParse(cpuResult.Output.Trim().Replace(",", "."), out double cpuUsage))
            {
                stats.CpuUsage = cpuUsage;
            }

            // 메모리 사용률
            var memResult = await _sshService.ExecuteCommandAsync("free | grep Mem | awk '{print ($3/$2) * 100.0}'");
            if (memResult.IsSuccess && double.TryParse(memResult.Output.Trim().Replace(",", "."), out double memUsage))
            {
                stats.MemoryUsage = memUsage;
            }

            // 메모리 상세 정보
            var memDetailResult = await _sshService.ExecuteCommandAsync("free -h | grep Mem | awk '{print $2,$3,$4}'");
            if (memDetailResult.IsSuccess)
            {
                var parts = memDetailResult.Output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    stats.TotalMemory = parts[0];
                    stats.UsedMemory = parts[1];
                    stats.FreeMemory = parts[2];
                }
            }

            // 디스크 사용률 (루트 파티션)
            var diskResult = await _sshService.ExecuteCommandAsync("df -h / | tail -1 | awk '{print $5}' | sed 's/%//'");
            if (diskResult.IsSuccess && double.TryParse(diskResult.Output.Trim(), out double diskUsage))
            {
                stats.DiskUsage = diskUsage;
            }

            // 디스크 상세 정보
            var diskDetailResult = await _sshService.ExecuteCommandAsync("df -h / | tail -1 | awk '{print $2,$3,$4}'");
            if (diskDetailResult.IsSuccess)
            {
                var parts = diskDetailResult.Output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    stats.TotalDisk = parts[0];
                    stats.UsedDisk = parts[1];
                    stats.FreeDisk = parts[2];
                }
            }

            // 업타임
            var uptimeResult = await _sshService.ExecuteCommandAsync("uptime -p");
            if (uptimeResult.IsSuccess)
            {
                stats.Uptime = uptimeResult.Output.Trim();
            }

            // 프로세스 수
            var processResult = await _sshService.ExecuteCommandAsync("ps aux | wc -l");
            if (processResult.IsSuccess && int.TryParse(processResult.Output.Trim(), out int processCount))
            {
                stats.ProcessCount = processCount - 1; // 헤더 제외
            }

            // OS 정보
            var osResult = await _sshService.ExecuteCommandAsync("cat /etc/os-release | grep PRETTY_NAME | cut -d'\"' -f2");
            if (osResult.IsSuccess)
            {
                stats.OsInfo = osResult.Output.Trim();
            }

            // 커널 버전
            var kernelResult = await _sshService.ExecuteCommandAsync("uname -r");
            if (kernelResult.IsSuccess)
            {
                stats.KernelVersion = kernelResult.Output.Trim();
            }

            // 네트워크 사용량 (수신/송신)
            var netResult = await _sshService.ExecuteCommandAsync("cat /proc/net/dev | grep -E '(eth0|ens|enp)' | head -1 | awk '{print $2,$10}'");
            if (netResult.IsSuccess)
            {
                var parts = netResult.Output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[0], out long rx) && long.TryParse(parts[1], out long tx))
                {
                    stats.NetworkRxBytes = rx;
                    stats.NetworkTxBytes = tx;
                }
            }

            // 로드 평균
            var loadResult = await _sshService.ExecuteCommandAsync("cat /proc/loadavg | awk '{print $1,$2,$3}'");
            if (loadResult.IsSuccess)
            {
                stats.LoadAverage = loadResult.Output.Trim();
            }

            stats.LastUpdated = DateTime.Now;
            stats.IsSuccess = true;
        }
        catch (Exception ex)
        {
            stats.IsSuccess = false;
            stats.ErrorMessage = ex.Message;
        }

        return stats;
    }

    /// <summary>
    /// 서비스 상태 확인
    /// </summary>
    public async Task<ServiceStatus> GetServiceStatusAsync(string serviceName)
    {
        var status = new ServiceStatus { ServiceName = serviceName };

        try
        {
            var result = await _sshService.ExecuteCommandAsync($"systemctl is-active {serviceName}");
            status.IsRunning = result.IsSuccess && result.Output.Trim() == "active";

            if (status.IsRunning)
            {
                var statusResult = await _sshService.ExecuteCommandAsync($"systemctl status {serviceName} | grep 'Active:' | awk '{{print $2,$3}}'");
                if (statusResult.IsSuccess)
                {
                    status.StatusText = statusResult.Output.Trim();
                }
            }
            else
            {
                status.StatusText = "inactive";
            }
        }
        catch
        {
            status.IsRunning = false;
            status.StatusText = "unknown";
        }

        return status;
    }

    /// <summary>
    /// 상위 프로세스 목록
    /// </summary>
    public async Task<string> GetTopProcessesAsync(int count = 10)
    {
        try
        {
            var result = await _sshService.ExecuteCommandAsync($"ps aux --sort=-%cpu | head -n {count + 1}");
            if (result.IsSuccess)
            {
                return result.Output;
            }
        }
        catch
        {
            // 무시
        }

        return "프로세스 정보를 가져올 수 없습니다.";
    }
}

/// <summary>
/// 서버 통계
/// </summary>
public class ServerStats
{
    public double CpuUsage { get; set; } // %
    public double MemoryUsage { get; set; } // %
    public double DiskUsage { get; set; } // %

    public string TotalMemory { get; set; } = "";
    public string UsedMemory { get; set; } = "";
    public string FreeMemory { get; set; } = "";

    public string TotalDisk { get; set; } = "";
    public string UsedDisk { get; set; } = "";
    public string FreeDisk { get; set; } = "";

    public string Uptime { get; set; } = "";
    public int ProcessCount { get; set; }
    public string OsInfo { get; set; } = "";
    public string KernelVersion { get; set; } = "";

    public long NetworkRxBytes { get; set; }
    public long NetworkTxBytes { get; set; }

    public string NetworkRx => FormatBytes(NetworkRxBytes);
    public string NetworkTx => FormatBytes(NetworkTxBytes);

    public string LoadAverage { get; set; } = "";

    public DateTime LastUpdated { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorMessage { get; set; } = "";

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F2} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

/// <summary>
/// 서비스 상태
/// </summary>
public class ServiceStatus
{
    public string ServiceName { get; set; } = "";
    public bool IsRunning { get; set; }
    public string StatusText { get; set; } = "";
    public string StatusIcon => IsRunning ? "✓" : "✗";
    public string StatusColor => IsRunning ? "Green" : "Red";
}
