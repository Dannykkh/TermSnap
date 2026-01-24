using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TermSnap.Models;

/// <summary>
/// Port Forwarding 타입
/// </summary>
public enum PortForwardingType
{
    /// <summary>
    /// Local Port Forwarding (로컬 → 원격)
    /// </summary>
    Local,

    /// <summary>
    /// Remote Port Forwarding (원격 → 로컬)
    /// </summary>
    Remote,

    /// <summary>
    /// Dynamic Port Forwarding (SOCKS Proxy)
    /// </summary>
    Dynamic
}

/// <summary>
/// Port Forwarding 상태
/// </summary>
public enum PortForwardingStatus
{
    /// <summary>
    /// 중지됨
    /// </summary>
    Stopped,

    /// <summary>
    /// 시작 중
    /// </summary>
    Starting,

    /// <summary>
    /// 실행 중
    /// </summary>
    Running,

    /// <summary>
    /// 오류 발생
    /// </summary>
    Error
}

/// <summary>
/// Port Forwarding 설정
/// </summary>
public class PortForwardingConfig : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private PortForwardingType _type = PortForwardingType.Local;
    private string _localHost = "localhost";
    private int _localPort;
    private string _remoteHost = string.Empty;
    private int _remotePort;
    private PortForwardingStatus _status = PortForwardingStatus.Stopped;
    private string? _errorMessage;
    private int _connectionCount;
    private DateTime? _startedAt;
    private bool _autoStart;

    /// <summary>
    /// Port Forwarding 이름 (사용자 지정)
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Port Forwarding 타입
    /// </summary>
    public PortForwardingType Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    /// <summary>
    /// 로컬 호스트 (기본: localhost)
    /// </summary>
    public string LocalHost
    {
        get => _localHost;
        set => SetProperty(ref _localHost, value);
    }

    /// <summary>
    /// 로컬 포트
    /// </summary>
    public int LocalPort
    {
        get => _localPort;
        set => SetProperty(ref _localPort, value);
    }

    /// <summary>
    /// 원격 호스트 (Local/Remote Forwarding용)
    /// </summary>
    public string RemoteHost
    {
        get => _remoteHost;
        set => SetProperty(ref _remoteHost, value);
    }

    /// <summary>
    /// 원격 포트 (Local/Remote Forwarding용)
    /// </summary>
    public int RemotePort
    {
        get => _remotePort;
        set => SetProperty(ref _remotePort, value);
    }

    /// <summary>
    /// 현재 상태
    /// </summary>
    public PortForwardingStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    /// <summary>
    /// 오류 메시지
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>
    /// 현재 활성 연결 수 (Dynamic Forwarding용)
    /// </summary>
    public int ConnectionCount
    {
        get => _connectionCount;
        set => SetProperty(ref _connectionCount, value);
    }

    /// <summary>
    /// 시작 시간
    /// </summary>
    public DateTime? StartedAt
    {
        get => _startedAt;
        set => SetProperty(ref _startedAt, value);
    }

    /// <summary>
    /// SSH 연결 시 자동 시작
    /// </summary>
    public bool AutoStart
    {
        get => _autoStart;
        set => SetProperty(ref _autoStart, value);
    }

    /// <summary>
    /// Port Forwarding 설명 (UI 표시용)
    /// </summary>
    public string Description
    {
        get
        {
            return Type switch
            {
                PortForwardingType.Local => $"{LocalHost}:{LocalPort} → {RemoteHost}:{RemotePort}",
                PortForwardingType.Remote => $"{RemoteHost}:{RemotePort} ← {LocalHost}:{LocalPort}",
                PortForwardingType.Dynamic => $"SOCKS Proxy: {LocalHost}:{LocalPort}",
                _ => "Unknown"
            };
        }
    }

    /// <summary>
    /// 상태 아이콘 (UI 표시용)
    /// </summary>
    public string StatusIcon
    {
        get
        {
            return Status switch
            {
                PortForwardingStatus.Stopped => "⚪",
                PortForwardingStatus.Starting => "🟡",
                PortForwardingStatus.Running => "🟢",
                PortForwardingStatus.Error => "🔴",
                _ => "⚪"
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // Description과 StatusIcon은 다른 속성 변경 시 같이 업데이트
        if (propertyName != nameof(Description) &&
            (propertyName == nameof(Type) || propertyName == nameof(LocalHost) ||
             propertyName == nameof(LocalPort) || propertyName == nameof(RemoteHost) ||
             propertyName == nameof(RemotePort)))
        {
            OnPropertyChanged(nameof(Description));
        }

        if (propertyName == nameof(Status))
        {
            OnPropertyChanged(nameof(StatusIcon));
        }
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// 유효성 검사
    /// </summary>
    public bool Validate(out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            errorMessage = "이름을 입력하세요.";
            return false;
        }

        if (LocalPort <= 0 || LocalPort > 65535)
        {
            errorMessage = "로컬 포트는 1-65535 범위여야 합니다.";
            return false;
        }

        // 잘 알려진 포트 (1-1023) 경고
        if (LocalPort < 1024 && LocalPort > 0)
        {
            errorMessage = $"포트 {LocalPort}는 시스템 예약 포트입니다. 관리자 권한이 필요할 수 있습니다.";
            // 경고이지만 허용
        }

        if (Type != PortForwardingType.Dynamic)
        {
            if (string.IsNullOrWhiteSpace(RemoteHost))
            {
                errorMessage = "원격 호스트를 입력하세요.";
                return false;
            }

            if (RemotePort <= 0 || RemotePort > 65535)
            {
                errorMessage = "원격 포트는 1-65535 범위여야 합니다.";
                return false;
            }
        }

        // 포트 사용 가능 여부 확인 (Local/Dynamic Forward만)
        if (Type == PortForwardingType.Local || Type == PortForwardingType.Dynamic)
        {
            if (!IsPortAvailable(LocalPort))
            {
                errorMessage = $"포트 {LocalPort}는 이미 사용 중입니다.";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }

    /// <summary>
    /// 포트 사용 가능 여부 확인
    /// </summary>
    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
