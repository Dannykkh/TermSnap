using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TermSnap.Models;
using Renci.SshNet;
using SshNet.PuttyKeyFile;

namespace TermSnap.Services;

/// <summary>
/// SSH 연결 및 명령어 실행 서비스
/// </summary>
public class SshService : IDisposable
{
    private SshClient? _sshClient;
    private ShellStream? _shellStream;
    private readonly ServerConfig _config;
    private bool _isConnected;
    private bool _isShellInitialized;
    private string _currentDirectory = "~";
    private readonly object _shellLock = new();

    // 프롬프트 감지용 마커
    private const string PROMPT_MARKER = "###PROMPT_END###";
    private const string COMMAND_START_MARKER = "###CMD_START###";

    public bool IsConnected => _isConnected && _sshClient?.IsConnected == true;
    public bool IsShellInitialized => _isShellInitialized;
    public string CurrentDirectory => _currentDirectory;

    /// <summary>
    /// ShellStream 출력 이벤트
    /// </summary>
    public event EventHandler<ShellOutputEventArgs>? OutputReceived;

    public SshService(ServerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// SSH 서버에 연결
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                if (_sshClient != null && _sshClient.IsConnected)
                {
                    return true;
                }

                ConnectionInfo connectionInfo;

                if (_config.AuthType == AuthenticationType.Password)
                {
                    // 암호화된 비밀번호 복호화
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

                    connectionInfo = new ConnectionInfo(
                        _config.Host,
                        _config.Port,
                        _config.Username,
                        new PasswordAuthenticationMethod(_config.Username, password)
                    );
                }
                else
                {
                    // SSH 키 파일 로드 (passphrase 지원)
                    IPrivateKeySource keyFile;
                    
                    // Passphrase 복호화 (있는 경우)
                    string? passphrase = null;
                    if (!string.IsNullOrEmpty(_config.EncryptedPassphrase))
                    {
                        try
                        {
                            passphrase = EncryptionService.Decrypt(_config.EncryptedPassphrase);
                        }
                        catch
                        {
                            throw new Exception("저장된 passphrase를 복호화할 수 없습니다.");
                        }
                    }

                    // .ppk (PuTTY) 형식인지 확인
                    bool isPpkFile = Path.GetExtension(_config.PrivateKeyPath)
                        .Equals(".ppk", StringComparison.OrdinalIgnoreCase);

                    if (isPpkFile)
                    {
                        // PuTTY .ppk 파일 로드 (SshNet.PuttyKeyFile 사용)
                        if (!string.IsNullOrEmpty(passphrase))
                        {
                            keyFile = new PuttyKeyFile(_config.PrivateKeyPath, passphrase);
                        }
                        else
                        {
                            keyFile = new PuttyKeyFile(_config.PrivateKeyPath);
                        }
                    }
                    else
                    {
                        // OpenSSH 형식 (.pem, .key 등)
                        if (!string.IsNullOrEmpty(passphrase))
                        {
                            keyFile = new PrivateKeyFile(_config.PrivateKeyPath, passphrase);
                        }
                        else
                        {
                            keyFile = new PrivateKeyFile(_config.PrivateKeyPath);
                        }
                    }

                    connectionInfo = new ConnectionInfo(
                        _config.Host,
                        _config.Port,
                        _config.Username,
                        new PrivateKeyAuthenticationMethod(_config.Username, keyFile)
                    );
                }

                _sshClient = new SshClient(connectionInfo);
                _sshClient.Connect();
                _isConnected = true;

                // 연결 시간 업데이트
                _config.LastConnected = DateTime.Now;

                return true;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                throw new Exception($"SSH 연결 실패: {ex.Message}", ex);
            }
        });
    }

    /// <summary>
    /// 명령어 실행 (현재 디렉토리 컨텍스트 유지)
    /// </summary>
    public async Task<CommandResult> ExecuteCommandAsync(string command)
    {
        return await Task.Run(() =>
        {
            var result = new CommandResult { Command = command };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (!IsConnected)
                {
                    throw new InvalidOperationException("SSH 연결이 되어있지 않습니다.");
                }

                if (_sshClient == null)
                {
                    throw new InvalidOperationException("SSH 클라이언트가 초기화되지 않았습니다.");
                }

                // cd 명령어 처리
                var trimmedCommand = command.Trim();
                if (trimmedCommand.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) ||
                    trimmedCommand.Equals("cd", StringComparison.OrdinalIgnoreCase))
                {
                    // cd 명령어: 디렉토리 변경 후 pwd로 현재 위치 확인
                    var cdTarget = trimmedCommand.Length > 3 ? trimmedCommand[3..].Trim() : "~";
                    var cdCommand = $"cd {_currentDirectory} && cd {cdTarget} && pwd";

                    using var cdCmd = _sshClient.CreateCommand(cdCommand);
                    cdCmd.Execute();

                    if (cdCmd.ExitStatus == 0 && !string.IsNullOrEmpty(cdCmd.Result))
                    {
                        _currentDirectory = cdCmd.Result.Trim();
                        result.Output = $"디렉토리 변경: {_currentDirectory}";
                        result.ExitCode = 0;
                    }
                    else
                    {
                        result.Error = cdCmd.Error ?? "디렉토리 변경 실패";
                        result.ExitCode = cdCmd.ExitStatus ?? -1;
                    }
                }
                else
                {
                    // 일반 명령어: 현재 디렉토리에서 실행
                    var fullCommand = _currentDirectory != "~"
                        ? $"cd {_currentDirectory} && {command}"
                        : command;

                    using var cmd = _sshClient.CreateCommand(fullCommand);
                    cmd.Execute();

                    result.Output = cmd.Result;
                    result.Error = cmd.Error;
                    result.ExitCode = cmd.ExitStatus ?? 0;
                }
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
            }

            result.CurrentDirectory = _currentDirectory;
            return result;
        });
    }

    /// <summary>
    /// 연결 테스트
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await ConnectAsync();
            var result = await ExecuteCommandAsync("echo 'test'");
            return result.IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    #region ShellStream 기반 세션 유지 기능

    /// <summary>
    /// ShellStream 초기화 (세션 상태 유지용)
    /// </summary>
    public async Task<bool> InitializeShellStreamAsync()
    {
        if (!IsConnected || _sshClient == null)
        {
            throw new InvalidOperationException("SSH 연결이 되어있지 않습니다.");
        }

        return await Task.Run(() =>
        {
            try
            {
                lock (_shellLock)
                {
                    // 기존 ShellStream이 있으면 정리
                    if (_shellStream != null)
                    {
                        _shellStream.Dispose();
                        _shellStream = null;
                    }

                    // ShellStream 생성
                    _shellStream = _sshClient.CreateShellStream(
                        "xterm-256color",  // 터미널 타입
                        120, 30,           // 컬럼, 행
                        800, 600,          // 픽셀 (사용 안함)
                        1024               // 버퍼 크기
                    );

                    // 초기 프롬프트 대기
                    Thread.Sleep(500);
                    ClearBuffer();

                    // 커스텀 프롬프트 설정 (프롬프트 감지를 위해)
                    _shellStream.WriteLine($"export PS1='{PROMPT_MARKER}'");
                    Thread.Sleep(200);
                    ClearBuffer();

                    // 현재 디렉토리 가져오기
                    UpdateCurrentDirectory();

                    _isShellInitialized = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _isShellInitialized = false;
                throw new Exception($"ShellStream 초기화 실패: {ex.Message}", ex);
            }
        });
    }

    /// <summary>
    /// ShellStream을 통한 명령어 실행 (세션 상태 유지)
    /// </summary>
    public async Task<ShellCommandResult> ExecuteShellCommandAsync(string command, int timeoutMs = 30000)
    {
        if (!IsConnected || _shellStream == null || !_isShellInitialized)
        {
            throw new InvalidOperationException("ShellStream이 초기화되지 않았습니다.");
        }

        return await Task.Run(() =>
        {
            var result = new ShellCommandResult { Command = command };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                lock (_shellLock)
                {
                    // 버퍼 비우기
                    ClearBuffer();

                    // 명령어 전송 (마커로 감싸서 출력 구분)
                    // TERM=dumb로 컬러 출력 비활성화 (pm2, htop 등 테이블 출력 프로그램용)
                    _shellStream.WriteLine($"echo '{COMMAND_START_MARKER}'; TERM=dumb {command}; echo '{PROMPT_MARKER}'");

                    // 출력 읽기 (프롬프트 마커까지)
                    var output = ReadUntilMarker(PROMPT_MARKER, timeoutMs);

                    // 출력 정리 (마커 및 에코 제거)
                    output = CleanOutput(output, command);

                    result.Output = output;
                    result.IsSuccess = true;

                    // cd 명령어 감지 시 현재 디렉토리 업데이트
                    if (command.TrimStart().StartsWith("cd ", StringComparison.OrdinalIgnoreCase) ||
                        command.TrimStart().Equals("cd", StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateCurrentDirectory();
                    }

                    result.CurrentDirectory = _currentDirectory;
                }
            }
            catch (TimeoutException)
            {
                result.IsSuccess = false;
                result.IsTimeout = true;
                result.Error = $"명령어 실행 타임아웃 ({timeoutMs}ms)";
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Error = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
            }

            return result;
        });
    }

    /// <summary>
    /// 프롬프트 마커까지 출력 읽기
    /// </summary>
    private string ReadUntilMarker(string marker, int timeoutMs)
    {
        if (_shellStream == null)
            throw new InvalidOperationException("ShellStream이 null입니다.");

        var output = new StringBuilder();
        var startTime = DateTime.Now;

        while (true)
        {
            // 타임아웃 체크
            if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException();
            }

            // 데이터가 있으면 읽기
            if (_shellStream.DataAvailable)
            {
                var buffer = new byte[4096];
                var bytesRead = _shellStream.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    output.Append(text);

                    // 출력 이벤트 발생
                    OutputReceived?.Invoke(this, new ShellOutputEventArgs(text));

                    // 마커 감지
                    if (output.ToString().Contains(marker))
                    {
                        break;
                    }
                }
            }
            else
            {
                Thread.Sleep(50);
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// 출력 정리 (마커 및 에코 제거)
    /// </summary>
    private string CleanOutput(string output, string command)
    {
        // 디버그: raw 출력 로깅
        System.Diagnostics.Debug.WriteLine($"=== RAW OUTPUT START ===\n{output}\n=== RAW OUTPUT END ===");
        try
        {
            var debugPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TermSnap", "debug_output.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(debugPath)!);
            System.IO.File.WriteAllText(debugPath, $"Command: {command}\n\nRaw Output:\n{output}");
        }
        catch { }

        // ANSI 이스케이프 코드 제거 (포괄적 패턴)
        // CSI 시퀀스: ESC[ ... 문자
        output = Regex.Replace(output, @"\x1B\[[0-9;?]*[a-zA-Z]", "");
        // OSC 시퀀스: ESC] ... BEL 또는 ST
        output = Regex.Replace(output, @"\x1B\][^\x07]*\x07", "");
        output = Regex.Replace(output, @"\x1B\][^\x1B]*\x1B\\", "");
        // 기타 ESC 시퀀스
        output = Regex.Replace(output, @"\x1B[()][AB012]", "");
        output = Regex.Replace(output, @"\x1B[=>]", "");
        // 남은 ESC 문자 제거
        output = Regex.Replace(output, @"\x1B", "");
        // 잘린 CSI 시퀀스 제거 (ESC 없이 [ 로 시작하는 경우)
        output = Regex.Replace(output, @"^\[\?[0-9;]*[a-zA-Z]", "", RegexOptions.Multiline);

        // 라인별로 분리
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanLines = new List<string>();

        // 마커 존재 여부 확인
        bool hasStartMarker = output.Contains(COMMAND_START_MARKER);
        bool hasEndMarker = output.Contains(PROMPT_MARKER);
        var startCapture = !hasStartMarker; // 마커가 없으면 처음부터 캡처

        foreach (var line in lines)
        {
            // 시작 마커 이후부터 캡처
            if (line.Contains(COMMAND_START_MARKER))
            {
                startCapture = true;
                continue;
            }

            // 종료 마커에서 중단
            if (line.Contains(PROMPT_MARKER))
            {
                break;
            }

            // 명령어 에코 라인 스킵 (명령어 자체 또는 echo 명령 포함 라인)
            var trimmedLine = line.TrimEnd();
            if (trimmedLine == command.TrimEnd() ||
                trimmedLine.Contains($"echo '{COMMAND_START_MARKER}'") ||
                trimmedLine.Contains($"echo '{PROMPT_MARKER}'"))
            {
                continue;
            }

            if (startCapture)
            {
                cleanLines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, cleanLines);
    }

    /// <summary>
    /// 버퍼 비우기
    /// </summary>
    private void ClearBuffer()
    {
        if (_shellStream == null) return;

        while (_shellStream.DataAvailable)
        {
            var buffer = new byte[4096];
            _shellStream.Read(buffer, 0, buffer.Length);
        }
    }

    /// <summary>
    /// 현재 디렉토리 업데이트
    /// </summary>
    private void UpdateCurrentDirectory()
    {
        if (_shellStream == null) return;

        try
        {
            ClearBuffer();
            _shellStream.WriteLine($"pwd; echo '{PROMPT_MARKER}'");

            var output = ReadUntilMarker(PROMPT_MARKER, 5000);

            // pwd 결과 추출
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("/") && !trimmed.Contains(PROMPT_MARKER))
                {
                    _currentDirectory = trimmed;
                    break;
                }
            }
        }
        catch
        {
            // 실패해도 무시
        }
    }

    /// <summary>
    /// ShellStream 활성 여부
    /// </summary>
    public bool HasActiveShellStream => _shellStream != null && _isShellInitialized;

    /// <summary>
    /// ShellStream 인스턴스 반환 (로그 스트리밍 등 외부 사용용)
    /// </summary>
    public ShellStream? GetShellStream() => _shellStream;

    #endregion

    /// <summary>
    /// 연결 해제
    /// </summary>
    public void Disconnect()
    {
        // ShellStream 정리
        if (_shellStream != null)
        {
            try
            {
                _shellStream.WriteLine("exit");
            }
            catch { }

            _shellStream.Dispose();
            _shellStream = null;
            _isShellInitialized = false;
        }

        if (_sshClient != null && _sshClient.IsConnected)
        {
            _sshClient.Disconnect();
            _isConnected = false;
        }

        _currentDirectory = "~";
    }

    public void Dispose()
    {
        Disconnect();
        _sshClient?.Dispose();
    }
}
