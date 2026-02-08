using System.Collections.Generic;
using TermSnap.Core.Sessions;
using TermSnap.ViewModels;

namespace TermSnap.Models;

/// <summary>
/// 세션 상태 저장용 모델 (앱 종료 시 저장, 시작 시 복원)
/// </summary>
public class SessionState
{
    /// <summary>
    /// 세션 타입 (Local, SSH, Selector)
    /// </summary>
    public SessionType Type { get; set; } = SessionType.Selector;

    /// <summary>
    /// 탭 헤더 텍스트
    /// </summary>
    public string TabHeader { get; set; } = string.Empty;

    /// <summary>
    /// 로컬 터미널: 쉘 타입 (PowerShell, Cmd, WSL, GitBash)
    /// </summary>
    public string ShellType { get; set; } = "PowerShell";

    /// <summary>
    /// 작업 디렉토리
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// SSH 세션: 서버 프로필 이름
    /// </summary>
    public string? ServerProfileName { get; set; }

    /// <summary>
    /// 이 탭이 선택(활성)되어 있었는지
    /// </summary>
    public bool IsSelected { get; set; } = false;

    /// <summary>
    /// Block UI 사용 여부
    /// </summary>
    public bool UseBlockUI { get; set; } = true;

    /// <summary>
    /// 탭 인덱스 (순서 보존용)
    /// </summary>
    public int TabIndex { get; set; } = 0;

    /// <summary>
    /// 프로젝트 세션: 프로젝트 경로
    /// </summary>
    public string? ProjectPath { get; set; }

    /// <summary>
    /// 프로젝트 세션: 프로젝트 이름
    /// </summary>
    public string? ProjectName { get; set; }

    /// <summary>
    /// 프로젝트 세션: 서브세션 목록
    /// </summary>
    public List<SubSessionState>? SubSessions { get; set; }

    /// <summary>
    /// 프로젝트 세션: 선택된 서브세션 인덱스
    /// </summary>
    public int SelectedSubSessionIndex { get; set; } = 0;
}

/// <summary>
/// 서브세션 상태 저장용 모델
/// </summary>
public class SubSessionState
{
    /// <summary>
    /// 서브세션 타입 (Local, SSH)
    /// </summary>
    public SessionType Type { get; set; } = SessionType.Local;

    /// <summary>
    /// 탭 헤더
    /// </summary>
    public string TabHeader { get; set; } = string.Empty;

    /// <summary>
    /// 쉘 타입
    /// </summary>
    public string ShellType { get; set; } = "PowerShell";

    /// <summary>
    /// 작업 디렉토리
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Block UI 사용 여부
    /// </summary>
    public bool UseBlockUI { get; set; } = true;
}
