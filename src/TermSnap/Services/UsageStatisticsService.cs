using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// 사용량 통계 서비스 (싱글톤)
/// AI API 호출, 캐시 히트율, 명령어 통계 관리
/// </summary>
public class UsageStatisticsService : IDisposable
{
    private static readonly Lazy<UsageStatisticsService> _instance =
        new(() => new UsageStatisticsService(), isThreadSafe: true);

    public static UsageStatisticsService Instance => _instance.Value;

    // 세션 내 통계 (메모리)
    private long _sessionApiCalls = 0;
    private long _sessionCacheHits = 0;
    private long _sessionCacheMisses = 0;
    private DateTime _sessionStartTime = DateTime.Now;

    // 통계 변경 이벤트
    public event EventHandler? StatisticsChanged;

    private UsageStatisticsService()
    {
        InitializeDatabase();
    }

    #region Session Statistics (메모리)

    /// <summary>
    /// 세션 시작 시간
    /// </summary>
    public DateTime SessionStartTime => _sessionStartTime;

    /// <summary>
    /// 현재 세션 AI API 호출 횟수
    /// </summary>
    public long SessionApiCalls => _sessionApiCalls;

    /// <summary>
    /// 현재 세션 캐시 히트 횟수
    /// </summary>
    public long SessionCacheHits => _sessionCacheHits;

    /// <summary>
    /// 현재 세션 캐시 미스 횟수
    /// </summary>
    public long SessionCacheMisses => _sessionCacheMisses;

    /// <summary>
    /// 현재 세션 캐시 히트율 (%)
    /// </summary>
    public double SessionCacheHitRate
    {
        get
        {
            var total = _sessionCacheHits + _sessionCacheMisses;
            return total > 0 ? (double)_sessionCacheHits / total * 100 : 0;
        }
    }

    /// <summary>
    /// AI API 호출 기록
    /// </summary>
    public void RecordApiCall()
    {
        Interlocked.Increment(ref _sessionApiCalls);
        RecordDailyApiCall();
        StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 캐시 히트 기록
    /// </summary>
    public void RecordCacheHit()
    {
        Interlocked.Increment(ref _sessionCacheHits);
        RecordDailyCacheHit();
        StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 캐시 미스 기록 (AI 호출 필요)
    /// </summary>
    public void RecordCacheMiss()
    {
        Interlocked.Increment(ref _sessionCacheMisses);
        RecordDailyCacheMiss();
        StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 세션 통계 초기화
    /// </summary>
    public void ResetSessionStatistics()
    {
        _sessionApiCalls = 0;
        _sessionCacheHits = 0;
        _sessionCacheMisses = 0;
        _sessionStartTime = DateTime.Now;
        StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Database (영구 저장)

    private SqliteConnection? _connection;

    private void InitializeDatabase()
    {
        try
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TermSnap", "statistics.db");

            var dbDir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
            {
                Directory.CreateDirectory(dbDir);
            }

            _connection = new SqliteConnection($"Data Source={dbPath}");
            _connection.Open();

            CreateTables();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UsageStatisticsService] DB 초기화 실패: {ex.Message}");
        }
    }

    private void CreateTables()
    {
        if (_connection == null) return;

        using var command = _connection.CreateCommand();

        // 일별 통계 테이블
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS daily_statistics (
                date TEXT PRIMARY KEY,
                api_calls INTEGER DEFAULT 0,
                cache_hits INTEGER DEFAULT 0,
                cache_misses INTEGER DEFAULT 0,
                commands_executed INTEGER DEFAULT 0,
                commands_success INTEGER DEFAULT 0,
                commands_failed INTEGER DEFAULT 0
            );

            -- 명령어 카테고리별 통계 테이블
            CREATE TABLE IF NOT EXISTS category_statistics (
                category TEXT PRIMARY KEY,
                count INTEGER DEFAULT 0,
                last_used TEXT
            );
        ";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 일별 API 호출 기록
    /// </summary>
    private void RecordDailyApiCall()
    {
        if (_connection == null) return;

        try
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            using var command = _connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO daily_statistics (date, api_calls)
                VALUES (@date, 1)
                ON CONFLICT(date) DO UPDATE SET api_calls = api_calls + 1
            ";
            command.Parameters.AddWithValue("@date", today);
            command.ExecuteNonQuery();
        }
        catch { /* 무시 */ }
    }

    /// <summary>
    /// 일별 캐시 히트 기록
    /// </summary>
    private void RecordDailyCacheHit()
    {
        if (_connection == null) return;

        try
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            using var command = _connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO daily_statistics (date, cache_hits)
                VALUES (@date, 1)
                ON CONFLICT(date) DO UPDATE SET cache_hits = cache_hits + 1
            ";
            command.Parameters.AddWithValue("@date", today);
            command.ExecuteNonQuery();
        }
        catch { /* 무시 */ }
    }

    /// <summary>
    /// 일별 캐시 미스 기록
    /// </summary>
    private void RecordDailyCacheMiss()
    {
        if (_connection == null) return;

        try
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            using var command = _connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO daily_statistics (date, cache_misses)
                VALUES (@date, 1)
                ON CONFLICT(date) DO UPDATE SET cache_misses = cache_misses + 1
            ";
            command.Parameters.AddWithValue("@date", today);
            command.ExecuteNonQuery();
        }
        catch { /* 무시 */ }
    }

