using System;
using System.Threading.Tasks;

namespace TermSnap.Core;

/// <summary>
/// 명령어 실행 결과
/// </summary>
public class CommandExecutionResult
{
    public string Command { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string? Error { get; set; }
    public int ExitCode { get; set; }
    public bool IsSuccess => ExitCode == 0 && string.IsNullOrEmpty(Error);
    public TimeSpan Duration { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.Now;
    public string CurrentDirectory { get; set; } = "~";
    public bool IsTimeout { get; set; }
}

/// <summary>
/// 터미널 세션 타입
/// </summary>
public enum TerminalType
{
    Local,      // 로컬 PowerShell/CMD
    SSH,        // 원격 SSH
    Docker,     // Docker 컨테이너
    WSL         // Windows Subsystem for Linux
}

/// <summary>
/// 터미널 연결 상태
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error
}

/// <summary>
/// 터미널 출력 이벤트 인자
/// </summary>
public class TerminalOutputEventArgs : EventArgs
{
    public string Data { get; }
    public string? RawData { get; }  // ANSI 코드 포함된 원시 데이터
    public bool IsError { get; }
    public DateTime Timestamp { get; }

    public TerminalOutputEventArgs(string data, bool isError = false, string? rawData = null)
    {
        Data = data;
        RawData = rawData;
        IsError = isError;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// 터미널 세션 인터페이스 - SSH/로컬/Docker 등 추상화
/// </summary>
public interface ITerminalSession : IDisposable
{
    /// <summary>
    /// 세션 ID (고유 식별자)
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// 터미널 타입
    /// </summary>
    TerminalType Type { get; }

    /// <summary>
    /// 연결 상태
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// 연결됨 여부
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 세션 이름 (표시용)
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 현재 작업 디렉토리
    /// </summary>
    string CurrentDirectory { get; }

    /// <summary>
    /// 쉘 타입 (bash, zsh, powershell, cmd)
    /// </summary>
    string ShellType { get; }

    /// <summary>
    /// 연결
    /// </summary>
    Task<bool> ConnectAsync();

    /// <summary>
    /// 연결 해제
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// 명령어 실행
    /// </summary>
    Task<CommandExecutionResult> ExecuteCommandAsync(string command, int timeoutMs = 30000);

    /// <summary>
    /// 명령어 실행 취소
    /// </summary>
    Task CancelCurrentCommandAsync();

    /// <summary>
    /// 실시간 출력 이벤트
    /// </summary>
    event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <summary>
    /// 연결 상태 변경 이벤트
    /// </summary>
    event EventHandler<ConnectionState>? StateChanged;
}

/// <summary>
/// 터미널 세션 기본 구현 (공통 로직)
/// </summary>
public abstract class TerminalSessionBase : ITerminalSession
{
    private bool _disposed = false;

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..8];
    public abstract TerminalType Type { get; }
    public abstract string DisplayName { get; }
    public abstract string ShellType { get; }

    private ConnectionState _state = ConnectionState.Disconnected;
    public ConnectionState State
    {
        get => _state;
        protected set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsConnected => State == ConnectionState.Connected;

    private string _currentDirectory = "~";
    public string CurrentDirectory
    {
        get => _currentDirectory;
        protected set => _currentDirectory = value;
    }

    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;
    public event EventHandler<ConnectionState>? StateChanged;

    public abstract Task<bool> ConnectAsync();
    public abstract Task DisconnectAsync();
    public abstract Task<CommandExecutionResult> ExecuteCommandAsync(string command, int timeoutMs = 30000);
    public abstract Task CancelCurrentCommandAsync();

    /// <summary>
    /// 출력 이벤트 발생
    /// </summary>
    protected void RaiseOutputReceived(string data, bool isError = false, string? rawData = null)
    {
        OutputReceived?.Invoke(this, new TerminalOutputEventArgs(data, isError, rawData));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // 연결 해제
            if (IsConnected)
            {
                DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
            }
        }

        _disposed = true;
    }

    ~TerminalSessionBase()
    {
        Dispose(false);
    }
}
