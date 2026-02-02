using System.Collections.Generic;
using System.Linq;
using TermSnap.Services;

namespace TermSnap.Models;

/// <summary>
/// 애플리케이션 설정
/// </summary>
public class AppConfig
{
    // Gemini API 키 (암호화 저장) - 하위 호환성
    public string EncryptedGeminiApiKey { get; set; } = string.Empty;

    // AI 모델 설정 목록 (2026년 1월 기준 최신)
    public List<AIModelConfig> AIModels { get; set; } = new()
    {
        // Gemini 모델들 (Google)
        new AIModelConfig { Provider = AIProviderType.Gemini, ModelId = "gemini-3-pro", ModelDisplayName = "Gemini 3 Pro" },
        new AIModelConfig { Provider = AIProviderType.Gemini, ModelId = "gemini-3-flash", ModelDisplayName = "Gemini 3 Flash" },
        new AIModelConfig { Provider = AIProviderType.Gemini, ModelId = "gemini-3-flash-preview", ModelDisplayName = "Gemini 3 Flash Preview" },
        new AIModelConfig { Provider = AIProviderType.Gemini, ModelId = "gemini-2.5-flash", ModelDisplayName = "Gemini 2.5 Flash" },
        new AIModelConfig { Provider = AIProviderType.Gemini, ModelId = "gemini-2.0-flash", ModelDisplayName = "Gemini 2.0 Flash" },

        // OpenAI 모델들
        new AIModelConfig { Provider = AIProviderType.OpenAI, ModelId = "gpt-5.2", ModelDisplayName = "GPT-5.2" },
        new AIModelConfig { Provider = AIProviderType.OpenAI, ModelId = "gpt-5.2-thinking", ModelDisplayName = "GPT-5.2 Thinking" },
        new AIModelConfig { Provider = AIProviderType.OpenAI, ModelId = "gpt-5.2-pro", ModelDisplayName = "GPT-5.2 Pro" },
        new AIModelConfig { Provider = AIProviderType.OpenAI, ModelId = "gpt-5.2-codex", ModelDisplayName = "GPT-5.2 Codex" },
        new AIModelConfig { Provider = AIProviderType.OpenAI, ModelId = "gpt-4o", ModelDisplayName = "GPT-4o" },
        new AIModelConfig { Provider = AIProviderType.OpenAI, ModelId = "gpt-4o-mini", ModelDisplayName = "GPT-4o Mini" },

        // Claude 모델들 (Anthropic)
        new AIModelConfig { Provider = AIProviderType.Claude, ModelId = "claude-opus-4-5-20251101", ModelDisplayName = "Claude Opus 4.5" },
        new AIModelConfig { Provider = AIProviderType.Claude, ModelId = "claude-sonnet-4-5-20251101", ModelDisplayName = "Claude Sonnet 4.5" },
        new AIModelConfig { Provider = AIProviderType.Claude, ModelId = "claude-3-7-sonnet-20250224", ModelDisplayName = "Claude 3.7 Sonnet" },
        new AIModelConfig { Provider = AIProviderType.Claude, ModelId = "claude-3-5-sonnet-20241022", ModelDisplayName = "Claude 3.5 Sonnet" },

        // Grok 모델들 (xAI)
        new AIModelConfig { Provider = AIProviderType.Grok, ModelId = "grok-4.1", ModelDisplayName = "Grok 4.1" },
        new AIModelConfig { Provider = AIProviderType.Grok, ModelId = "grok-4.1-fast", ModelDisplayName = "Grok 4.1 Fast" },
        new AIModelConfig { Provider = AIProviderType.Grok, ModelId = "grok-3", ModelDisplayName = "Grok 3" }
    };

    // 여러 서버 프로필 목록
    public List<ServerConfig> ServerProfiles { get; set; } = new();

    // 마지막으로 사용한 프로필 이름
    public string LastUsedProfile { get; set; } = string.Empty;

    // 명령어 스니펫 컬렉션 (SSH 서버용)
    public CommandSnippetCollection CommandSnippets { get; set; } = new();

    // 로컬 터미널 스니펫 목록 (PowerShell/CMD/WSL/GitBash)
    public List<CommandSnippet> LocalSnippets { get; set; } = new();

