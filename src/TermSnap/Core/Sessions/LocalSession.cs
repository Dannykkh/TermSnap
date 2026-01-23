using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using TermSnap.Services;

namespace TermSnap.Core.Sessions;

/// <summary>
/// 로컬 터미널 세션 (PowerShell/CMD/WSL/GitBash)
/// ConPTY API를 사용하여 대화형 프로그램 지원
/// </summary>
public class LocalSession : TerminalSessionBase
{
    private IntPtr _hPseudoConsole = IntPtr.Zero;
    private SafeFileHandle? _hPipeIn;
    private SafeFileHandle? _hPipeOut;
    private SafeFileHandle? _hPipeInWrite;
    private SafeFileHandle? _hPipeOutRead;
    private FileStream? _writeStream;  // ConPTY 쓰기 스트림 (재사용)
    private Process? _shellProcess;
    private CancellationTokenSource? _readCancellationTokenSource;
    private Task? _readTask;
    private readonly LocalShellType _shellType;
    private readonly object _processLock = new();

    // 커스텀 쉘 설정
    private readonly string? _customShellPath;
    private readonly string? _customShellArgs;
    private readonly string? _customDisplayName;

    // 명령어 실행 상태
    private StringBuilder _currentOutput = new();
    private DateTime _commandStartTime;
    private string _currentCommand = string.Empty;

    // ANSI 이스케이프 코드 제거용 정규식
    // CSI sequences, OSC sequences, 기타 제어 시퀀스 포함
    private static readonly Regex AnsiRegex = new(
        @"\x1b\[[0-9;?]*[A-Za-z]|\x1b\][^\x07]*\x07|\x1b\][^\x1b]*\x1b\\|\x1b[PX^_][^\x1b]*\x1b\\|\x1b[@-Z\\^_]|\x1b\[[\x30-\x3f]*[\x20-\x2f]*[\x40-\x7e]",
        RegexOptions.Compiled);

    public enum LocalShellType
    {
        PowerShell,
        Cmd,
        WSL,
        GitBash,
        Custom
    }

    public override TerminalType Type => _shellType == LocalShellType.WSL ? TerminalType.WSL : TerminalType.Local;
    public override string DisplayName => _customDisplayName ?? _shellType switch
    {
        LocalShellType.PowerShell => "PowerShell",
        LocalShellType.Cmd => "CMD",
        LocalShellType.WSL => "WSL (Ubuntu)",
        LocalShellType.GitBash => "Git Bash",
        _ => "Local Terminal"
    };
    public override string ShellType => _shellType.ToString().ToLower();

