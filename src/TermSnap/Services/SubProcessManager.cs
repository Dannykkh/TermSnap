using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// 서브 프로세스 관리자 - 셸에서 실행된 자식 프로세스 추적 및 관리
/// WMI 대신 P/Invoke 사용 (성능 최적화)
/// </summary>
public class SubProcessManager : IDisposable
{
    private readonly int _parentProcessId;
    private readonly HashSet<int> _trackedProcessIds = new();
    private readonly object _lock = new();
    private bool _disposed = false;
    private bool _isRunning = false;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;

    // P/Invoke for getting parent process ID (WMI 대체)
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId; // Parent PID
    }

    /// <summary>
    /// 관리 중인 서브 프로세스 목록
    /// </summary>
    public ObservableCollection<SubProcessInfo> Processes { get; } = new();

    /// <summary>
    /// 새 프로세스 감지 이벤트
    /// </summary>
    public event EventHandler<SubProcessInfo>? ProcessStarted;

    /// <summary>
    /// 프로세스 종료 이벤트
    /// </summary>
    public event EventHandler<SubProcessInfo>? ProcessStopped;

    /// <summary>
    /// 서브 프로세스 관리자 생성
    /// </summary>
    /// <param name="parentProcessId">부모 셸 프로세스 ID</param>
    public SubProcessManager(int parentProcessId)
    {
        _parentProcessId = parentProcessId;
    }

    /// <summary>
    /// 모니터링 시작
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;

        _isRunning = true;
        _cts = new CancellationTokenSource();

        // 완전 백그라운드 스레드에서 실행
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token), _cts.Token);

        Debug.WriteLine($"[SubProcessManager] 모니터링 시작, 부모 PID: {_parentProcessId}");
    }

    /// <summary>
    /// 모니터링 중지
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cts?.Cancel();

        try
        {
            _monitorTask?.Wait(1000);
        }
        catch { }

        _cts?.Dispose();
        _cts = null;

        Debug.WriteLine("[SubProcessManager] 모니터링 중지");
    }

    /// <summary>
    /// 백그라운드 모니터링 루프
    /// </summary>
    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        // 초기 지연 (탭 초기화 완료 대기)
        await Task.Delay(2000, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshProcessesInternalAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SubProcessManager] Monitor error: {ex.Message}");
            }

            // 5초 간격으로 체크
            await Task.Delay(5000, cancellationToken);
        }
    }

    /// <summary>
    /// 프로세스 목록 새로고침 (외부 호출용)
    /// </summary>
    public async Task RefreshProcessesAsync()
    {
        await Task.Run(() => RefreshProcessesInternalAsync());
    }

    /// <summary>
    /// 프로세스 목록 새로고침 (내부)
    /// </summary>
    private Task RefreshProcessesInternalAsync()
    {
        try
        {
            var childProcessIds = GetChildProcessIdsFast(_parentProcessId);
            var currentIds = new HashSet<int>(childProcessIds);

            lock (_lock)
            {
                // 새로 시작된 프로세스 감지
                foreach (var pid in currentIds.Except(_trackedProcessIds))
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        var info = CreateProcessInfoFast(proc);
                        if (info != null && !IsIgnoredProcess(info.ProcessName))
                        {
                            InvokeOnUI(() =>
                            {
                                Processes.Add(info);
                                ProcessStarted?.Invoke(this, info);
                            });
                        }
                    }
                    catch { }
                }

                // 종료된 프로세스 감지
                foreach (var pid in _trackedProcessIds.Except(currentIds))
                {
                    InvokeOnUI(() =>
                    {
                        var info = Processes.FirstOrDefault(p => p.ProcessId == pid);
                        if (info != null)
                        {
                            info.Status = SubProcessStatus.Stopped;
                            info.EndTime = DateTime.Now;
                            ProcessStopped?.Invoke(this, info);
                        }
                    });
                }

                _trackedProcessIds.Clear();
                foreach (var id in currentIds)
                    _trackedProcessIds.Add(id);
            }

            // 실행 중인 프로세스 정보 업데이트
            UpdateRunningProcesses();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SubProcessManager] Refresh error: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// UI 스레드에서 실행 (BeginInvoke로 비동기)
    /// </summary>
    private static void InvokeOnUI(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app != null)
        {
            app.Dispatcher.BeginInvoke(action);
        }
    }

    /// <summary>
    /// 무시할 프로세스 (시스템 프로세스)
    /// </summary>
    private static bool IsIgnoredProcess(string processName)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "conhost", "OpenConsole", "wsl", "wslhost",
            "WindowsTerminal", "cmd", "powershell", "pwsh"
        };
        return ignored.Contains(processName);
    }

    /// <summary>
    /// P/Invoke로 부모 프로세스 ID 조회 (WMI보다 훨씬 빠름)
    /// </summary>
    private static int GetParentProcessId(Process proc)
    {
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            int status = NtQueryInformationProcess(
                proc.Handle,
                0, // ProcessBasicInformation
                ref pbi,
                Marshal.SizeOf(pbi),
                out _);

            if (status == 0)
            {
                return pbi.InheritedFromUniqueProcessId.ToInt32();
            }
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// 부모 프로세스의 모든 자식 프로세스 ID 조회 (P/Invoke 사용)
    /// </summary>
    private List<int> GetChildProcessIdsFast(int parentId)
    {
        var result = new List<int>();
        var allProcesses = Process.GetProcesses();

        try
        {
            foreach (var proc in allProcesses)
            {
                try
                {
                    var procParentId = GetParentProcessId(proc);
                    if (procParentId == parentId)
                    {
                        result.Add(proc.Id);
                        // 재귀적으로 손자 프로세스도 추적
                        result.AddRange(GetChildProcessIdsFast(proc.Id));
                    }
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SubProcessManager] GetChildProcessIdsFast error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 프로세스 정보 생성 (WMI 없이)
    /// </summary>
    private SubProcessInfo? CreateProcessInfoFast(Process proc)
    {
        try
        {
            var info = new SubProcessInfo
            {
                ProcessId = proc.Id,
                ProcessName = proc.ProcessName,
                ParentProcessId = _parentProcessId,
                Status = SubProcessStatus.Running
            };

            // StartTime (권한 문제 시 현재 시간 사용)
            try
            {
                info.StartTime = proc.StartTime;
            }
            catch
            {
                info.StartTime = DateTime.Now;
            }

            // 명령어 라인 (MainModule에서 가져옴, WMI 대체)
            try
            {
                info.CommandLine = proc.MainModule?.FileName ?? proc.ProcessName;
            }
            catch
            {
                info.CommandLine = proc.ProcessName;
            }

            // 메모리 사용량
            try
            {
                info.MemoryUsage = proc.WorkingSet64;
            }
            catch { }

            return info;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 실행 중인 프로세스 정보 업데이트
    /// </summary>
    private void UpdateRunningProcesses()
    {
        List<SubProcessInfo> runningProcesses;
        lock (_lock)
        {
            runningProcesses = Processes.Where(p => p.IsRunning).ToList();
        }

        foreach (var info in runningProcesses)
        {
            try
            {
                var proc = Process.GetProcessById(info.ProcessId);
                var memoryUsage = proc.WorkingSet64;
                proc.Dispose();

                InvokeOnUI(() =>
                {
                    info.MemoryUsage = memoryUsage;
                });
            }
            catch
            {
                // 프로세스가 종료됨
                InvokeOnUI(() =>
                {
                    info.Status = SubProcessStatus.Stopped;
                    info.EndTime = DateTime.Now;
                });
            }
        }
    }

    /// <summary>
    /// 프로세스 종료
    /// </summary>
    public async Task<bool> KillProcessAsync(int processId)
    {
        return await Task.Run(() =>
        {
            try
            {
                var proc = Process.GetProcessById(processId);
                proc.Kill(entireProcessTree: true);
                proc.Dispose();

                InvokeOnUI(() =>
                {
                    var info = Processes.FirstOrDefault(p => p.ProcessId == processId);
                    if (info != null)
                    {
                        info.Status = SubProcessStatus.Stopped;
                        info.EndTime = DateTime.Now;
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SubProcessManager] Kill process error: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// 종료된 프로세스 목록에서 제거
    /// </summary>
    public void RemoveStoppedProcess(SubProcessInfo info)
    {
        if (!info.IsRunning)
        {
            Processes.Remove(info);
        }
    }

    /// <summary>
    /// 모든 서브 프로세스 종료
    /// </summary>
    public async Task KillAllAsync()
    {
        var tasks = Processes
            .Where(p => p.IsRunning)
            .Select(p => KillProcessAsync(p.ProcessId));

        await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _disposed = true;
    }
}
