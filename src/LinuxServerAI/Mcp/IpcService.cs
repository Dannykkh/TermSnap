using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Nebula.ViewModels;

namespace Nebula.Mcp;

/// <summary>
/// WPF 앱 내에서 실행되는 IPC 서버 (Named Pipe)
/// MCP 서버 프로세스의 요청을 수신하고 UI에서 처리
/// </summary>
public class IpcService : IDisposable
{
    private const string PipeName = "Nebula_MCP";
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _isRunning;
    private MainViewModel? _mainViewModel;

    public static IpcService Instance { get; } = new();

    public bool IsRunning => _isRunning;

    /// <summary>
    /// IPC 서버 시작
    /// </summary>
    public void Start(MainViewModel mainViewModel)
    {
        if (_isRunning)
            return;

        _mainViewModel = mainViewModel;
        _cts = new CancellationTokenSource();
        _isRunning = true;

        _serverTask = Task.Run(() => RunServerLoop(_cts.Token));

        Debug.WriteLine($"[IPC] 서버 시작: pipe={PipeName}");
    }

    /// <summary>
    /// IPC 서버 중지
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _cts?.Cancel();

        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException) { }

        _cts?.Dispose();
        _cts = null;

        Debug.WriteLine("[IPC] 서버 중지됨");
    }

    /// <summary>
    /// 서버 루프 - 연결을 계속 수신
    /// </summary>
    private async Task RunServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                Debug.WriteLine("[IPC] 클라이언트 연결 대기 중...");

                await pipe.WaitForConnectionAsync(ct);

                Debug.WriteLine("[IPC] 클라이언트 연결됨");

                // 연결된 클라이언트 처리
                await HandleClientAsync(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IPC] 서버 오류: {ex.Message}");
                await Task.Delay(1000, ct);
            }
        }
    }

    /// <summary>
    /// 클라이언트 요청 처리
    /// </summary>
    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            while (pipe.IsConnected && !ct.IsCancellationRequested)
            {
                // 요청 읽기
                var requestJson = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(requestJson))
                {
                    await Task.Delay(10, ct);
                    continue;
                }

                Debug.WriteLine($"[IPC] 요청 수신: {requestJson}");

                // 요청 파싱
                var request = IpcRequest.FromJson(requestJson);
                if (request == null)
                {
                    var errorResponse = IpcResponse.Fail("", "Invalid request format");
                    await writer.WriteLineAsync(errorResponse.ToJson());
                    continue;
                }

                // 요청 처리
                var response = await ProcessRequestAsync(request);

                // 응답 전송
                var responseJson = response.ToJson();
                Debug.WriteLine($"[IPC] 응답 전송: {responseJson}");
                await writer.WriteLineAsync(responseJson);
            }
        }
        catch (IOException)
        {
            Debug.WriteLine("[IPC] 클라이언트 연결 끊김");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IPC] 클라이언트 처리 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 요청 처리 - UI 스레드에서 실행
    /// </summary>
    private async Task<IpcResponse> ProcessRequestAsync(IpcRequest request)
    {
        try
        {
            return request.Command switch
            {
                IpcCommand.Ping => IpcResponse.Ok(request.RequestId, "pong"),
                IpcCommand.ListProfiles => await ProcessListProfilesAsync(request),
                IpcCommand.Connect => await ProcessConnectAsync(request),
                IpcCommand.Disconnect => await ProcessDisconnectAsync(request),
                IpcCommand.Status => await ProcessStatusAsync(request),
                IpcCommand.Execute => await ProcessExecuteAsync(request),
                IpcCommand.GetSessions => await ProcessGetSessionsAsync(request),
                IpcCommand.ServerStats => await ProcessServerStatsAsync(request),
                IpcCommand.SftpList => await ProcessSftpListAsync(request),
                IpcCommand.SftpDownload => await ProcessSftpDownloadAsync(request),
                IpcCommand.SftpUpload => await ProcessSftpUploadAsync(request),
                _ => IpcResponse.Fail(request.RequestId, $"Unknown command: {request.Command}")
            };
        }
        catch (Exception ex)
        {
            return IpcResponse.Fail(request.RequestId, ex.Message);
        }
    }

    /// <summary>
    /// 서버 프로필 목록 조회
    /// </summary>
    private Task<IpcResponse> ProcessListProfilesAsync(IpcRequest request)
    {
        var profiles = McpSessionManager.Instance.GetServerProfiles();
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(profiles);
        return Task.FromResult(IpcResponse.Ok(request.RequestId, json));
    }

    /// <summary>
    /// SSH 연결 - UI 스레드에서 탭 생성
    /// </summary>
    private async Task<IpcResponse> ProcessConnectAsync(IpcRequest request)
    {
        if (string.IsNullOrEmpty(request.ProfileName))
            return IpcResponse.Fail(request.RequestId, "ProfileName is required");

        if (_mainViewModel == null)
            return IpcResponse.Fail(request.RequestId, "MainViewModel not initialized");

        // UI 스레드에서 세션 생성
        var tcs = new TaskCompletionSource<IpcResponse>();

        // 비동기 람다를 Dispatcher에서 실행 (TCS로 결과 반환)
#pragma warning disable CS4014 // Because this call is not awaited
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var result = await McpSessionManager.Instance.CreateSessionAsync(
                    _mainViewModel,
                    request.ProfileName);

                if (result.Success)
                {
                    tcs.SetResult(IpcResponse.Ok(request.RequestId, result.Message, result.SessionId));
                }
                else
                {
                    tcs.SetResult(IpcResponse.Fail(request.RequestId, result.Message ?? "Connection failed"));
                }
            }
            catch (Exception ex)
            {
                tcs.SetResult(IpcResponse.Fail(request.RequestId, ex.Message));
            }
        });
