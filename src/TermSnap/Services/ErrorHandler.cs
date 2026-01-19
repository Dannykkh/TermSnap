using System;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// 오류 처리 및 자동 재시도 서비스
/// </summary>
public class ErrorHandler
{
    private readonly IAIProvider _aiProvider;
    private readonly SshService _sshService;
    private readonly int _maxRetryAttempts;

    public ErrorHandler(IAIProvider aiProvider, SshService sshService, int maxRetryAttempts = 3)
    {
        _aiProvider = aiProvider ?? throw new ArgumentNullException(nameof(aiProvider));
        _sshService = sshService ?? throw new ArgumentNullException(nameof(sshService));
        _maxRetryAttempts = maxRetryAttempts;
    }

    /// <summary>
    /// 명령어 실행 및 자동 재시도
    /// </summary>
    public async Task<(bool success, CommandResult result, string[] attempts)> ExecuteWithRetry(
        string initialCommand,
        Action<string>? onAttempt = null)
    {
        var attempts = new System.Collections.Generic.List<string>();
        var currentCommand = initialCommand;
        CommandResult? lastResult = null;

        for (int i = 0; i < _maxRetryAttempts; i++)
        {
            attempts.Add(currentCommand);
            onAttempt?.Invoke($"시도 {i + 1}/{_maxRetryAttempts}: {currentCommand}");

            var result = await _sshService.ExecuteCommandAsync(currentCommand);
            lastResult = result;

            if (result.IsSuccess)
            {
                return (true, result, attempts.ToArray());
            }

            // 마지막 시도인 경우 재시도하지 않음
            if (i == _maxRetryAttempts - 1)
            {
                break;
            }

            // AI에게 오류 분석 및 수정 요청
            onAttempt?.Invoke($"오류 발생, AI에게 수정 방법 요청 중...");

            try
            {
                var fixedCommand = await _aiProvider.AnalyzeErrorAndSuggestFix(
                    currentCommand,
                    result.Error,
                    result.Output
                );

                // AI가 오류를 수정할 수 없다고 판단한 경우
                if (fixedCommand.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                {
                    onAttempt?.Invoke($"AI 분석: {fixedCommand}");
                    break;
                }

                currentCommand = fixedCommand;
            }
            catch (Exception ex)
            {
                onAttempt?.Invoke($"AI 분석 실패: {ex.Message}");
                break;
            }
        }

        return (false, lastResult!, attempts.ToArray());
    }

    /// <summary>
    /// 위험한 명령어 검사
    /// </summary>
    public static bool IsDangerousCommand(string command)
    {
        var dangerousPatterns = new[]
        {
            "rm -rf /",
            "rm -rf /*",
            "dd if=",
            "mkfs",
            "> /dev/sda",
            "mv /* /dev/null",
            "chmod -R 777 /",
            "chown -R",
            ":(){ :|:& };:",  // Fork bomb
        };

        foreach (var pattern in dangerousPatterns)
        {
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 명령어 실행 전 확인이 필요한지 검사
    /// </summary>
    public static bool RequiresConfirmation(string command)
    {
        var confirmationPatterns = new[]
        {
            "rm ",
            "sudo ",
            "systemctl stop",
            "systemctl restart",
            "reboot",
            "shutdown",
            "kill ",
            "pkill ",
            "apt-get remove",
            "yum remove",
            "dnf remove",
        };

        foreach (var pattern in confirmationPatterns)
        {
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
