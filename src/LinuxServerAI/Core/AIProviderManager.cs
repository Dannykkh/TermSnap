using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Nebula.Models;
using Nebula.Services;

namespace Nebula.Core;

/// <summary>
/// AI Provider 싱글톤 매니저 - Provider 인스턴스 캐싱 및 관리
/// </summary>
public sealed class AIProviderManager : IDisposable
{
    private static readonly Lazy<AIProviderManager> _instance =
        new(() => new AIProviderManager(), isThreadSafe: true);

    public static AIProviderManager Instance => _instance.Value;

    // Provider 캐시 (키: "Provider_ModelId")
    private readonly ConcurrentDictionary<string, IAIProvider> _providerCache = new();
    private readonly ConcurrentDictionary<string, IEmbeddingService> _embeddingCache = new();
    private readonly object _lock = new();
    private bool _disposed = false;

    // 현재 활성 Provider
    private IAIProvider? _currentProvider;
    private IEmbeddingService? _currentEmbeddingService;
    private AIModelConfig? _currentConfig;
    private EmbeddingSettings? _embeddingSettings;

    // 로컬 임베딩 서비스 (싱글톤)
    private LocalEmbeddingService? _localEmbeddingService;
    private bool _localModelInitializing = false;

    private AIProviderManager() { }

    /// <summary>
    /// 현재 활성 AI Provider
    /// </summary>
    public IAIProvider? CurrentProvider => _currentProvider;

    /// <summary>
    /// 현재 활성 Embedding Service
    /// </summary>
    public IEmbeddingService? CurrentEmbeddingService => _currentEmbeddingService;

    /// <summary>
    /// 현재 설정
    /// </summary>
    public AIModelConfig? CurrentConfig => _currentConfig;

    /// <summary>
    /// 현재 임베딩 설정
    /// </summary>
    public EmbeddingSettings? EmbeddingSettings => _embeddingSettings;

    /// <summary>
    /// Provider가 초기화되었는지
    /// </summary>
    public bool IsInitialized => _currentProvider != null;

    /// <summary>
    /// 로컬 임베딩 모델 사용 가능 여부
    /// </summary>
    public bool IsLocalEmbeddingAvailable => LocalEmbeddingService.IsModelAvailable();

    /// <summary>
    /// 로컬 임베딩 모델 다운로드 진행 중 여부
    /// </summary>
    public bool IsLocalModelInitializing => _localModelInitializing;

    /// <summary>
    /// 임베딩 설정 초기화
    /// </summary>
    public async Task InitializeEmbeddingAsync(EmbeddingSettings settings)
    {
        _embeddingSettings = settings;

        switch (settings.Type)
        {
            case EmbeddingType.Local:
                await InitializeLocalEmbeddingAsync();
                break;

            case EmbeddingType.API:
                // API 임베딩은 현재 Provider의 API 키 사용
                if (_currentConfig != null && _currentConfig.IsConfigured)
                {
                    var apiEmbedding = new ApiEmbeddingService(settings.ApiProvider, _currentConfig.ApiKey);
                    _currentEmbeddingService = apiEmbedding;
                }
                break;

            case EmbeddingType.Disabled:
                _currentEmbeddingService = null;
                break;
        }
    }

    /// <summary>
    /// 로컬 임베딩 서비스 초기화
    /// </summary>
    private async Task InitializeLocalEmbeddingAsync()
    {
        if (_localEmbeddingService != null && _localEmbeddingService.IsReady)
        {
            _currentEmbeddingService = _localEmbeddingService;
            return;
        }

        if (!LocalEmbeddingService.IsModelAvailable())
        {
            // 모델이 없으면 다운로드 필요
            _currentEmbeddingService = null;
            return;
        }

        try
        {
            _localModelInitializing = true;
            _localEmbeddingService = new LocalEmbeddingService();
            await _localEmbeddingService.InitializeAsync();
            _currentEmbeddingService = _localEmbeddingService;

            if (_embeddingSettings != null)
            {
                _embeddingSettings.LocalModelDownloaded = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"로컬 임베딩 초기화 실패: {ex.Message}");
            _currentEmbeddingService = null;
        }
        finally
        {
            _localModelInitializing = false;
        }
    }

    /// <summary>
    /// 로컬 임베딩 모델 다운로드
    /// </summary>
    public async Task<bool> DownloadLocalEmbeddingModelAsync(IProgress<(string status, int percent)>? progress = null)
    {
        try
        {
            _localModelInitializing = true;
            await LocalEmbeddingService.DownloadModelAsync(progress);

            // 다운로드 후 초기화
            _localEmbeddingService = new LocalEmbeddingService();
            await _localEmbeddingService.InitializeAsync();
            _currentEmbeddingService = _localEmbeddingService;

            if (_embeddingSettings != null)
            {
                _embeddingSettings.LocalModelDownloaded = true;
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"모델 다운로드 실패: {ex.Message}");
            throw;
        }
        finally
        {
            _localModelInitializing = false;
        }
    }

