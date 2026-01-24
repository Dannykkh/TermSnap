namespace TermSnap.Models;

/// <summary>
/// Port Forwarding 템플릿
/// </summary>
public class PortForwardingTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PortForwardingType Type { get; set; }
    public int LocalPort { get; set; }
    public string RemoteHost { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public string Icon { get; set; } = "Connection";

    /// <summary>
    /// 템플릿에서 Port Forwarding 설정 생성
    /// </summary>
    public PortForwardingConfig CreateConfig()
    {
        return new PortForwardingConfig
        {
            Name = Name,
            Type = Type,
            LocalHost = "localhost",
            LocalPort = LocalPort,
            RemoteHost = RemoteHost,
            RemotePort = RemotePort,
            AutoStart = false,
            Status = PortForwardingStatus.Stopped
        };
    }

    /// <summary>
    /// 기본 템플릿 목록
    /// </summary>
    public static PortForwardingTemplate[] GetDefaultTemplates()
    {
        return new[]
        {
            new PortForwardingTemplate
            {
                Name = "MySQL",
                Description = "MySQL/MariaDB 데이터베이스",
                Type = PortForwardingType.Local,
                LocalPort = 3306,
                RemoteHost = "localhost",
                RemotePort = 3306,
                Icon = "Database"
            },
            new PortForwardingTemplate
            {
                Name = "PostgreSQL",
                Description = "PostgreSQL 데이터베이스",
                Type = PortForwardingType.Local,
                LocalPort = 5432,
                RemoteHost = "localhost",
                RemotePort = 5432,
                Icon = "Database"
            },
            new PortForwardingTemplate
            {
                Name = "Redis",
                Description = "Redis 캐시 서버",
                Type = PortForwardingType.Local,
                LocalPort = 6379,
                RemoteHost = "localhost",
                RemotePort = 6379,
                Icon = "DatabaseOutline"
            },
            new PortForwardingTemplate
            {
                Name = "MongoDB",
                Description = "MongoDB 데이터베이스",
                Type = PortForwardingType.Local,
                LocalPort = 27017,
                RemoteHost = "localhost",
                RemotePort = 27017,
                Icon = "Database"
            },
            new PortForwardingTemplate
            {
                Name = "HTTP",
                Description = "웹 서버 (HTTP)",
                Type = PortForwardingType.Local,
                LocalPort = 8080,
                RemoteHost = "localhost",
                RemotePort = 80,
                Icon = "Web"
            },
            new PortForwardingTemplate
            {
                Name = "HTTPS",
                Description = "웹 서버 (HTTPS)",
                Type = PortForwardingType.Local,
                LocalPort = 8443,
                RemoteHost = "localhost",
                RemotePort = 443,
                Icon = "WebCheck"
            },
            new PortForwardingTemplate
            {
                Name = "SOCKS Proxy",
                Description = "브라우저 프록시 (SOCKS5)",
                Type = PortForwardingType.Dynamic,
                LocalPort = 1080,
                RemoteHost = "",
                RemotePort = 0,
                Icon = "ShieldLock"
            },
            new PortForwardingTemplate
            {
                Name = "RDP",
                Description = "원격 데스크톱 (Windows)",
                Type = PortForwardingType.Local,
                LocalPort = 3389,
                RemoteHost = "localhost",
                RemotePort = 3389,
                Icon = "Monitor"
            },
            new PortForwardingTemplate
            {
                Name = "VNC",
                Description = "VNC 원격 접속",
                Type = PortForwardingType.Local,
                LocalPort = 5900,
                RemoteHost = "localhost",
                RemotePort = 5900,
                Icon = "Monitor"
            },
            new PortForwardingTemplate
            {
                Name = "Elasticsearch",
                Description = "Elasticsearch 검색 엔진",
                Type = PortForwardingType.Local,
                LocalPort = 9200,
                RemoteHost = "localhost",
                RemotePort = 9200,
                Icon = "Magnify"
            }
        };
    }
}
