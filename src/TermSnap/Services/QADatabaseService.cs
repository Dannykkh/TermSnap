using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TermSnap.Models;
using TermSnap.Core;

namespace TermSnap.Services;

/// <summary>
/// 사용자 정의 Q&A 데이터베이스 서비스
/// SQLite + FTS5 전문검색 + 벡터 임베딩 지원
///
/// 두 개의 DB 사용:
/// - base: 프로그램 폴더의 qa_knowledge.db (배포용, 읽기 전용)
/// - custom: %APPDATA%의 qa_custom.db (사용자 추가용)
/// </summary>
public class QADatabaseService : IDisposable
{
    // 사용자 데이터 폴더 (%APPDATA%/TermSnap)
    private static readonly string UserDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TermSnap"
    );

    // 프로그램 폴더 (exe 위치)
    // 주의: 단일 파일 배포 시 AppDomain.CurrentDomain.BaseDirectory는 임시 폴더를 반환함
    // Environment.ProcessPath를 사용해야 실제 exe 위치를 얻을 수 있음 (.NET 6+)
    private static readonly string ProgramDirectory = Path.GetDirectoryName(Environment.ProcessPath)
        ?? AppDomain.CurrentDomain.BaseDirectory;

    // 기본 DB: 프로그램 폴더 (배포용)
    private static readonly string BaseDatabasePath = Path.Combine(ProgramDirectory, "qa_knowledge.db");

    // 사용자 DB: %APPDATA% (사용자 추가용)
    private static readonly string CustomDatabasePath = Path.Combine(UserDataDirectory, "qa_custom.db");

    private SqliteConnection? _connection;
    private bool _disposed = false;
    private bool _hasBaseDb = false;

    private static QADatabaseService? _instance;
    private static readonly object _lock = new();

    // 벡터 검색 임계값
    private const double MIN_SIMILARITY = 0.7;

    /// <summary>
    /// 싱글톤 인스턴스
    /// </summary>
    public static QADatabaseService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new QADatabaseService();
                }
            }
            return _instance;
        }
    }

    private QADatabaseService()
    {
        Initialize();
    }

    /// <summary>
    /// 현재 Embedding 서비스 (AIProviderManager에서 가져옴)
    /// </summary>
    private IEmbeddingService? EmbeddingService => AIProviderManager.Instance.CurrentEmbeddingService;

    /// <summary>
    /// 데이터베이스 초기화
    /// 사용자 DB를 메인으로 열고, 기본 DB를 ATTACH
    /// </summary>
    private void Initialize()
    {
        // 사용자 데이터 폴더 생성
        if (!Directory.Exists(UserDataDirectory))
        {
            Directory.CreateDirectory(UserDataDirectory);
        }

        // 사용자 DB를 메인으로 연결 (쓰기 가능)
        _connection = new SqliteConnection($"Data Source={CustomDatabasePath}");
        _connection.Open();

        // 사용자 DB 테이블 생성
        CreateTables();

        // 기본 DB가 있으면 ATTACH (읽기 전용)
        if (File.Exists(BaseDatabasePath))
        {
            try
            {
                using var attachCmd = _connection.CreateCommand();
                attachCmd.CommandText = $"ATTACH DATABASE '{BaseDatabasePath}' AS base";
                attachCmd.ExecuteNonQuery();
                _hasBaseDb = true;
                System.Diagnostics.Debug.WriteLine($"[QA] 기본 DB 연결됨: {BaseDatabasePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QA] 기본 DB ATTACH 실패: {ex.Message}");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[QA] 기본 DB 없음: {BaseDatabasePath}");
        }
    }

    /// <summary>
    /// 테이블 생성 (FTS5 포함)
    /// </summary>
    private void CreateTables()
    {
        using var command = _connection!.CreateCommand();

        command.CommandText = @"
            -- 메인 Q&A 테이블
            CREATE TABLE IF NOT EXISTS qa_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                question TEXT NOT NULL,
                answer TEXT NOT NULL,
                category TEXT,
                tags TEXT,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                modified_at TEXT,
                use_count INTEGER DEFAULT 0,
                embedding_vector TEXT,
                is_active INTEGER DEFAULT 1
            );

            -- 인덱스 생성
            CREATE INDEX IF NOT EXISTS idx_qa_category ON qa_entries(category);
            CREATE INDEX IF NOT EXISTS idx_qa_use_count ON qa_entries(use_count DESC);
            CREATE INDEX IF NOT EXISTS idx_qa_is_active ON qa_entries(is_active);
        ";
        command.ExecuteNonQuery();

        // FTS5 가상 테이블
        command.CommandText = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS qa_fts USING fts5(
                question,
                answer,
                category,
                tags,
                content='qa_entries',
                content_rowid='id',
                tokenize='unicode61'
            );
        ";
        command.ExecuteNonQuery();

        // FTS5 동기화 트리거
        command.CommandText = @"
            CREATE TRIGGER IF NOT EXISTS qa_ai AFTER INSERT ON qa_entries BEGIN
                INSERT INTO qa_fts(rowid, question, answer, category, tags)
                VALUES (new.id, new.question, new.answer, new.category, new.tags);
            END;

            CREATE TRIGGER IF NOT EXISTS qa_ad AFTER DELETE ON qa_entries BEGIN
                INSERT INTO qa_fts(qa_fts, rowid, question, answer, category, tags)
                VALUES ('delete', old.id, old.question, old.answer, old.category, old.tags);
            END;

            CREATE TRIGGER IF NOT EXISTS qa_au AFTER UPDATE ON qa_entries BEGIN
                INSERT INTO qa_fts(qa_fts, rowid, question, answer, category, tags)
                VALUES ('delete', old.id, old.question, old.answer, old.category, old.tags);
                INSERT INTO qa_fts(rowid, question, answer, category, tags)
                VALUES (new.id, new.question, new.answer, new.category, new.tags);
            END;
        ";
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // 트리거가 이미 존재하면 무시
        }
    }

    /// <summary>
    /// Q&A 항목 추가
    /// </summary>
    public async Task<int> AddEntry(QAEntry entry)
    {
        // 임베딩 생성
        string? embeddingVector = null;
        var embeddingService = EmbeddingService;
        if (embeddingService != null)
        {
            try
            {
                var embedding = await embeddingService.GetEmbeddingAsync(entry.Question);
                embeddingVector = IEmbeddingService.SerializeVector(embedding);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Embedding 생성 실패: {ex.Message}");
            }
        }

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            INSERT INTO qa_entries (question, answer, category, tags, created_at, embedding_vector, is_active)
            VALUES (@question, @answer, @category, @tags, @created_at, @embedding_vector, 1);
            SELECT last_insert_rowid();
        ";

        command.Parameters.AddWithValue("@question", entry.Question);
        command.Parameters.AddWithValue("@answer", entry.Answer);
        command.Parameters.AddWithValue("@category", entry.Category ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@tags", entry.Tags ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@created_at", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("@embedding_vector", embeddingVector ?? (object)DBNull.Value);

        var result = command.ExecuteScalar();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Q&A 항목 수정
    /// </summary>
    public async Task UpdateEntry(QAEntry entry)
    {
        // 질문이 변경되었으면 임베딩 재생성
        string? embeddingVector = entry.EmbeddingVector;
        var embeddingService = EmbeddingService;
        if (embeddingService != null)
        {
            try
            {
                var embedding = await embeddingService.GetEmbeddingAsync(entry.Question);
                embeddingVector = IEmbeddingService.SerializeVector(embedding);
            }
            catch { /* 실패 시 기존 벡터 유지 */ }
        }

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            UPDATE qa_entries 
            SET question = @question,
                answer = @answer,
                category = @category,
                tags = @tags,
                modified_at = @modified_at,
                embedding_vector = @embedding_vector
            WHERE id = @id
        ";

        command.Parameters.AddWithValue("@id", entry.Id);
        command.Parameters.AddWithValue("@question", entry.Question);
        command.Parameters.AddWithValue("@answer", entry.Answer);
        command.Parameters.AddWithValue("@category", entry.Category ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@tags", entry.Tags ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@modified_at", DateTime.Now.ToString("o"));
        command.Parameters.AddWithValue("@embedding_vector", embeddingVector ?? (object)DBNull.Value);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Q&A 항목 삭제 (소프트 삭제)
    /// </summary>
    public void DeleteEntry(int id)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "UPDATE qa_entries SET is_active = 0 WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Q&A 항목 완전 삭제
    /// </summary>
    public void HardDeleteEntry(int id)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "DELETE FROM qa_entries WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 모든 활성 Q&A 항목 조회 (기본 DB + 사용자 DB)
    /// </summary>
    public List<QAEntry> GetAllEntries(bool includeInactive = false)
    {
        var entries = new List<QAEntry>();

        using var command = _connection!.CreateCommand();

        // 사용자 DB (main) + 기본 DB (base) 합쳐서 조회
        var whereClause = includeInactive ? "" : "WHERE is_active = 1";

        if (_hasBaseDb)
        {
            command.CommandText = $@"
                SELECT *, 'custom' as source FROM main.qa_entries {whereClause}
                UNION ALL
                SELECT *, 'base' as source FROM base.qa_entries {whereClause}
                ORDER BY use_count DESC, created_at DESC
            ";
        }
        else
        {
            command.CommandText = $@"
                SELECT *, 'custom' as source FROM qa_entries {whereClause}
                ORDER BY use_count DESC, created_at DESC
            ";
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var entry = ReadQAEntry(reader);
            // source 컬럼으로 기본/사용자 구분 가능
            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// ID로 Q&A 항목 조회
    /// </summary>
    public QAEntry? GetEntryById(int id)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "SELECT * FROM qa_entries WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return ReadQAEntry(reader);
        }
        return null;
    }

    /// <summary>
    /// FTS5 키워드 검색 (기본 DB + 사용자 DB)
    /// </summary>
    public List<QAEntry> Search(string query, int limit = 10)
    {
        var entries = new List<QAEntry>();
        if (string.IsNullOrWhiteSpace(query)) return entries;

        // 한글/특수문자 처리를 위해 따옴표로 감싸기
        var sanitizedQuery = query.Replace("\"", "\"\"");

        // 사용자 DB에서 FTS 검색
        using (var command = _connection!.CreateCommand())
        {
            command.CommandText = @"
                SELECT qa_entries.*, bm25(qa_fts) as score
                FROM qa_fts
                JOIN qa_entries ON qa_fts.rowid = qa_entries.id
                WHERE qa_fts MATCH @query AND qa_entries.is_active = 1
                ORDER BY score
                LIMIT @limit
            ";
            command.Parameters.AddWithValue("@query", $"\"{sanitizedQuery}\"");
            command.Parameters.AddWithValue("@limit", limit);

            try
            {
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    entries.Add(ReadQAEntry(reader));
                }
            }
            catch (SqliteException)
            {
                // FTS 실패 시 LIKE 폴백
                entries.AddRange(SearchByLike(query, limit));
            }
        }

        // 기본 DB에서도 검색 (FTS는 ATTACH에서 지원 안됨, LIKE 사용)
        if (_hasBaseDb)
        {
            using var baseCmd = _connection!.CreateCommand();
            baseCmd.CommandText = @"
                SELECT * FROM base.qa_entries
                WHERE is_active = 1 AND (
                    question LIKE @query OR
                    answer LIKE @query OR
                    category LIKE @query OR
                    tags LIKE @query
                )
                ORDER BY use_count DESC
                LIMIT @limit
            ";
            baseCmd.Parameters.AddWithValue("@query", $"%{query}%");
            baseCmd.Parameters.AddWithValue("@limit", limit);

            using var reader = baseCmd.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(ReadQAEntry(reader));
            }
        }

        // 중복 제거 후 정렬
        return entries
            .GroupBy(e => e.Question)
            .Select(g => g.First())
            .OrderByDescending(e => e.UseCount)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// LIKE 검색 (폴백)
    /// </summary>
    private List<QAEntry> SearchByLike(string query, int limit)
    {
        var entries = new List<QAEntry>();

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            SELECT * FROM qa_entries 
            WHERE is_active = 1 AND (
                question LIKE @query OR 
                answer LIKE @query OR 
                category LIKE @query OR 
                tags LIKE @query
            )
            ORDER BY use_count DESC
            LIMIT @limit
        ";

        command.Parameters.AddWithValue("@query", $"%{query}%");
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(ReadQAEntry(reader));
        }

        return entries;
    }

    /// <summary>
    /// 벡터 유사도 검색
    /// </summary>
    public async Task<List<(QAEntry Entry, double Similarity)>> SearchByVector(string query, int limit = 10)
    {
        var results = new List<(QAEntry, double)>();

        var embeddingService = EmbeddingService;
        if (embeddingService == null) return results;

        try
        {
            // 쿼리 임베딩 생성
            var queryEmbedding = await embeddingService.GetEmbeddingAsync(query);

            // 모든 임베딩이 있는 항목 조회
            using var command = _connection!.CreateCommand();
            command.CommandText = @"
                SELECT * FROM qa_entries 
                WHERE is_active = 1 AND embedding_vector IS NOT NULL
            ";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entry = ReadQAEntry(reader);
                if (!string.IsNullOrEmpty(entry.EmbeddingVector))
                {
                    var entryVector = IEmbeddingService.DeserializeVector(entry.EmbeddingVector);
                    var similarity = IEmbeddingService.CosineSimilarity(queryEmbedding, entryVector);

                    if (similarity >= MIN_SIMILARITY)
                    {
                        results.Add((entry, similarity));
                    }
                }
            }

            // 유사도 순 정렬
            results = results.OrderByDescending(r => r.Item2).Take(limit).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"벡터 검색 실패: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// 하이브리드 검색 (FTS5 + 벡터)
    /// </summary>
    public async Task<List<(QAEntry Entry, double Score, string Method)>> HybridSearch(string query, int limit = 10)
    {
        var results = new Dictionary<int, (QAEntry Entry, double Score, string Method)>();

        // 1. FTS5 키워드 검색
        var ftsResults = Search(query, limit);
        foreach (var entry in ftsResults)
        {
            if (!results.ContainsKey(entry.Id))
            {
                results[entry.Id] = (entry, 0.8, "keyword"); // FTS 히트는 0.8 기본 점수
            }
        }

        // 2. 벡터 검색
        var vectorResults = await SearchByVector(query, limit);
        foreach (var (entry, similarity) in vectorResults)
        {
            if (results.ContainsKey(entry.Id))
            {
                // 이미 FTS에서 찾은 경우 점수 합산 (가중치 부여)
                var existing = results[entry.Id];
                results[entry.Id] = (entry, Math.Min(1.0, existing.Score + similarity * 0.5), "hybrid");
            }
            else
            {
                results[entry.Id] = (entry, similarity, "vector");
            }
        }

        // 점수 순 정렬
        return results.Values
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// 가장 유사한 Q&A 찾기 (질문에 대한 답변용)
    /// </summary>
    public async Task<(QAEntry? Entry, double Similarity)> FindBestMatch(string question)
    {
        var results = await HybridSearch(question, 1);
        if (results.Count > 0 && results[0].Score >= 0.75)
        {
            var best = results[0];
            // 사용 횟수 증가
            IncrementUseCount(best.Entry.Id);
            return (best.Entry, best.Score);
        }
        return (null, 0);
    }

    /// <summary>
    /// 사용 횟수 증가
    /// </summary>
    public void IncrementUseCount(int id)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "UPDATE qa_entries SET use_count = use_count + 1 WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 카테고리 목록 조회
    /// </summary>
    public List<string> GetCategories()
    {
        var categories = new List<string>();

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            SELECT DISTINCT category FROM qa_entries 
            WHERE category IS NOT NULL AND is_active = 1
            ORDER BY category
        ";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var category = reader.GetString(0);
            if (!string.IsNullOrEmpty(category))
            {
                categories.Add(category);
            }
        }

        return categories;
    }

    /// <summary>
    /// 통계 조회
    /// </summary>
    public (int TotalCount, int WithEmbedding, int TotalUseCount) GetStatistics()
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            SELECT 
                COUNT(*) as total,
                SUM(CASE WHEN embedding_vector IS NOT NULL THEN 1 ELSE 0 END) as with_embedding,
                SUM(use_count) as total_use
            FROM qa_entries WHERE is_active = 1
        ";

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return (
                reader.GetInt32(0),
                reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                reader.IsDBNull(2) ? 0 : reader.GetInt32(2)
            );
        }

        return (0, 0, 0);
    }

    /// <summary>
    /// 임베딩 없는 항목에 임베딩 추가 (배치 처리)
    /// </summary>
    public async Task<int> GenerateEmbeddingsForExistingEntries(int batchSize = 50)
    {
        var embeddingService = EmbeddingService;
        if (embeddingService == null) return 0;

        var entriesWithoutEmbedding = new List<QAEntry>();

        using (var command = _connection!.CreateCommand())
        {
            command.CommandText = @"
                SELECT * FROM qa_entries 
                WHERE is_active = 1 AND embedding_vector IS NULL
                LIMIT @limit
            ";
            command.Parameters.AddWithValue("@limit", batchSize);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                entriesWithoutEmbedding.Add(ReadQAEntry(reader));
            }
        }

        var processedCount = 0;
        foreach (var entry in entriesWithoutEmbedding)
        {
            try
            {
                var embedding = await embeddingService.GetEmbeddingAsync(entry.Question);
                var vectorStr = IEmbeddingService.SerializeVector(embedding);

                using var updateCmd = _connection!.CreateCommand();
                updateCmd.CommandText = "UPDATE qa_entries SET embedding_vector = @vector WHERE id = @id";
                updateCmd.Parameters.AddWithValue("@vector", vectorStr);
                updateCmd.Parameters.AddWithValue("@id", entry.Id);
                updateCmd.ExecuteNonQuery();

                processedCount++;
                await Task.Delay(100); // API 레이트 리밋 방지
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"임베딩 생성 실패 (ID: {entry.Id}): {ex.Message}");
            }
        }

        return processedCount;
    }

    /// <summary>
    /// Reader에서 QAEntry 읽기
    /// </summary>
    private static QAEntry ReadQAEntry(SqliteDataReader reader)
    {
        return new QAEntry
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            Question = reader.GetString(reader.GetOrdinal("question")),
            Answer = reader.GetString(reader.GetOrdinal("answer")),
            Category = reader.IsDBNull(reader.GetOrdinal("category")) ? null : reader.GetString(reader.GetOrdinal("category")),
            Tags = reader.IsDBNull(reader.GetOrdinal("tags")) ? null : reader.GetString(reader.GetOrdinal("tags")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            ModifiedAt = reader.IsDBNull(reader.GetOrdinal("modified_at")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("modified_at"))),
            UseCount = reader.GetInt32(reader.GetOrdinal("use_count")),
            EmbeddingVector = reader.IsDBNull(reader.GetOrdinal("embedding_vector")) ? null : reader.GetString(reader.GetOrdinal("embedding_vector")),
            IsActive = reader.GetInt32(reader.GetOrdinal("is_active")) == 1
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _connection?.Close();
        _connection?.Dispose();
    }
}