    // 명령어 히스토리 컬렉션
    public CommandHistoryCollection CommandHistory { get; set; } = new();

    // 최근 열었던 폴더 목록 (최대 10개)
    public List<RecentFolder> RecentFolders { get; set; } = new();
    
    // 최근 폴더 최대 개수
    private const int MaxRecentFolders = 10;

    public bool AutoExecuteCommands { get; set; } = false; // 명령어 자동 실행 여부
    public bool ShowCommandBeforeExecution { get; set; } = true; // 실행 전 명령어 표시
    public int MaxRetryAttempts { get; set; } = 3; // 최대 재시도 횟수
    public string Theme { get; set; } = "Dark"; // UI 테마 (기본값 다크)
    public string? Language { get; set; } = "en-US"; // UI 언어 (en-US, ko-KR)

    // 임베딩 설정
    public EmbeddingSettings Embedding { get; set; } = new();

    // AI CLI 설정 (Claude Code, Codex, Gemini CLI 등)
    public AICLISettings AICLISettings { get; set; } = new();

    // 로컬 터미널 UI 설정
    public LocalTerminalUISettings LocalTerminalUI { get; set; } = new();

    // 메인 창 상태 설정
    public WindowSettings MainWindowSettings { get; set; } = new();

    // 선택된 AI 제공자
    public AIProviderType SelectedProvider { get; set; } = AIProviderType.Gemini;
    
    // 선택된 모델 ID
    public string SelectedModelId { get; set; } = "gemini-2.0-flash";

    // 설정 파일 버전 (마이그레이션용)
    public int ConfigVersion { get; set; } = 2;

    /// <summary>
    /// 프로필 이름으로 검색
    /// </summary>
    public ServerConfig? GetProfileByName(string profileName)
    {
        return ServerProfiles.FirstOrDefault(p => p.ProfileName == profileName);
    }

    /// <summary>
    /// 프로필 추가 또는 업데이트
    /// </summary>
    public void SaveProfile(ServerConfig profile)
    {
        var existing = GetProfileByName(profile.ProfileName);
        if (existing != null)
        {
            ServerProfiles.Remove(existing);
        }
        ServerProfiles.Add(profile);
    }

    /// <summary>
    /// 프로필 삭제
    /// </summary>
    public bool DeleteProfile(string profileName)
    {
        var profile = GetProfileByName(profileName);
        if (profile != null)
        {
            return ServerProfiles.Remove(profile);
        }
        return false;
    }

    /// <summary>
    /// 마지막으로 사용한 프로필 가져오기
    /// </summary>
    public ServerConfig? GetLastUsedProfile()
    {
        if (!string.IsNullOrEmpty(LastUsedProfile))
        {
            return GetProfileByName(LastUsedProfile);
        }

        // 즐겨찾기가 있으면 첫 번째 즐겨찾기 반환
        var favorite = ServerProfiles.FirstOrDefault(p => p.IsFavorite);
        if (favorite != null)
            return favorite;

        // 마지막으로 연결한 프로필 반환
        return ServerProfiles.OrderByDescending(p => p.LastConnected).FirstOrDefault();
    }

    /// <summary>
    /// API 키가 설정된 모델 목록 반환
    /// </summary>
    public List<AIModelConfig> GetConfiguredModels()
    {
        return AIModels.Where(m => m.IsConfigured).ToList();
    }
    
    /// <summary>
    /// 특정 제공자의 설정된 모델 목록 반환
    /// </summary>
    public List<AIModelConfig> GetConfiguredModelsByProvider(AIProviderType provider)
    {
        return AIModels.Where(m => m.Provider == provider && m.IsConfigured).ToList();
    }
    
    /// <summary>
    /// 특정 모델의 설정 정보 반환
    /// </summary>
    public AIModelConfig? GetModelConfig(AIProviderType provider, string modelId)
    {
        return AIModels.FirstOrDefault(m => m.Provider == provider && m.ModelId == modelId);
    }

