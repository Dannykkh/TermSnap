using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// Ralph Loop 서비스
/// PRD 기반 자동 루프 실행 관리
/// </summary>
public class RalphLoopService : IDisposable
{
    private readonly RalphLoopConfig _config;
    private CancellationTokenSource? _cts;
    private readonly StringBuilder _outputBuffer = new();
    private DateTime _lastOutputTime = DateTime.MinValue;
    private bool _isWaitingForCompletion = false;

    // 완료 감지 패턴
    private static readonly string[] CompletionPatterns = new[]
    {
        @"all\s*tasks?\s*(are\s*)?(complete|done|finished)",
        @"prd\s*(items?\s*)?(complete|done|finished)",
        @"nothing\s*(left\s*)?to\s*do",
        @"everything\s*(is\s*)?(complete|done)",
        @"\[RALPH_COMPLETE\]",  // 명시적 완료 마커
        @"EXIT_RALPH_LOOP"       // 종료 명령
    };

    // 오류 감지 패턴
    private static readonly string[] ErrorPatterns = new[]
    {
        @"fatal\s*error",
        @"unrecoverable",
        @"context\s*(window\s*)?(full|exceeded|limit)",
        @"rate\s*limit\s*exceeded"
    };

    // 컨텍스트 오염 감지 패턴 (리셋 필요)
    private static readonly string[] ContextPollutionPatterns = new[]
    {
        @"i('m|\s+am)\s*(confused|lost|stuck)",
        @"let('s|\s+us)\s+start\s+(over|fresh)",
        @"context\s*(is\s*)?(too\s*)?(long|full)",
        @"repeating\s*(the\s+same|myself)"
    };

    /// <summary>
    /// 출력 수신 이벤트
    /// </summary>
    public event Action<string>? OutputReceived;

    /// <summary>
    /// 상태 변경 이벤트
    /// </summary>
    public event Action<RalphLoopState>? StateChanged;

    /// <summary>
    /// 프롬프트 전송 요청 이벤트 (ViewModel에서 처리)
    /// </summary>
    public event Func<string, Task>? SendPromptRequested;

    /// <summary>
    /// 컨텍스트 리셋 요청 이벤트 (AI CLI 재시작)
    /// </summary>
    public event Func<Task>? ResetContextRequested;

    /// <summary>
    /// 반복 완료 이벤트
    /// </summary>
    public event Action<int>? IterationCompleted;

    public RalphLoopService(RalphLoopConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Ralph Loop 시작
    /// </summary>
    public async Task StartAsync()
    {
        if (_config.IsRunning) return;
        if (string.IsNullOrWhiteSpace(_config.PRD))
        {
            throw new InvalidOperationException("PRD가 비어있습니다.");
        }

        _cts = new CancellationTokenSource();
        _config.Reset();
        _config.StartTime = DateTime.Now;
        _config.State = RalphLoopState.Running;
        StateChanged?.Invoke(_config.State);

        try
        {
            await RunLoopAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            _config.State = RalphLoopState.Paused;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RalphLoop] 오류: {ex.Message}");
            _config.State = RalphLoopState.Error;
            _config.CurrentTask = ex.Message;
        }
        finally
        {
            StateChanged?.Invoke(_config.State);
        }
    }