    /// <summary>
    /// 명령어 실행 기록
    /// </summary>
    public void RecordCommandExecution(bool isSuccess, string? category = null)
    {
        if (_connection == null) return;

        try
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            using var command = _connection.CreateCommand();

            if (isSuccess)
            {
                command.CommandText = @"
                    INSERT INTO daily_statistics (date, commands_executed, commands_success)
                    VALUES (@date, 1, 1)
                    ON CONFLICT(date) DO UPDATE SET
                        commands_executed = commands_executed + 1,
                        commands_success = commands_success + 1
                ";
            }
            else
            {
                command.CommandText = @"
                    INSERT INTO daily_statistics (date, commands_executed, commands_failed)
                    VALUES (@date, 1, 1)
                    ON CONFLICT(date) DO UPDATE SET
                        commands_executed = commands_executed + 1,
                        commands_failed = commands_failed + 1
                ";
            }
            command.Parameters.AddWithValue("@date", today);
            command.ExecuteNonQuery();

            // 카테고리 통계 업데이트
            if (!string.IsNullOrEmpty(category))
            {
                RecordCategoryUsage(category);
            }

            StatisticsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch { /* 무시 */ }
    }

    /// <summary>
    /// 카테고리 사용 기록
    /// </summary>
    private void RecordCategoryUsage(string category)
    {
        if (_connection == null) return;

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO category_statistics (category, count, last_used)
                VALUES (@category, 1, @lastUsed)
                ON CONFLICT(category) DO UPDATE SET
                    count = count + 1,
                    last_used = @lastUsed
            ";
            command.Parameters.AddWithValue("@category", category);
            command.Parameters.AddWithValue("@lastUsed", DateTime.Now.ToString("O"));
            command.ExecuteNonQuery();
        }
        catch { /* 무시 */ }
    }

    #endregion

    #region Statistics Queries

    /// <summary>
    /// 일별 통계 조회
    /// </summary>
    public List<DailyStatistics> GetDailyStatistics(int days = 30)
    {
        var results = new List<DailyStatistics>();
        if (_connection == null) return results;

        try
        {
            var startDate = DateTime.Today.AddDays(-days + 1).ToString("yyyy-MM-dd");

            using var command = _connection.CreateCommand();
            command.CommandText = @"
                SELECT date, api_calls, cache_hits, cache_misses,
                       commands_executed, commands_success, commands_failed
                FROM daily_statistics
                WHERE date >= @startDate
                ORDER BY date DESC
            ";
            command.Parameters.AddWithValue("@startDate", startDate);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new DailyStatistics
                {
                    Date = DateTime.Parse(reader.GetString(0)),
                    ApiCalls = reader.GetInt64(1),
                    CacheHits = reader.GetInt64(2),
                    CacheMisses = reader.GetInt64(3),
                    CommandsExecuted = reader.GetInt64(4),
                    CommandsSuccess = reader.GetInt64(5),
                    CommandsFailed = reader.GetInt64(6)
                });
            }
        }
        catch { /* 무시 */ }

        return results;
    }

    /// <summary>
    /// 전체 통계 요약 조회
    /// </summary>
    public UsageStatisticsSummary GetStatisticsSummary()
    {
        var summary = new UsageStatisticsSummary();
        if (_connection == null) return summary;

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = @"
                SELECT
                    SUM(api_calls) as total_api_calls,
                    SUM(cache_hits) as total_cache_hits,
                    SUM(cache_misses) as total_cache_misses,
                    SUM(commands_executed) as total_commands,
                    SUM(commands_success) as total_success,
                    SUM(commands_failed) as total_failed
                FROM daily_statistics
            ";

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                summary.TotalApiCalls = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                summary.TotalCacheHits = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                summary.TotalCacheMisses = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                summary.TotalCommandsExecuted = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                summary.TotalCommandsSuccess = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                summary.TotalCommandsFailed = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
            }
        }
        catch { /* 무시 */ }

        return summary;
    }

    /// <summary>
    /// 오늘 통계 조회
    /// </summary>
    public DailyStatistics GetTodayStatistics()
    {
        var today = new DailyStatistics { Date = DateTime.Today };
        if (_connection == null) return today;

        try
        {
            var todayStr = DateTime.Today.ToString("yyyy-MM-dd");

            using var command = _connection.CreateCommand();
            command.CommandText = @"
                SELECT api_calls, cache_hits, cache_misses,
                       commands_executed, commands_success, commands_failed
                FROM daily_statistics
                WHERE date = @date
            ";
            command.Parameters.AddWithValue("@date", todayStr);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                today.ApiCalls = reader.GetInt64(0);
                today.CacheHits = reader.GetInt64(1);
                today.CacheMisses = reader.GetInt64(2);
                today.CommandsExecuted = reader.GetInt64(3);
                today.CommandsSuccess = reader.GetInt64(4);
                today.CommandsFailed = reader.GetInt64(5);
            }
        }
        catch { /* 무시 */ }

        return today;
    }

    /// <summary>
    /// 카테고리별 통계 조회
    /// </summary>
    public List<CategoryStatistics> GetCategoryStatistics()
    {
        var results = new List<CategoryStatistics>();
        if (_connection == null) return results;

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = @"
                SELECT category, count, last_used
                FROM category_statistics
                ORDER BY count DESC
            ";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new CategoryStatistics
                {
                    Category = reader.GetString(0),
                    Count = reader.GetInt64(1),
                    LastUsed = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2))
                });
            }
        }
        catch { /* 무시 */ }

        return results;
    }

    /// <summary>
    /// 주간 통계 조회
    /// </summary>
    public WeeklyStatistics GetWeeklyStatistics()
    {
        var daily = GetDailyStatistics(7);
        return new WeeklyStatistics
        {
            TotalApiCalls = daily.Sum(d => d.ApiCalls),
            TotalCacheHits = daily.Sum(d => d.CacheHits),
            TotalCacheMisses = daily.Sum(d => d.CacheMisses),
            TotalCommandsExecuted = daily.Sum(d => d.CommandsExecuted),
            TotalCommandsSuccess = daily.Sum(d => d.CommandsSuccess),
            TotalCommandsFailed = daily.Sum(d => d.CommandsFailed),
            DailyStats = daily
        };
    }

    #endregion

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }
}

