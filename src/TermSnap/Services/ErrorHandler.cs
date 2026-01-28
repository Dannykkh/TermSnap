using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// 오류 분석 결과 (구조화)
/// </summary>
public class ErrorAnalysisResult
{
    /// <summary>
    /// 수정된 명령어
    /// </summary>
    public string? FixedCommand { get; set; }

    /// <summary>
    /// 오류 원인 분석
    /// </summary>
    public string? ErrorCause { get; set; }

    /// <summary>
    /// 해결 방법 설명
    /// </summary>
    public string? Solution { get; set; }

    /// <summary>
    /// 수정 가능 여부
    /// </summary>
    public bool IsFixable { get; set; }

    /// <summary>
    /// 추가 조치가 필요한지 (예: 패키지 설치, 권한 변경)
    /// </summary>
    public string? RequiresAction { get; set; }

    /// <summary>
    /// 분석 성공 여부
    /// </summary>
    public bool IsAnalysisSuccessful { get; set; }

    /// <summary>
    /// 분석 실패 시 에러 메시지
    /// </summary>
    public string? AnalysisError { get; set; }
}

/// <summary>
/// 명령어 실행 시도 기록
/// </summary>
public class ExecutionAttempt
{
    /// <summary>
    /// 시도 번호 (1부터 시작)
    /// </summary>
    public int AttemptNumber { get; set; }

    /// <summary>
    /// 실행한 명령어
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// 실행 결과
    /// </summary>
    public CommandResult? Result { get; set; }

    /// <summary>
    /// 오류 분석 결과 (실패 시)
    /// </summary>
    public ErrorAnalysisResult? Analysis { get; set; }

    /// <summary>
    /// 실행 성공 여부
    /// </summary>
    public bool IsSuccess => Result?.IsSuccess ?? false;
}

/// <summary>
/// 재시도 결과
/// </summary>
public class RetryExecutionResult
{
    /// <summary>
    /// 최종 성공 여부
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 최종 명령어 실행 결과
    /// </summary>
    public CommandResult? FinalResult { get; set; }

    /// <summary>
    /// 모든 시도 기록
    /// </summary>
    public List<ExecutionAttempt> Attempts { get; set; } = new();

    /// <summary>
    /// 총 시도 횟수
    /// </summary>
    public int TotalAttempts => Attempts.Count;

