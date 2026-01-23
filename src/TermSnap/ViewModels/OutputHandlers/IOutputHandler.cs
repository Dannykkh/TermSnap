using System;
using TermSnap.Core;
using TermSnap.Models;

namespace TermSnap.ViewModels.OutputHandlers;

/// <summary>
/// 출력 처리 핸들러 인터페이스
/// </summary>
public interface IOutputHandler : IDisposable
{
    /// <summary>
    /// 출력 데이터 처리
    /// </summary>
    void HandleOutput(TerminalOutputEventArgs e);

    /// <summary>
    /// 현재 CommandBlock 설정 (비인터랙티브 모드에서 사용)
    /// </summary>
    void SetCurrentBlock(CommandBlock? block);
}