    /// <summary>
    /// Ralph Loop 중지
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _config.State = RalphLoopState.Paused;
        StateChanged?.Invoke(_config.State);
    }

    /// <summary>
    /// 메인 루프 실행
    /// </summary>
    private async Task RunLoopAsync(CancellationToken ct)
    {
        // 초기 프롬프트 생성
        var initialPrompt = GenerateInitialPrompt();

        while (!ct.IsCancellationRequested && _config.CurrentIteration < _config.MaxIterations)
        {
            _config.CurrentIteration++;
            _config.State = RalphLoopState.Running;
            _config.CurrentTask = $"반복 #{_config.CurrentIteration}";
            StateChanged?.Invoke(_config.State);

            // 프롬프트 전송
            _outputBuffer.Clear();
            _isWaitingForCompletion = true;
            _lastOutputTime = DateTime.Now;

            var prompt = _config.CurrentIteration == 1 ? initialPrompt : GenerateContinuePrompt();

            if (SendPromptRequested != null)
            {
                await SendPromptRequested.Invoke(prompt);
            }

            // AI 응답 대기
            _config.State = RalphLoopState.WaitingForResponse;
            StateChanged?.Invoke(_config.State);

            await WaitForResponseAsync(ct);

            // 응답 분석
            var analysis = AnalyzeOutput(_outputBuffer.ToString());

            if (analysis == OutputAnalysis.Completed)
            {
                _config.State = RalphLoopState.Completed;
                _config.CurrentTask = "모든 작업 완료!";
                break;
            }
            else if (analysis == OutputAnalysis.Error)
            {
                _config.State = RalphLoopState.Error;
                break;
            }
            else if (analysis == OutputAnalysis.ContextPolluted)
            {
                // 컨텍스트 오염 - 리셋 필요
                Debug.WriteLine("[RalphLoop] 컨텍스트 오염 감지, 리셋 중...");
                if (ResetContextRequested != null)
                {
                    await ResetContextRequested.Invoke();
                    await Task.Delay(2000, ct);  // 재시작 대기
                }
            }

            IterationCompleted?.Invoke(_config.CurrentIteration);

            // 다음 반복 전 짧은 대기
            await Task.Delay(1000, ct);
        }

        if (_config.CurrentIteration >= _config.MaxIterations)
        {
            _config.State = RalphLoopState.Completed;
            _config.CurrentTask = "최대 반복 횟수 도달";
        }
    }

    /// <summary>
    /// AI 응답 대기
    /// </summary>
    private async Task WaitForResponseAsync(CancellationToken ct)
    {
        const int timeoutSeconds = 300;  // 5분 타임아웃
        const int idleSeconds = 30;      // 30초 동안 출력 없으면 완료로 간주

        var startTime = DateTime.Now;

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(500, ct);

            // 타임아웃 체크
            if ((DateTime.Now - startTime).TotalSeconds > timeoutSeconds)
            {
                Debug.WriteLine("[RalphLoop] 응답 타임아웃");
                break;
            }

            // 유휴 상태 체크 (출력이 멈춘 경우)
            if (_lastOutputTime != DateTime.MinValue &&
                (DateTime.Now - _lastOutputTime).TotalSeconds > idleSeconds)
            {
                Debug.WriteLine("[RalphLoop] 응답 완료 (유휴 감지)");
                break;
            }

            // 완료 패턴 감지
            var currentOutput = _outputBuffer.ToString();
            if (ContainsPattern(currentOutput, CompletionPatterns))
            {
                Debug.WriteLine("[RalphLoop] 완료 패턴 감지");
                break;
            }
        }

        _isWaitingForCompletion = false;
    }

    /// <summary>
    /// 초기 프롬프트 생성
    /// </summary>
    private string GenerateInitialPrompt()
    {
        return $@"You are in RALPH LOOP mode. Your task is to implement the following PRD completely.

## PRD (Product Requirements Document)
{_config.PRD}

## Instructions
1. Analyze the PRD and break it down into tasks
2. Implement each task one by one
3. After each task, commit your changes with a clear message
4. Continue until ALL tasks are complete
5. When everything is done, output: [RALPH_COMPLETE]

## Important
- Progress is saved in files and git, not in context
- If you get confused, say 'let's start fresh' and I'll reset
- Focus on one task at a time
- Test your changes before moving on

Start now. What's the first task?";
    }

    /// <summary>
    /// 계속 프롬프트 생성
    /// </summary>
    private string GenerateContinuePrompt()
    {
        return @"Continue with the PRD implementation.

Check the current state of the codebase and continue from where you left off.
If all tasks are complete, output: [RALPH_COMPLETE]

What's the next task?";
    }

    /// <summary>
    /// AI 출력 수신 (외부에서 호출)
    /// </summary>
    public void OnOutputReceived(string output)
    {
        if (_isWaitingForCompletion)
        {
            _outputBuffer.Append(output);
            _lastOutputTime = DateTime.Now;
        }
    }

    /// <summary>
    /// 출력 분석
    /// </summary>
    private OutputAnalysis AnalyzeOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return OutputAnalysis.Continue;

        var lowerOutput = output.ToLower();

        if (ContainsPattern(lowerOutput, CompletionPatterns))
            return OutputAnalysis.Completed;

        if (ContainsPattern(lowerOutput, ErrorPatterns))
            return OutputAnalysis.Error;

        if (ContainsPattern(lowerOutput, ContextPollutionPatterns))
            return OutputAnalysis.ContextPolluted;

        return OutputAnalysis.Continue;
    }

    /// <summary>
    /// 패턴 포함 여부 확인
    /// </summary>
    private bool ContainsPattern(string text, string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

/// <summary>
/// 출력 분석 결과
/// </summary>
public enum OutputAnalysis
{
    Continue,         // 계속 진행
    Completed,        // 완료
    Error,            // 오류
    ContextPolluted   // 컨텍스트 오염
}
