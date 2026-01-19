using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TermSnap.Models;
using Renci.SshNet;

namespace TermSnap.Services;

/// <summary>
/// 파일 트리 서비스 - SSH 및 로컬 파일 시스템 디렉토리 조회
/// </summary>
public class FileTreeService
{
    private readonly SftpClient? _sftpClient;
    private readonly bool _isLocal;

    /// <summary>
    /// 숨김 파일 표시 여부
    /// </summary>
    public bool ShowHiddenFiles { get; set; } = true;

    /// <summary>
    /// SSH용 생성자
    /// </summary>
    public FileTreeService(SftpClient sftpClient)
    {
        _sftpClient = sftpClient;
        _isLocal = false;
    }

    /// <summary>
    /// 로컬용 생성자
    /// </summary>
    public FileTreeService()
    {
        _isLocal = true;
    }

    /// <summary>
    /// 디렉토리 내용 가져오기
    /// </summary>
    public async Task<List<FileTreeItem>> GetDirectoryContentsAsync(string path)
    {
        if (_isLocal)
        {
            return await GetLocalDirectoryContentsAsync(path);
        }
        else
        {
            return await GetRemoteDirectoryContentsAsync(path);
        }
    }

    /// <summary>
    /// 로컬 디렉토리 내용 가져오기
    /// </summary>
    private Task<List<FileTreeItem>> GetLocalDirectoryContentsAsync(string path)
    {
        return Task.Run(() =>
        {
            var items = new List<FileTreeItem>();

            try
            {
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists) return items;

                // 디렉토리 먼저
                foreach (var dir in dirInfo.GetDirectories().OrderBy(d => d.Name))
                {
                    // 숨김 파일 필터링
                    if (!ShowHiddenFiles && (dir.Attributes & FileAttributes.Hidden) != 0)
                        continue;

                    items.Add(new FileTreeItem(dir.Name, dir.FullName, true)
                    {
                        LastModified = dir.LastWriteTime
                    });
                }

                // 파일
                foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name))
                {
                    // 숨김 파일 필터링
                    if (!ShowHiddenFiles && (file.Attributes & FileAttributes.Hidden) != 0)
                        continue;

                    items.Add(new FileTreeItem(file.Name, file.FullName, false)
                    {
                        Size = file.Length,
                        LastModified = file.LastWriteTime
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 권한 없음 - 무시
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"로컬 디렉토리 조회 실패: {ex.Message}");
            }

            return items;
        });
    }

    /// <summary>
    /// 원격(SSH) 디렉토리 내용 가져오기
    /// </summary>
    private Task<List<FileTreeItem>> GetRemoteDirectoryContentsAsync(string path)
    {
        return Task.Run(() =>
        {
            var items = new List<FileTreeItem>();

            if (_sftpClient == null || !_sftpClient.IsConnected)
                return items;

            try
            {
                var files = _sftpClient.ListDirectory(path);

                // 디렉토리 먼저
                var directories = files
                    .Where(f => f.IsDirectory && f.Name != "." && f.Name != "..")
                    .OrderBy(f => f.Name);

                foreach (var dir in directories)
                {
                    // 숨김 파일 필터링
                    if (!ShowHiddenFiles && dir.Name.StartsWith("."))
                        continue;

                    var fullPath = path.TrimEnd('/') + "/" + dir.Name;
                    items.Add(new FileTreeItem(dir.Name, fullPath, true)
                    {
                        LastModified = dir.LastWriteTime,
                        Permissions = GetPermissionString(dir)
                    });
                }

                // 파일
                var regularFiles = files
                    .Where(f => !f.IsDirectory)
                    .OrderBy(f => f.Name);

                foreach (var file in regularFiles)
                {
                    // 숨김 파일 필터링
                    if (!ShowHiddenFiles && file.Name.StartsWith("."))
                        continue;

                    var fullPath = path.TrimEnd('/') + "/" + file.Name;
                    items.Add(new FileTreeItem(file.Name, fullPath, false)
                    {
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        Permissions = GetPermissionString(file)
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"원격 디렉토리 조회 실패: {ex.Message}");
            }

            return items;
        });
    }

    /// <summary>
    /// 권한 문자열 생성 (rwxr-xr-x 형식)
    /// </summary>
    private static string GetPermissionString(Renci.SshNet.Sftp.ISftpFile file)
    {
        try
        {
            var attrs = file.Attributes;
            var perms = new char[10];

            // 파일 타입
            perms[0] = file.IsDirectory ? 'd' : 
                       file.IsSymbolicLink ? 'l' : '-';

            // Owner
            perms[1] = attrs.OwnerCanRead ? 'r' : '-';
            perms[2] = attrs.OwnerCanWrite ? 'w' : '-';
            perms[3] = attrs.OwnerCanExecute ? 'x' : '-';

            // Group
            perms[4] = attrs.GroupCanRead ? 'r' : '-';
            perms[5] = attrs.GroupCanWrite ? 'w' : '-';
            perms[6] = attrs.GroupCanExecute ? 'x' : '-';

            // Others
            perms[7] = attrs.OthersCanRead ? 'r' : '-';
            perms[8] = attrs.OthersCanWrite ? 'w' : '-';
            perms[9] = attrs.OthersCanExecute ? 'x' : '-';

            return new string(perms);
        }
        catch
        {
            return "----------";
        }
    }

    /// <summary>
    /// 루트 노드 생성
    /// </summary>
    public FileTreeItem CreateRootNode(string path, string displayName = "")
    {
        var name = string.IsNullOrEmpty(displayName) ? path : displayName;
        return new FileTreeItem(name, path, true)
        {
            IsExpanded = true
        };
    }

    /// <summary>
    /// 홈 디렉토리 경로 가져오기
    /// </summary>
    public string GetHomeDirectory()
    {
        if (_isLocal)
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (_sftpClient != null && _sftpClient.IsConnected)
        {
            return _sftpClient.WorkingDirectory;
        }
        return "/";
    }

    /// <summary>
    /// 드라이브 목록 가져오기 (Windows 로컬용)
    /// </summary>
    public List<FileTreeItem> GetDrives()
    {
        var items = new List<FileTreeItem>();

        if (!_isLocal) return items;

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                var name = string.IsNullOrEmpty(drive.VolumeLabel) 
                    ? drive.Name 
                    : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
                    
                items.Add(new FileTreeItem(name, drive.Name, true));
            }
        }

        return items;
    }

    /// <summary>
    /// 파일 존재 여부 확인
    /// </summary>
    public bool FileExists(string path)
    {
        if (_isLocal)
        {
            return File.Exists(path);
        }
        else if (_sftpClient != null && _sftpClient.IsConnected)
        {
            return _sftpClient.Exists(path);
        }
        return false;
    }

    /// <summary>
    /// 디렉토리 존재 여부 확인
    /// </summary>
    public bool DirectoryExists(string path)
    {
        if (_isLocal)
        {
            return Directory.Exists(path);
        }
        else if (_sftpClient != null && _sftpClient.IsConnected)
        {
            try
            {
                var attrs = _sftpClient.GetAttributes(path);
                return attrs.IsDirectory;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }
}
