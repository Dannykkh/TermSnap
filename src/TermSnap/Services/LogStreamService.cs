using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;
using Renci.SshNet;

namespace TermSnap.Services;

/// <summary>
/// 실시간 로그 스트리밍 서비스
/// tail -f 명령어를 사용하여 로그 파일을 실시간으로 모니터링
/// </summary>
public class LogStreamService : IDisposable
{
    private readonly ServerConfig _config;
    private SshClient? _sshClient;
    private ShellStream? _shellStream;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _readTask;
    private bool _isStreaming = false;
    private bool _disposed = false;

    /// <summary>
    /// 새 로그 라인 수신 이벤트
    /// </summary>
    public event EventHandler<LogLineEventArgs>? LogLineReceived;

    /// <summary>
    /// 스트리밍 상태 변경 이벤트
    /// </summary>
    public event EventHandler<bool>? StreamingStateChanged;

    /// <summary>
    /// 오류 발생 이벤트
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// 스트리밍 중 여부
    /// </summary>
    public bool IsStreaming => _isStreaming;

    /// <summary>
    /// 연결 상태
    /// </summary>
    public bool IsConnected => _sshClient?.IsConnected ?? false;

    /// <summary>
    /// 기본 로그 파일 목록
    /// </summary>
    public static readonly string[] DefaultLogFiles = new[]
    {
        "/var/log/syslog",
        "/var/log/messages",
        "/var/log/auth.log",
        "/var/log/secure",
        "/var/log/kern.log",
        "/var/log/dmesg",
        "/var/log/nginx/access.log",
        "/var/log/nginx/error.log",
        "/var/log/apache2/access.log",
        "/var/log/apache2/error.log",
        "/var/log/mysql/error.log",
        "/var/log/postgresql/postgresql-main.log",
        "journalctl -f"  // systemd journal
    };

    public LogStreamService(ServerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// SSH 연결
    /// </summary>
    public async Task ConnectAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                ConnectionInfo connectionInfo;

                if (!string.IsNullOrEmpty(_config.PrivateKeyPath) && System.IO.File.Exists(_config.PrivateKeyPath))
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

                    var keyAuth = new PrivateKeyAuthenticationMethod(_config.Username, keyFile);
                    connectionInfo = new ConnectionInfo(_config.Host, _config.Port, _config.Username, keyAuth);
                }
                else
                {
                    string password = string.Empty;
                    if (!string.IsNullOrEmpty(_config.EncryptedPassword))
                    {
                        password = EncryptionService.Decrypt(_config.EncryptedPassword);
                    }

                    var passAuth = new PasswordAuthenticationMethod(_config.Username, password);
                    connectionInfo = new ConnectionInfo(_config.Host, _config.Port, _config.Username, passAuth);
                }

