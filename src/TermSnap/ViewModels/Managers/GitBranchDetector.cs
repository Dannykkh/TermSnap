using System.IO;

namespace TermSnap.ViewModels.Managers;

/// <summary>
/// Git 브랜치 감지기
/// </summary>
public static class GitBranchDetector
{
    /// <summary>
    /// 지정된 디렉토리의 Git 브랜치를 가져옵니다
    /// </summary>
    /// <param name="directory">확인할 디렉토리 경로</param>
    /// <returns>Git 브랜치 이름 (Git 저장소가 아니면 null)</returns>
    public static string? GetBranch(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        try
        {
            // .git 디렉토리가 있는지 확인 (상위 디렉토리까지 검색)
            var currentDir = new DirectoryInfo(directory);
            while (currentDir != null)
            {
                var gitDir = Path.Combine(currentDir.FullName, ".git");
                if (Directory.Exists(gitDir))
                {
                    // .git/HEAD 파일 읽기
                    var headFile = Path.Combine(gitDir, "HEAD");
                    if (File.Exists(headFile))
                    {
                        var headContent = File.ReadAllText(headFile).Trim();

                        // ref: refs/heads/main -> "main"
                        if (headContent.StartsWith("ref: refs/heads/"))
                        {
                            return headContent.Substring("ref: refs/heads/".Length);
                        }
                        // detached HEAD (커밋 해시)
                        else if (headContent.Length == 40) // SHA-1 해시
                        {
                            return headContent.Substring(0, 7); // 짧은 해시
                        }
                    }
                    break;
                }

                currentDir = currentDir.Parent;
            }
        }
        catch
        {
            // Git 브랜치를 가져오는 중 오류 발생 시 무시
        }

        return null;
    }
}
