using System;
using System.Collections.Concurrent;
using TermSnap.Models;

namespace TermSnap.Core;

/// <summary>
/// 싱글톤 서비스 컨테이너 - 앱 전역 서비스 관리
/// </summary>
public sealed class ServiceLocator : IDisposable
{
    private static readonly Lazy<ServiceLocator> _instance = 
        new(() => new ServiceLocator(), isThreadSafe: true);
    
    public static ServiceLocator Instance => _instance.Value;

    private readonly ConcurrentDictionary<Type, object> _services = new();
    private readonly ConcurrentDictionary<Type, Func<object>> _factories = new();
    private readonly object _lock = new();
    private bool _disposed = false;

    private ServiceLocator() { }

    /// <summary>
    /// 싱글톤 서비스 등록
    /// </summary>
    public void Register<T>(T service) where T : class
    {
        if (service == null) throw new ArgumentNullException(nameof(service));
        _services[typeof(T)] = service;
    }

    /// <summary>
    /// 팩토리 메서드 등록 (지연 생성)
    /// </summary>
    public void RegisterFactory<T>(Func<T> factory) where T : class
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        _factories[typeof(T)] = () => factory();
    }

    /// <summary>
    /// 서비스 가져오기 (없으면 예외)
    /// </summary>
    public T Get<T>() where T : class
    {
        var type = typeof(T);

        // 이미 등록된 인스턴스가 있는지 확인
        if (_services.TryGetValue(type, out var service))
        {
            return (T)service;
        }

        // 팩토리가 있으면 생성 후 등록
        if (_factories.TryGetValue(type, out var factory))
        {
            lock (_lock)
            {
                // 이중 체크 (다른 스레드에서 이미 생성했을 수 있음)
                if (_services.TryGetValue(type, out service))
                {
                    return (T)service;
                }

                service = factory();
                _services[type] = service;
                return (T)service;
            }
        }

        throw new InvalidOperationException($"서비스 '{type.Name}'가 등록되지 않았습니다.");
    }

    /// <summary>
    /// 서비스 가져오기 (없으면 null)
    /// </summary>
    public T? TryGet<T>() where T : class
    {
        try
        {
            return Get<T>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 서비스가 등록되어 있는지 확인
    /// </summary>
    public bool IsRegistered<T>() where T : class
    {
        var type = typeof(T);
        return _services.ContainsKey(type) || _factories.ContainsKey(type);
    }

    /// <summary>
    /// 모든 서비스 해제
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var service in _services.Values)
            {
                if (service is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"서비스 해제 실패: {ex.Message}");
                    }
                }
            }

            _services.Clear();
            _factories.Clear();
        }
    }

    /// <summary>
    /// 특정 서비스 해제 및 제거
    /// </summary>
    public void Unregister<T>() where T : class
    {
        var type = typeof(T);
        
        if (_services.TryRemove(type, out var service))
        {
            if (service is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _factories.TryRemove(type, out _);
    }
}
