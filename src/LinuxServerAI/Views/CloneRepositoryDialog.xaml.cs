using System;
using System.Text.RegularExpressions;
using System.Windows;

namespace Nebula.Views;

/// <summary>
/// Git 저장소 복제 대화상자
/// </summary>
public partial class CloneRepositoryDialog : Window
{
    public string RepositoryUrl => RepositoryUrlBox.Text.Trim();
    public string TargetPath => TargetPathBox.Text.Trim();

    public CloneRepositoryDialog()
    {
        InitializeComponent();
        
        // URL 변경 시 자동으로 폴더 이름 추출
        RepositoryUrlBox.TextChanged += (s, e) => AutoFillTargetPath();
        
        // 기본 대상 폴더 설정
        var defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        TargetPathBox.Text = System.IO.Path.Combine(defaultPath, "Projects");
    }

    private void AutoFillTargetPath()
    {
        var url = RepositoryUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            // Git URL에서 저장소 이름 추출
            // https://github.com/user/repo.git -> repo
            // git@github.com:user/repo.git -> repo
            var repoName = ExtractRepoName(url);
            
            if (!string.IsNullOrEmpty(repoName))
            {
                var basePath = System.IO.Path.GetDirectoryName(TargetPathBox.Text);
                if (string.IsNullOrEmpty(basePath))
                {
                    basePath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Projects");
                }
                TargetPathBox.Text = System.IO.Path.Combine(basePath, repoName);
            }
        }
        catch
        {
            // 추출 실패 시 무시
        }
    }

    private static string? ExtractRepoName(string url)
    {
        // HTTPS: https://github.com/user/repo.git
        // SSH: git@github.com:user/repo.git
        
        // .git 확장자 제거
        url = url.TrimEnd('/');
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^4];
        }

        // 마지막 슬래시 또는 콜론 이후의 이름
        var lastSlash = url.LastIndexOf('/');
        var lastColon = url.LastIndexOf(':');
        var lastSeparator = Math.Max(lastSlash, lastColon);

        if (lastSeparator >= 0 && lastSeparator < url.Length - 1)
        {
            return url[(lastSeparator + 1)..];
        }

        return null;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "저장소를 복제할 위치를 선택하세요",
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };

        // 현재 경로가 있으면 초기 경로로 설정
        var currentPath = System.IO.Path.GetDirectoryName(TargetPathBox.Text);
        if (!string.IsNullOrEmpty(currentPath) && System.IO.Directory.Exists(currentPath))
        {
            dialog.SelectedPath = currentPath;
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            // 선택한 폴더에 저장소 이름 추가
            var repoName = ExtractRepoName(RepositoryUrlBox.Text);
            if (!string.IsNullOrEmpty(repoName))
            {
                TargetPathBox.Text = System.IO.Path.Combine(dialog.SelectedPath, repoName);
            }
            else
            {
                TargetPathBox.Text = dialog.SelectedPath;
            }
        }
    }

    private void CloneButton_Click(object sender, RoutedEventArgs e)
    {
        // 유효성 검사
        if (string.IsNullOrWhiteSpace(RepositoryUrl))
        {
            MessageBox.Show("저장소 URL을 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            RepositoryUrlBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetPath))
        {
            MessageBox.Show("대상 폴더를 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            TargetPathBox.Focus();
            return;
        }

        // 대상 폴더가 이미 존재하고 비어있지 않은 경우 경고
        if (System.IO.Directory.Exists(TargetPath))
        {
            var files = System.IO.Directory.GetFileSystemEntries(TargetPath);
            if (files.Length > 0)
            {
                var result = MessageBox.Show(
                    $"'{TargetPath}' 폴더가 이미 존재하고 비어있지 않습니다.\n계속하시겠습니까?",
                    "경고",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;
            }
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
