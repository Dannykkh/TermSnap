using System;
using System.Collections.Concurrent;
using TermSnap.Models;
using TermSnap.Services.Agents;

namespace TermSnap.Services;

/// <summary>
/// 에이전트 서비스 매니저 - 앱 전역 싱글톤 + 탭별 인스턴스 관리
/// </summary>
public class AgentServiceManager : IDisposable
{
    private static readonly Lazy<AgentServiceManager> _instance =
        new(() => new AgentServiceManager());

    /// <summary>
    /// 앱 전역 싱글톤 인스턴스
    /// </summary>
    public static AgentServiceManager Instance => _instance.Value;

    // ===== 앱 전역 싱글톤 서비스 (설정/라우팅) =====
    private readonly Lazy<SmartRouterService> _router;
    private readonly Lazy<AgentRegistry> _agentRegistry;

    // ===== 탭별 인스턴스 관리 =====
    private readonly ConcurrentDictionary<string, AgentOrchestrator> _orchestrators = new();
    private readonly ConcurrentDictionary<string, ParallelAgentService> _parallelServices = new();

    private bool _disposed;

    private AgentServiceManager()
    {
        // 앱 전역 서비스는 Lazy로 지연 초기화
        _router = new Lazy<SmartRouterService>(() => new SmartRouterService());
        _agentRegistry = new Lazy<AgentRegistry>(() => new AgentRegistry(_router.Value));
    }

    #region 앱 전역 싱글톤 서비스

    /// <summary>
    /// 스마트 라우터 (앱 전역 싱글톤)
    /// - 설정 읽기
    /// - 복잡도 분석
    /// - 모델 선택
    /// </summary>
    public SmartRouterService Router => _router.Value;

    /// <summary>
    /// 에이전트 레지스트리 (앱 전역 싱글톤)
    /// - Oracle, Librarian 등 에이전트 인스턴스 관리
    /// - 상태가 없는 서비스이므로 공유 가능
    /// </summary>
    public AgentRegistry Agents => _agentRegistry.Value;

    /// <summary>
    /// 설정 변경 시 호출 (모델 목록 새로고침)
    /// </summary>
    public void RefreshConfiguration()
    {
        if (_router.IsValueCreated)
        {
            _router.Value.RefreshAvailableModels();
        }
    }

    #endregion

    #region 탭별 인스턴스 관리

    /// <summary>
    /// 탭별 오케스트레이터 가져오기 (없으면 생성)
    /// </summary>
    /// <param name="tabId">탭 고유 ID</param>
    public AgentOrchestrator GetOrchestrator(string tabId)
    {
        return _orchestrators.GetOrAdd(tabId, _ =>
            new AgentOrchestrator(Router));
    }

    /// <summary>
    /// 탭별 병렬 에이전트 서비스 가져오기 (없으면 생성)
    /// </summary>
    /// <param name="tabId">탭 고유 ID</param>
    /// <param name="maxConcurrency">최대 동시 실행 수</param>
    public ParallelAgentService GetParallelService(string tabId, int maxConcurrency = 3)
    {
        return _parallelServices.GetOrAdd(tabId, _ =>
            new ParallelAgentService(maxConcurrency, Router));
    }

    /// <summary>
    /// 탭 닫힐 때 리소스 해제
    /// </summary>
    public void ReleaseTab(string tabId)
    {
        if (_orchestrators.TryRemove(tabId, out var orchestrator))
        {
            orchestrator.Dispose();
        }

        if (_parallelServices.TryRemove(tabId, out var parallelService))
        {
            parallelService.Dispose();
        }
    }

    /// <summary>
    /// 특정 탭의 오케스트레이터가 있는지 확인
    /// </summary>
    public bool HasOrchestrator(string tabId) => _orchestrators.ContainsKey(tabId);

    /// <summary>
    /// 활성 탭 수
    /// </summary>
    public int ActiveTabCount => _orchestrators.Count;

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var orchestrator in _orchestrators.Values)
        {
            orchestrator.Dispose();
        }
        _orchestrators.Clear();

        foreach (var service in _parallelServices.Values)
        {
            service.Dispose();
        }
        _parallelServices.Clear();

        _disposed = true;
    }

    #endregion
}

/// <summary>
/// 에이전트 레지스트리 - 전문 에이전트 인스턴스 관리 (앱 전역 싱글톤)
/// </summary>
public class AgentRegistry
{
    private readonly SmartRouterService _router;

    // 에이전트들은 상태가 없으므로 싱글톤으로 공유 가능
    private readonly Lazy<OracleAgent> _oracle;
    private readonly Lazy<LibrarianAgent> _librarian;
    private readonly Lazy<FrontendEngineerAgent> _frontendEngineer;
    private readonly Lazy<BackendEngineerAgent> _backendEngineer;
    private readonly Lazy<DevOpsAgent> _devOps;

    public AgentRegistry(SmartRouterService router)
    {
        _router = router;

        _oracle = new Lazy<OracleAgent>(() => new OracleAgent(_router));
        _librarian = new Lazy<LibrarianAgent>(() => new LibrarianAgent(_router));
        _frontendEngineer = new Lazy<FrontendEngineerAgent>(() => new FrontendEngineerAgent(_router));
        _backendEngineer = new Lazy<BackendEngineerAgent>(() => new BackendEngineerAgent(_router));
        _devOps = new Lazy<DevOpsAgent>(() => new DevOpsAgent(_router));
    }

    public OracleAgent Oracle => _oracle.Value;
    public LibrarianAgent Librarian => _librarian.Value;
    public FrontendEngineerAgent FrontendEngineer => _frontendEngineer.Value;
    public BackendEngineerAgent BackendEngineer => _backendEngineer.Value;
    public DevOpsAgent DevOps => _devOps.Value;

    /// <summary>
    /// 역할로 에이전트 가져오기
    /// </summary>
    public ISubAgent? GetAgent(AgentRole role) => role switch
    {
        AgentRole.Oracle => Oracle,
        AgentRole.Librarian => Librarian,
        AgentRole.FrontendEngineer => FrontendEngineer,
        AgentRole.BackendEngineer => BackendEngineer,
        AgentRole.DevOps => DevOps,
        _ => null
    };
}