    /// <summary>
    /// 최근 폴더 추가 (중복 시 상단으로 이동, 최대 10개 유지)
    /// </summary>
    public void AddRecentFolder(string path, string folderType = "Local")
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        // 기존 항목 제거 (중복 방지)
        RecentFolders.RemoveAll(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        // 새 항목 맨 앞에 추가
        RecentFolders.Insert(0, new RecentFolder(path, folderType));

        // 최대 개수 초과 시 오래된 항목 제거
        while (RecentFolders.Count > MaxRecentFolders)
        {
            RecentFolders.RemoveAt(RecentFolders.Count - 1);
        }
    }

    /// <summary>
    /// 최근 폴더 목록 가져오기
    /// </summary>
    public List<RecentFolder> GetRecentFolders()
    {
        return RecentFolders.OrderByDescending(f => f.LastOpened).Take(MaxRecentFolders).ToList();
    }

    /// <summary>
    /// 최근 폴더 제거
    /// </summary>
    public void RemoveRecentFolder(string path)
    {
        RecentFolders.RemoveAll(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// 임베딩 타입
/// </summary>
[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum EmbeddingType
{
    /// <summary>
    /// 로컬 ONNX 모델 (API 호출 없음, 빠름)
    /// </summary>
    Local,

    /// <summary>
    /// API 호출 (Gemini, OpenAI)
    /// </summary>
    API,

    /// <summary>
    /// 임베딩 비활성화 (FTS5만 사용)
    /// </summary>
    Disabled
}

/// <summary>
/// 임베딩 설정
/// </summary>
public class EmbeddingSettings
{
    /// <summary>
    /// 임베딩 타입 (Local, API, Disabled)
    /// </summary>
    public EmbeddingType Type { get; set; } = EmbeddingType.Local;

    /// <summary>
    /// API 사용 시 제공자 (Gemini, OpenAI)
    /// </summary>
    public AIProviderType ApiProvider { get; set; } = AIProviderType.Gemini;

    /// <summary>
    /// 로컬 모델 다운로드 완료 여부
    /// </summary>
    public bool LocalModelDownloaded { get; set; } = false;

    /// <summary>
    /// 캐시 히트 임계값 (이 이상이면 캐시 사용)
    /// </summary>
    public double CacheHitThreshold { get; set; } = 0.85;

    /// <summary>
    /// 최소 유사도 임계값 (이 이상이면 결과 반환)
    /// </summary>
    public double MinSimilarity { get; set; } = 0.75;
}

/// <summary>
/// AI CLI 도구 설정
/// </summary>
public class AICLISettings
{
    /// <summary>
    /// 선택된 AI CLI (claude, codex, gemini, aider, custom)
    /// </summary>
    public string SelectedCLI { get; set; } = "claude";

    /// <summary>
    /// 마지막으로 사용한 전체 명령어
    /// </summary>
    public string LastCommand { get; set; } = "claude";

    /// <summary>
    /// 자동 모드로 마지막 실행했는지 여부
    /// </summary>
    public bool LastAutoMode { get; set; } = false;

    /// <summary>
    /// AI CLI 섹션 펼침 상태
    /// </summary>
    public bool IsExpanded { get; set; } = false;

    /// <summary>
    /// 커스텀 CLI 명령어 (custom 선택 시 사용)
    /// </summary>
    public string CustomCommand { get; set; } = "";

    /// <summary>
    /// 지원되는 AI CLI 목록
    /// </summary>
    public static List<AICLIInfo> GetAvailableCLIs()
    {
        return new List<AICLIInfo>
        {
            new AICLIInfo
            {
                Id = "claude",
                Name = "Claude Code",
                Command = "claude",
                AutoModeFlag = "--dangerously-skip-permissions",
                Description = LocalizationService.Instance.GetString("AICLI.Claude.Description"),
                IconKind = "Robot",
                InstallCommand = "npm install -g @anthropic-ai/claude-code",
                InstallDescription = LocalizationService.Instance.GetString("AICLI.Claude.InstallDesc"),
                WebsiteUrl = "https://github.com/anthropics/claude-code"
            },
            new AICLIInfo
            {
                Id = "codex",
                Name = "Codex CLI",
                Command = "codex",
                AutoModeFlag = "--full-auto",
                Description = LocalizationService.Instance.GetString("AICLI.Codex.Description"),
                IconKind = "CodeBraces",
                InstallCommand = "npm install -g @openai/codex",
                InstallDescription = LocalizationService.Instance.GetString("AICLI.Codex.InstallDesc"),
                WebsiteUrl = "https://github.com/openai/codex"
            },
            new AICLIInfo
            {
                Id = "gemini",
                Name = "Gemini CLI",
                Command = "gemini",
                AutoModeFlag = "-y",
                Description = LocalizationService.Instance.GetString("AICLI.Gemini.Description"),
                IconKind = "Google",
                InstallCommand = "npm install -g @anthropic/gemini-cli",
                InstallDescription = LocalizationService.Instance.GetString("AICLI.Gemini.InstallDesc"),
                WebsiteUrl = "https://github.com/anthropics/gemini-cli"
            },
            new AICLIInfo
            {
                Id = "aider",
                Name = "Aider",
                Command = "aider",
                AutoModeFlag = "--yes",
                Description = LocalizationService.Instance.GetString("AICLI.Aider.Description"),
                IconKind = "AccountMultiple",
                InstallCommand = "pip install aider-chat",
                InstallDescription = LocalizationService.Instance.GetString("AICLI.Aider.InstallDesc"),
                WebsiteUrl = "https://github.com/paul-gauthier/aider"
            },
            new AICLIInfo
            {
                Id = "custom",
                Name = LocalizationService.Instance.GetString("AICLI.Custom.Name"),
                Command = "",
                AutoModeFlag = "",
                Description = LocalizationService.Instance.GetString("AICLI.Custom.Description"),
                IconKind = "Cog",
                InstallCommand = "",
                InstallDescription = LocalizationService.Instance.GetString("AICLI.Custom.InstallDesc"),
                WebsiteUrl = ""
            }
        };
    }
}

/// <summary>
/// AI CLI 정보
/// </summary>
public class AICLIInfo
{
    /// <summary>
    /// CLI 식별자
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// 표시 이름
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 기본 명령어
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// 자동 모드 플래그
    /// </summary>
    public string AutoModeFlag { get; set; } = "";

    /// <summary>
    /// 설명
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// MaterialDesign 아이콘 종류
    /// </summary>
    public string IconKind { get; set; } = "Console";

    /// <summary>
    /// 설치 명령어
    /// </summary>
    public string InstallCommand { get; set; } = "";

    /// <summary>
    /// 설치 방법 설명
    /// </summary>
    public string InstallDescription { get; set; } = "";

    /// <summary>
    /// 공식 웹사이트 URL
    /// </summary>
    public string WebsiteUrl { get; set; } = "";
}

/// <summary>
/// 로컬 터미널 UI 설정 (마지막 선택 옵션 저장)
/// </summary>
public class LocalTerminalUISettings
{
    /// <summary>
    /// 블록 UI 사용 여부 (true: 채팅 스타일, false: 터미널 스타일)
    /// </summary>
    public bool UseBlockUI { get; set; } = true;

    /// <summary>
    /// 마지막으로 선택한 쉘 타입
    /// </summary>
    public string LastShellType { get; set; } = "PowerShell";
}

/// <summary>
/// 창 상태 설정 (위치, 크기, 최대화 여부)
/// </summary>
public class WindowSettings
{
    /// <summary>
    /// 창 왼쪽 위치
    /// </summary>
    public double Left { get; set; } = double.NaN;

    /// <summary>
    /// 창 상단 위치
    /// </summary>
    public double Top { get; set; } = double.NaN;

    /// <summary>
    /// 창 너비
    /// </summary>
    public double Width { get; set; } = 1400;

    /// <summary>
    /// 창 높이
    /// </summary>
    public double Height { get; set; } = 900;

    /// <summary>
    /// 창 상태 (Normal, Maximized)
    /// </summary>
    public string WindowState { get; set; } = "Normal";

    /// <summary>
    /// 설정이 유효한지 확인
    /// </summary>
    public bool IsValid => !double.IsNaN(Left) && !double.IsNaN(Top) && Width > 100 && Height > 100;
}
