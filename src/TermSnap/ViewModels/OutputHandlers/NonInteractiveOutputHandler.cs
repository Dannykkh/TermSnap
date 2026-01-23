using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using TermSnap.Core;
using TermSnap.Models;

namespace TermSnap.ViewModels.OutputHandlers;

/// <summary>
/// 비인터랙티브 모드 출력 핸들러 (일반 명령어)
/// 출력을 버퍼에 담아서 쓰로틀링 후 CommandBlock에 추가
/// </summary>
public class NonInteractiveOutputHandler : IOutputHandler
{
    private readonly ConcurrentQueue<string> _outputBuffer = new();
    private readonly ConcurrentQueue<string> _errorBuffer = new();
    private Timer? _flushTimer;
    private CommandBlock? _currentBlock;

    private const int FlushIntervalMs = 100;
    private const int MaxBufferSize = 50;

    /// <summary>
    /// 메시지 추가 콜백 (블록이 없을 때 사용)
    /// </summary>
    public Action<string, MessageType>? AddMessageCallback { get; set; }

    public NonInteractiveOutputHandler()
    {
        StartFlushTimer();
    }

    /// <summary>
    /// 출력 데이터 처리 - 버퍼에 담아서 쓰로틀링
    /// </summary>
    public void HandleOutput(TerminalOutputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        // 버퍼에 추가
        if (e.IsError)
        {
            _errorBuffer.Enqueue(e.Data);
        }
        else
        {
            _outputBuffer.Enqueue(e.Data);
        }

        // 버퍼가 너무 크면 즉시 플러시
        if (_outputBuffer.Count + _errorBuffer.Count >= MaxBufferSize)
        {
            FlushOutputBuffer();
        }
    }

    /// <summary>
    /// 현재 CommandBlock 설정
    /// </summary>
    public void SetCurrentBlock(CommandBlock? block)
    {
        // 이전 블록이 있으면 마지막 플러시
        if (_currentBlock != null && _currentBlock != block)
        {
            FlushOutputBuffer();
        }

        _currentBlock = block;
    }

    /// <summary>
    /// 플러시 타이머 시작
    /// </summary>
    private void StartFlushTimer()
    {
        StopFlushTimer();
        _flushTimer = new Timer(_ => FlushOutputBuffer(), null, FlushIntervalMs, FlushIntervalMs);
    }

    /// <summary>
    /// 플러시 타이머 중지
    /// </summary>
    private void StopFlushTimer()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
    }

    /// <summary>
    /// 버퍼의 출력을 UI에 플러시
    /// </summary>
    private void FlushOutputBuffer()
    {
        var outputs = new List<string>();
        var errors = new List<string>();

        // 버퍼에서 모든 데이터 가져오기
        while (_outputBuffer.TryDequeue(out var output))
        {
            outputs.Add(output);
        }
        while (_errorBuffer.TryDequeue(out var error))
        {
            errors.Add(error);
        }

        if (outputs.Count == 0 && errors.Count == 0)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            // 현재 블록이 있으면 블록에 추가
            if (_currentBlock != null)
            {
                if (outputs.Count > 0)
                {
                    _currentBlock.Output += string.Join("", outputs);
                }
                if (errors.Count > 0)
                {
                    _currentBlock.Error += string.Join("", errors);
                }
            }
            else
            {
                // 블록이 없으면 메시지로 추가 (구식 UI)
                if (outputs.Count > 0 && AddMessageCallback != null)
                {
                    AddMessageCallback(string.Join("", outputs), MessageType.Normal);
                }
                if (errors.Count > 0 && AddMessageCallback != null)
                {
                    AddMessageCallback(string.Join("", errors), MessageType.Error);
                }
            }
        });
    }

    public void Dispose()
    {
        // 마지막 플러시
        FlushOutputBuffer();

        // 타이머 정리
        StopFlushTimer();
    }
}