                _sshClient = new SshClient(connectionInfo);
                _sshClient.Connect();
            }
            catch (Exception ex)
            {
                throw new Exception($"SSH 연결 실패: {ex.Message}", ex);
            }
        });
    }

    /// <summary>
    /// 로그 스트리밍 시작
    /// </summary>
    /// <param name="logPath">로그 파일 경로 또는 journalctl 명령어</param>
    /// <param name="tailLines">초기 표시할 마지막 N줄 (기본: 100)</param>
    public async Task StartStreamingAsync(string logPath, int tailLines = 100)
    {
        if (_isStreaming)
        {
            await StopStreamingAsync();
        }

        if (_sshClient == null || !_sshClient.IsConnected)
        {
            throw new InvalidOperationException("SSH가 연결되지 않았습니다.");
        }

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();

            // ShellStream 생성
            _shellStream = _sshClient.CreateShellStream("xterm-256color", 200, 50, 800, 600, 4096);

            // 프롬프트 대기
            await Task.Delay(500);
            _shellStream.ReadLine(); // 초기 출력 클리어

            // 로그 모니터링 명령어 실행
            string command;
            if (logPath.StartsWith("journalctl"))
            {
                // journalctl 명령어인 경우
                command = $"{logPath} -n {tailLines}";
            }
            else
            {
                // 일반 파일인 경우 tail -f 사용
                command = $"tail -n {tailLines} -f \"{logPath}\"";
            }

            _shellStream.WriteLine(command);

            _isStreaming = true;
            StreamingStateChanged?.Invoke(this, true);

            // 비동기 읽기 시작
            _readTask = ReadStreamAsync(_cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            _isStreaming = false;
            StreamingStateChanged?.Invoke(this, false);
            ErrorOccurred?.Invoke(this, $"스트리밍 시작 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 스트림에서 로그 읽기
    /// </summary>
    private async Task ReadStreamAsync(CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _shellStream != null)
            {
                if (_shellStream.DataAvailable)
                {
                    var data = _shellStream.Read();
                    buffer.Append(data);

                    // 줄 단위로 분리
                    var content = buffer.ToString();
                    var lines = content.Split('\n');

                    // 마지막 줄은 아직 완성되지 않았을 수 있으므로 버퍼에 유지
                    buffer.Clear();
                    if (!content.EndsWith('\n') && lines.Length > 0)
                    {
                        buffer.Append(lines[lines.Length - 1]);
                        lines = lines.Take(lines.Length - 1).ToArray();
                    }

                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var cleanLine = CleanAnsiCodes(line.TrimEnd('\r'));
                            if (!string.IsNullOrWhiteSpace(cleanLine))
                            {
                                LogLineReceived?.Invoke(this, new LogLineEventArgs
                                {
                                    Line = cleanLine,
                                    Timestamp = DateTime.Now,
                                    Level = DetectLogLevel(cleanLine)
                                });
                            }
                        }
                    }
                }
                else
                {
                    await Task.Delay(50, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정상적인 취소
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"로그 읽기 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// ANSI 코드 제거
    /// </summary>
    private static string CleanAnsiCodes(string input)
    {
        // ANSI escape 코드 제거
        return System.Text.RegularExpressions.Regex.Replace(input, @"\x1B\[[0-9;]*[a-zA-Z]", "");
    }

    /// <summary>
    /// 로그 레벨 감지
    /// </summary>
    private static LogLevel DetectLogLevel(string line)
    {
        var lowerLine = line.ToLowerInvariant();

        if (lowerLine.Contains("error") || lowerLine.Contains("fail") || lowerLine.Contains("fatal"))
            return LogLevel.Error;
        if (lowerLine.Contains("warn"))
            return LogLevel.Warning;
        if (lowerLine.Contains("debug"))
            return LogLevel.Debug;
        if (lowerLine.Contains("info"))
            return LogLevel.Info;

        return LogLevel.Default;
    }

    /// <summary>
    /// 로그 스트리밍 중지
    /// </summary>
    public async Task StopStreamingAsync()
    {
        if (!_isStreaming)
            return;

        try
        {
            // Ctrl+C 전송하여 tail 종료
            if (_shellStream != null && _sshClient?.IsConnected == true)
            {
                _shellStream.Write("\x03"); // Ctrl+C
                await Task.Delay(200);
            }

            // 취소 요청
            _cancellationTokenSource?.Cancel();

            // 읽기 태스크 완료 대기
            if (_readTask != null)
            {
                try
                {
                    await Task.WhenAny(_readTask, Task.Delay(1000));
                }
                catch { }
            }

            // ShellStream 정리
            _shellStream?.Dispose();
            _shellStream = null;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
        finally
        {
            _isStreaming = false;
            StreamingStateChanged?.Invoke(this, false);
        }
    }

    /// <summary>
    /// 연결 해제
    /// </summary>
    public void Disconnect()
    {
        try
        {
            // 타임아웃을 설정하여 데드락 방지
            var stopTask = StopStreamingAsync();
            if (!stopTask.Wait(TimeSpan.FromSeconds(5)))
            {
                System.Diagnostics.Debug.WriteLine("[LogStreamService] StopStreaming 타임아웃");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LogStreamService] Disconnect 중 오류: {ex.Message}");
        }

        try
        {
            if (_sshClient?.IsConnected == true)
            {
                _sshClient.Disconnect();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LogStreamService] SSH Disconnect 중 오류: {ex.Message}");
        }
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
            // 관리 리소스 정리
            try
            {
                // 이벤트 구독 해제
                LogLineReceived = null;
                StreamingStateChanged = null;
                ErrorOccurred = null;

                // 연결 종료
                Disconnect();

                // 리소스 정리
                _shellStream?.Dispose();
                _shellStream = null;

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                _sshClient?.Dispose();
                _sshClient = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogStreamService] Dispose 중 오류: {ex.Message}");
            }
        }

        _disposed = true;
    }

    ~LogStreamService()
    {
        Dispose(false);
    }
}

/// <summary>
/// 로그 라인 이벤트 인수
/// </summary>
public class LogLineEventArgs : EventArgs
{
    public string Line { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
}

/// <summary>
/// 로그 레벨
/// </summary>
public enum LogLevel
{
    Default,
    Debug,
    Info,
    Warning,
    Error
}
