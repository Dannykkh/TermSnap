using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TermSnap.Mcp;

/// <summary>
/// IPC 명령 타입
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum IpcCommand
{
    // 서버 프로필 관리
    ListProfiles,
    Connect,
    Disconnect,
    Status,

    // 명령어 실행
    Execute,

    // SFTP 파일 전송
    SftpList,
    SftpDownload,
    SftpUpload,

    // 서버 모니터링
    ServerStats,

    // 기타
    Ping,
    GetSessions
}

/// <summary>
/// IPC 요청 메시지
/// </summary>
public class IpcRequest
{
    /// <summary>
    /// 요청 ID (응답 매칭용)
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 명령 타입
    /// </summary>
    public IpcCommand Command { get; set; }

    /// <summary>
    /// 세션 ID (연결된 SSH 세션)
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 프로필 이름 (연결 시 사용)
    /// </summary>
    public string? ProfileName { get; set; }

    /// <summary>
    /// 실행할 명령어 텍스트
    /// </summary>
    public string? CommandText { get; set; }

    /// <summary>
    /// 원격 경로 (SFTP용)
    /// </summary>
    public string? RemotePath { get; set; }

    /// <summary>
    /// 로컬 경로 (SFTP용)
    /// </summary>
    public string? LocalPath { get; set; }

    /// <summary>
    /// 추가 옵션 (확장성)
    /// </summary>
    public Dictionary<string, string>? Options { get; set; }

    /// <summary>
    /// JSON 직렬화
    /// </summary>
    public string ToJson() => JsonConvert.SerializeObject(this);

    /// <summary>
    /// JSON 역직렬화
    /// </summary>
    public static IpcRequest? FromJson(string json) =>
        JsonConvert.DeserializeObject<IpcRequest>(json);
}

/// <summary>
/// IPC 응답 메시지
/// </summary>
public class IpcResponse
{
    /// <summary>
    /// 요청 ID (요청과 매칭)
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 세션 ID (연결 성공 시 반환)
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 결과 데이터 (JSON 문자열 또는 일반 문자열)
    /// </summary>
    public string? Data { get; set; }

    /// <summary>
    /// 오류 메시지
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// 추가 메타데이터
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// 성공 응답 생성
    /// </summary>
    public static IpcResponse Ok(string requestId, string? data = null, string? sessionId = null)
    {
        return new IpcResponse
        {
            RequestId = requestId,
            Success = true,
            Data = data,
            SessionId = sessionId
        };
    }

    /// <summary>
    /// 실패 응답 생성
    /// </summary>
    public static IpcResponse Fail(string requestId, string error)
    {
        return new IpcResponse
        {
            RequestId = requestId,
            Success = false,
            Error = error
        };
    }

    /// <summary>
    /// JSON 직렬화
    /// </summary>
    public string ToJson() => JsonConvert.SerializeObject(this);

    /// <summary>
    /// JSON 역직렬화
    /// </summary>
    public static IpcResponse? FromJson(string json) =>
        JsonConvert.DeserializeObject<IpcResponse>(json);
}

/// <summary>
/// 서버 프로필 정보 (간략화된 버전)
/// </summary>
public class ServerProfileInfo
{
    public string ProfileName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public DateTime? LastConnected { get; set; }
    public string? Memo { get; set; }
}

/// <summary>
/// SSH 세션 상태 정보
/// </summary>
public class SessionStatusInfo
{
    public string SessionId { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public string CurrentDirectory { get; set; } = "~";
    public DateTime ConnectedAt { get; set; }
    public bool IsMcpControlled { get; set; }
}

/// <summary>
/// 명령 실행 결과
/// </summary>
public class CommandExecuteResult
{
    public string Command { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string CurrentDirectory { get; set; } = "~";
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// 서버 통계 정보
/// </summary>
public class ServerStatsInfo
{
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public double MemoryTotal { get; set; }
    public double MemoryUsed { get; set; }
    public List<DiskUsageInfo> Disks { get; set; } = new();
    public TimeSpan Uptime { get; set; }
}

/// <summary>
/// 디스크 사용량 정보
/// </summary>
public class DiskUsageInfo
{
    public string MountPoint { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public double TotalGB { get; set; }
    public double UsedGB { get; set; }
    public double AvailableGB { get; set; }
    public double UsagePercent { get; set; }
}

/// <summary>
/// SFTP 파일 정보
/// </summary>
public class SftpFileInfo
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string Permissions { get; set; } = string.Empty;
}
