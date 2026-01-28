using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace TermSnap.Models;

/// <summary>
/// 서브 프로세스 상태
/// </summary>
public enum SubProcessStatus
{
    Running,
    Stopped,
    Error
}

/// <summary>
/// 서브 프로세스 정보
/// </summary>
public class SubProcessInfo : INotifyPropertyChanged
{
    private SubProcessStatus _status = SubProcessStatus.Running;
    private string _output = string.Empty;
    private long _memoryUsage = 0;
    private double _cpuUsage = 0;

    /// <summary>
    /// 프로세스 ID
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// 프로세스 이름
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// 실행 명령어
    /// </summary>
    public string CommandLine { get; set; } = string.Empty;

    /// <summary>
    /// 작업 디렉토리
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 시작 시간
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 종료 시간 (종료된 경우)
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 사용 중인 포트 목록
    /// </summary>
    public List<int> Ports { get; set; } = new();

    /// <summary>
    /// 프로세스 상태
    /// </summary>
    public SubProcessStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(StatusText)); }
    }

    /// <summary>
    /// 실행 중 여부
    /// </summary>
    public bool IsRunning => Status == SubProcessStatus.Running;

    /// <summary>
    /// 상태 텍스트
    /// </summary>
    public string StatusText => Status switch
    {
        SubProcessStatus.Running => "실행 중",
        SubProcessStatus.Stopped => "종료됨",
        SubProcessStatus.Error => "오류",
        _ => "알 수 없음"
    };

    /// <summary>
    /// 출력 로그 (최근 출력)
    /// </summary>
    public string Output
    {
        get => _output;
        set { _output = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 출력 로그 버퍼 (전체)
    /// </summary>
    public StringBuilder OutputBuffer { get; } = new StringBuilder();

    /// <summary>
    /// 메모리 사용량 (bytes)
    /// </summary>
    public long MemoryUsage
    {
        get => _memoryUsage;
        set { _memoryUsage = value; OnPropertyChanged(); OnPropertyChanged(nameof(MemoryUsageFormatted)); }
    }

    /// <summary>
    /// 메모리 사용량 (포맷됨)
    /// </summary>
    public string MemoryUsageFormatted => FormatBytes(MemoryUsage);

    /// <summary>
    /// CPU 사용률 (%)
    /// </summary>
    public double CpuUsage
    {
        get => _cpuUsage;
        set { _cpuUsage = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 실행 시간
    /// </summary>
    public TimeSpan RunningTime => (EndTime ?? DateTime.Now) - StartTime;

    /// <summary>
    /// 실행 시간 (포맷됨)
    /// </summary>
    public string RunningTimeFormatted
    {
        get
        {
            var time = RunningTime;
            if (time.TotalHours >= 1)
                return $"{(int)time.TotalHours}h {time.Minutes}m";
            if (time.TotalMinutes >= 1)
                return $"{time.Minutes}m {time.Seconds}s";
            return $"{time.Seconds}s";
        }
    }

    /// <summary>
    /// 포트 표시 문자열
    /// </summary>
    public string PortsDisplay => Ports.Count > 0 ? string.Join(", ", Ports) : "-";

    /// <summary>
    /// 부모 프로세스 ID (셸 프로세스)
    /// </summary>
    public int ParentProcessId { get; set; }

    /// <summary>
    /// 출력에 로그 추가
    /// </summary>
    public void AppendOutput(string text)
    {
        OutputBuffer.Append(text);

        // 버퍼 크기 제한 (1MB)
        if (OutputBuffer.Length > 1024 * 1024)
        {
            OutputBuffer.Remove(0, OutputBuffer.Length - 512 * 1024);
        }

        // 최근 출력 업데이트 (마지막 500자)
        var bufferStr = OutputBuffer.ToString();
        Output = bufferStr.Length > 500 ? bufferStr[^500..] : bufferStr;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.#} {sizes[order]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