#region Statistics Models

/// <summary>
/// 일별 통계
/// </summary>
public class DailyStatistics
{
    public DateTime Date { get; set; }
    public long ApiCalls { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public long CommandsExecuted { get; set; }
    public long CommandsSuccess { get; set; }
    public long CommandsFailed { get; set; }

    public double CacheHitRate => (CacheHits + CacheMisses) > 0
        ? (double)CacheHits / (CacheHits + CacheMisses) * 100 : 0;

    public double SuccessRate => CommandsExecuted > 0
        ? (double)CommandsSuccess / CommandsExecuted * 100 : 0;

    public string DateText => Date.ToString("MM/dd (ddd)");
}

/// <summary>
/// 전체 통계 요약
/// </summary>
public class UsageStatisticsSummary
{
    public long TotalApiCalls { get; set; }
    public long TotalCacheHits { get; set; }
    public long TotalCacheMisses { get; set; }
    public long TotalCommandsExecuted { get; set; }
    public long TotalCommandsSuccess { get; set; }
    public long TotalCommandsFailed { get; set; }

    public double CacheHitRate => (TotalCacheHits + TotalCacheMisses) > 0
        ? (double)TotalCacheHits / (TotalCacheHits + TotalCacheMisses) * 100 : 0;

    public double SuccessRate => TotalCommandsExecuted > 0
        ? (double)TotalCommandsSuccess / TotalCommandsExecuted * 100 : 0;

    /// <summary>
    /// 캐시로 절약한 API 호출 수 (캐시 히트 = 절약된 API 호출)
    /// </summary>
    public long SavedApiCalls => TotalCacheHits;
}

/// <summary>
/// 주간 통계
/// </summary>
public class WeeklyStatistics
{
    public long TotalApiCalls { get; set; }
    public long TotalCacheHits { get; set; }
    public long TotalCacheMisses { get; set; }
    public long TotalCommandsExecuted { get; set; }
    public long TotalCommandsSuccess { get; set; }
    public long TotalCommandsFailed { get; set; }
    public List<DailyStatistics> DailyStats { get; set; } = new();

    public double CacheHitRate => (TotalCacheHits + TotalCacheMisses) > 0
        ? (double)TotalCacheHits / (TotalCacheHits + TotalCacheMisses) * 100 : 0;

    public double SuccessRate => TotalCommandsExecuted > 0
        ? (double)TotalCommandsSuccess / TotalCommandsExecuted * 100 : 0;
}

/// <summary>
/// 카테고리별 통계
/// </summary>
public class CategoryStatistics
{
    public string Category { get; set; } = string.Empty;
    public long Count { get; set; }
    public DateTime? LastUsed { get; set; }

    public string CategoryIcon => Category?.ToLower() switch
    {
        "파일" => "📁",
        "네트워크" => "🌐",
        "프로세스" => "⚙️",
        "시스템" => "🖥️",
        "패키지" => "📦",
        _ => "💻"
    };
}

#endregion
