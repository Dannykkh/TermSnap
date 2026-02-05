using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TermSnap.Services;

/// <summary>
/// 프로젝트 분석 기반 Claude Code 리소스 추천 서비스
/// GitHub: Dannykkh/claude-code-agent-customizations에서 스킬/에이전트/훅/MCP 검색
/// </summary>
public class SkillRecommendationService
{
    private const string RepoBase = "https://api.github.com/repos/Dannykkh/claude-code-agent-customizations/contents";
    private const string RawBase = "https://raw.githubusercontent.com/Dannykkh/claude-code-agent-customizations/main";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    // GitHub 캐시 (API 호출 최소화)
    private static List<GitHubResource>? _cachedSkills;
    private static List<GitHubResource>? _cachedAgents;
    private static List<GitHubResource>? _cachedCommands;
    private static List<GitHubResource>? _cachedHooks;
    private static List<GitHubResource>? _cachedMCPs;
    private static List<ExternalMCPInfo>? _cachedExternalMCPs;
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    static SkillRecommendationService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TermSnap-SkillRecommender");
    }

    /// <summary>
    /// GitHub에서 가져온 리소스 정보
    /// </summary>
    public class GitHubResource
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = ""; // "dir" or "file"
        public string Path { get; set; } = "";
        public string Url { get; set; } = "";
        public string Description { get; set; } = ""; // README에서 추출
    }

    /// <summary>
    /// 외부 MCP 서버 정보 (README에서 파싱)
    /// </summary>
    public class ExternalMCPInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string InstallCommand { get; set; } = "";
        public string Category { get; set; } = "";
        public string Url { get; set; } = "";
    }

    #region Models

    /// <summary>
    /// Claude Code 리소스 타입
    /// </summary>
    public enum ResourceType
    {
        Skill,      // /skills - 특정 작업에 대한 전문 지식
        Agent,      // /agents - Task 에이전트 정의
        Command,    // /commands - 슬래시 커맨드
        Hook,       // /hooks - 자동 실행 스크립트
        MCP         // /mcp-servers - MCP 서버
    }

    /// <summary>
    /// 프로젝트 기술 스택 정보
    /// </summary>
    public class ProjectStack
    {
        public string ProjectPath { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public List<string> DetectedTechnologies { get; set; } = new();
        public List<string> DetectedFrameworks { get; set; } = new();
        public string PrimaryLanguage { get; set; } = "";
    }

    /// <summary>
    /// 추천 리소스 정보
    /// </summary>
    public class RecommendedResource
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public ResourceType Type { get; set; }
        public string Category { get; set; } = "";
        public int Priority { get; set; } // 1=필수, 2=권장, 3=선택
        public string SourceUrl { get; set; } = "";
        public string InstallPath { get; set; } = ""; // 설치될 경로
        public bool IsInstalled { get; set; }
        public bool IsGlobalInstall { get; set; } // 글로벌(~/.claude/) 설치 여부
    }

    /// <summary>
    /// 전체 추천 결과
    /// </summary>
    public class RecommendationResult
    {
        public ProjectStack Stack { get; set; } = new();
        public List<RecommendedResource> Skills { get; set; } = new();
        public List<RecommendedResource> Agents { get; set; } = new();
        public List<RecommendedResource> Commands { get; set; } = new();
        public List<RecommendedResource> Hooks { get; set; } = new();
        public List<RecommendedResource> MCPs { get; set; } = new();

        public int TotalCount => Skills.Count + Agents.Count + Commands.Count + Hooks.Count + MCPs.Count;
    }

    #endregion

    #region Main API

    /// <summary>
    /// 프로젝트 분석 및 전체 리소스 추천
    /// </summary>
    public async Task<RecommendationResult> AnalyzeAndRecommend(string projectPath)
    {
        var stack = AnalyzeProject(projectPath);
        var result = new RecommendationResult { Stack = stack };

        result.Skills = GetRecommendedSkills(stack);
        result.Agents = GetRecommendedAgents(stack);
        result.Commands = GetRecommendedCommands(stack);
        result.Hooks = GetRecommendedHooks(stack);
        result.MCPs = GetRecommendedMCPs(stack);

        // 설치 여부 체크
        await CheckInstalledStatus(result, projectPath);

        return result;
    }

    /// <summary>
    /// 리소스 설치 (기존 파일 있으면 백업 후 덮어쓰기)
    /// </summary>
    public async Task<bool> InstallResource(RecommendedResource resource, string projectPath, bool backupExisting = true)
    {
        try
        {
            // MCP 서버인 경우 settings.local.json에 등록
            if (resource.Type == ResourceType.MCP)
            {
                return await InstallMcpServer(resource, projectPath);
            }

            // 원본 파일 다운로드
            var content = await _httpClient.GetStringAsync(resource.SourceUrl);

            // 설치 경로 결정
            var installPath = GetInstallPath(resource, projectPath);
            var dir = Path.GetDirectoryName(installPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // 기존 파일이 있으면 백업
            if (backupExisting && File.Exists(installPath))
            {
                var backupPath = installPath + ".bak";
                // 기존 백업 파일이 있으면 삭제
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Move(installPath, backupPath);
                Debug.WriteLine($"[SkillRecommendation] 기존 파일 백업: {backupPath}");
            }

            // 파일 저장
            await File.WriteAllTextAsync(installPath, content, Encoding.UTF8);

            Debug.WriteLine($"[SkillRecommendation] 설치 완료: {resource.Name} → {installPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillRecommendation] 설치 실패 ({resource.Name}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 리소스 삭제
    /// </summary>
    public Task<bool> DeleteResource(RecommendedResource resource, string projectPath)
    {
        try
        {
            // MCP 서버인 경우 settings.local.json에서 제거
            if (resource.Type == ResourceType.MCP)
            {
                return Task.FromResult(RemoveMcpServer(resource, projectPath));
            }

            var installPath = GetInstallPath(resource, projectPath);

            if (File.Exists(installPath))
            {
                File.Delete(installPath);
                Debug.WriteLine($"[SkillRecommendation] 삭제 완료: {resource.Name} ({installPath})");
                return Task.FromResult(true);
            }
            else if (Directory.Exists(installPath))
            {
                Directory.Delete(installPath, recursive: true);
                Debug.WriteLine($"[SkillRecommendation] 폴더 삭제 완료: {resource.Name} ({installPath})");
                return Task.FromResult(true);
            }

            Debug.WriteLine($"[SkillRecommendation] 삭제할 파일 없음: {resource.Name}");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillRecommendation] 삭제 실패 ({resource.Name}): {ex.Message}");
            return Task.FromResult(false);
        }
    }

    #region MCP Server Settings Management

    /// <summary>
    /// MCP 서버 설치 (settings.local.json에 등록)
    /// </summary>
    private async Task<bool> InstallMcpServer(RecommendedResource resource, string projectPath)
    {
        try
        {
            var settingsPath = GetSettingsLocalJsonPath(projectPath);
            var settings = LoadSettingsLocalJson(settingsPath);

            // mcpServers 객체가 없으면 생성
            if (!settings.TryGetPropertyValue("mcpServers", out var mcpServersNode) || mcpServersNode == null)
            {
                mcpServersNode = new System.Text.Json.Nodes.JsonObject();
                settings["mcpServers"] = mcpServersNode;
            }

            var mcpServers = mcpServersNode.AsObject();

            // 이미 등록되어 있으면 스킵
            if (mcpServers.ContainsKey(resource.Name))
            {
                Debug.WriteLine($"[SkillRecommendation] MCP 서버 이미 등록됨: {resource.Name}");
                return true;
            }

            // MCP 서버 설정 생성
            var serverConfig = CreateMcpServerConfig(resource);
            mcpServers[resource.Name] = serverConfig;

            // 저장
            await SaveSettingsLocalJson(settingsPath, settings);

            Debug.WriteLine($"[SkillRecommendation] MCP 서버 등록 완료: {resource.Name}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillRecommendation] MCP 서버 설치 실패 ({resource.Name}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// MCP 서버 삭제 (settings.local.json에서 제거)
    /// </summary>
    private bool RemoveMcpServer(RecommendedResource resource, string projectPath)
    {
        try
        {
            var settingsPath = GetSettingsLocalJsonPath(projectPath);
            if (!File.Exists(settingsPath))
            {
                Debug.WriteLine($"[SkillRecommendation] settings.local.json 없음");
                return false;
            }

            var settings = LoadSettingsLocalJson(settingsPath);

            if (!settings.TryGetPropertyValue("mcpServers", out var mcpServersNode) || mcpServersNode == null)
            {
                Debug.WriteLine($"[SkillRecommendation] mcpServers 섹션 없음");
                return false;
            }

            var mcpServers = mcpServersNode.AsObject();

            if (!mcpServers.ContainsKey(resource.Name))
            {
                Debug.WriteLine($"[SkillRecommendation] MCP 서버 미등록: {resource.Name}");
                return false;
            }

            // 삭제
            mcpServers.Remove(resource.Name);

            // 저장
            SaveSettingsLocalJson(settingsPath, settings).Wait();

            Debug.WriteLine($"[SkillRecommendation] MCP 서버 제거 완료: {resource.Name}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillRecommendation] MCP 서버 삭제 실패 ({resource.Name}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// settings.local.json 경로 가져오기
    /// </summary>
    private string GetSettingsLocalJsonPath(string projectPath)
    {
        return Path.Combine(projectPath, ".claude", "settings.local.json");
    }

    /// <summary>
    /// settings.local.json 로드 (없으면 빈 객체 반환)
    /// </summary>
    private System.Text.Json.Nodes.JsonObject LoadSettingsLocalJson(string path)
    {
        if (File.Exists(path))
        {
            var content = File.ReadAllText(path);
            var node = System.Text.Json.Nodes.JsonNode.Parse(content);
            return node?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
        }
        return new System.Text.Json.Nodes.JsonObject();
    }

    /// <summary>
    /// settings.local.json 저장
    /// </summary>
    private async Task SaveSettingsLocalJson(string path, System.Text.Json.Nodes.JsonObject settings)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = settings.ToJsonString(options);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    /// <summary>
    /// MCP 서버 설정 생성
    /// </summary>
    private System.Text.Json.Nodes.JsonObject CreateMcpServerConfig(RecommendedResource resource)
    {
        var config = new System.Text.Json.Nodes.JsonObject();

        // 설명에서 설치 명령어 추출 (npx @anthropic-ai/mcp-server-xxx 형식)
        var description = resource.Description ?? "";
        var installMatch = System.Text.RegularExpressions.Regex.Match(
            description,
            @"npx\s+(@[\w\-/]+)"
        );

        if (installMatch.Success)
        {
            var packageName = installMatch.Groups[1].Value;
            config["command"] = "npx";
            config["args"] = new System.Text.Json.Nodes.JsonArray { "-y", packageName };
        }
        else
        {
            // 기본 형식 (Anthropic 공식 MCP 서버)
            config["command"] = "npx";
            config["args"] = new System.Text.Json.Nodes.JsonArray { "-y", $"@anthropic-ai/mcp-server-{resource.Name}" };
        }

        return config;
    }

    /// <summary>
    /// MCP 서버가 settings.local.json에 등록되어 있는지 확인
    /// </summary>
    public bool IsMcpServerInstalled(string serverName, string projectPath)
    {
        try
        {
            var settingsPath = GetSettingsLocalJsonPath(projectPath);
            if (!File.Exists(settingsPath)) return false;

            var settings = LoadSettingsLocalJson(settingsPath);
            if (!settings.TryGetPropertyValue("mcpServers", out var mcpServersNode) || mcpServersNode == null)
                return false;

            return mcpServersNode.AsObject().ContainsKey(serverName);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Mnemo 장기기억 시스템이 글로벌 settings.json에 설치되어 있는지 확인
    /// Mnemo는 save-conversation, save-response 훅으로 설치됨
    /// </summary>
    private bool IsMnemoInstalled()
    {
        try
        {
            // 글로벌 settings.json 경로
            var globalSettingsPath = Path.Combine(GlobalClaudePath, "settings.json");
            if (!File.Exists(globalSettingsPath)) return false;

            var content = File.ReadAllText(globalSettingsPath);
            var settings = System.Text.Json.Nodes.JsonNode.Parse(content)?.AsObject();
            if (settings == null) return false;

            // hooks 섹션에서 save-conversation 또는 save-response 확인
            if (!settings.TryGetPropertyValue("hooks", out var hooksNode) || hooksNode == null)
                return false;

            var hooks = hooksNode.AsObject();

            // Mnemo 훅 패턴 확인 (save-conversation.ps1 또는 save-response.ps1)
            foreach (var hookEntry in hooks)
            {
                var hookArray = hookEntry.Value?.AsArray();
                if (hookArray == null) continue;

                foreach (var hook in hookArray)
                {
                    var hookObj = hook?.AsObject();
                    if (hookObj == null) continue;

                    var command = hookObj["command"]?.ToString() ?? "";
                    if (command.Contains("save-conversation") || command.Contains("save-response"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    /// <summary>
    /// 선택된 리소스들 일괄 설치
    /// </summary>
    public async Task<(int Success, int Failed)> InstallResources(List<RecommendedResource> resources, string projectPath)
    {
        int success = 0, failed = 0;

        foreach (var resource in resources)
        {
            if (await InstallResource(resource, projectPath))
                success++;
            else
                failed++;
        }

        return (success, failed);
    }

    #endregion

    #region Project Analysis

    /// <summary>
    /// 프로젝트 기술 스택 분석
    /// </summary>
    public ProjectStack AnalyzeProject(string projectPath)
    {
        var stack = new ProjectStack
        {
            ProjectPath = projectPath,
            ProjectName = Path.GetFileName(projectPath)
        };

        if (!Directory.Exists(projectPath))
            return stack;

        // 파일 존재 여부로 기술 스택 감지
        var detectors = new Dictionary<string, (string Tech, string Framework, string Language)>
        {
            // .NET
            { "*.csproj", (".NET", "", "C#") },
            { "*.fsproj", (".NET", "", "F#") },

            // WPF/WinForms
            { "**/App.xaml", ("WPF", "WPF", "C#") },
            { "**/*.xaml", ("WPF", "XAML", "C#") },

            // JavaScript/TypeScript
            { "package.json", ("Node.js", "", "JavaScript") },
            { "tsconfig.json", ("TypeScript", "", "TypeScript") },

            // React/Next.js
            { "**/src/App.tsx", ("React", "React", "TypeScript") },
            { "**/src/App.jsx", ("React", "React", "JavaScript") },
            { "next.config.js", ("Next.js", "Next.js", "JavaScript") },
            { "next.config.mjs", ("Next.js", "Next.js", "JavaScript") },

            // Python
            { "requirements.txt", ("Python", "", "Python") },
            { "pyproject.toml", ("Python", "", "Python") },

            // Java/Spring
            { "pom.xml", ("Maven", "Spring", "Java") },
            { "build.gradle", ("Gradle", "Spring", "Java") },

            // Docker
            { "Dockerfile", ("Docker", "", "") },
            { "docker-compose.yml", ("Docker", "Docker Compose", "") },

            // Database
            { "*.sql", ("SQL", "", "SQL") },
            { "prisma/schema.prisma", ("Prisma", "Prisma", "") },

            // Git
            { ".git", ("Git", "", "") },
        };

        foreach (var (pattern, info) in detectors)
        {
            try
            {
                var searchPattern = pattern.Replace("**/", "");
                var searchOption = pattern.Contains("**/") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                bool found;
                if (pattern.Contains("*"))
                {
                    found = Directory.GetFiles(projectPath, searchPattern, searchOption).Any();
                }
                else
                {
                    found = File.Exists(Path.Combine(projectPath, pattern)) ||
                            Directory.Exists(Path.Combine(projectPath, pattern));
                }

                if (found)
                {
                    if (!string.IsNullOrEmpty(info.Tech) && !stack.DetectedTechnologies.Contains(info.Tech))
                        stack.DetectedTechnologies.Add(info.Tech);

                    if (!string.IsNullOrEmpty(info.Framework) && !stack.DetectedFrameworks.Contains(info.Framework))
                        stack.DetectedFrameworks.Add(info.Framework);

                    if (!string.IsNullOrEmpty(info.Language) && string.IsNullOrEmpty(stack.PrimaryLanguage))
                        stack.PrimaryLanguage = info.Language;
                }
            }
            catch { }
        }

        // package.json 분석
        AnalyzePackageJson(projectPath, stack);

        // .csproj 분석
        AnalyzeCsproj(projectPath, stack);

        Debug.WriteLine($"[SkillRecommendation] 기술: {string.Join(", ", stack.DetectedTechnologies)}");
        Debug.WriteLine($"[SkillRecommendation] 프레임워크: {string.Join(", ", stack.DetectedFrameworks)}");

        return stack;
    }

    private void AnalyzePackageJson(string projectPath, ProjectStack stack)
    {
        var packageJsonPath = Path.Combine(projectPath, "package.json");
        if (!File.Exists(packageJsonPath)) return;

        try
        {
            var content = File.ReadAllText(packageJsonPath);
            var json = JsonDocument.Parse(content);

            var dependencies = new List<string>();

            if (json.RootElement.TryGetProperty("dependencies", out var deps))
                foreach (var dep in deps.EnumerateObject())
                    dependencies.Add(dep.Name);

            if (json.RootElement.TryGetProperty("devDependencies", out var devDeps))
                foreach (var dep in devDeps.EnumerateObject())
                    dependencies.Add(dep.Name);

            var frameworkMap = new Dictionary<string, string>
            {
                { "react", "React" },
                { "next", "Next.js" },
                { "vue", "Vue.js" },
                { "angular", "Angular" },
                { "express", "Express" },
                { "nestjs", "NestJS" },
                { "@nestjs/core", "NestJS" },
                { "@mui/material", "MUI" },
                { "tailwindcss", "Tailwind CSS" },
                { "prisma", "Prisma" },
                { "typeorm", "TypeORM" },
            };

            foreach (var dep in dependencies)
            {
                if (frameworkMap.TryGetValue(dep, out var framework) &&
                    !stack.DetectedFrameworks.Contains(framework))
                {
                    stack.DetectedFrameworks.Add(framework);
                }
            }
        }
        catch { }
    }

    private void AnalyzeCsproj(string projectPath, ProjectStack stack)
    {
        try
        {
            var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories);
            foreach (var csproj in csprojFiles.Take(3))
            {
                var content = File.ReadAllText(csproj);

                if (content.Contains("UseWPF") || content.Contains("PresentationFramework"))
                    if (!stack.DetectedFrameworks.Contains("WPF"))
                        stack.DetectedFrameworks.Add("WPF");

                if (content.Contains("UseWindowsForms"))
                    if (!stack.DetectedFrameworks.Contains("WinForms"))
                        stack.DetectedFrameworks.Add("WinForms");

                if (content.Contains("Microsoft.AspNetCore"))
                    if (!stack.DetectedFrameworks.Contains("ASP.NET Core"))
                        stack.DetectedFrameworks.Add("ASP.NET Core");
            }
        }
        catch { }
    }

    #endregion

    #region Skill Recommendations

    /// <summary>
    /// 스킬 정의 (이름, 설명, 관련 기술, 우선순위)
    /// </summary>
    private static readonly (string Name, string Desc, string[] RelatedTech, int Priority)[] SkillDefinitions =
    {
        // 기본 설치 (ClaudeHookService가 설치)
        ("gepetto", "19단계 구현 계획 생성 워크플로우", new[] { "*" }, 1),
        ("mnemo", "장기기억 + 세션 핸드오프 통합 시스템", new[] { "*" }, 1),
        ("long-term-memory", "세션 간 장기기억 관리 (MEMORY.md + 대화 로그)", new[] { "*" }, 1),
        ("session-handoff", "세션 핸드오프 (작업 상태 전달)", new[] { "*" }, 1),

        // 공통 필수
        ("code-reviewer", "코드 리뷰 자동화 (500줄 제한, 보안 검사)", new[] { "*" }, 1),
        ("humanizer", "AI 생성 텍스트를 자연스럽게 변환", new[] { "*" }, 2),

        // React/Next.js
        ("react-dev", "React 컴포넌트 개발 최적화", new[] { "React", "Next.js" }, 1),
        ("vercel-react-best-practices", "Vercel 45+ React 최적화 규칙", new[] { "React", "Next.js" }, 1),
        ("web-design-guidelines", "UI/UX 접근성 검토", new[] { "React", "Next.js", "Vue.js", "Angular" }, 2),
        ("mui", "MUI 컴포넌트 가이드라인", new[] { "MUI" }, 1),

        // Python
        ("python-backend-fastapi", "FastAPI 백엔드 개발 모범 사례", new[] { "Python" }, 1),

        // Docker
        ("docker-deploy", "Docker 배포 자동화", new[] { "Docker" }, 1),

        // Node.js/TypeScript
        ("dependency-updater", "npm 의존성 업데이트 관리", new[] { "Node.js", "TypeScript" }, 2),
        ("openapi-to-typescript", "OpenAPI → TypeScript 타입 생성", new[] { "TypeScript" }, 3),

        // API/Backend
        ("api-tester", "REST/GraphQL API 테스트", new[] { "Express", "NestJS", "ASP.NET Core", "Spring" }, 1),

        // Database
        ("database-schema-designer", "데이터베이스 스키마 설계", new[] { "Prisma", "TypeORM", "SQL" }, 2),

        // 문서화
        ("mermaid-diagrams", "Mermaid 다이어그램 생성", new[] { "*" }, 3),
        ("c4-architecture", "C4 아키텍처 다이어그램", new[] { "*" }, 3),
        ("ppt-generator", "PowerPoint 문서 생성", new[] { "*" }, 3),

        // Java/Spring
        ("gemini", "Gemini AI 연동", new[] { "*" }, 3),
        ("perplexity", "Perplexity AI 검색", new[] { "*" }, 3),
    };

    private List<RecommendedResource> GetRecommendedSkills(ProjectStack stack)
    {
        var allTech = stack.DetectedTechnologies.Concat(stack.DetectedFrameworks).ToHashSet();
        var result = new List<RecommendedResource>();

        foreach (var (name, desc, relatedTech, priority) in SkillDefinitions)
        {
            // "*"는 모든 프로젝트에 적용
            var matches = relatedTech.Contains("*") || relatedTech.Any(t => allTech.Contains(t));
            if (matches)
            {
                // ClaudeHookService가 폴더 구조로 설치하는 스킬은 폴더 경로로 설정
                // gepetto: .claude/skills/gepetto/ (SKILL.md 파일 포함)
                // mnemo: 글로벌 설치 (~/.claude/settings.json 훅에 등록)
                var isFolderBased = name is "long-term-memory" or "session-handoff" or "gepetto";
                var isMnemo = name == "mnemo";

                result.Add(new RecommendedResource
                {
                    Name = name,
                    Description = desc,
                    Type = ResourceType.Skill,
                    Category = GetSkillCategory(name),
                    Priority = priority,
                    SourceUrl = (isFolderBased || isMnemo) ? "" : $"{RawBase}/skills/{name}/skill.md",
                    InstallPath = isFolderBased ? $".claude/skills/{name}" : (isMnemo ? "hooks" : $".claude/skills/{name}.md"),
                    IsGlobalInstall = isMnemo // Mnemo는 글로벌 설치
                });
            }
        }

        return result.GroupBy(r => r.Name).Select(g => g.First())
            .OrderBy(r => r.Priority).ToList();
    }

    private string GetSkillCategory(string name)
    {
        if (name is "long-term-memory" or "session-handoff" or "gepetto" or "mnemo")
            return "기본";
        if (name.Contains("react") || name.Contains("mui") || name.Contains("web-design"))
            return "프론트엔드";
        if (name.Contains("api") || name.Contains("backend") || name.Contains("fastapi"))
            return "백엔드";
        if (name.Contains("docker") || name.Contains("deploy"))
            return "배포";
        if (name.Contains("database") || name.Contains("schema"))
            return "데이터베이스";
        if (name.Contains("mermaid") || name.Contains("c4") || name.Contains("ppt"))
            return "문서화";
        if (name.Contains("code-review") || name.Contains("humanizer"))
            return "코드 품질";
        return "유틸리티";
    }

    #endregion

    #region Agent Recommendations

    /// <summary>
    /// 에이전트 정의
    /// </summary>
    private static readonly (string Name, string Desc, string[] RelatedTech, int Priority)[] AgentDefinitions =
    {
        // 공통
        ("code-reviewer", "코드 리뷰 에이전트", new[] { "*" }, 1),
        ("documentation", "문서화 에이전트", new[] { "*" }, 2),
        ("explore-agent", "코드베이스 탐색 에이전트", new[] { "*" }, 1),
        ("feature-tracker", "기능 추적 에이전트", new[] { "*" }, 2),
        ("memory-writer", "장기기억 작성 에이전트", new[] { "*" }, 1),

        // Frontend
        ("frontend-react", "React 프론트엔드 에이전트", new[] { "React", "Next.js" }, 1),
        ("react-best-practices", "React 모범 사례 에이전트", new[] { "React", "Next.js" }, 1),
        ("ui-ux-designer", "UI/UX 디자인 에이전트", new[] { "React", "Next.js", "Vue.js" }, 2),

        // Backend
        ("backend-spring", "Spring Boot 백엔드 에이전트", new[] { "Spring", "Java" }, 1),
        ("python-fastapi-guidelines", "FastAPI 가이드라인 에이전트", new[] { "Python" }, 1),
        ("api-tester", "API 테스트 에이전트", new[] { "Express", "NestJS", "ASP.NET Core", "Spring" }, 1),
        ("api-comparator", "API 비교 에이전트", new[] { "Express", "NestJS", "ASP.NET Core" }, 2),

        // Database
        ("database-mysql", "MySQL 데이터베이스 에이전트", new[] { "SQL", "Prisma", "TypeORM" }, 2),

        // AI/ML
        ("ai-ml", "AI/ML 에이전트", new[] { "Python" }, 2),

        // QA
        ("qa-engineer", "QA 엔지니어 에이전트", new[] { "*" }, 2),
        ("qa-writer", "QA 테스트 작성 에이전트", new[] { "*" }, 2),

        // Migration
        ("migration-helper", "마이그레이션 도우미 에이전트", new[] { "*" }, 3),

        // Specialized
        ("mermaid-diagram-specialist", "Mermaid 다이어그램 전문가", new[] { "*" }, 3),
        ("naming-conventions", "네이밍 컨벤션 에이전트", new[] { "*" }, 3),
    };

    private List<RecommendedResource> GetRecommendedAgents(ProjectStack stack)
    {
        var allTech = stack.DetectedTechnologies.Concat(stack.DetectedFrameworks).ToHashSet();
        var result = new List<RecommendedResource>();

        foreach (var (name, desc, relatedTech, priority) in AgentDefinitions)
        {
            var matches = relatedTech.Contains("*") || relatedTech.Any(t => allTech.Contains(t));
            if (matches)
            {
                result.Add(new RecommendedResource
                {
                    Name = name,
                    Description = desc,
                    Type = ResourceType.Agent,
                    Category = GetAgentCategory(name),
                    Priority = priority,
                    SourceUrl = $"{RawBase}/agents/{name}.md",
                    InstallPath = $".claude/agents/{name}.md"
                });
            }
        }

        return result.GroupBy(r => r.Name).Select(g => g.First())
            .OrderBy(r => r.Priority).ToList();
    }

    private string GetAgentCategory(string name)
    {
        if (name.Contains("frontend") || name.Contains("react") || name.Contains("ui"))
            return "프론트엔드";
        if (name.Contains("backend") || name.Contains("api") || name.Contains("fastapi") || name.Contains("spring"))
            return "백엔드";
        if (name.Contains("database") || name.Contains("mysql"))
            return "데이터베이스";
        if (name.Contains("qa") || name.Contains("test"))
            return "테스트";
        if (name.Contains("doc") || name.Contains("mermaid"))
            return "문서화";
        return "공통";
    }

    #endregion

    #region Command Recommendations

    /// <summary>
    /// 커맨드 정의
    /// </summary>
    private static readonly (string Name, string Desc, string[] RelatedTech, int Priority)[] CommandDefinitions =
    {
        // 공통 필수
        ("check-todos", "TODO 항목 검토", new[] { "*" }, 1),
        ("daily-sync", "일일 동기화", new[] { "*" }, 2),
        ("update-docs", "문서 업데이트", new[] { "*" }, 2),

        // 코드 관련
        ("review", "코드 리뷰 실행", new[] { "*" }, 1),
        ("test", "테스트 실행", new[] { "*" }, 1),
        ("generate", "코드 생성", new[] { "*" }, 2),
        ("migrate", "마이그레이션 실행", new[] { "*" }, 3),

        // 문서화
        ("write-api-docs", "API 문서 작성", new[] { "Express", "NestJS", "ASP.NET Core", "Spring" }, 1),
        ("write-changelog", "Changelog 작성", new[] { "*" }, 2),
        ("write-prd", "PRD 문서 작성", new[] { "*" }, 3),

        // Git 관련
        ("sync-branch", "브랜치 동기화", new[] { "Git" }, 2),
        ("explain-pr-changes", "PR 변경사항 설명", new[] { "Git" }, 2),
    };

    private List<RecommendedResource> GetRecommendedCommands(ProjectStack stack)
    {
        var allTech = stack.DetectedTechnologies.Concat(stack.DetectedFrameworks).ToHashSet();
        var result = new List<RecommendedResource>();

        foreach (var (name, desc, relatedTech, priority) in CommandDefinitions)
        {
            var matches = relatedTech.Contains("*") || relatedTech.Any(t => allTech.Contains(t));
            if (matches)
            {
                result.Add(new RecommendedResource
                {
                    Name = name,
                    Description = desc,
                    Type = ResourceType.Command,
                    Category = "커맨드",
                    Priority = priority,
                    SourceUrl = $"{RawBase}/commands/{name}.md",
                    InstallPath = $".claude/commands/{name}.md"
                });
            }
        }

        return result.GroupBy(r => r.Name).Select(g => g.First())
            .OrderBy(r => r.Priority).ToList();
    }

    #endregion

    #region Hook Recommendations

    /// <summary>
    /// 훅 정의
    /// </summary>
    private static readonly (string Name, string Desc, string[] RelatedTech, int Priority)[] HookDefinitions =
    {
        // 장기기억 (필수)
        ("save-conversation.sh", "대화 저장 훅 (장기기억)", new[] { "*" }, 1),
        ("update-memory.sh", "메모리 업데이트 훅 (장기기억)", new[] { "*" }, 1),

        // 코드 품질
        ("validate-code.sh", "코드 검증 훅", new[] { "*" }, 2),
        ("format-code.sh", "코드 포맷팅 훅", new[] { "*" }, 2),

        // 언어별 포맷터
        ("format-typescript.sh", "TypeScript 포맷팅", new[] { "TypeScript", "Node.js" }, 2),
        ("format-java.sh", "Java 포맷팅", new[] { "Java", "Spring" }, 2),

        // 문서 검증
        ("validate-docs.sh", "문서 검증 훅", new[] { "*" }, 3),
        ("validate-api.sh", "API 검증 훅", new[] { "Express", "NestJS", "ASP.NET Core" }, 2),

        // 보안
        ("protect-files.sh", "파일 보호 훅", new[] { "*" }, 2),
        ("check-new-file.sh", "새 파일 검사 훅", new[] { "*" }, 3),
    };

    private List<RecommendedResource> GetRecommendedHooks(ProjectStack stack)
    {
        var allTech = stack.DetectedTechnologies.Concat(stack.DetectedFrameworks).ToHashSet();
        var result = new List<RecommendedResource>();

        foreach (var (name, desc, relatedTech, priority) in HookDefinitions)
        {
            var matches = relatedTech.Contains("*") || relatedTech.Any(t => allTech.Contains(t));
            if (matches)
            {
                result.Add(new RecommendedResource
                {
                    Name = name,
                    Description = desc,
                    Type = ResourceType.Hook,
                    Category = "훅",
                    Priority = priority,
                    SourceUrl = $"{RawBase}/hooks/{name}",
                    InstallPath = $"hooks/{name}"
                });
            }
        }

        return result.GroupBy(r => r.Name).Select(g => g.First())
            .OrderBy(r => r.Priority).ToList();
    }

    #endregion

    #region MCP Recommendations

    /// <summary>
    /// MCP 서버 정의
    /// </summary>
    private static readonly (string Name, string Desc, string[] RelatedTech, int Priority)[] MCPDefinitions =
    {
        // ClaudeHookService에서 "orchestrator" 키로 settings.local.json에 등록함
        ("orchestrator", "Claude 오케스트레이터 MCP (에이전트 관리)", new[] { "*" }, 2),
    };

    private List<RecommendedResource> GetRecommendedMCPs(ProjectStack stack)
    {
        var allTech = stack.DetectedTechnologies.Concat(stack.DetectedFrameworks).ToHashSet();
        var result = new List<RecommendedResource>();

        foreach (var (name, desc, relatedTech, priority) in MCPDefinitions)
        {
            var matches = relatedTech.Contains("*") || relatedTech.Any(t => allTech.Contains(t));
            if (matches)
            {
                result.Add(new RecommendedResource
                {
                    Name = name,
                    Description = desc,
                    Type = ResourceType.MCP,
                    Category = "MCP",
                    Priority = priority,
                    SourceUrl = $"{RawBase}/mcp-servers/{name}",
                    InstallPath = $".claude/mcp-servers/{name}"
                });
            }
        }

        return result.OrderBy(r => r.Priority).ToList();
    }

    #endregion

    #region Dynamic GitHub Fetching

    /// <summary>
    /// GitHub 캐시 갱신 (필요시)
    /// </summary>
    public async Task RefreshGitHubCacheIfNeeded(bool forceRefresh = false)
    {
        if (!forceRefresh && DateTime.Now < _cacheExpiry)
            return;

        Debug.WriteLine("[SkillRecommendation] GitHub 캐시 갱신 중...");

        try
        {
            // 병렬로 모든 리소스 목록 가져오기
            var tasks = new[]
            {
                FetchGitHubDirectory("skills"),
                FetchGitHubDirectory("agents"),
                FetchGitHubDirectory("commands"),
                FetchGitHubDirectory("hooks"),
                FetchGitHubDirectory("mcp-servers"),
            };

            var results = await Task.WhenAll(tasks);

            _cachedSkills = results[0];
            _cachedAgents = results[1];
            _cachedCommands = results[2];
            _cachedHooks = results[3];
            _cachedMCPs = results[4];

            // 외부 MCP 서버 목록 파싱
            _cachedExternalMCPs = await FetchExternalMCPsFromReadme();

            _cacheExpiry = DateTime.Now.Add(CacheDuration);
            Debug.WriteLine($"[SkillRecommendation] 캐시 갱신 완료: Skills={_cachedSkills?.Count}, Agents={_cachedAgents?.Count}, Commands={_cachedCommands?.Count}, Hooks={_cachedHooks?.Count}, MCPs={_cachedMCPs?.Count}, ExternalMCPs={_cachedExternalMCPs?.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillRecommendation] GitHub 캐시 갱신 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// GitHub 디렉토리 내용 가져오기
    /// </summary>
    private async Task<List<GitHubResource>> FetchGitHubDirectory(string path)
    {
        var result = new List<GitHubResource>();
        try
        {
            var response = await _httpClient.GetStringAsync($"{RepoBase}/{path}");
            var json = JsonDocument.Parse(response);

            foreach (var item in json.RootElement.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString() ?? "";
                var type = item.GetProperty("type").GetString() ?? "";
                var itemPath = item.GetProperty("path").GetString() ?? "";

                // README, LICENSE 등 메타 파일 제외
                var lowerName = name.ToLowerInvariant();
                if (lowerName.StartsWith("readme") || lowerName.StartsWith("license") ||
                    lowerName.StartsWith("contributing") || lowerName.StartsWith("changelog"))
                    continue;

                // .md 파일이거나 디렉토리인 경우만
                if (type == "dir" || name.EndsWith(".md") || name.EndsWith(".sh"))
                {
                    result.Add(new GitHubResource
                    {
                        Name = name.Replace(".md", "").Replace(".sh", ""),
                        Type = type,
                        Path = itemPath,
                        Url = $"{RawBase}/{itemPath}"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillRecommendation] {path} 목록 조회 실패: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// mcp-servers/README.md에서 외부 MCP 서버 목록 파싱
    /// </summary>
    private async Task<List<ExternalMCPInfo>> FetchExternalMCPsFromReadme()
    {
        var result = new List<ExternalMCPInfo>();
        try
        {
            var readmeUrl = $"{RawBase}/mcp-servers/README.md";
            var content = await _httpClient.GetStringAsync(readmeUrl);

            // README에서 MCP 서버 정보 파싱
            // 형식: ## 서버이름 또는 ### 서버이름 또는 - **서버이름**: 설명
            var lines = content.Split('\n');
            ExternalMCPInfo? current = null;
            string currentCategory = "";

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // 카테고리 헤더 감지 (## 또는 ###)
                if (trimmed.StartsWith("## ") && !trimmed.Contains("설치") && !trimmed.Contains("사용"))
                {
                    currentCategory = trimmed.Substring(3).Trim();
                    continue;
                }

                // MCP 서버 항목 감지
                // 패턴 1: - **서버이름**: 설명
                if (trimmed.StartsWith("- **") || trimmed.StartsWith("* **"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        trimmed,
                        @"^[-*]\s*\*\*([^*]+)\*\*[:\s]*(.*)$"
                    );

                    if (match.Success)
                    {
                        current = new ExternalMCPInfo
                        {
                            Name = match.Groups[1].Value.Trim(),
                            Description = match.Groups[2].Value.Trim(),
                            Category = currentCategory
                        };
                        result.Add(current);
                    }
                }
                // 패턴 2: npx 또는 npm install 명령어
                else if (current != null && (trimmed.Contains("npx") || trimmed.Contains("npm install") || trimmed.Contains("pip install")))
                {
                    // 코드 블록 내용 추출
                    var cmd = trimmed.Replace("`", "").Trim();
                    if (!string.IsNullOrEmpty(cmd) && !cmd.StartsWith("#"))
                    {
                        current.InstallCommand = cmd;
                    }
                }
                // 패턴 3: URL 추출
                else if (current != null && trimmed.Contains("github.com"))
                {
                    var urlMatch = System.Text.RegularExpressions.Regex.Match(
                        trimmed,
                        @"https?://github\.com/[^\s\)>\]]+");
                    if (urlMatch.Success)
                    {
                        current.Url = urlMatch.Value;
                    }
                }
            }

            // 중복 제거
            result = result.GroupBy(x => x.Name).Select(g => g.First()).ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillRecommendation] 외부 MCP README 파싱 실패: {ex.Message}");
        }

        // 하드코딩된 인기 MCP 서버 추가 (README에 없을 경우 대비)
        AddPopularExternalMCPs(result);

        return result;
    }

    /// <summary>
    /// 인기 있는 외부 MCP 서버 추가
    /// </summary>
    private void AddPopularExternalMCPs(List<ExternalMCPInfo> list)
    {
        var popularMCPs = new List<ExternalMCPInfo>
        {
            new() { Name = "filesystem", Description = "파일 시스템 접근 MCP", Category = "파일", InstallCommand = "npx @anthropic-ai/mcp-server-filesystem" },
            new() { Name = "github", Description = "GitHub API 연동 MCP", Category = "개발", InstallCommand = "npx @anthropic-ai/mcp-server-github" },
            new() { Name = "postgres", Description = "PostgreSQL 데이터베이스 MCP", Category = "데이터베이스", InstallCommand = "npx @anthropic-ai/mcp-server-postgres" },
            new() { Name = "sqlite", Description = "SQLite 데이터베이스 MCP", Category = "데이터베이스", InstallCommand = "npx @anthropic-ai/mcp-server-sqlite" },
            new() { Name = "puppeteer", Description = "웹 브라우저 자동화 MCP", Category = "웹", InstallCommand = "npx @anthropic-ai/mcp-server-puppeteer" },
            new() { Name = "brave-search", Description = "Brave 검색 엔진 MCP", Category = "검색", InstallCommand = "npx @anthropic-ai/mcp-server-brave-search" },
            new() { Name = "fetch", Description = "HTTP 요청 MCP", Category = "네트워크", InstallCommand = "npx @anthropic-ai/mcp-server-fetch" },
            new() { Name = "memory", Description = "장기 기억 저장 MCP", Category = "기억", InstallCommand = "npx @anthropic-ai/mcp-server-memory" },
            new() { Name = "sequential-thinking", Description = "순차적 사고 MCP", Category = "사고", InstallCommand = "npx @anthropic-ai/mcp-server-sequential-thinking" },
            new() { Name = "slack", Description = "Slack 연동 MCP", Category = "커뮤니케이션", InstallCommand = "npx @anthropic-ai/mcp-server-slack" },
            new() { Name = "google-drive", Description = "Google Drive 연동 MCP", Category = "클라우드", InstallCommand = "npx @anthropic-ai/mcp-server-google-drive" },
            new() { Name = "google-maps", Description = "Google Maps API MCP", Category = "지도", InstallCommand = "npx @anthropic-ai/mcp-server-google-maps" },
            new() { Name = "aws-kb-retrieval", Description = "AWS Knowledge Base 검색 MCP", Category = "AWS", InstallCommand = "npx @anthropic-ai/mcp-server-aws-kb-retrieval" },
            new() { Name = "sentry", Description = "Sentry 오류 추적 MCP", Category = "모니터링", InstallCommand = "npx @anthropic-ai/mcp-server-sentry" },
            new() { Name = "gitlab", Description = "GitLab API 연동 MCP", Category = "개발", InstallCommand = "npx @anthropic-ai/mcp-server-gitlab" },
            new() { Name = "everart", Description = "이미지 생성 MCP", Category = "AI", InstallCommand = "npx @anthropic-ai/mcp-server-everart" },
        };

        foreach (var mcp in popularMCPs)
        {
            if (!list.Any(x => x.Name.Equals(mcp.Name, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(mcp);
            }
        }
    }

    /// <summary>
    /// GitHub에서 가져온 동적 리소스를 RecommendedResource로 변환
    /// </summary>
    public async Task<List<RecommendedResource>> GetDynamicResources(ResourceType type, ProjectStack? stack = null)
    {
        await RefreshGitHubCacheIfNeeded();

        var resources = type switch
        {
            ResourceType.Skill => _cachedSkills ?? new(),
            ResourceType.Agent => _cachedAgents ?? new(),
            ResourceType.Command => _cachedCommands ?? new(),
            ResourceType.Hook => _cachedHooks ?? new(),
            ResourceType.MCP => _cachedMCPs ?? new(),
            _ => new List<GitHubResource>()
        };

        var result = resources.Select(r => new RecommendedResource
        {
            Name = r.Name,
            Description = GetResourceDescription(r.Name, type),
            Type = type,
            Category = GetResourceCategory(r.Name, type),
            Priority = GetResourcePriority(r.Name, type, stack),
            SourceUrl = GetSourceUrl(r, type),
            InstallPath = GetInstallPath(r.Name, type)
        }).ToList();

        // MCP의 경우 외부 MCP도 추가
        if (type == ResourceType.MCP && _cachedExternalMCPs != null)
        {
            foreach (var ext in _cachedExternalMCPs)
            {
                result.Add(new RecommendedResource
                {
                    Name = ext.Name,
                    Description = ext.Description + (!string.IsNullOrEmpty(ext.InstallCommand) ? $"\n설치: {ext.InstallCommand}" : ""),
                    Type = ResourceType.MCP,
                    Category = ext.Category,
                    Priority = 3, // 외부 MCP는 선택으로
                    SourceUrl = ext.Url,
                    InstallPath = "" // 외부 MCP는 설치 경로 없음
                });
            }
        }

        return result.GroupBy(r => r.Name).Select(g => g.First())
            .OrderBy(r => r.Priority).ToList();
    }

    /// <summary>
    /// 리소스 설명 가져오기 (하드코딩 + GitHub 이름 기반)
    /// </summary>
    private string GetResourceDescription(string name, ResourceType type)
    {
        // 먼저 하드코딩된 정의에서 검색
        var hardcodedDesc = type switch
        {
            ResourceType.Skill => SkillDefinitions.FirstOrDefault(s => s.Name == name).Desc,
            ResourceType.Agent => AgentDefinitions.FirstOrDefault(a => a.Name == name).Desc,
            ResourceType.Command => CommandDefinitions.FirstOrDefault(c => c.Name == name).Desc,
            ResourceType.Hook => HookDefinitions.FirstOrDefault(h => h.Name == name).Desc,
            ResourceType.MCP => MCPDefinitions.FirstOrDefault(m => m.Name == name).Desc,
            _ => null
        };

        if (!string.IsNullOrEmpty(hardcodedDesc))
            return hardcodedDesc;

        // 이름에서 설명 추론
        return name.Replace("-", " ").Replace("_", " ");
    }

    /// <summary>
    /// 리소스 카테고리 결정
    /// </summary>
    private string GetResourceCategory(string name, ResourceType type)
    {
        return type switch
        {
            ResourceType.Skill => GetSkillCategory(name),
            ResourceType.Agent => GetAgentCategory(name),
            ResourceType.Command => "커맨드",
            ResourceType.Hook => "훅",
            ResourceType.MCP => "MCP",
            _ => "기타"
        };
    }

    /// <summary>
    /// 리소스 우선순위 결정
    /// </summary>
    private int GetResourcePriority(string name, ResourceType type, ProjectStack? stack)
    {
        // 하드코딩된 정의에서 검색
        var hardcodedPriority = type switch
        {
            ResourceType.Skill => SkillDefinitions.FirstOrDefault(s => s.Name == name).Priority,
            ResourceType.Agent => AgentDefinitions.FirstOrDefault(a => a.Name == name).Priority,
            ResourceType.Command => CommandDefinitions.FirstOrDefault(c => c.Name == name).Priority,
            ResourceType.Hook => HookDefinitions.FirstOrDefault(h => h.Name == name).Priority,
            ResourceType.MCP => MCPDefinitions.FirstOrDefault(m => m.Name == name).Priority,
            _ => 0
        };

        if (hardcodedPriority > 0)
            return hardcodedPriority;

        // 프로젝트 스택 기반 우선순위
        if (stack != null)
        {
            var allTech = stack.DetectedTechnologies.Concat(stack.DetectedFrameworks).ToHashSet();

            // 이름에 기술 스택이 포함되면 우선순위 높임
            foreach (var tech in allTech)
            {
                if (name.Contains(tech.ToLower().Replace(" ", "-").Replace(".", "")))
                    return 2;
            }
        }

        return 3; // 기본 우선순위
    }

    /// <summary>
    /// 소스 URL 결정
    /// </summary>
    private string GetSourceUrl(GitHubResource resource, ResourceType type)
    {
        return type switch
        {
            ResourceType.Skill => $"{RawBase}/skills/{resource.Name}/skill.md",
            ResourceType.Agent => $"{RawBase}/agents/{resource.Name}.md",
            ResourceType.Command => $"{RawBase}/commands/{resource.Name}.md",
            ResourceType.Hook => $"{RawBase}/hooks/{resource.Name}",
            ResourceType.MCP => $"{RawBase}/mcp-servers/{resource.Name}",
            _ => resource.Url
        };
    }

    /// <summary>
    /// 설치 경로 결정
    /// </summary>
    private string GetInstallPath(string name, ResourceType type)
    {
        return type switch
        {
            ResourceType.Skill => $".claude/skills/{name}.md",
            ResourceType.Agent => $".claude/agents/{name}.md",
            ResourceType.Command => $".claude/commands/{name}.md",
            ResourceType.Hook => $"hooks/{name}",
            ResourceType.MCP => $".claude/mcp-servers/{name}",
            _ => ""
        };
    }

    /// <summary>
    /// 모든 사용 가능한 리소스 가져오기 (GitHub + 하드코딩 병합)
    /// </summary>
    public async Task<RecommendationResult> GetAllAvailableResources(string projectPath)
    {
        var stack = AnalyzeProject(projectPath);

        // GitHub 캐시 갱신 시도 (실패해도 계속 진행)
        try
        {
            await RefreshGitHubCacheIfNeeded();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillRecommendation] GitHub 캐시 갱신 실패, 하드코딩 사용: {ex.Message}");
        }

        var result = new RecommendationResult { Stack = stack };

        // GitHub 동적 리소스 가져오기
        var dynamicSkills = await GetDynamicResources(ResourceType.Skill, stack);
        var dynamicAgents = await GetDynamicResources(ResourceType.Agent, stack);
        var dynamicCommands = await GetDynamicResources(ResourceType.Command, stack);
        var dynamicHooks = await GetDynamicResources(ResourceType.Hook, stack);
        var dynamicMCPs = await GetDynamicResources(ResourceType.MCP, stack);

        // 하드코딩된 정의도 함께 사용 (폴백 + 병합)
        var hardcodedSkills = GetRecommendedSkills(stack);
        var hardcodedAgents = GetRecommendedAgents(stack);
        var hardcodedCommands = GetRecommendedCommands(stack);
        var hardcodedHooks = GetRecommendedHooks(stack);
        var hardcodedMCPs = GetRecommendedMCPs(stack);

        // 병합 (중복 제거, 하드코딩 우선)
        result.Skills = MergeResources(hardcodedSkills, dynamicSkills);
        result.Agents = MergeResources(hardcodedAgents, dynamicAgents);
        result.Commands = MergeResources(hardcodedCommands, dynamicCommands);
        result.Hooks = MergeResources(hardcodedHooks, dynamicHooks);
        result.MCPs = MergeResources(hardcodedMCPs, dynamicMCPs);

        Debug.WriteLine($"[SkillRecommendation] 최종 결과: Skills={result.Skills.Count}, Agents={result.Agents.Count}, Commands={result.Commands.Count}, Hooks={result.Hooks.Count}, MCPs={result.MCPs.Count}");

        // 설치 여부 체크
        await CheckInstalledStatus(result, projectPath);

        return result;
    }

    /// <summary>
    /// 두 리소스 목록 병합 (hardcoded 우선, 중복 제거)
    /// </summary>
    private List<RecommendedResource> MergeResources(List<RecommendedResource> hardcoded, List<RecommendedResource> dynamic)
    {
        var result = new Dictionary<string, RecommendedResource>(StringComparer.OrdinalIgnoreCase);

        // 하드코딩된 것 먼저 추가
        foreach (var r in hardcoded)
        {
            if (!result.ContainsKey(r.Name))
                result[r.Name] = r;
        }

        // 동적으로 가져온 것 추가 (중복 시 스킵)
        foreach (var r in dynamic)
        {
            if (!result.ContainsKey(r.Name))
                result[r.Name] = r;
        }

        return result.Values.OrderBy(r => r.Priority).ThenBy(r => r.Name).ToList();
    }

    /// <summary>
    /// 외부 MCP 서버 목록만 가져오기
    /// </summary>
    public async Task<List<ExternalMCPInfo>> GetExternalMCPs()
    {
        await RefreshGitHubCacheIfNeeded();
        return _cachedExternalMCPs ?? new();
    }

    #endregion

    #region Utilities

    /// <summary>
    /// 설치 경로 결정
    /// </summary>
    private string GetInstallPath(RecommendedResource resource, string projectPath)
    {
        return Path.Combine(projectPath, resource.InstallPath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// 글로벌 Claude 설정 경로 (~/.claude/)
    /// </summary>
    private static readonly string GlobalClaudePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    /// <summary>
    /// 설치 여부 확인 (프로젝트 + 글로벌 경로 모두 확인)
    /// </summary>
    private Task CheckInstalledStatus(RecommendationResult result, string projectPath)
    {
        // 프로젝트 경로만 확인 (Hooks 등 글로벌 경로가 없는 리소스)
        void CheckProjectOnly(List<RecommendedResource> resources)
        {
            foreach (var r in resources)
            {
                var path = GetInstallPath(r, projectPath);
                r.IsInstalled = File.Exists(path) || Directory.Exists(path);
            }
        }

        // 프로젝트 + 글로벌 경로 모두 확인 (Skills, Agents, Commands)
        void CheckWithGlobal(List<RecommendedResource> resources)
        {
            foreach (var r in resources)
            {
                // Mnemo는 글로벌 settings.json 훅으로 설치 확인
                if (r.Name == "mnemo")
                {
                    r.IsInstalled = IsMnemoInstalled();
                    r.IsGlobalInstall = true;
                    continue;
                }

                // 1. 프로젝트 경로 확인
                var projectInstallPath = GetInstallPath(r, projectPath);
                if (File.Exists(projectInstallPath) || Directory.Exists(projectInstallPath))
                {
                    r.IsInstalled = true;
                    r.IsGlobalInstall = false;
                    continue;
                }

                // 2. 글로벌 경로 확인 (~/.claude/skills/, ~/.claude/agents/, ~/.claude/commands/)
                // InstallPath가 ".claude/skills/name.md"이므로 ".claude/" 접두사를 제거하여
                // ~/.claude/ + skills/name.md 로 올바르게 결합
                var installPath = r.InstallPath.Replace('/', Path.DirectorySeparatorChar);
                var claudePrefix = $".claude{Path.DirectorySeparatorChar}";
                if (installPath.StartsWith(claudePrefix))
                {
                    installPath = installPath.Substring(claudePrefix.Length);
                }
                var globalPath = Path.Combine(GlobalClaudePath, installPath);

                if (File.Exists(globalPath) || Directory.Exists(globalPath))
                {
                    r.IsInstalled = true;
                    r.IsGlobalInstall = true;
                    continue;
                }

                r.IsInstalled = false;
                r.IsGlobalInstall = false;
            }
        }

        // MCP 서버는 settings.local.json 기반으로 확인
        void CheckMcpList(List<RecommendedResource> mcps)
        {
            foreach (var r in mcps)
            {
                r.IsInstalled = IsMcpServerInstalled(r.Name, projectPath);
            }
        }

        CheckWithGlobal(result.Skills);    // 글로벌 + 프로젝트
        CheckWithGlobal(result.Agents);    // 글로벌 + 프로젝트
        CheckWithGlobal(result.Commands);  // 글로벌 + 프로젝트
        CheckProjectOnly(result.Hooks);    // 프로젝트만 (hooks는 로컬)
        CheckMcpList(result.MCPs);         // settings.local.json 기반

        return Task.CompletedTask;
    }

    /// <summary>
    /// 설치 스크립트 생성
    /// </summary>
    public string GenerateInstallScript(RecommendationResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Claude Code 리소스 설치 스크립트");
        sb.AppendLine($"# 프로젝트: {result.Stack.ProjectName}");
        sb.AppendLine($"# 생성일: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"# 감지된 기술: {string.Join(", ", result.Stack.DetectedTechnologies)}");
        sb.AppendLine($"# 감지된 프레임워크: {string.Join(", ", result.Stack.DetectedFrameworks)}");
        sb.AppendLine();

        void WriteSection(string title, List<RecommendedResource> resources)
        {
            if (!resources.Any()) return;

            sb.AppendLine($"# === {title} ({resources.Count}개) ===");
            foreach (var r in resources.OrderBy(x => x.Priority))
            {
                var prefix = r.Priority switch
                {
                    1 => "[필수]",
                    2 => "[권장]",
                    _ => "[선택]"
                };
                sb.AppendLine($"# {prefix} {r.Name}: {r.Description}");
                sb.AppendLine($"# curl -o \"{r.InstallPath}\" \"{r.SourceUrl}\"");
            }
            sb.AppendLine();
        }

        WriteSection("스킬", result.Skills);
        WriteSection("에이전트", result.Agents);
        WriteSection("커맨드", result.Commands);
        WriteSection("훅", result.Hooks);
        WriteSection("MCP", result.MCPs);

        return sb.ToString();
    }

    /// <summary>
    /// GitHub에서 사용 가능한 모든 스킬 목록 가져오기
    /// </summary>
    public async Task<List<string>> GetAvailableSkillsFromGitHub()
    {
        try
        {
            var response = await _httpClient.GetStringAsync($"{RepoBase}/skills");
            var json = JsonDocument.Parse(response);

            var skills = new List<string>();
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var name) &&
                    item.TryGetProperty("type", out var type) &&
                    type.GetString() == "dir")
                {
                    skills.Add(name.GetString() ?? "");
                }
            }

            return skills.Where(s => !string.IsNullOrEmpty(s)).ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillRecommendation] GitHub 목록 조회 실패: {ex.Message}");
            return new List<string>();
        }
    }

    #endregion
}
