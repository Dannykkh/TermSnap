using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace TermSnap.McpServer.Tools;

/// <summary>
/// SSH 관련 MCP 도구들
/// </summary>
public static class SshTools
{
    /// <summary>
    /// 저장된 서버 프로필 목록 조회
    /// </summary>
    [McpServerTool(Name = "ssh_list_profiles")]
    [Description("List all saved SSH server profiles. Returns profile names, hosts, and last connection times.")]
    public static async Task<string> ListProfiles(IpcClient ipcClient)
    {
        var response = await ipcClient.ListProfilesAsync();

        if (response == null)
            return "Error: Could not connect to TermSnap app";

        if (!response.Success)
            return $"Error: {response.Error}";

        return response.Data ?? "[]";
    }

    /// <summary>
    /// SSH 서버에 연결
    /// </summary>
    [McpServerTool(Name = "ssh_connect")]
    [Description("Connect to an SSH server using a saved profile. Opens a new SSH tab in TermSnap UI. Returns session ID for subsequent commands.")]
    public static async Task<string> Connect(
        IpcClient ipcClient,
        [Description("Name of the saved server profile to connect to")] string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return "Error: profileName is required";

        var response = await ipcClient.ConnectSshAsync(profileName);

        if (response == null)
            return "Error: Could not connect to TermSnap app";

        if (!response.Success)
            return $"Error: {response.Error}";

        return $"Connected successfully. Session ID: {response.SessionId}\n\nUse this session ID for ssh_execute, ssh_disconnect, and other commands.";
    }

    /// <summary>
    /// SSH 연결 해제
    /// </summary>
    [McpServerTool(Name = "ssh_disconnect")]
    [Description("Disconnect from an SSH session and close the tab in TermSnap UI.")]
    public static async Task<string> Disconnect(
        IpcClient ipcClient,
        [Description("Session ID returned from ssh_connect")] string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return "Error: sessionId is required";

        var response = await ipcClient.DisconnectSshAsync(sessionId);

        if (response == null)
            return "Error: Could not connect to TermSnap app";

        if (!response.Success)
            return $"Error: {response.Error}";

        return "Disconnected successfully";
    }

    /// <summary>
    /// 세션 상태 조회
    /// </summary>
    [McpServerTool(Name = "ssh_status")]
    [Description("Get the status of an SSH session including connection state and current directory.")]
    public static async Task<string> Status(
        IpcClient ipcClient,
        [Description("Session ID returned from ssh_connect")] string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return "Error: sessionId is required";

        var response = await ipcClient.GetStatusAsync(sessionId);

        if (response == null)
            return "Error: Could not connect to TermSnap app";

        if (!response.Success)
            return $"Error: {response.Error}";

        return response.Data ?? "{}";
    }

    /// <summary>
    /// SSH 명령어 실행
    /// </summary>
    [McpServerTool(Name = "ssh_execute")]
    [Description("Execute a command on the connected SSH server. The command and result are displayed in TermSnap UI.")]
    public static async Task<string> Execute(
        IpcClient ipcClient,
        [Description("Session ID returned from ssh_connect")] string sessionId,
        [Description("Linux command to execute")] string command)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return "Error: sessionId is required";

        if (string.IsNullOrWhiteSpace(command))
            return "Error: command is required";

        var response = await ipcClient.ExecuteAsync(sessionId, command);

        if (response == null)
            return "Error: Could not connect to TermSnap app";

        if (!response.Success)
            return $"Error: {response.Error}";

        return response.Data ?? "{}";
    }

    /// <summary>
    /// 모든 활성 세션 목록
    /// </summary>
    [McpServerTool(Name = "ssh_list_sessions")]
    [Description("List all active MCP-controlled SSH sessions.")]
    public static async Task<string> ListSessions(IpcClient ipcClient)
    {
        var response = await ipcClient.GetSessionsAsync();

        if (response == null)
            return "Error: Could not connect to TermSnap app";

        if (!response.Success)
            return $"Error: {response.Error}";

        return response.Data ?? "[]";
    }

    /// <summary>
    /// 서버 리소스 통계 조회
    /// </summary>
    [McpServerTool(Name = "ssh_server_stats")]
    [Description("Get server resource statistics including CPU, memory, and disk usage.")]
    public static async Task<string> ServerStats(
        IpcClient ipcClient,
        [Description("Session ID returned from ssh_connect")] string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return "Error: sessionId is required";

        var response = await ipcClient.GetServerStatsAsync(sessionId);

        if (response == null)
            return "Error: Could not connect to TermSnap app";

        if (!response.Success)
            return $"Error: {response.Error}";

        return response.Data ?? "{}";
    }

    /// <summary>
    /// SFTP 디렉토리 목록 조회
    /// </summary>
    [McpServerTool(Name = "sftp_list")]
    [Description("List files and directories in a remote path using SFTP.")]
    public static async Task<string> SftpList(
        IpcClient ipcClient,
        [Description("Session ID returned from ssh_connect")] string sessionId,
        [Description("Remote directory path to list (default: current directory)")] string path = ".")
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return "Error: sessionId is required";

        var response = await ipcClient.SftpListAsync(sessionId, path);

        if (response == null)
            return "Error: Could not connect to TermSnap app";

        if (!response.Success)
            return $"Error: {response.Error}";

        return response.Data ?? "[]";
    }

    /// <summary>
    /// SFTP 파일 다운로드
    /// </summary>
    [McpServerTool(Name = "sftp_download")]
    [Description("Download a file from the remote server using SFTP.")]
    public static async Task<string> SftpDownload(
        IpcClient ipcClient,
        [Description("Session ID returned from ssh_connect")] string sessionId,
        [Description("Remote file path to download")] string remotePath,
        [Description("Local path to save the file")] string localPath)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return "Error: sessionId is required";

        if (string.IsNullOrWhiteSpace(remotePath))
            return "Error: remotePath is required";

        if (string.IsNullOrWhiteSpace(localPath))
            return "Error: localPath is required";

        var response = await ipcClient.SftpDownloadAsync(sessionId, remotePath, localPath);

        if (response == null)
            return "Error: Could not connect to TermSnap app";

        if (!response.Success)
            return $"Error: {response.Error}";

        return response.Data ?? "Download completed";
    }

    /// <summary>
    /// SFTP 파일 업로드
    /// </summary>
    [McpServerTool(Name = "sftp_upload")]
    [Description("Upload a file to the remote server using SFTP.")]
    public static async Task<string> SftpUpload(
        IpcClient ipcClient,
        [Description("Session ID returned from ssh_connect")] string sessionId,
        [Description("Local file path to upload")] string localPath,
        [Description("Remote path to save the file")] string remotePath)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return "Error: sessionId is required";

        if (string.IsNullOrWhiteSpace(localPath))
            return "Error: localPath is required";

        if (string.IsNullOrWhiteSpace(remotePath))
            return "Error: remotePath is required";

        var response = await ipcClient.SftpUploadAsync(sessionId, localPath, remotePath);

        if (response == null)
            return "Error: Could not connect to TermSnap app";

        if (!response.Success)
            return $"Error: {response.Error}";

        return response.Data ?? "Upload completed";
    }
}