    public LocalSession(LocalShellType shellType = LocalShellType.PowerShell)
    {
        _shellType = shellType;
        CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// 커스텀 쉘 경로로 세션 생성
    /// </summary>
    public LocalSession(string shellPath, string shellArgs, string displayName, LocalShellType shellType = LocalShellType.Custom)
    {
        _shellType = shellType;
        _customShellPath = shellPath;
        _customShellArgs = shellArgs;
        _customDisplayName = displayName;
        CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public override async Task<bool> ConnectAsync()
    {
        if (IsConnected) return true;

        State = ConnectionState.Connecting;

        return await Task.Run(() =>
        {
            try
            {
                // ConPTY 지원 여부 확인 (Windows 10 1809 이상)
                if (IsConPtySupported())
                {
                    try
                    {
                        return ConnectWithConPty();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ConPTY] ConPTY 연결 실패, Process 모드로 폴백: {ex.Message}");
                        RaiseOutputReceived($"[ConPTY 실패, 기본 모드로 전환]", false);
                        CleanupConPty();
                        return ConnectWithProcess();
                    }
                }
                else
                {
                    return ConnectWithProcess();
                }
            }
            catch (Exception ex)
            {
                State = ConnectionState.Error;
                RaiseOutputReceived($"로컬 셸 시작 실패: {ex.Message}", true);
                return false;
            }
        });
    }

    private static bool IsConPtySupported()
    {
        // Windows 10 버전 1809 (빌드 17763) 이상에서 ConPTY 지원
        var version = Environment.OSVersion.Version;
        return version.Major >= 10 && version.Build >= 17763;
    }

    #region ConPTY 방식

    private bool ConnectWithConPty()
    {
        try
        {
            Debug.WriteLine("[ConPTY] Starting ConPTY connection...");

            // 파이프 생성
            if (!CreatePseudoConsolePipes(out _hPipeIn, out _hPipeOut, out _hPipeInWrite, out _hPipeOutRead))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Exception($"파이프 생성 실패: Win32Error={error}");
            }
            Debug.WriteLine("[ConPTY] Pipes created successfully");

            // Pseudo Console 생성 (130x40으로 초기화, 나중에 ResizeTerminal로 동적 조정됨)
            var size = new COORD { X = 130, Y = 40 };
            var hr = CreatePseudoConsole(size, _hPipeIn!.DangerousGetHandle(), _hPipeOut!.DangerousGetHandle(), 0, out _hPseudoConsole);
            if (hr != 0)
            {
                throw new Exception($"Pseudo Console 생성 실패: HRESULT=0x{hr:X8}");
            }
            Debug.WriteLine($"[ConPTY] PseudoConsole created: Handle={_hPseudoConsole}");

            // 프로세스 시작
            var (app, args) = GetShellCommand();
            Debug.WriteLine($"[ConPTY] Starting process: {app} {args}");
            Debug.WriteLine($"[ConPTY] DisplayName: {DisplayName}, CustomPath: {_customShellPath}");
            _shellProcess = StartProcessWithPseudoConsole(app, args, _hPseudoConsole);
            Debug.WriteLine($"[ConPTY] Process started: PID={_shellProcess.Id}");

            // 읽기 파이프에서 입력 핸들 닫기 (PseudoConsole이 사용)
            _hPipeIn.Close();
            _hPipeIn = null;

            // 쓰기 파이프에서 출력 핸들 닫기 (PseudoConsole이 사용)
            _hPipeOut.Close();
            _hPipeOut = null;

            // 쓰기 스트림 생성 (재사용을 위해 클래스 필드에 저장)
            _writeStream = new FileStream(_hPipeInWrite!, FileAccess.Write, 4096, false);
            _hPipeInWrite = null; // 소유권이 FileStream으로 이전됨
            Debug.WriteLine("[ConPTY] Write stream created");

            // 출력 읽기 시작
            _readCancellationTokenSource = new CancellationTokenSource();
            _readTask = ReadOutputAsync(_hPipeOutRead!, _readCancellationTokenSource.Token);
            Debug.WriteLine("[ConPTY] Output reader started");

            State = ConnectionState.Connected;
            RaiseOutputReceived(string.Format(LocalizationService.Instance.GetString("LocalTerminal.ShellStartedBracket"), DisplayName), false);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConPTY] Error: {ex.Message}");
            CleanupConPty();
            throw;
        }
    }

    private static bool CreatePseudoConsolePipes(
        out SafeFileHandle? hPipeIn,
        out SafeFileHandle? hPipeOut,
        out SafeFileHandle? hPipeInWrite,
        out SafeFileHandle? hPipeOutRead)
    {
        hPipeIn = null;
        hPipeOut = null;
        hPipeInWrite = null;
        hPipeOutRead = null;

        var securityAttributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
            lpSecurityDescriptor = IntPtr.Zero
        };

        // Input 파이프 (PTY -> 프로세스)
        if (!CreatePipe(out var hPipeInRead, out var hPipeInWritePtr, ref securityAttributes, 0))
            return false;

        hPipeIn = new SafeFileHandle(hPipeInRead, true);
        hPipeInWrite = new SafeFileHandle(hPipeInWritePtr, true);

        // Output 파이프 (프로세스 -> PTY)
        if (!CreatePipe(out var hPipeOutReadPtr, out var hPipeOutWrite, ref securityAttributes, 0))
        {
            hPipeIn.Close();
            hPipeInWrite.Close();
            return false;
        }

        hPipeOutRead = new SafeFileHandle(hPipeOutReadPtr, true);
        hPipeOut = new SafeFileHandle(hPipeOutWrite, true);

        return true;
    }

    private Process StartProcessWithPseudoConsole(string app, string args, IntPtr hPseudoConsole)
    {
        var startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        // 속성 리스트 크기 가져오기
        IntPtr lpSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);

        startupInfo.lpAttributeList = Marshal.AllocHGlobal(lpSize);

        try
        {
            if (!InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref lpSize))
            {
                throw new Exception("InitializeProcThreadAttributeList 실패");
            }

            // Pseudo Console 핸들 설정
            if (!UpdateProcThreadAttribute(
                startupInfo.lpAttributeList,
                0,
                PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                hPseudoConsole,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw new Exception("UpdateProcThreadAttribute 실패");
            }

            var processInfo = new PROCESS_INFORMATION();
            var commandLine = string.IsNullOrEmpty(args) ? app : $"{app} {args}";

            if (!CreateProcess(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero,
                CurrentDirectory,
                ref startupInfo,
                out processInfo))
            {
                throw new Exception($"CreateProcess 실패: {Marshal.GetLastWin32Error()}");
            }

            // 프로세스 핸들로 Process 객체 생성
            var process = Process.GetProcessById((int)processInfo.dwProcessId);

            // 핸들 정리
            CloseHandle(processInfo.hThread);
            CloseHandle(processInfo.hProcess);

            return process;
        }
        finally
        {
            DeleteProcThreadAttributeList(startupInfo.lpAttributeList);
            Marshal.FreeHGlobal(startupInfo.lpAttributeList);
        }
    }

    private Task ReadOutputAsync(SafeFileHandle hPipeOutRead, CancellationToken cancellationToken)
    {
        // 핸들 소유권을 FileStream에 넘기므로 CleanupConPty에서 중복 종료 방지
        _hPipeOutRead = null;

        // 동기 읽기를 별도 스레드에서 실행 (CreatePipe는 비동기 I/O를 지원하지 않음)
        return Task.Run(() =>
        {
            var buffer = new byte[4096];
            var decoder = Encoding.UTF8.GetDecoder();  // 상태 유지 디코더
            var charBuffer = new char[4096];

            try
            {
                // 동기 모드로 FileStream 생성 (isAsync: false)
                // FileStream이 SafeFileHandle의 소유권을 가짐
                using var stream = new FileStream(hPipeOutRead, FileAccess.Read, buffer.Length, false);
                Debug.WriteLine("[ConPTY] Output reader stream created");

                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead;
                    try
                    {
                        // 동기 읽기 - 데이터가 올 때까지 블로킹
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (IOException ex)
                    {
                        Debug.WriteLine($"[ConPTY] Pipe read IOException: {ex.Message}");
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        Debug.WriteLine("[ConPTY] Pipe disposed");
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        // 파이프가 닫힘
                        Debug.WriteLine("[ConPTY] Pipe closed (0 bytes read)");
                        break;
                    }

                    // UTF-8 디코더를 사용하여 멀티바이트 문자 경계 처리
                    // Decoder는 불완전한 바이트 시퀀스를 내부 버퍼에 보관
                    int charsRead = decoder.GetChars(buffer, 0, bytesRead, charBuffer, 0, false);
                    var rawOutput = new string(charBuffer, 0, charsRead);

                    Debug.WriteLine($"[ConPTY Read] {bytesRead} bytes -> {charsRead} chars, preview: '{rawOutput.Substring(0, Math.Min(100, rawOutput.Length))}'");

                    // ANSI 이스케이프 코드 제거 (WPF TextBlock은 ANSI를 렌더링하지 못함)
                    var cleanOutput = StripAnsiCodes(rawOutput);
                    Debug.WriteLine($"[ConPTY Read] Clean output: '{cleanOutput.Substring(0, Math.Min(100, cleanOutput.Length))}'");

                    // 출력 수집
                    _currentOutput.Append(cleanOutput);

                    // UI에 전달 (rawOutput도 함께 전달하여 터미널 컨트롤에서 사용)
                    Debug.WriteLine($"[ConPTY Read] Raising OutputReceived event, rawOutput length: {rawOutput.Length}");
                    RaiseOutputReceived(cleanOutput, false, rawOutput);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[ConPTY] Read cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConPTY] 읽기 오류: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                Debug.WriteLine("[ConPTY] Output reader stopped");
            }
        }, cancellationToken);
    }

    private void CleanupConPty()
    {
        // 쓰기 스트림 정리
        try
        {
            _writeStream?.Dispose();
        }
        catch { }
        _writeStream = null;

        if (_hPseudoConsole != IntPtr.Zero)
        {
            ClosePseudoConsole(_hPseudoConsole);
            _hPseudoConsole = IntPtr.Zero;
        }

        _hPipeIn?.Close();
        _hPipeOut?.Close();
        _hPipeInWrite?.Close();
        _hPipeOutRead?.Close();

        _hPipeIn = null;
        _hPipeOut = null;
        _hPipeInWrite = null;
        _hPipeOutRead = null;
    }

    /// <summary>
    /// ANSI 이스케이프 코드 제거
    /// </summary>
    private static string StripAnsiCodes(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // 정규식으로 ANSI 코드 제거
        var result = AnsiRegex.Replace(input, "");

        // 추가로 남아있을 수 있는 제어 문자 제거 (Bell, Backspace 제외한 일부)
        // \x07 (Bell), \x08 (Backspace), \x09 (Tab), \x0A (LF), \x0D (CR)은 유지
        var sb = new StringBuilder(result.Length);
        foreach (var c in result)
        {
            // 일반 출력 가능한 문자 또는 허용된 제어 문자만 유지
            if (c >= ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Process 방식 (폴백)

    private bool ConnectWithProcess()
    {
        var (app, args) = GetShellCommand();
        Debug.WriteLine($"[LocalSession] Starting shell: {app} {args}");
        Debug.WriteLine($"[LocalSession] DisplayName: {DisplayName}, CustomPath: {_customShellPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = app,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = CurrentDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["LANG"] = "en_US.UTF-8";

        _shellProcess = new Process { StartInfo = startInfo };
        _shellProcess.EnableRaisingEvents = true;
        _shellProcess.Exited += (s, e) => State = ConnectionState.Disconnected;

        _shellProcess.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _currentOutput.AppendLine(e.Data);
                RaiseOutputReceived(e.Data);
            }
        };

        _shellProcess.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                RaiseOutputReceived(e.Data, true);
            }
        };

        _shellProcess.Start();
        _shellProcess.BeginOutputReadLine();
        _shellProcess.BeginErrorReadLine();

        State = ConnectionState.Connected;
        RaiseOutputReceived(string.Format(LocalizationService.Instance.GetString("LocalTerminal.ShellStartedWithApp"), DisplayName, app), false);
        return true;
    }

    #endregion

    private (string app, string args) GetShellCommand()
    {
        // 커스텀 쉘 경로가 있으면 사용
        if (!string.IsNullOrEmpty(_customShellPath))
        {
            return (_customShellPath, _customShellArgs ?? "");
        }

        return _shellType switch
        {
            LocalShellType.PowerShell => ("powershell.exe", "-NoLogo -NoExit"),
            LocalShellType.Cmd => ("cmd.exe", "/K chcp 65001 >nul"),
            LocalShellType.WSL => ("wsl.exe", "-e bash"),
            LocalShellType.GitBash => (FindGitBashPath() ?? throw new Exception("Git Bash를 찾을 수 없습니다."), "--login -i"),
            _ => throw new ArgumentException($"지원하지 않는 셸 타입: {_shellType}")
        };
    }

    private static string? FindGitBashPath()
    {
        var possiblePaths = new[]
        {
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Git\bin\bash.exe"),
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\Git\bin\bash.exe")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static string RemoveAnsiEscapeCodes(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            input,
            @"\x1B\[[0-9;]*[a-zA-Z]|\x1B\].*?\x07|\x1B\[.*?[@-~]",
            string.Empty);
    }

    public override async Task DisconnectAsync()
    {
        try
        {
            _readCancellationTokenSource?.Cancel();

            if (_readTask != null)
            {
                try { await _readTask.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch { }
            }

            lock (_processLock)
            {
                if (_shellProcess != null && !_shellProcess.HasExited)
                {
                    try { _shellProcess.Kill(true); }
                    catch { }
                }
                _shellProcess?.Dispose();
                _shellProcess = null;
            }

            CleanupConPty();
            State = ConnectionState.Disconnected;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Disconnect error: {ex.Message}");
            State = ConnectionState.Disconnected;
        }
    }

    public override async Task<CommandExecutionResult> ExecuteCommandAsync(string command, int timeoutMs = 0)
    {
        var result = new CommandExecutionResult
        {
            Command = command,
            ExecutedAt = DateTime.Now
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _currentCommand = command;
            _currentOutput.Clear();
            _commandStartTime = DateTime.Now;

            // ConPTY 모드
            if (_hPseudoConsole != IntPtr.Zero && _writeStream != null)
            {
                await WriteToConPtyAsync(command + "\r\n");

                // 출력 대기 (최소 1초, 최대 타임아웃)
                var waitTime = timeoutMs > 0 ? Math.Min(timeoutMs, 2000) : 1000;
                await WaitForOutputAsync(waitTime);

                result.Output = CleanOutput(_currentOutput.ToString(), command);
                result.ExitCode = 0;
            }
            // Process 모드
            else if (_shellProcess?.StandardInput != null)
            {
                await _shellProcess.StandardInput.WriteLineAsync(command);
                await _shellProcess.StandardInput.FlushAsync();

                // 출력 대기 (최소 1초, 출력이 멈출 때까지)
                var waitTime = timeoutMs > 0 ? Math.Min(timeoutMs, 5000) : 2000;
                await WaitForOutputAsync(waitTime);

                result.Output = CleanOutput(_currentOutput.ToString(), command);
                result.ExitCode = 0;
            }
            else
            {
                result.Error = "셸이 연결되지 않았습니다.";
                result.ExitCode = -1;
            }

            result.CurrentDirectory = CurrentDirectory;
            UpdateCurrentDirectoryIfNeeded(command);
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

        return result;
    }

    /// <summary>
    /// 출력이 멈출 때까지 대기 (최대 maxWaitMs)
    /// </summary>
    private async Task WaitForOutputAsync(int maxWaitMs)
    {
        var startLen = _currentOutput.Length;
        var waitedMs = 0;
        var stableCount = 0;

        while (waitedMs < maxWaitMs)
        {
            await Task.Delay(100);
            waitedMs += 100;

            var currentLen = _currentOutput.Length;

            // 출력이 변하지 않으면 카운트 증가
            if (currentLen == startLen)
            {
                stableCount++;
                // 500ms 동안 출력이 없으면 완료로 간주
                if (stableCount >= 5)
                    break;
            }
            else
            {
                startLen = currentLen;
                stableCount = 0;
            }
        }
    }

    private async Task WriteToConPtyAsync(string text)
    {
        if (_writeStream == null) return;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await _writeStream.WriteAsync(bytes, 0, bytes.Length);
            await _writeStream.FlushAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConPTY] 쓰기 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 원시 입력 전송 (인터랙티브 프로그램용)
    /// 출력 대기 없이 즉시 입력을 전송
    /// </summary>
    public async Task SendRawInputAsync(string input)
    {
        Debug.WriteLine($"[SendRawInput] Input: '{input}', ConPTY: {_hPseudoConsole != IntPtr.Zero}, WriteStream: {_writeStream != null}, Process: {_shellProcess != null}");

        if (_hPseudoConsole != IntPtr.Zero && _writeStream != null)
        {
            // ConPTY 모드: 입력 그대로 전송 (줄바꿈 포함)
            await WriteToConPtyAsync(input + "\r\n");
            Debug.WriteLine($"[ConPTY] Raw input sent: {input.Length} chars");
            RaiseOutputReceived($"[DEBUG: ConPTY input sent: {input}]", false);
        }
        else if (_shellProcess?.StandardInput != null)
        {
            // Process 모드
            await _shellProcess.StandardInput.WriteLineAsync(input);
            await _shellProcess.StandardInput.FlushAsync();
            Debug.WriteLine($"[Process] Raw input sent: {input.Length} chars");
            RaiseOutputReceived($"[DEBUG: Process input sent: {input}]", false);
        }
        else
        {
            Debug.WriteLine("[SendRawInput] No valid output stream!");
            RaiseOutputReceived("[DEBUG: No valid output stream!]", true);
        }
    }

    /// <summary>
    /// 키 입력 전송 (특수 키용)
    /// </summary>
    public async Task SendKeyAsync(string key)
    {
        Debug.WriteLine($"[SendKeyAsync] key length: {key.Length}, bytes: {string.Join(",", Encoding.UTF8.GetBytes(key).Select(b => b.ToString("X2")))}");

        if (_hPseudoConsole != IntPtr.Zero && _writeStream != null)
        {
            Debug.WriteLine("[SendKeyAsync] Using ConPTY mode");
            await WriteToConPtyAsync(key);
        }
        else if (_shellProcess?.StandardInput != null)
        {
            Debug.WriteLine("[SendKeyAsync] Using Process mode");
            await _shellProcess.StandardInput.WriteAsync(key);
            await _shellProcess.StandardInput.FlushAsync();
        }
        else
        {
            Debug.WriteLine("[SendKeyAsync] No valid stream available!");
        }
    }

    /// <summary>
    /// 터미널 크기 변경 (ConPTY에 알림)
    /// </summary>
    public void ResizeTerminal(int columns, int rows)
    {
        if (_hPseudoConsole != IntPtr.Zero)
        {
            var size = new COORD { X = (short)columns, Y = (short)rows };
            var hr = ResizePseudoConsole(_hPseudoConsole, size);
            if (hr != 0)
            {
                Debug.WriteLine($"[ConPTY] ResizePseudoConsole failed: HRESULT=0x{hr:X8}");
            }
            else
            {
                Debug.WriteLine($"[ConPTY] ✓ Terminal resized to {columns}x{rows} (cols×rows)");
            }
        }
    }

    /// <summary>
    /// Ctrl+C 전송
    /// </summary>
    public async Task SendCtrlCAsync()
    {
        if (_hPseudoConsole != IntPtr.Zero && _writeStream != null)
        {
            await WriteToConPtyAsync("\x03"); // ETX (Ctrl+C)
        }
        else if (_shellProcess != null)
        {
            // Process 모드에서는 Kill
            try { _shellProcess.Kill(); }
            catch { }
        }
    }

    /// <summary>
    /// 터미널에 직접 출력 (ANSI 코드 포함 가능, 입력 없이 출력만)
    /// </summary>
    public async Task WriteDirectOutputAsync(string text)
    {
        if (_hPseudoConsole != IntPtr.Zero && _writeStream != null)
        {
            await WriteToConPtyAsync(text);
        }
    }

    private static string CleanOutput(string output, string command)
    {
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new StringBuilder();

        foreach (var line in lines)
        {
            // 명령어 에코 제거
            if (line.TrimEnd().EndsWith(command.Trim()))
                continue;

            // 빈 프롬프트 제거
            var trimmed = line.Trim();
            if (trimmed == "PS>" || trimmed == ">" || trimmed == "$")
                continue;

            result.AppendLine(line);
        }

        return result.ToString().Trim();
    }

    private void UpdateCurrentDirectoryIfNeeded(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Set-Location ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("sl ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var pathStart = trimmed.IndexOf(' ') + 1;
                var path = trimmed[pathStart..].Trim().Trim('"', '\'');

                if (Path.IsPathRooted(path))
                    CurrentDirectory = path;
                else
                    CurrentDirectory = Path.GetFullPath(Path.Combine(CurrentDirectory, path));
            }
            catch { }
        }
    }

    public override async Task CancelCurrentCommandAsync()
    {
        await SendCtrlCAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _readCancellationTokenSource?.Cancel();
            _readCancellationTokenSource?.Dispose();

            lock (_processLock)
            {
                if (_shellProcess != null && !_shellProcess.HasExited)
                {
                    try { _shellProcess.Kill(true); }
                    catch { }
                }
                _shellProcess?.Dispose();
                _shellProcess = null;
            }

            CleanupConPty();
        }

        base.Dispose(disposing);
    }

    #region P/Invoke

    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = (IntPtr)0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    #endregion
}
