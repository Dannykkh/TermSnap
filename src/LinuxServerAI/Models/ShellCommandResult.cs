using System;

namespace Nebula.Models;

/// <summary>
/// ShellStream 명령어 실행 결과
/// </summary>
public class ShellCommandResult
{
    /// <summary>
    /// 실행한 명령어
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// 명령어 출력
    /// </summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// 오류 메시지
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// 현재 작업 디렉토리
    /// </summary>
    public string CurrentDirectory { get; set; } = "~";

    /// <summary>
    /// 실행 시간
    /// </summary>
    public DateTime ExecutedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 실행 소요 시간
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 타임아웃 여부
    /// </summary>
    public bool IsTimeout { get; set; }
}

/// <summary>
/// ShellStream 출력 이벤트 인자
/// </summary>
public class ShellOutputEventArgs : EventArgs
{
    public string Data { get; }
    public bool IsError { get; }
    public DateTime Timestamp { get; }

    public ShellOutputEventArgs(string data, bool isError = false)
    {
        Data = data;
        IsError = isError;
        Timestamp = DateTime.Now;
    }
}
