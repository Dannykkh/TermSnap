using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nebula.Models;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using SshNet.PuttyKeyFile;

namespace Nebula.Services;

/// <summary>
/// SFTP 파일 전송 서비스
/// </summary>
public class SftpService : IDisposable
{
    private readonly ServerConfig _config;
    private SftpClient? _sftpClient;
    private bool _isConnected = false;

    public bool IsConnected => _isConnected && _sftpClient != null && _sftpClient.IsConnected;

    public event EventHandler<FileTransferProgressEventArgs>? TransferProgress;

    /// <summary>
    /// 내부 SftpClient 반환 (FileTreeService용)
    /// </summary>
    public SftpClient? GetSftpClient() => _sftpClient;

    public SftpService(ServerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// SFTP 연결
    /// </summary>
    public async Task ConnectAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                ConnectionInfo connectionInfo;

                // 인증 방식 선택
                if (!string.IsNullOrEmpty(_config.PrivateKeyPath) && File.Exists(_config.PrivateKeyPath))
                {
                    // SSH 키 인증
                    IPrivateKeySource keyFile;
                    
                    // Passphrase 복호화 (있는 경우)
                    string? passphrase = null;
                    if (!string.IsNullOrEmpty(_config.EncryptedPassphrase))
                    {
                        passphrase = EncryptionService.Decrypt(_config.EncryptedPassphrase);
                    }

                    // .ppk (PuTTY) 형식인지 확인
                    bool isPpkFile = Path.GetExtension(_config.PrivateKeyPath)
                        .Equals(".ppk", StringComparison.OrdinalIgnoreCase);

                    if (isPpkFile)
                    {
                        // PuTTY .ppk 파일 로드
                        keyFile = !string.IsNullOrEmpty(passphrase)
                            ? new PuttyKeyFile(_config.PrivateKeyPath, passphrase)
                            : new PuttyKeyFile(_config.PrivateKeyPath);
                    }
                    else
                    {
                        // OpenSSH 형식 (.pem, .key 등)
                        keyFile = !string.IsNullOrEmpty(passphrase)
                            ? new PrivateKeyFile(_config.PrivateKeyPath, passphrase)
                            : new PrivateKeyFile(_config.PrivateKeyPath);
                    }

                    var keyAuth = new PrivateKeyAuthenticationMethod(_config.Username, keyFile);
                    connectionInfo = new ConnectionInfo(_config.Host, _config.Port, _config.Username, keyAuth);
                }
                else
                {
                    // 비밀번호 인증
                    string password = string.Empty;
                    if (!string.IsNullOrEmpty(_config.EncryptedPassword))
                    {
                        password = EncryptionService.Decrypt(_config.EncryptedPassword);
                    }

                    var passAuth = new PasswordAuthenticationMethod(_config.Username, password);
                    connectionInfo = new ConnectionInfo(_config.Host, _config.Port, _config.Username, passAuth);
                }

                _sftpClient = new SftpClient(connectionInfo);
                _sftpClient.Connect();
                _isConnected = true;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                throw new Exception($"SFTP 연결 실패: {ex.Message}", ex);
            }
        });
    }

    /// <summary>
    /// SFTP 연결 해제
    /// </summary>
    public void Disconnect()
    {
        if (_sftpClient != null && _sftpClient.IsConnected)
        {
            _sftpClient.Disconnect();
        }
        _isConnected = false;
    }

    /// <summary>
    /// 디렉토리 목록 조회
    /// </summary>
    public async Task<List<RemoteFileInfo>> ListDirectoryAsync(string remotePath)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SFTP가 연결되지 않았습니다.");

        return await Task.Run(() =>
        {
            var files = _sftpClient!.ListDirectory(remotePath)
                .Where(f => f.Name != "." && f.Name != "..")
                .Select(f => new RemoteFileInfo
                {
                    Name = f.Name,
                    FullPath = f.FullName,
                    IsDirectory = f.IsDirectory,
                    Size = f.Length,
                    LastModified = f.LastWriteTime,
                    Permissions = null // TODO: SSH.NET doesn't expose PermissionsString
                })
                .OrderByDescending(f => f.IsDirectory)
                .ThenBy(f => f.Name)
                .ToList();

            return files;
        });
    }

    /// <summary>
    /// 파일 업로드
    /// </summary>
    public async Task UploadFileAsync(string localPath, string remotePath, Action<ulong>? progressCallback = null)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SFTP가 연결되지 않았습니다.");

        if (!File.Exists(localPath))
            throw new FileNotFoundException($"로컬 파일을 찾을 수 없습니다: {localPath}");

        await Task.Run(() =>
        {
            using var fileStream = File.OpenRead(localPath);
            var fileSize = (ulong)fileStream.Length;

            _sftpClient!.UploadFile(fileStream, remotePath, bytesTransferred =>
            {
                progressCallback?.Invoke(bytesTransferred);
                TransferProgress?.Invoke(this, new FileTransferProgressEventArgs
                {
                    FileName = Path.GetFileName(localPath),
                    TotalBytes = fileSize,
                    TransferredBytes = bytesTransferred,
                    IsUpload = true
                });
            });
        });
    }

    /// <summary>
    /// 파일 다운로드
    /// </summary>
    public async Task DownloadFileAsync(string remotePath, string localPath, Action<ulong>? progressCallback = null)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SFTP가 연결되지 않았습니다.");

        await Task.Run(() =>
        {
            // 원격 파일 크기 가져오기
            var remoteFile = _sftpClient!.Get(remotePath);
            var fileSize = (ulong)remoteFile.Length;

            using var fileStream = File.Create(localPath);
            _sftpClient.DownloadFile(remotePath, fileStream, bytesTransferred =>
            {
                progressCallback?.Invoke(bytesTransferred);
                TransferProgress?.Invoke(this, new FileTransferProgressEventArgs
                {
                    FileName = Path.GetFileName(remotePath),
                    TotalBytes = fileSize,
                    TransferredBytes = bytesTransferred,
                    IsUpload = false
                });
            });
        });
    }

    /// <summary>
    /// 디렉토리 생성
    /// </summary>
    public async Task CreateDirectoryAsync(string remotePath)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SFTP가 연결되지 않았습니다.");

        await Task.Run(() =>
        {
            _sftpClient!.CreateDirectory(remotePath);
        });
    }

    /// <summary>
    /// 파일 또는 디렉토리 삭제
    /// </summary>
    public async Task DeleteAsync(string remotePath, bool isDirectory)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SFTP가 연결되지 않았습니다.");

        await Task.Run(() =>
        {
            if (isDirectory)
            {
                DeleteDirectoryRecursive(remotePath);
            }
            else
            {
                _sftpClient!.DeleteFile(remotePath);
            }
        });
    }

    /// <summary>
    /// 디렉토리 재귀적 삭제
    /// </summary>
    private void DeleteDirectoryRecursive(string path)
    {
        var files = _sftpClient!.ListDirectory(path);

        foreach (var file in files)
        {
            if (file.Name == "." || file.Name == "..")
                continue;

            if (file.IsDirectory)
            {
                DeleteDirectoryRecursive(file.FullName);
            }
            else
            {
                _sftpClient.DeleteFile(file.FullName);
            }
        }

        _sftpClient.DeleteDirectory(path);
    }

    /// <summary>
    /// 파일 이름 변경
    /// </summary>
    public async Task RenameAsync(string oldPath, string newPath)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SFTP가 연결되지 않았습니다.");

        await Task.Run(() =>
        {
            _sftpClient!.RenameFile(oldPath, newPath);
        });
    }

    /// <summary>
    /// 파일 존재 확인
    /// </summary>
    public async Task<bool> ExistsAsync(string remotePath)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SFTP가 연결되지 않았습니다.");

        return await Task.Run(() =>
        {
            return _sftpClient!.Exists(remotePath);
        });
    }

    #region 파일 편집기용 메서드

    /// <summary>
    /// 원격 파일 내용 읽기
    /// </summary>
    public async Task<string> ReadFileAsync(string remotePath, Encoding? encoding = null)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SFTP가 연결되지 않았습니다.");

        return await Task.Run(() =>
        {
            using var stream = _sftpClient!.OpenRead(remotePath);
            using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    /// <summary>
    /// 원격 파일에 내용 쓰기
    /// </summary>
    public async Task WriteFileAsync(string remotePath, string content, Encoding? encoding = null)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SFTP가 연결되지 않았습니다.");

        await Task.Run(() =>
        {
            var bytes = (encoding ?? Encoding.UTF8).GetBytes(content);
            using var stream = _sftpClient!.Create(remotePath);
            stream.Write(bytes, 0, bytes.Length);
        });
    }

    /// <summary>
    /// 파일 정보 조회
    /// </summary>
    public async Task<RemoteFileInfo?> GetFileInfoAsync(string remotePath)
    {
        if (!IsConnected)
            throw new InvalidOperationException("SFTP가 연결되지 않았습니다.");

        return await Task.Run(() =>
        {
            try
            {
                var file = _sftpClient!.Get(remotePath);
                return new RemoteFileInfo
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsDirectory = file.IsDirectory,
                    Size = file.Length,
                    LastModified = file.LastWriteTime,
                    Permissions = null
                };
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>
    /// 텍스트 파일 여부 확인 (확장자 기반)
    /// </summary>
    public static bool IsTextFile(string fileName)
    {
        var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".log", ".conf", ".cfg", ".ini", ".json", ".xml", ".yaml", ".yml",
            ".sh", ".bash", ".zsh", ".fish", ".py", ".rb", ".pl", ".php", ".js", ".ts",
            ".html", ".htm", ".css", ".scss", ".sass", ".less",
            ".c", ".cpp", ".h", ".hpp", ".cs", ".java", ".go", ".rs", ".swift",
            ".sql", ".md", ".rst", ".csv", ".tsv",
            ".env", ".gitignore", ".dockerignore", ".editorconfig",
            ".service", ".socket", ".timer", ".target", // systemd
            ".cron", ".crontab", ".sudoers", ".hosts", ".fstab", // system config
            ""  // 확장자가 없는 파일도 텍스트로 간주 (대부분 config 파일)
        };

        var ext = Path.GetExtension(fileName);
        return textExtensions.Contains(ext);
    }

    #endregion

    public void Dispose()
    {
        Disconnect();
        _sftpClient?.Dispose();
    }
}

/// <summary>
/// 원격 파일 정보
/// </summary>
public class RemoteFileInfo
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string? Permissions { get; set; }

    public string SizeFormatted
    {
        get
        {
            if (IsDirectory) return "<DIR>";
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F2} KB";
            if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024.0):F2} MB";
            return $"{Size / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public string Icon => IsDirectory ? "📁" : "📄";
}

/// <summary>
/// 파일 전송 진행률 이벤트
/// </summary>
public class FileTransferProgressEventArgs : EventArgs
{
    public string FileName { get; set; } = string.Empty;
    public ulong TotalBytes { get; set; }
    public ulong TransferredBytes { get; set; }
    public bool IsUpload { get; set; }

    public double ProgressPercentage => TotalBytes > 0 ? (TransferredBytes / (double)TotalBytes) * 100 : 0;
}
