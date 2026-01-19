using System;

namespace Nebula.Models;

/// <summary>
/// 명령어 실행 결과
/// </summary>
public class CommandResult
{
    public string Command { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public bool IsSuccess => ExitCode == 0 && string.IsNullOrWhiteSpace(Error);
    public DateTime ExecutedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public string CurrentDirectory { get; set; } = "~";

    public CommandResult()
    {
        ExecutedAt = DateTime.Now;
    }

    public override string ToString()
    {
        if (IsSuccess)
            return $"✓ 성공: {Command}\n{Output}";
        else
            return $"✗ 실패 (코드 {ExitCode}): {Command}\n{Error}";
    }
}
