using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// SQLite 기반 명령어 히스토리 데이터베이스 서비스
/// FTS5 전문 검색 + 벡터 임베딩 지원 (하이브리드 RAG)
/// </summary>
public class HistoryDatabaseService : IDisposable
{
    private static readonly string DatabaseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TermSnap"
    );

    private static readonly string DatabasePath = Path.Combine(DatabaseDirectory, "history.db");

    private SqliteConnection? _connection;
    private bool _disposed = false;

    private static HistoryDatabaseService? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// 싱글톤 인스턴스
    /// </summary>
    public static HistoryDatabaseService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new HistoryDatabaseService();
                }
            }
            return _instance;
        }
    }

    private HistoryDatabaseService()
    {
        Initialize();
    }

    /// <summary>
    /// 데이터베이스 초기화
    /// </summary>
    private void Initialize()
    {
        if (!Directory.Exists(DatabaseDirectory))
        {
            Directory.CreateDirectory(DatabaseDirectory);
        }

        _connection = new SqliteConnection($"Data Source={DatabasePath}");
        _connection.Open();

        // 테이블 생성
        CreateTables();
    }

    /// <summary>
    /// SQLite 연결 객체 반환 (내부용)
    /// </summary>
    public SqliteConnection GetConnection()
    {
        return _connection ?? throw new InvalidOperationException("Database not initialized");
    }

    /// <summary>
    /// 테이블 생성 (FTS5 포함)
    /// </summary>
    private void CreateTables()
    {
        using var command = _connection!.CreateCommand();

        // 메인 히스토리 테이블 (임베딩 벡터 컬럼 추가)
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS command_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_input TEXT NOT NULL,
                generated_command TEXT NOT NULL,
                original_command TEXT,
                explanation TEXT,
                output TEXT,
                error TEXT,
                server_profile TEXT,
                is_success INTEGER DEFAULT 0,
                was_edited INTEGER DEFAULT 0,
                executed_at TEXT NOT NULL,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                embedding_vector TEXT,
                use_count INTEGER DEFAULT 1,
                display_order INTEGER DEFAULT 0
            );

            -- 인덱스 생성
            CREATE INDEX IF NOT EXISTS idx_history_executed_at ON command_history(executed_at DESC);
            CREATE INDEX IF NOT EXISTS idx_history_server_profile ON command_history(server_profile);
            CREATE INDEX IF NOT EXISTS idx_history_is_success ON command_history(is_success);
            CREATE INDEX IF NOT EXISTS idx_history_use_count ON command_history(use_count DESC);
        ";
        command.ExecuteNonQuery();

        // 기존 테이블에 새 컬럼 추가 (마이그레이션)
        try
        {
            command.CommandText = "ALTER TABLE command_history ADD COLUMN embedding_vector TEXT";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { /* 이미 존재하면 무시 */ }

        try
        {
            command.CommandText = "ALTER TABLE command_history ADD COLUMN use_count INTEGER DEFAULT 1";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { /* 이미 존재하면 무시 */ }

        try
        {
            command.CommandText = "ALTER TABLE command_history ADD COLUMN display_order INTEGER DEFAULT 0";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { /* 이미 존재하면 무시 */ }

        // AI JSON 응답 필드 마이그레이션
        try
        {
            command.CommandText = "ALTER TABLE command_history ADD COLUMN confidence REAL DEFAULT 1.0";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { /* 이미 존재하면 무시 */ }

        try
        {
            command.CommandText = "ALTER TABLE command_history ADD COLUMN warning TEXT";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { /* 이미 존재하면 무시 */ }

        try
        {
            command.CommandText = "ALTER TABLE command_history ADD COLUMN alternatives_json TEXT";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { /* 이미 존재하면 무시 */ }

        try
        {
            command.CommandText = "ALTER TABLE command_history ADD COLUMN requires_sudo INTEGER DEFAULT 0";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { /* 이미 존재하면 무시 */ }

        try
        {
            command.CommandText = "ALTER TABLE command_history ADD COLUMN is_dangerous INTEGER DEFAULT 0";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { /* 이미 존재하면 무시 */ }

        try
        {
            command.CommandText = "ALTER TABLE command_history ADD COLUMN category TEXT";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { /* 이미 존재하면 무시 */ }

        try
        {
            command.CommandText = "ALTER TABLE command_history ADD COLUMN estimated_duration INTEGER";
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { /* 이미 존재하면 무시 */ }

        // FTS5 가상 테이블 생성 (전문 검색용)
        command.CommandText = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS command_history_fts USING fts5(
                user_input,
                generated_command,
                explanation,
                output,
                content='command_history',
                content_rowid='id'
            );
        ";
        command.ExecuteNonQuery();

        // FTS 트리거 생성 (자동 동기화)
        command.CommandText = @"
            -- INSERT 트리거
            CREATE TRIGGER IF NOT EXISTS history_ai AFTER INSERT ON command_history BEGIN
                INSERT INTO command_history_fts(rowid, user_input, generated_command, explanation, output)
                VALUES (new.id, new.user_input, new.generated_command, new.explanation, new.output);
            END;

            -- DELETE 트리거
            CREATE TRIGGER IF NOT EXISTS history_ad AFTER DELETE ON command_history BEGIN
                INSERT INTO command_history_fts(command_history_fts, rowid, user_input, generated_command, explanation, output)
                VALUES ('delete', old.id, old.user_input, old.generated_command, old.explanation, old.output);
            END;

            -- UPDATE 트리거
            CREATE TRIGGER IF NOT EXISTS history_au AFTER UPDATE ON command_history BEGIN
                INSERT INTO command_history_fts(command_history_fts, rowid, user_input, generated_command, explanation, output)
                VALUES ('delete', old.id, old.user_input, old.generated_command, old.explanation, old.output);
                INSERT INTO command_history_fts(rowid, user_input, generated_command, explanation, output)
                VALUES (new.id, new.user_input, new.generated_command, new.explanation, new.output);
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
    /// 히스토리 추가
    /// </summary>
    public long AddHistory(CommandHistory history, string? embeddingVector = null)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            INSERT INTO command_history
                (user_input, generated_command, original_command, explanation, output, error,
                 server_profile, is_success, was_edited, executed_at, embedding_vector, use_count,
                 confidence, warning, alternatives_json, requires_sudo, is_dangerous, category, estimated_duration)
            VALUES
                (@userInput, @generatedCommand, @originalCommand, @explanation, @output, @error,
                 @serverProfile, @isSuccess, @wasEdited, @executedAt, @embeddingVector, 1,
                 @confidence, @warning, @alternativesJson, @requiresSudo, @isDangerous, @category, @estimatedDuration);
            SELECT last_insert_rowid();
        ";

        command.Parameters.AddWithValue("@userInput", history.UserInput);
        command.Parameters.AddWithValue("@generatedCommand", history.GeneratedCommand);
        command.Parameters.AddWithValue("@originalCommand", history.OriginalCommand ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@explanation", history.Explanation ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@output", history.Output ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@error", history.Error ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@serverProfile", history.ServerProfile ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@isSuccess", history.IsSuccess ? 1 : 0);
        command.Parameters.AddWithValue("@wasEdited", history.WasEdited ? 1 : 0);
        command.Parameters.AddWithValue("@executedAt", history.ExecutedAt.ToString("O"));
        command.Parameters.AddWithValue("@embeddingVector", embeddingVector ?? (object)DBNull.Value);

        // AI JSON 응답 필드
        command.Parameters.AddWithValue("@confidence", history.Confidence);
        command.Parameters.AddWithValue("@warning", history.Warning ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@alternativesJson", history.AlternativesJson ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@requiresSudo", history.RequiresSudo ? 1 : 0);
        command.Parameters.AddWithValue("@isDangerous", history.IsDangerous ? 1 : 0);
        command.Parameters.AddWithValue("@category", history.Category ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@estimatedDuration", history.EstimatedDuration ?? (object)DBNull.Value);

        return (long)command.ExecuteScalar()!;
    }

    /// <summary>
    /// 임베딩 벡터 업데이트
    /// </summary>
    public bool UpdateEmbedding(long id, string embeddingVector)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "UPDATE command_history SET embedding_vector = @vector WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@vector", embeddingVector);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 사용 횟수 증가
    /// </summary>
    public bool IncrementUseCount(long id)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "UPDATE command_history SET use_count = use_count + 1 WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 임베딩이 있는 모든 히스토리 조회 (벡터 검색용)
    /// </summary>
    public List<(CommandHistory History, string EmbeddingVector)> GetHistoriesWithEmbedding(int limit = 1000)
    {
        var results = new List<(CommandHistory, string)>();

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            SELECT * FROM command_history 
            WHERE embedding_vector IS NOT NULL AND is_success = 1
            ORDER BY use_count DESC, executed_at DESC
            LIMIT @limit
        ";
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var history = ReadHistoryFromReader(reader);
            var vector = reader.GetString(reader.GetOrdinal("embedding_vector"));
            results.Add((history, vector));
        }

        return results;
    }

    /// <summary>
    /// 코사인 유사도 기반 벡터 검색
    /// </summary>
    public List<(CommandHistory History, double Similarity)> SearchByEmbedding(
        float[] queryVector, 
        double minSimilarity = 0.7, 
        int limit = 10)
    {
        var results = new List<(CommandHistory, double)>();
        var historiesWithEmbedding = GetHistoriesWithEmbedding();

        foreach (var (history, vectorStr) in historiesWithEmbedding)
        {
            var historyVector = IEmbeddingService.DeserializeVector(vectorStr);
            var similarity = IEmbeddingService.CosineSimilarity(queryVector, historyVector);

            if (similarity >= minSimilarity)
            {
                results.Add((history, similarity));
            }
        }

        return results
            .OrderByDescending(r => r.Item2)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// 자주 사용하는 명령어 조회 (빈도 기반 + 드래그앤드롭 정렬 지원)
    /// </summary>
    public List<FrequentCommand> GetFrequentCommands(int limit = 20, string? serverProfile = null)
    {
        var results = new List<FrequentCommand>();

        // display_order 컬럼 추가 (없으면)
        try
        {
            using var alterCommand = _connection!.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE command_history ADD COLUMN display_order INTEGER DEFAULT 0";
            alterCommand.ExecuteNonQuery();
        }
        catch
        {
            // 컬럼이 이미 존재하면 예외 무시
        }

        using var command = _connection!.CreateCommand();

        if (string.IsNullOrEmpty(serverProfile))
        {
            command.CommandText = @"
                SELECT generated_command, user_input, explanation, SUM(use_count) as total_use,
                       COUNT(*) as exec_count, MAX(executed_at) as last_used,
                       COALESCE(MAX(display_order), 0) as display_order
                FROM command_history
                WHERE is_success = 1
                GROUP BY generated_command
                ORDER BY CASE WHEN COALESCE(MAX(display_order), 0) > 0 THEN COALESCE(MAX(display_order), 0) ELSE 999999 END,
                         total_use DESC, exec_count DESC
                LIMIT @limit
            ";
        }
        else
        {
            command.CommandText = @"
                SELECT generated_command, user_input, explanation, SUM(use_count) as total_use,
                       COUNT(*) as exec_count, MAX(executed_at) as last_used,
                       COALESCE(MAX(display_order), 0) as display_order
                FROM command_history
                WHERE is_success = 1 AND server_profile = @serverProfile
                GROUP BY generated_command
                ORDER BY CASE WHEN COALESCE(MAX(display_order), 0) > 0 THEN COALESCE(MAX(display_order), 0) ELSE 999999 END,
                         total_use DESC, exec_count DESC
                LIMIT @limit
            ";
            command.Parameters.AddWithValue("@serverProfile", serverProfile);
        }

        command.Parameters.AddWithValue("@limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new FrequentCommand
            {
                Command = reader.GetString(0),
                Description = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Explanation = reader.IsDBNull(2) ? null : reader.GetString(2),
                TotalUseCount = reader.GetInt64(3),
                ExecutionCount = reader.GetInt64(4),
                LastUsed = DateTime.Parse(reader.GetString(5)),
                DisplayOrder = reader.GetInt32(6)
            });
        }

        return results;
    }

    /// <summary>
    /// 임베딩이 없는 히스토리 조회 (배치 임베딩 생성용)
    /// </summary>
    public List<CommandHistory> GetHistoriesWithoutEmbedding(int limit = 100)
    {
        var results = new List<CommandHistory>();

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            SELECT * FROM command_history 
            WHERE embedding_vector IS NULL AND is_success = 1
            ORDER BY executed_at DESC
            LIMIT @limit
        ";
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadHistoryFromReader(reader));
        }

        return results;
    }

    /// <summary>
    /// 전문 검색 (FTS5)
    /// </summary>
    public List<CommandHistory> Search(string query, int limit = 50)
    {
        var results = new List<CommandHistory>();

        if (string.IsNullOrWhiteSpace(query))
            return GetRecentHistory(limit);

        using var command = _connection!.CreateCommand();

        // FTS5 MATCH 검색 (BM25 랭킹)
        command.CommandText = @"
            SELECT h.*, bm25(command_history_fts) as rank
            FROM command_history h
            INNER JOIN command_history_fts fts ON h.id = fts.rowid
            WHERE command_history_fts MATCH @query
            ORDER BY rank
            LIMIT @limit
        ";

        // 검색어를 FTS5 형식으로 변환 (부분 일치를 위해 * 추가)
        var ftsQuery = string.Join(" OR ", query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(term => $"\"{term}\"*"));

        command.Parameters.AddWithValue("@query", ftsQuery);
        command.Parameters.AddWithValue("@limit", limit);

        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadHistoryFromReader(reader));
            }
        }
        catch (SqliteException)
        {
            // FTS 쿼리 실패 시 LIKE 검색으로 폴백
            return SearchWithLike(query, limit);
        }

        return results;
    }

    /// <summary>
    /// LIKE 검색 (FTS 실패 시 폴백)
    /// </summary>
    private List<CommandHistory> SearchWithLike(string query, int limit)
    {
        var results = new List<CommandHistory>();

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            SELECT * FROM command_history
            WHERE user_input LIKE @query
               OR generated_command LIKE @query
               OR explanation LIKE @query
               OR output LIKE @query
            ORDER BY executed_at DESC
            LIMIT @limit
        ";

        command.Parameters.AddWithValue("@query", $"%{query}%");
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadHistoryFromReader(reader));
        }

        return results;
    }

    /// <summary>
    /// 유사 명령어 검색 (이전에 비슷한 입력이 있었는지)
    /// </summary>
    public List<CommandHistory> FindSimilar(string userInput, int limit = 5)
    {
        var results = new List<CommandHistory>();

        if (string.IsNullOrWhiteSpace(userInput))
            return results;

        using var command = _connection!.CreateCommand();

        // FTS5로 유사한 입력 검색
        command.CommandText = @"
            SELECT h.*, bm25(command_history_fts, 10.0, 1.0, 0.5, 0.1) as rank
            FROM command_history h
            INNER JOIN command_history_fts fts ON h.id = fts.rowid
            WHERE command_history_fts MATCH @query
              AND h.is_success = 1
            ORDER BY rank
            LIMIT @limit
        ";

        // 핵심 단어만 추출하여 검색
        var words = userInput.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Take(5);
        var ftsQuery = string.Join(" OR ", words.Select(w => $"user_input:\"{w}\"*"));

        if (string.IsNullOrEmpty(ftsQuery))
            return results;

        command.Parameters.AddWithValue("@query", ftsQuery);
        command.Parameters.AddWithValue("@limit", limit);

        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadHistoryFromReader(reader));
            }
        }
        catch (SqliteException)
        {
            // 검색 실패 시 빈 결과 반환
        }

        return results;
    }

    /// <summary>
    /// 최근 히스토리 조회
    /// </summary>
    public List<CommandHistory> GetRecentHistory(int limit = 100)
    {
        var results = new List<CommandHistory>();

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            SELECT * FROM command_history
            ORDER BY executed_at DESC
            LIMIT @limit
        ";
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadHistoryFromReader(reader));
        }

        return results;
    }

    /// <summary>
    /// 서버별 히스토리 조회
    /// </summary>
    public List<CommandHistory> GetHistoryByServer(string serverProfile, int limit = 100)
    {
        var results = new List<CommandHistory>();

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            SELECT * FROM command_history
            WHERE server_profile = @serverProfile
            ORDER BY executed_at DESC
            LIMIT @limit
        ";
        command.Parameters.AddWithValue("@serverProfile", serverProfile);
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadHistoryFromReader(reader));
        }

        return results;
    }

    /// <summary>
    /// 히스토리 삭제
    /// </summary>
    public bool DeleteHistory(long id)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "DELETE FROM command_history WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 자주 사용하는 명령어 업데이트 (설명 수정)
    /// </summary>
    public bool UpdateFrequentCommand(FrequentCommand cmd)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            UPDATE command_history
            SET user_input = @description,
                generated_command = @generatedCommand
            WHERE generated_command = @originalCommand
        ";
        command.Parameters.AddWithValue("@description", cmd.Description ?? "");
        command.Parameters.AddWithValue("@generatedCommand", cmd.Command ?? "");
        command.Parameters.AddWithValue("@originalCommand", cmd.Command ?? "");

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 자주 사용하는 명령어 삭제 (해당 명령어의 모든 히스토리 삭제)
    /// </summary>
    public bool DeleteFrequentCommand(FrequentCommand cmd)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "DELETE FROM command_history WHERE generated_command = @command";
        command.Parameters.AddWithValue("@command", cmd.Command ?? "");

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 자주 사용하는 명령어 정렬 순서 업데이트
    /// </summary>
    public bool UpdateFrequentCommandOrder(string commandText, int displayOrder)
    {
        // display_order 컬럼이 없을 수 있으므로, 먼저 컬럼 추가 시도 (이미 있으면 무시됨)
        try
        {
            using var alterCommand = _connection!.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE command_history ADD COLUMN display_order INTEGER DEFAULT 0";
            alterCommand.ExecuteNonQuery();
        }
        catch
        {
            // 컬럼이 이미 존재하면 예외 무시
        }

        // 정렬 순서 업데이트
        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            UPDATE command_history
            SET display_order = @displayOrder
            WHERE generated_command = @command
        ";
        command.Parameters.AddWithValue("@displayOrder", displayOrder);
        command.Parameters.AddWithValue("@command", commandText ?? "");

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 전체 히스토리 삭제
    /// </summary>
    public void ClearAllHistory()
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "DELETE FROM command_history";
        command.ExecuteNonQuery();

        // FTS 인덱스 재구성
        command.CommandText = "INSERT INTO command_history_fts(command_history_fts) VALUES('rebuild')";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 히스토리 개수 조회
    /// </summary>
    public long GetHistoryCount()
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM command_history";
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>
    /// 서버 프로필별 히스토리 개수 조회
    /// </summary>
    public long GetHistoryCount(string? serverProfile = null)
    {
        using var command = _connection!.CreateCommand();

        if (string.IsNullOrEmpty(serverProfile))
        {
            command.CommandText = "SELECT COUNT(*) FROM command_history";
        }
        else
        {
            command.CommandText = "SELECT COUNT(*) FROM command_history WHERE server_profile = @serverProfile";
            command.Parameters.AddWithValue("@serverProfile", serverProfile);
        }

        return (long)command.ExecuteScalar()!;
    }

    /// <summary>
    /// 통계 정보 조회
    /// </summary>
    public HistoryStatistics GetStatistics()
    {
        var stats = new HistoryStatistics();

        using var command = _connection!.CreateCommand();

        // 전체 개수
        command.CommandText = "SELECT COUNT(*) FROM command_history";
        stats.TotalCount = (long)command.ExecuteScalar()!;

        // 성공 개수
        command.CommandText = "SELECT COUNT(*) FROM command_history WHERE is_success = 1";
        stats.SuccessCount = (long)command.ExecuteScalar()!;

        // 서버별 개수
        command.CommandText = @"
            SELECT server_profile, COUNT(*) as cnt
            FROM command_history
            WHERE server_profile IS NOT NULL
            GROUP BY server_profile
            ORDER BY cnt DESC
        ";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            stats.CountByServer[reader.GetString(0)] = reader.GetInt64(1);
        }

        return stats;
    }

    /// <summary>
    /// JSON에서 마이그레이션
    /// </summary>
    public void MigrateFromJson(CommandHistoryCollection collection)
    {
        if (collection == null || collection.Items.Count == 0)
            return;

        using var transaction = _connection!.BeginTransaction();

        try
        {
            foreach (var history in collection.Items)
            {
                AddHistory(history);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Reader에서 CommandHistory 객체 생성
    /// </summary>
    private static CommandHistory ReadHistoryFromReader(SqliteDataReader reader)
    {
        var history = new CommandHistory(
            reader.GetString(reader.GetOrdinal("user_input")),
            reader.GetString(reader.GetOrdinal("generated_command")),
            reader.IsDBNull(reader.GetOrdinal("server_profile")) ? "" : reader.GetString(reader.GetOrdinal("server_profile"))
        )
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            OriginalCommand = reader.IsDBNull(reader.GetOrdinal("original_command")) ? null : reader.GetString(reader.GetOrdinal("original_command")),
            Explanation = reader.IsDBNull(reader.GetOrdinal("explanation")) ? null : reader.GetString(reader.GetOrdinal("explanation")),
            Output = reader.IsDBNull(reader.GetOrdinal("output")) ? null : reader.GetString(reader.GetOrdinal("output")),
            Error = reader.IsDBNull(reader.GetOrdinal("error")) ? null : reader.GetString(reader.GetOrdinal("error")),
            IsSuccess = reader.GetInt32(reader.GetOrdinal("is_success")) == 1,
            WasEdited = reader.GetInt32(reader.GetOrdinal("was_edited")) == 1,
            ExecutedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("executed_at")))
        };

        // AI JSON 응답 필드 (새 컬럼은 없을 수 있으므로 안전하게 읽기)
        try
        {
            var confidenceOrdinal = reader.GetOrdinal("confidence");
            if (!reader.IsDBNull(confidenceOrdinal))
                history.Confidence = reader.GetDouble(confidenceOrdinal);
        }
        catch { /* 컬럼이 없으면 무시 */ }

        try
        {
            var warningOrdinal = reader.GetOrdinal("warning");
            if (!reader.IsDBNull(warningOrdinal))
                history.Warning = reader.GetString(warningOrdinal);
        }
        catch { /* 컬럼이 없으면 무시 */ }

        try
        {
            var alternativesOrdinal = reader.GetOrdinal("alternatives_json");
            if (!reader.IsDBNull(alternativesOrdinal))
                history.AlternativesJson = reader.GetString(alternativesOrdinal);
        }
        catch { /* 컬럼이 없으면 무시 */ }

        try
        {
            var requiresSudoOrdinal = reader.GetOrdinal("requires_sudo");
            if (!reader.IsDBNull(requiresSudoOrdinal))
                history.RequiresSudo = reader.GetInt32(requiresSudoOrdinal) == 1;
        }
        catch { /* 컬럼이 없으면 무시 */ }

        try
        {
            var isDangerousOrdinal = reader.GetOrdinal("is_dangerous");
            if (!reader.IsDBNull(isDangerousOrdinal))
                history.IsDangerous = reader.GetInt32(isDangerousOrdinal) == 1;
        }
        catch { /* 컬럼이 없으면 무시 */ }

        try
        {
            var categoryOrdinal = reader.GetOrdinal("category");
            if (!reader.IsDBNull(categoryOrdinal))
                history.Category = reader.GetString(categoryOrdinal);
        }
        catch { /* 컬럼이 없으면 무시 */ }

        try
        {
            var estimatedDurationOrdinal = reader.GetOrdinal("estimated_duration");
            if (!reader.IsDBNull(estimatedDurationOrdinal))
                history.EstimatedDuration = reader.GetInt32(estimatedDurationOrdinal);
        }
        catch { /* 컬럼이 없으면 무시 */ }

        return history;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _connection?.Close();
        _connection?.Dispose();
    }
}

/// <summary>
/// 히스토리 통계
/// </summary>
public class HistoryStatistics
{
    public long TotalCount { get; set; }
    public long SuccessCount { get; set; }
    public double SuccessRate => TotalCount > 0 ? (double)SuccessCount / TotalCount * 100 : 0;
    public Dictionary<string, long> CountByServer { get; set; } = new();
}

/// <summary>
/// 자주 사용하는 명령어 정보
/// </summary>
public class FrequentCommand
{
    public string Command { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Explanation { get; set; }
    public long TotalUseCount { get; set; }
    public long ExecutionCount { get; set; }
    public DateTime LastUsed { get; set; }
    public int DisplayOrder { get; set; } = 0; // 드래그앤드롭 정렬 순서
}
