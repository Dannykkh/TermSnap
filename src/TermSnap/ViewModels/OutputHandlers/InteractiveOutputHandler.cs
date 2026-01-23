using System;
using TermSnap.Core;
using TermSnap.Models;

namespace TermSnap.ViewModels.OutputHandlers;

/// <summary>
/// 인터랙티브 모드 출력 핸들러 (claude, vim 등)
/// VT100 출력을 즉시 터미널 컨트롤로 전달
/// </summary>
public class InteractiveOutputHandler : IOutputHandler
{
    /// <summary>
    /// 원시 VT100 출력 이벤트 (터미널 컨트롤용)
    /// </summary>
    public event Action<string>? RawOutputReceived;

    /// <summary>
    /// 출력 데이터 처리 - 즉시 이벤트 발생
    /// </summary>
    public void HandleOutput(TerminalOutputEventArgs e)
    {
        // 인터랙티브 모드: VT100 원시 출력(ANSI 포함)을 터미널 컨트롤로 전달
        var output = e.RawData ?? e.Data;

        if (string.IsNullOrEmpty(output))
            return;

        RawOutputReceived?.Invoke(output);
    }

    /// <summary>
    /// 현재 CommandBlock 설정 (인터랙티브 모드에서는 사용 안 함)
    /// </summary>
    public void SetCurrentBlock(CommandBlock? block)
    {
        // 인터랙티브 모드에서는 블록을 사용하지 않음
    }

    public void Dispose()
    {
        // 정리할 리소스 없음
    }
}
