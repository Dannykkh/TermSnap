using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TermSnap.Models;

/// <summary>
/// 에이전트 역할 (전문 분야)
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum AgentRole
{
    /// <summary>
    /// 일반 에이전트 - 기본 작업 처리
    /// </summary>
    General,

    /// <summary>
    /// 오라클 - 고급 조언, 전략 수립, 아키텍처 설계
    /// </summary>
    Oracle,

    /// <summary>
    /// 라이브러리안 - 문서 검색, API 참조, 라이브러리 사용법
    /// </summary>
    Librarian,

    /// <summary>
    /// 프론트엔드 엔지니어 - UI/UX, React, CSS, 웹 개발
    /// </summary>
    FrontendEngineer,

    /// <summary>
    /// 백엔드 엔지니어 - API, 데이터베이스, 서버 로직
    /// </summary>
    BackendEngineer,

    /// <summary>
    /// DevOps 엔지니어 - 배포, CI/CD, 인프라, Docker
    /// </summary>
    DevOps,

    /// <summary>
    /// 보안 전문가 - 보안 분석, 취약점 검토
    /// </summary>
    SecurityExpert,

    /// <summary>
    /// 테스트 엔지니어 - 테스트 작성, QA
    /// </summary>
    TestEngineer,

    /// <summary>
    /// 코드 리뷰어 - 코드 품질, 베스트 프랙티스
    /// </summary>
    CodeReviewer
}

/// <summary>
/// 에이전트 역할별 기본 정보
/// </summary>
public static class AgentRoleInfo
{
    /// <summary>
    /// 역할별 표시 이름
    /// </summary>
    public static string GetDisplayName(AgentRole role) => role switch
    {
        AgentRole.General => "General Agent",
        AgentRole.Oracle => "Oracle (Strategy)",
        AgentRole.Librarian => "Librarian (Docs)",
        AgentRole.FrontendEngineer => "Frontend Engineer",
        AgentRole.BackendEngineer => "Backend Engineer",
        AgentRole.DevOps => "DevOps Engineer",
        AgentRole.SecurityExpert => "Security Expert",
        AgentRole.TestEngineer => "Test Engineer",
        AgentRole.CodeReviewer => "Code Reviewer",
        _ => role.ToString()
    };

    /// <summary>
    /// 역할별 아이콘 (Material Design Icons)
    /// </summary>
    public static string GetIcon(AgentRole role) => role switch
    {
        AgentRole.General => "Robot",
        AgentRole.Oracle => "Crystal Ball",
        AgentRole.Librarian => "BookOpenPageVariant",
        AgentRole.FrontendEngineer => "MonitorDashboard",
        AgentRole.BackendEngineer => "ServerNetwork",
        AgentRole.DevOps => "Docker",
        AgentRole.SecurityExpert => "ShieldCheck",
        AgentRole.TestEngineer => "TestTube",
        AgentRole.CodeReviewer => "CodeBraces",
        _ => "Robot"
    };

    /// <summary>
    /// 역할별 권장 모델 티어
    /// </summary>
    public static ModelTier GetRecommendedTier(AgentRole role) => role switch
    {
        AgentRole.Oracle => ModelTier.Powerful,          // 고급 분석 필요
        AgentRole.SecurityExpert => ModelTier.Powerful,  // 보안 분석 중요
        AgentRole.Librarian => ModelTier.Fast,           // 검색 위주
        AgentRole.CodeReviewer => ModelTier.Balanced,
        AgentRole.FrontendEngineer => ModelTier.Balanced,
        AgentRole.BackendEngineer => ModelTier.Balanced,
        AgentRole.DevOps => ModelTier.Balanced,
        AgentRole.TestEngineer => ModelTier.Fast,
        AgentRole.General => ModelTier.Balanced,
        _ => ModelTier.Balanced
    };
}