    /// <summary>
    /// 현재 Provider 설정 (앱 시작 시 또는 설정 변경 시 호출)
    /// </summary>
    public void SetCurrentProvider(AIModelConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (!config.IsConfigured) throw new InvalidOperationException("API 키가 설정되지 않았습니다.");

        lock (_lock)
        {
            _currentConfig = config;

            // 캐시에서 Provider 조회 또는 생성
            var cacheKey = GetCacheKey(config);

            if (!_providerCache.TryGetValue(cacheKey, out var provider))
            {
                provider = AIProviderFactory.CreateProviderFromConfig(config);
                _providerCache[cacheKey] = provider;
            }
            _currentProvider = provider;

            // 임베딩 설정이 API면 업데이트
            if (_embeddingSettings?.Type == EmbeddingType.API)
            {
                var embeddingKey = $"api_{config.Provider}_{config.ApiKey[..Math.Min(8, config.ApiKey.Length)]}";
                if (!_embeddingCache.TryGetValue(embeddingKey, out var embeddingService))
                {
                    embeddingService = new ApiEmbeddingService(config.Provider, config.ApiKey);
                    _embeddingCache[embeddingKey] = embeddingService;
                }
                _currentEmbeddingService = embeddingService;
            }
        }
    }

    /// <summary>
    /// 특정 설정으로 Provider 가져오기 (캐시 사용)
    /// </summary>
    public IAIProvider GetProvider(AIModelConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (!config.IsConfigured) throw new InvalidOperationException("API 키가 설정되지 않았습니다.");

        var cacheKey = GetCacheKey(config);

        return _providerCache.GetOrAdd(cacheKey, _ =>
            AIProviderFactory.CreateProviderFromConfig(config));
    }

    /// <summary>
    /// 특정 설정으로 API Embedding 서비스 가져오기 (캐시 사용)
    /// </summary>
    public IEmbeddingService GetApiEmbeddingService(AIModelConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (!config.IsConfigured) throw new InvalidOperationException("API 키가 설정되지 않았습니다.");

        var cacheKey = $"api_{config.Provider}_{config.ApiKey[..Math.Min(8, config.ApiKey.Length)]}";

        return _embeddingCache.GetOrAdd(cacheKey, _ =>
            new ApiEmbeddingService(config.Provider, config.ApiKey));
    }

    /// <summary>
    /// 로컬 Embedding 서비스 가져오기
    /// </summary>
    public LocalEmbeddingService? GetLocalEmbeddingService()
    {
        return _localEmbeddingService;
    }

    /// <summary>
    /// Provider 타입별로 가져오기
    /// </summary>
    public IAIProvider? GetProviderByType(AIProviderType type)
    {
        foreach (var kvp in _providerCache)
        {
            if (kvp.Key.StartsWith(type.ToString()))
            {
                return kvp.Value;
            }
        }
        return null;
    }

    /// <summary>
    /// 캐시 키 생성
    /// </summary>
    private static string GetCacheKey(AIModelConfig config)
    {
        // API 키 앞 8자리만 사용 (보안상 전체 키 사용 안 함)
        var keyPrefix = config.ApiKey.Length > 8
            ? config.ApiKey[..8]
            : config.ApiKey;
        return $"{config.Provider}_{config.ModelId}_{keyPrefix}";
    }

    /// <summary>
    /// 특정 Provider 캐시 제거
    /// </summary>
    public void InvalidateProvider(AIModelConfig config)
    {
        var cacheKey = GetCacheKey(config);
        _providerCache.TryRemove(cacheKey, out _);

        if (_currentConfig != null && GetCacheKey(_currentConfig) == cacheKey)
        {
            _currentProvider = null;
            _currentConfig = null;
        }
    }

    /// <summary>
    /// 모든 캐시 제거
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            _providerCache.Clear();

            // Embedding 서비스 Dispose
            foreach (var service in _embeddingCache.Values)
            {
                service?.Dispose();
            }
            _embeddingCache.Clear();

            _currentProvider = null;
            _currentEmbeddingService = null;
            _currentConfig = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            // EmbeddingService들 Dispose
            foreach (var embeddingService in _embeddingCache.Values)
            {
                embeddingService?.Dispose();
            }

            _localEmbeddingService?.Dispose();
            _localEmbeddingService = null;

            _providerCache.Clear();
            _embeddingCache.Clear();
            _currentProvider = null;
            _currentEmbeddingService = null;
            _currentConfig = null;
        }
    }
}