#pragma warning restore CS4014

        return await tcs.Task;
    }

    /// <summary>
    /// SSH 연결 해제
    /// </summary>
    private async Task<IpcResponse> ProcessDisconnectAsync(IpcRequest request)
    {
        if (string.IsNullOrEmpty(request.SessionId))
            return IpcResponse.Fail(request.RequestId, "SessionId is required");

        if (_mainViewModel == null)
            return IpcResponse.Fail(request.RequestId, "MainViewModel not initialized");

        var tcs = new TaskCompletionSource<IpcResponse>();

        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var result = McpSessionManager.Instance.DisconnectSession(_mainViewModel, request.SessionId);
                if (result)
                {
                    tcs.SetResult(IpcResponse.Ok(request.RequestId, "Disconnected"));
                }
                else
                {
                    tcs.SetResult(IpcResponse.Fail(request.RequestId, "Session not found"));
                }
            }
            catch (Exception ex)
            {
                tcs.SetResult(IpcResponse.Fail(request.RequestId, ex.Message));
            }
        });

        return await tcs.Task;
    }

    /// <summary>
    /// 세션 상태 조회
    /// </summary>
    private Task<IpcResponse> ProcessStatusAsync(IpcRequest request)
    {
        if (string.IsNullOrEmpty(request.SessionId))
            return Task.FromResult(IpcResponse.Fail(request.RequestId, "SessionId is required"));

        var status = McpSessionManager.Instance.GetSessionStatus(request.SessionId);
        if (status == null)
            return Task.FromResult(IpcResponse.Fail(request.RequestId, "Session not found"));

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(status);
        return Task.FromResult(IpcResponse.Ok(request.RequestId, json, request.SessionId));
    }

    /// <summary>
    /// 명령어 실행
    /// </summary>
    private async Task<IpcResponse> ProcessExecuteAsync(IpcRequest request)
    {
        if (string.IsNullOrEmpty(request.SessionId))
            return IpcResponse.Fail(request.RequestId, "SessionId is required");

        if (string.IsNullOrEmpty(request.CommandText))
            return IpcResponse.Fail(request.RequestId, "CommandText is required");

        var result = await McpSessionManager.Instance.ExecuteCommandAsync(
            request.SessionId,
            request.CommandText);

        if (result == null)
            return IpcResponse.Fail(request.RequestId, "Session not found or not connected");

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
        return IpcResponse.Ok(request.RequestId, json, request.SessionId);
    }

    /// <summary>
    /// 모든 세션 목록 조회
    /// </summary>
    private Task<IpcResponse> ProcessGetSessionsAsync(IpcRequest request)
    {
        var sessions = McpSessionManager.Instance.GetAllSessions();
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(sessions);
        return Task.FromResult(IpcResponse.Ok(request.RequestId, json));
    }

    /// <summary>
    /// 서버 통계 조회
    /// </summary>
    private async Task<IpcResponse> ProcessServerStatsAsync(IpcRequest request)
    {
        if (string.IsNullOrEmpty(request.SessionId))
            return IpcResponse.Fail(request.RequestId, "SessionId is required");

        var stats = await McpSessionManager.Instance.GetServerStatsAsync(request.SessionId);
        if (stats == null)
            return IpcResponse.Fail(request.RequestId, "Session not found or not connected");

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(stats);
        return IpcResponse.Ok(request.RequestId, json, request.SessionId);
    }

    /// <summary>
    /// SFTP 파일 목록 조회
    /// </summary>
    private async Task<IpcResponse> ProcessSftpListAsync(IpcRequest request)
    {
        if (string.IsNullOrEmpty(request.SessionId))
            return IpcResponse.Fail(request.RequestId, "SessionId is required");

        var path = request.RemotePath ?? ".";
        var files = await McpSessionManager.Instance.SftpListAsync(request.SessionId, path);
        if (files == null)
            return IpcResponse.Fail(request.RequestId, "Session not found or not connected");

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(files);
        return IpcResponse.Ok(request.RequestId, json, request.SessionId);
    }

    /// <summary>
    /// SFTP 파일 다운로드
    /// </summary>
    private async Task<IpcResponse> ProcessSftpDownloadAsync(IpcRequest request)
    {
        if (string.IsNullOrEmpty(request.SessionId))
            return IpcResponse.Fail(request.RequestId, "SessionId is required");

        if (string.IsNullOrEmpty(request.RemotePath))
            return IpcResponse.Fail(request.RequestId, "RemotePath is required");

        if (string.IsNullOrEmpty(request.LocalPath))
            return IpcResponse.Fail(request.RequestId, "LocalPath is required");

        var result = await McpSessionManager.Instance.SftpDownloadAsync(
            request.SessionId,
            request.RemotePath,
            request.LocalPath);

        if (!result)
            return IpcResponse.Fail(request.RequestId, "Download failed");

        return IpcResponse.Ok(request.RequestId, $"Downloaded: {request.RemotePath} -> {request.LocalPath}");
    }

    /// <summary>
    /// SFTP 파일 업로드
    /// </summary>
    private async Task<IpcResponse> ProcessSftpUploadAsync(IpcRequest request)
    {
        if (string.IsNullOrEmpty(request.SessionId))
            return IpcResponse.Fail(request.RequestId, "SessionId is required");

        if (string.IsNullOrEmpty(request.LocalPath))
            return IpcResponse.Fail(request.RequestId, "LocalPath is required");

        if (string.IsNullOrEmpty(request.RemotePath))
            return IpcResponse.Fail(request.RequestId, "RemotePath is required");

        var result = await McpSessionManager.Instance.SftpUploadAsync(
            request.SessionId,
            request.LocalPath,
            request.RemotePath);

        if (!result)
            return IpcResponse.Fail(request.RequestId, "Upload failed");

        return IpcResponse.Ok(request.RequestId, $"Uploaded: {request.LocalPath} -> {request.RemotePath}");
    }

    public void Dispose()
    {
        Stop();
    }
}