    /// <summary>
    /// 마지막 오류 분석 결과
    /// </summary>
    public ErrorAnalysisResult? LastAnalysis =>
        Attempts.Count > 0 ? Attempts[^1].Analysis : null;
}

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
    /// 명령어 실행 및 자동 재시도 (기존 호환성 유지)
    /// </summary>
    public async Task<(bool success, CommandResult result, string[] attempts)> ExecuteWithRetry(
        string initialCommand,
        Action<string>? onAttempt = null)
    {
        var result = await ExecuteWithRetryAsync(initialCommand, onAttempt);

        var attemptCommands = new List<string>();
        foreach (var attempt in result.Attempts)
        {
            attemptCommands.Add(attempt.Command);
        }

        return (result.Success, result.FinalResult!, attemptCommands.ToArray());
    }

    /// <summary>
    /// 명령어 실행 및 자동 재시도 (구조화된 결과 반환)
    /// </summary>
    public async Task<RetryExecutionResult> ExecuteWithRetryAsync(
        string initialCommand,
        Action<string>? onAttempt = null)
    {
        var result = new RetryExecutionResult();
        var currentCommand = initialCommand;

        for (int i = 0; i < _maxRetryAttempts; i++)
        {
            var attempt = new ExecutionAttempt
            {
                AttemptNumber = i + 1,
                Command = currentCommand
            };

            onAttempt?.Invoke($"시도 {i + 1}/{_maxRetryAttempts}: {currentCommand}");

            var cmdResult = await _sshService.ExecuteCommandAsync(currentCommand);
            attempt.Result = cmdResult;
            result.Attempts.Add(attempt);
            result.FinalResult = cmdResult;

            if (cmdResult.IsSuccess)
            {
                result.Success = true;
                return result;
            }

            // 마지막 시도인 경우 재시도하지 않음
            if (i == _maxRetryAttempts - 1)
            {
                break;
            }

            // AI에게 오류 분석 요청 (JSON 구조화)
            onAttempt?.Invoke($"오류 발생, AI에게 분석 요청 중...");

            var analysis = await AnalyzeErrorAsync(currentCommand, cmdResult.Error, cmdResult.Output);
            attempt.Analysis = analysis;

            if (!analysis.IsAnalysisSuccessful)
            {
                onAttempt?.Invoke($"AI 분석 실패: {analysis.AnalysisError}");
                break;
            }

            if (!analysis.IsFixable)
            {
                onAttempt?.Invoke($"수정 불가: {analysis.ErrorCause}");
                if (!string.IsNullOrEmpty(analysis.RequiresAction))
                {
                    onAttempt?.Invoke($"필요한 조치: {analysis.RequiresAction}");
                }
                break;
            }

            if (string.IsNullOrWhiteSpace(analysis.FixedCommand))
            {
                onAttempt?.Invoke($"AI가 수정된 명령어를 제공하지 않았습니다.");
                break;
            }

            // 오류 분석 정보 표시
            if (!string.IsNullOrEmpty(analysis.ErrorCause))
            {
                onAttempt?.Invoke($"오류 원인: {analysis.ErrorCause}");
            }
            if (!string.IsNullOrEmpty(analysis.Solution))
            {
                onAttempt?.Invoke($"해결 방법: {analysis.Solution}");
            }

            currentCommand = analysis.FixedCommand;
            onAttempt?.Invoke($"수정된 명령어로 재시도: {currentCommand}");
        }

        result.Success = false;
        return result;
    }

    /// <summary>
    /// 오류 분석 (JSON 구조화 응답 사용)
    /// </summary>
    public async Task<ErrorAnalysisResult> AnalyzeErrorAsync(
        string command,
        string errorMessage,
        string? context = null)
    {
        try
        {
            var aiResponse = await _aiProvider.AnalyzeErrorAsync(command, errorMessage, context ?? "");

            return new ErrorAnalysisResult
            {
                FixedCommand = aiResponse.FixedCommand,
                ErrorCause = aiResponse.ErrorCause,
                Solution = aiResponse.Solution,
                IsFixable = aiResponse.IsFixable,
                RequiresAction = aiResponse.RequiresAction,
                IsAnalysisSuccessful = true
            };
        }
        catch (Exception ex)
        {
            return new ErrorAnalysisResult
            {
                IsAnalysisSuccessful = false,
                IsFixable = false,
                AnalysisError = ex.Message
            };
        }
    }

    /// <summary>
    /// 단순 오류 분석 (수정 명령어만 반환, 기존 호환성)
    /// </summary>
    public async Task<string?> AnalyzeAndGetFixedCommandAsync(
        string command,
        string errorMessage,
        string? context = null)
    {
        var result = await AnalyzeErrorAsync(command, errorMessage, context);

        if (result.IsAnalysisSuccessful && result.IsFixable)
        {
            return result.FixedCommand;
        }

        return null;
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
            "curl | sh",      // 원격 스크립트 실행
            "wget | sh",
            "curl | bash",
            "wget | bash",
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
            "killall ",
            "apt-get remove",
            "apt remove",
            "yum remove",
            "dnf remove",
            "pacman -R",
            "docker rm",
            "docker stop",
            "iptables",
            "ufw ",
            "firewall-cmd",
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

    /// <summary>
    /// 명령어 위험도 레벨 반환
    /// </summary>
    public static CommandRiskLevel GetRiskLevel(string command)
    {
        if (IsDangerousCommand(command))
            return CommandRiskLevel.Critical;

        if (RequiresConfirmation(command))
            return CommandRiskLevel.High;

        // 중간 위험도 패턴
        var mediumRiskPatterns = new[]
        {
            "chmod ",
            "chown ",
            "mv ",
            "cp -r",
            "find ",
            "xargs ",
            "sed -i",
            "awk ",
        };

        foreach (var pattern in mediumRiskPatterns)
        {
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return CommandRiskLevel.Medium;
            }
        }

        return CommandRiskLevel.Low;
    }
}

/// <summary>
/// 명령어 위험도 레벨
/// </summary>
public enum CommandRiskLevel
{
    /// <summary>낮음 - 일반 명령어</summary>
    Low,

    /// <summary>중간 - 파일 수정 가능성</summary>
    Medium,

    /// <summary>높음 - 확인 필요</summary>
    High,

    /// <summary>치명적 - 시스템 손상 가능</summary>
    Critical
}
