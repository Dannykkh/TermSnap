using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nebula.Mcp;

namespace Nebula.McpServer;

/// <summary>
/// MCP 서버에서 WPF 앱으로 IPC 요청을 보내는 클라이언트
/// </summary>
public class IpcClient : IDisposable
{
    private const string PipeName = "Nebula_MCP";
    private const int ConnectionTimeoutMs = 5000;
    private const int ReadTimeoutMs = 30000;

    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _isConnected;

    public bool IsConnected => _isConnected && _pipe?.IsConnected == true;

    /// <summary>
    /// WPF 앱에 연결
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            _pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await _pipe.ConnectAsync(ConnectionTimeoutMs);

            _reader = new StreamReader(_pipe, Encoding.UTF8, leaveOpen: true);
            _writer = new StreamWriter(_pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            _isConnected = true;

            // Ping 테스트
            var pingResponse = await SendRequestAsync(new IpcRequest { Command = IpcCommand.Ping });
            if (pingResponse?.Success != true)
            {
                Console.Error.WriteLine("[IPC Client] Ping failed");
                return false;
            }

            Console.Error.WriteLine("[IPC Client] Connected to WPF app");
            return true;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("[IPC Client] Connection timeout - is Nebula Terminal app running?");
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[IPC Client] Connection error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// IPC 요청 전송 및 응답 수신
    /// </summary>
    public async Task<IpcResponse?> SendRequestAsync(IpcRequest request)
    {
        if (!IsConnected || _writer == null || _reader == null)
        {
            // 재연결 시도
            if (!await ConnectAsync())
            {
                return IpcResponse.Fail(request.RequestId, "Not connected to WPF app");
            }
        }

        await _sendLock.WaitAsync();
        try
        {
            // 요청 전송
            var requestJson = request.ToJson();
            Console.Error.WriteLine($"[IPC Client] Sending: {requestJson}");
            await _writer!.WriteLineAsync(requestJson);

            // 응답 수신 (타임아웃 적용)
            using var cts = new CancellationTokenSource(ReadTimeoutMs);
            var responseJson = await _reader!.ReadLineAsync();

            if (string.IsNullOrEmpty(responseJson))
            {
                return IpcResponse.Fail(request.RequestId, "Empty response from WPF app");
            }

            Console.Error.WriteLine($"[IPC Client] Received: {responseJson}");
            return IpcResponse.FromJson(responseJson);
        }
        catch (OperationCanceledException)
        {
            return IpcResponse.Fail(request.RequestId, "Request timeout");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[IPC Client] Error: {ex.Message}");
            _isConnected = false;
            return IpcResponse.Fail(request.RequestId, ex.Message);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// 서버 프로필 목록 조회
    /// </summary>
    public async Task<IpcResponse?> ListProfilesAsync()
    {
        return await SendRequestAsync(new IpcRequest { Command = IpcCommand.ListProfiles });
    }

    /// <summary>
    /// SSH 연결
    /// </summary>
    public async Task<IpcResponse?> ConnectSshAsync(string profileName)
    {
        return await SendRequestAsync(new IpcRequest
        {
            Command = IpcCommand.Connect,
            ProfileName = profileName
        });
    }

    /// <summary>
    /// SSH 연결 해제
    /// </summary>
    public async Task<IpcResponse?> DisconnectSshAsync(string sessionId)
    {
        return await SendRequestAsync(new IpcRequest
        {
            Command = IpcCommand.Disconnect,
            SessionId = sessionId
        });
    }

    /// <summary>
    /// 세션 상태 조회
    /// </summary>
    public async Task<IpcResponse?> GetStatusAsync(string sessionId)
    {
        return await SendRequestAsync(new IpcRequest
        {
            Command = IpcCommand.Status,
            SessionId = sessionId
        });
    }

    /// <summary>
    /// 명령어 실행
    /// </summary>
    public async Task<IpcResponse?> ExecuteAsync(string sessionId, string command)
    {
        return await SendRequestAsync(new IpcRequest
        {
            Command = IpcCommand.Execute,
            SessionId = sessionId,
            CommandText = command
        });
    }

    /// <summary>
    /// 모든 세션 목록
    /// </summary>
    public async Task<IpcResponse?> GetSessionsAsync()
    {
        return await SendRequestAsync(new IpcRequest { Command = IpcCommand.GetSessions });
    }

    /// <summary>
    /// 서버 통계 조회
    /// </summary>
    public async Task<IpcResponse?> GetServerStatsAsync(string sessionId)
    {
        return await SendRequestAsync(new IpcRequest
        {
            Command = IpcCommand.ServerStats,
            SessionId = sessionId
        });
    }

    /// <summary>
    /// SFTP 파일 목록 조회
    /// </summary>
    public async Task<IpcResponse?> SftpListAsync(string sessionId, string path)
    {
        return await SendRequestAsync(new IpcRequest
        {
            Command = IpcCommand.SftpList,
            SessionId = sessionId,
            RemotePath = path
        });
    }

    /// <summary>
    /// SFTP 파일 다운로드
    /// </summary>
    public async Task<IpcResponse?> SftpDownloadAsync(string sessionId, string remotePath, string localPath)
    {
        return await SendRequestAsync(new IpcRequest
        {
            Command = IpcCommand.SftpDownload,
            SessionId = sessionId,
            RemotePath = remotePath,
            LocalPath = localPath
        });
    }

    /// <summary>
    /// SFTP 파일 업로드
    /// </summary>
    public async Task<IpcResponse?> SftpUploadAsync(string sessionId, string localPath, string remotePath)
    {
        return await SendRequestAsync(new IpcRequest
        {
            Command = IpcCommand.SftpUpload,
            SessionId = sessionId,
            LocalPath = localPath,
            RemotePath = remotePath
        });
    }

    public void Dispose()
    {
        _sendLock.Dispose();
        _reader?.Dispose();
        _writer?.Dispose();
        _pipe?.Dispose();
    }
}
