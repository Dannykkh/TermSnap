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

    static SkillRecommendationService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TermSnap-SkillRecommender");
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
    /// 리소스 설치
    /// </summary>
    public async Task<bool> InstallResource(RecommendedResource resource, string projectPath)
    {
        try
        {
            // 원본 파일 다운로드
            var content = await _httpClient.GetStringAsync(resource.SourceUrl);

            // 설치 경로 결정
            var installPath = GetInstallPath(resource, projectPath);
            var dir = Path.GetDirectoryName(installPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

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
                result.Add(new RecommendedResource
                {
                    Name = name,
                    Description = desc,
                    Type = ResourceType.Skill,
                    Category = GetSkillCategory(name),
                    Priority = priority,
                    SourceUrl = $"{RawBase}/skills/{name}/skill.md",
                    InstallPath = $".claude/skills/{name}.md"
                });
            }
        }

        return result.GroupBy(r => r.Name).Select(g => g.First())
            .OrderBy(r => r.Priority).ToList();
    }

    private string GetSkillCategory(string name)
    {
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
        ("claude-orchestrator-mcp", "Claude 오케스트레이터 MCP (에이전트 관리)", new[] { "*" }, 2),
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

    #region Utilities

    /// <summary>
    /// 설치 경로 결정
    /// </summary>
    private string GetInstallPath(RecommendedResource resource, string projectPath)
    {
        return Path.Combine(projectPath, resource.InstallPath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// 설치 여부 확인
    /// </summary>
    private Task CheckInstalledStatus(RecommendationResult result, string projectPath)
    {
        void CheckList(List<RecommendedResource> resources)
        {
            foreach (var r in resources)
            {
                var path = GetInstallPath(r, projectPath);
                r.IsInstalled = File.Exists(path) || Directory.Exists(path);
            }
        }

        CheckList(result.Skills);
        CheckList(result.Agents);
        CheckList(result.Commands);
        CheckList(result.Hooks);
        CheckList(result.MCPs);

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
