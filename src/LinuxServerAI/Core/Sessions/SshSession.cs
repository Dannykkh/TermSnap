using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nebula.Models;
using Nebula.Services;
using Renci.SshNet;

namespace Nebula.Core.Sessions;

/// <summary>
/// SSH 원격 터미널 세션
/// </summary>
public class SshSession : TerminalSessionBase
{
    private readonly ServerConfig _config;
    private SshClient? _sshClient;
    private ShellStream? _shellStream;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly object _shellLock = new();

    // 프롬프트 감지용 마커
    private const string PROMPT_MARKER = "###PROMPT_END###";
    private const int DEFAULT_TIMEOUT_MS = 30000;

    public override TerminalType Type => TerminalType.SSH;
    public override string DisplayName => $"{_config.Username}@{_config.Host}";
    public override string ShellType => "bash"; // 기본값, 실제로는 감지 필요

    /// <summary>
    /// 서버 설정
    /// </summary>
    public ServerConfig ServerConfig => _config;

    /// <summary>
    /// SSH 클라이언트 (외부 접근용 - 모니터링 등)
    /// </summary>
    public SshClient? SshClient => _sshClient;

    /// <summary>
    /// ShellStream 활성 여부
    /// </summary>
    public bool HasActiveShellStream => _shellStream != null;

