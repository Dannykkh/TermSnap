using System;

namespace Nebula.Models;

/// <summary>
/// 최근 열었던 폴더 정보
/// </summary>
public class RecentFolder
{
    /// <summary>
    /// 폴더 전체 경로
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 폴더 이름 (표시용)
    /// </summary>
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) 
                          ?? Path;

    /// <summary>
    /// 마지막으로 열었던 시간
    /// </summary>
    public DateTime LastOpened { get; set; } = DateTime.Now;

    /// <summary>
    /// 폴더 타입 (Local, WSL, SSH 등)
    /// </summary>
    public string FolderType { get; set; } = "Local";

    /// <summary>
    /// Git 저장소 여부
    /// </summary>
    public bool IsGitRepository { get; set; }

    /// <summary>
    /// 아이콘 (MaterialDesign PackIcon Kind)
    /// </summary>
    public string Icon => IsGitRepository ? "Git" : "Folder";

    public RecentFolder() { }

    public RecentFolder(string path, string folderType = "Local")
    {
        Path = path;
        FolderType = folderType;
        LastOpened = DateTime.Now;
        
        // Git 저장소 확인
        try
        {
            var gitPath = System.IO.Path.Combine(path, ".git");
            IsGitRepository = System.IO.Directory.Exists(gitPath);
        }
        catch
        {
            IsGitRepository = false;
        }
    }
}
