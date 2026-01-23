using System;
using System.Diagnostics;

namespace TermSnap.ViewModels.Managers;

/// <summary>
/// 프로세스 리소스 모니터 (CPU, 메모리)
/// </summary>
public class ResourceMonitor : IDisposable
{
    private DateTime _lastCpuTime = DateTime.MinValue;
    private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;

    /// <summary>
    /// CPU 사용률 (%)
    /// </summary>
    public double CpuUsage { get; private set; }

    /// <summary>
    /// 메모리 사용량 (MB)
    /// </summary>
    public long MemoryUsageMB { get; private set; }

    /// <summary>
    /// 리소스 사용량 업데이트 (주기적으로 호출)
    /// </summary>
    public void Update()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();

            // 메모리 사용량 (MB)
            MemoryUsageMB = currentProcess.WorkingSet64 / 1024 / 1024;

            // CPU 사용률 계산
            var currentTime = DateTime.UtcNow;
            var currentTotalProcessorTime = currentProcess.TotalProcessorTime;

            if (_lastCpuTime != DateTime.MinValue)
            {
                var timeDiff = (currentTime - _lastCpuTime).TotalMilliseconds;
                var cpuDiff = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;

                if (timeDiff > 0)
                {
                    // CPU 사용률 = (프로세스 CPU 시간 증가량 / 실제 시간 증가량) / 코어 수 * 100
                    var cpuPercentage = (cpuDiff / timeDiff / Environment.ProcessorCount) * 100;
                    CpuUsage = Math.Round(Math.Min(100, Math.Max(0, cpuPercentage)), 1);
                }
            }

            _lastCpuTime = currentTime;
            _lastTotalProcessorTime = currentTotalProcessorTime;
        }
        catch
        {
            // 리소스 정보를 가져오는 중 오류 발생 시 무시
        }
    }

    /// <summary>
    /// 리소스 초기화
    /// </summary>
    public void Reset()
    {
        _lastCpuTime = DateTime.MinValue;
        _lastTotalProcessorTime = TimeSpan.Zero;
        CpuUsage = 0;
        MemoryUsageMB = 0;
    }

    public void Dispose()
    {
        // 정리할 리소스 없음
    }
}
