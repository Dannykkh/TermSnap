using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Nebula.Models;

namespace Nebula.ViewModels;

/// <summary>
/// 세션 뷰모델 인터페이스 - SSH/로컬 세션 공통
/// </summary>
public interface ISessionViewModel : IDisposable
{
    /// <summary>
    /// 탭 헤더 텍스트
    /// </summary>
    string TabHeader { get; set; }

    /// <summary>
    /// 사용자 입력
    /// </summary>
    string UserInput { get; set; }

    /// <summary>
    /// 연결 상태
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 작업 중 여부
    /// </summary>
    bool IsBusy { get; }

    /// <summary>
    /// 상태 메시지
    /// </summary>
    string StatusMessage { get; }

    /// <summary>
    /// 현재 디렉토리
    /// </summary>
    string CurrentDirectory { get; }

    /// <summary>
    /// Block UI 사용 여부
    /// </summary>
    bool UseBlockUI { get; set; }

    /// <summary>
    /// 채팅 메시지 목록
    /// </summary>
    ObservableCollection<ChatMessage> Messages { get; }

    /// <summary>
    /// Command Block 목록
    /// </summary>
    ObservableCollection<CommandBlock> CommandBlocks { get; }

    /// <summary>
    /// 메시지 전송 커맨드
    /// </summary>
    ICommand SendMessageCommand { get; }

    /// <summary>
    /// 연결 해제 커맨드
    /// </summary>
    ICommand DisconnectCommand { get; }

    /// <summary>
    /// 세션 타입
    /// </summary>
    SessionType Type { get; }

    /// <summary>
    /// 탭 활성화 이벤트
    /// </summary>
    event EventHandler? Activated;

    /// <summary>
    /// 탭 비활성화 이벤트
    /// </summary>
    event EventHandler? Deactivated;

    /// <summary>
    /// 탭이 활성화될 때 호출 (내부적으로 Activated 이벤트 발생)
    /// </summary>
    void OnActivated();

    /// <summary>
    /// 탭이 비활성화될 때 호출 (내부적으로 Deactivated 이벤트 발생)
    /// </summary>
    void OnDeactivated();
}

/// <summary>
/// 세션 타입
/// </summary>
public enum SessionType
{
    /// <summary>
    /// 세션 선택 화면 (새 탭 시작)
    /// </summary>
    Selector,
    
    /// <summary>
    /// SSH 서버 연결
    /// </summary>
    SSH,
    
    /// <summary>
    /// 로컬 터미널
    /// </summary>
    Local,
    
    /// <summary>
    /// WSL (Windows Subsystem for Linux)
    /// </summary>
    WSL
}
