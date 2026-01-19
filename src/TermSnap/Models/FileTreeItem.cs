using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TermSnap.Models;

/// <summary>
/// 파일 트리 노드 모델 (Warp/IDE 스타일 파일 탐색기용)
/// </summary>
public class FileTreeItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _fullPath = string.Empty;
    private bool _isDirectory;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isLoading;
    private long _size;
    private DateTime _lastModified;
    private string _permissions = string.Empty;
    private FileTreeItem? _parent;
    private ObservableCollection<FileTreeItem> _children = new();

    /// <summary>
    /// 파일/폴더 이름
    /// </summary>
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Icon)); }
    }

    /// <summary>
    /// 전체 경로
    /// </summary>
    public string FullPath
    {
        get => _fullPath;
        set { _fullPath = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 디렉토리 여부
    /// </summary>
    public bool IsDirectory
    {
        get => _isDirectory;
        set { _isDirectory = value; OnPropertyChanged(); OnPropertyChanged(nameof(Icon)); }
    }

    /// <summary>
    /// 확장 상태
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(Icon)); }
    }

    /// <summary>
    /// 선택 상태
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 로딩 중 여부 (lazy loading)
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 파일 크기 (바이트)
    /// </summary>
    public long Size
    {
        get => _size;
        set { _size = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeFormatted)); }
    }

    /// <summary>
    /// 포맷된 파일 크기
    /// </summary>
    public string SizeFormatted
    {
        get
        {
            if (IsDirectory) return "";
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
            if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024):F1} MB";
            return $"{Size / (1024.0 * 1024 * 1024):F1} GB";
        }
    }

    /// <summary>
    /// 마지막 수정일
    /// </summary>
    public DateTime LastModified
    {
        get => _lastModified;
        set { _lastModified = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 권한 (Linux: rwxr-xr-x)
    /// </summary>
    public string Permissions
    {
        get => _permissions;
        set { _permissions = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 부모 노드
    /// </summary>
    public FileTreeItem? Parent
    {
        get => _parent;
        set { _parent = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 자식 노드들
    /// </summary>
    public ObservableCollection<FileTreeItem> Children
    {
        get => _children;
        set { _children = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChildren)); }
    }

    /// <summary>
    /// 자식이 있는지 여부
    /// </summary>
    public bool HasChildren => IsDirectory;

    /// <summary>
    /// 아이콘 (파일 타입에 따라)
    /// </summary>
    public string Icon
    {
        get
        {
            if (IsDirectory)
            {
                return IsExpanded ? "FolderOpen" : "Folder";
            }

            // 파일 확장자에 따른 아이콘
            var ext = System.IO.Path.GetExtension(Name).ToLowerInvariant();
            return ext switch
            {
                ".txt" or ".log" or ".md" => "FileDocument",
                ".cs" or ".py" or ".js" or ".ts" or ".java" or ".cpp" or ".c" or ".h" => "FileCode",
                ".json" or ".xml" or ".yaml" or ".yml" or ".toml" => "FileSettings",
                ".sh" or ".bash" or ".zsh" or ".ps1" or ".bat" or ".cmd" => "Console",
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".ico" => "FileImage",
                ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => "FolderZip",
                ".pdf" => "FilePdfBox",
                ".html" or ".htm" or ".css" => "LanguageHtml5",
                ".sql" or ".db" or ".sqlite" => "Database",
                ".conf" or ".cfg" or ".ini" or ".env" => "Cog",
                ".key" or ".pem" or ".crt" or ".cer" => "Key",
                _ => "File"
            };
        }
    }

    /// <summary>
    /// 숨김 파일 여부
    /// </summary>
    public bool IsHidden => Name.StartsWith(".");

    /// <summary>
    /// 플레이스홀더 아이템 (lazy loading용)
    /// </summary>
    public bool IsPlaceholder { get; set; }

    /// <summary>
    /// 기본 생성자
    /// </summary>
    public FileTreeItem() { }

    /// <summary>
    /// 편의 생성자
    /// </summary>
    public FileTreeItem(string name, string fullPath, bool isDirectory)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;

        // 디렉토리인 경우 플레이스홀더 추가 (lazy loading)
        if (isDirectory)
        {
            Children.Add(new FileTreeItem { IsPlaceholder = true, Name = "Loading..." });
        }
    }

    /// <summary>
    /// 플레이스홀더 생성
    /// </summary>
    public static FileTreeItem CreatePlaceholder()
    {
        return new FileTreeItem { IsPlaceholder = true, Name = "Loading..." };
    }

    /// <summary>
    /// 자식 노드 초기화 (플레이스홀더 제거)
    /// </summary>
    public void ClearChildren()
    {
        Children.Clear();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
