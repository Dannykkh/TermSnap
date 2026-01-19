using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Nebula.Core;

/// <summary>
/// 고정 크기 Ring Buffer를 지원하는 ObservableCollection
/// 최대 크기를 초과하면 오래된 항목을 자동 삭제
/// </summary>
public class RingBufferCollection<T> : ObservableCollection<T>
{
    private readonly int _maxSize;
    private readonly int _trimSize; // 한 번에 삭제할 개수 (성능 최적화)

    /// <summary>
    /// Ring Buffer Collection 생성
    /// </summary>
    /// <param name="maxSize">최대 보관 개수</param>
    /// <param name="trimPercent">초과 시 삭제할 비율 (기본 20%)</param>
    public RingBufferCollection(int maxSize = 1000, double trimPercent = 0.2)
    {
        _maxSize = maxSize;
        _trimSize = Math.Max(1, (int)(maxSize * trimPercent));
    }

    /// <summary>
    /// 현재 최대 크기
    /// </summary>
    public int MaxSize => _maxSize;

    /// <summary>
    /// 항목 추가 (오래된 항목 자동 삭제)
    /// </summary>
    public new void Add(T item)
    {
        // 최대 크기 초과 시 오래된 항목 일괄 삭제
        if (Count >= _maxSize)
        {
            TrimOldItems();
        }

        base.Add(item);
    }

    /// <summary>
    /// 오래된 항목 일괄 삭제 (성능 최적화)
    /// </summary>
    private void TrimOldItems()
    {
        // 이벤트 일시 중지를 위해 직접 RemoveAt 호출
        for (int i = 0; i < _trimSize && Count > 0; i++)
        {
            RemoveAt(0);
        }
    }

    /// <summary>
    /// 버퍼 크기 정보
    /// </summary>
    public string GetBufferInfo()
    {
        return $"{Count}/{_maxSize} ({(double)Count / _maxSize * 100:F1}%)";
    }
}

/// <summary>
/// 기존 ObservableCollection에 Ring Buffer 기능 추가하는 확장 메서드
/// </summary>
public static class ObservableCollectionExtensions
{
    /// <summary>
    /// 최대 크기를 유지하면서 항목 추가
    /// </summary>
    public static void AddWithLimit<T>(this ObservableCollection<T> collection, T item, int maxSize, int trimCount = 100)
    {
        if (collection.Count >= maxSize)
        {
            // 오래된 항목 일괄 삭제
            for (int i = 0; i < trimCount && collection.Count > 0; i++)
            {
                collection.RemoveAt(0);
            }
        }

        collection.Add(item);
    }
}