    public SshSession(ServerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public override async Task<bool> ConnectAsync()
    {
        if (IsConnected) return true;

        State = ConnectionState.Connecting;

        return await Task.Run(() =>
        {
            try
            {
                ConnectionInfo connectionInfo = CreateConnectionInfo();

                _sshClient = new SshClient(connectionInfo);
                _sshClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
                _sshClient.Connect();

                if (!_sshClient.IsConnected)
                {
                    State = ConnectionState.Error;
                    return false;
                }

                // ShellStream 초기화
                InitializeShellStream();

                State = ConnectionState.Connected;
                return true;
            }
            catch (Exception ex)
            {
                State = ConnectionState.Error;
                RaiseOutputReceived($"연결 실패: {ex.Message}", true);
                return false;
            }
        });
    }

    private ConnectionInfo CreateConnectionInfo()
    {
        if (_config.AuthType == AuthenticationType.Password)
        {
            string password = string.Empty;
            if (!string.IsNullOrEmpty(_config.EncryptedPassword))
            {
                try
                {
                    password = EncryptionService.Decrypt(_config.EncryptedPassword);
                }
                catch
                {
                    throw new Exception("저장된 비밀번호를 복호화할 수 없습니다.");
                }
            }

            return new ConnectionInfo(
                _config.Host,
                _config.Port,
                _config.Username,
                new PasswordAuthenticationMethod(_config.Username, password)
            );
        }
        else
        {
            PrivateKeyFile keyFile;

            if (!string.IsNullOrEmpty(_config.EncryptedPassphrase))
            {
                string passphrase = EncryptionService.Decrypt(_config.EncryptedPassphrase);
                keyFile = new PrivateKeyFile(_config.PrivateKeyPath, passphrase);
            }
            else
            {
                keyFile = new PrivateKeyFile(_config.PrivateKeyPath);
            }

            return new ConnectionInfo(
                _config.Host,
                _config.Port,
                _config.Username,
                new PrivateKeyAuthenticationMethod(_config.Username, keyFile)
            );
        }
    }

    private void InitializeShellStream()
    {
        if (_sshClient == null || !_sshClient.IsConnected) return;

        _shellStream = _sshClient.CreateShellStream(
            "xterm-256color",
            120, 30,
            800, 600,
            1024);

        // 초기화 대기
        Thread.Sleep(500);

        // 현재 디렉토리 가져오기
        UpdateCurrentDirectory();
    }

    private void UpdateCurrentDirectory()
    {
        try
        {
            if (_sshClient?.IsConnected == true)
            {
                using var cmd = _sshClient.CreateCommand("pwd");
                cmd.Execute();
                if (cmd.ExitStatus == 0)
                {
                    CurrentDirectory = cmd.Result.Trim();
                }
            }
        }
        catch
        {
            CurrentDirectory = "~";
        }
    }

    public override async Task DisconnectAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _cancellationTokenSource?.Cancel();

                lock (_shellLock)
                {
                    _shellStream?.Dispose();
                    _shellStream = null;
                }

                _sshClient?.Disconnect();
                _sshClient?.Dispose();
                _sshClient = null;

                State = ConnectionState.Disconnected;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disconnect error: {ex.Message}");
                State = ConnectionState.Disconnected;
            }
        });
    }

    public override async Task<CommandExecutionResult> ExecuteCommandAsync(string command, int timeoutMs = DEFAULT_TIMEOUT_MS)
    {
        var result = new CommandExecutionResult
        {
            Command = command,
            ExecutedAt = DateTime.Now
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (_sshClient == null || !_sshClient.IsConnected)
            {
                result.Error = "SSH 연결이 없습니다.";
                result.ExitCode = -1;
                return result;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            // ShellStream 모드 사용
            if (_shellStream != null)
            {
                result = await ExecuteWithShellStream(command, timeoutMs);
            }
            else
            {
                // 단일 명령 모드
                result = await ExecuteWithSshCommand(command, timeoutMs);
            }

            // 현재 디렉토리 업데이트
            if (result.IsSuccess)
            {
                UpdateCurrentDirectory();
                result.CurrentDirectory = CurrentDirectory;
            }
        }
        catch (OperationCanceledException)
        {
            result.Error = "명령어가 취소되었습니다.";
            result.ExitCode = -1;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.ExitCode = -1;
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        return result;
    }

    private async Task<CommandExecutionResult> ExecuteWithSshCommand(string command, int timeoutMs)
    {
        var result = new CommandExecutionResult { Command = command, ExecutedAt = DateTime.Now };

        await Task.Run(() =>
        {
            using var cmd = _sshClient!.CreateCommand(command);
            cmd.CommandTimeout = TimeSpan.FromMilliseconds(timeoutMs);

            var output = cmd.Execute();
            result.Output = output;
            result.Error = cmd.Error;
            result.ExitCode = cmd.ExitStatus ?? 0;
        });

        return result;
    }

    private async Task<CommandExecutionResult> ExecuteWithShellStream(string command, int timeoutMs)
    {
        var result = new CommandExecutionResult { Command = command, ExecutedAt = DateTime.Now };

        await Task.Run(() =>
        {
            lock (_shellLock)
            {
                if (_shellStream == null) return;

                // 이전 출력 클리어
                while (_shellStream.DataAvailable)
                {
                    _shellStream.Read(new byte[1024], 0, 1024);
                }

                // 마커와 함께 명령어 실행
                var markedCommand = $"{command}; echo '{PROMPT_MARKER}'$?";
                _shellStream.WriteLine(markedCommand);

                var output = new StringBuilder();
                var buffer = new byte[4096];
                var startTime = DateTime.Now;
                var foundMarker = false;

                while (!foundMarker && (DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    if (_cancellationTokenSource?.IsCancellationRequested == true)
                    {
                        throw new OperationCanceledException();
                    }

                    if (_shellStream.DataAvailable)
                    {
                        var bytesRead = _shellStream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            output.Append(text);

                            // 마커 확인
                            if (output.ToString().Contains(PROMPT_MARKER))
                            {
                                foundMarker = true;
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(50);
                    }
                }

                if (!foundMarker)
                {
                    result.IsTimeout = true;
                    result.Error = "명령어 실행 시간 초과";
                    result.ExitCode = -1;
                    return;
                }

                // 출력 파싱
                var fullOutput = output.ToString();
                var markerMatch = Regex.Match(fullOutput, $@"{Regex.Escape(PROMPT_MARKER)}(\d+)");

                if (markerMatch.Success)
                {
                    if (int.TryParse(markerMatch.Groups[1].Value, out var exitCode))
                    {
                        result.ExitCode = exitCode;
                    }

                    // 마커 전까지의 출력 추출
                    var markerIndex = fullOutput.IndexOf(PROMPT_MARKER);
                    var cleanOutput = fullOutput[..markerIndex];

                    // 첫 줄 (에코된 명령어) 제거
                    var lines = cleanOutput.Split('\n');
                    if (lines.Length > 1)
                    {
                        result.Output = string.Join('\n', lines[1..]).Trim();
                    }
                    else
                    {
                        result.Output = cleanOutput.Trim();
                    }
                }
                else
                {
                    result.Output = fullOutput;
                    result.ExitCode = 0;
                }
            }
        });

        return result;
    }

    public override async Task CancelCurrentCommandAsync()
    {
        _cancellationTokenSource?.Cancel();

        // Ctrl+C 시그널 전송
        await Task.Run(() =>
        {
            lock (_shellLock)
            {
                _shellStream?.Write("\x03"); // Ctrl+C
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            lock (_shellLock)
            {
                _shellStream?.Dispose();
                _shellStream = null;
            }

            _sshClient?.Dispose();
            _sshClient = null;
        }

        base.Dispose(disposing);
    }
}
