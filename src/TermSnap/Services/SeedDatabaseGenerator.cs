using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// 시드 데이터베이스 생성기 (빌드 타임 또는 수동 실행용)
/// JSON 파일에서 명령어를 읽어 임베딩과 함께 SQLite DB에 저장
/// </summary>
public class SeedDatabaseGenerator
{
    /// <summary>
    /// JSON 파일에서 시드 DB 생성
    /// </summary>
    /// <param name="jsonFilePath">linux-commands.json 경로</param>
    /// <param name="outputDbPath">출력할 seed-history.db 경로</param>
    /// <returns>임포트된 명령어 개수</returns>
    public async Task<int> GenerateFromJsonAsync(string jsonFilePath, string outputDbPath)
    {
        if (!File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException($"JSON 파일을 찾을 수 없습니다: {jsonFilePath}");
        }

        // 출력 디렉토리 생성
        var outputDir = Path.GetDirectoryName(outputDbPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // 기존 DB 파일 삭제
        if (File.Exists(outputDbPath))
        {
            File.Delete(outputDbPath);
        }

        // JSON 로드 (case-insensitive)
        var json = await File.ReadAllTextAsync(jsonFilePath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var seedData = JsonSerializer.Deserialize<SeedDataFile>(json, options);

        if (seedData?.KnowledgeBase == null || seedData.KnowledgeBase.Count == 0)
        {
            throw new InvalidOperationException("JSON 파일이 비어있거나 잘못된 형식입니다.");
        }

        Console.WriteLine($"JSON에서 {seedData.KnowledgeBase.Count}개의 명령어를 로드했습니다.");

        // SQLite DB 생성
        using var connection = new SqliteConnection($"Data Source={outputDbPath}");
        connection.Open();

        // 테이블 생성
        CreateTables(connection);

        // 임베딩 서비스 초기화
        IEmbeddingService? embeddingService = null;

        try
        {
            // 로컬 임베딩 서비스 사용 (ONNX)
            var localEmbedding = new LocalEmbeddingService();

            // 모델 다운로드 확인
            if (!LocalEmbeddingService.IsModelAvailable())
            {
                Console.WriteLine("⚠️  ONNX 모델이 없습니다. 임베딩 없이 진행합니다.");
                Console.WriteLine($"   모델 다운로드 필요: {LocalEmbeddingService.GetModelDirectory()}");
            }
            else
            {
                await localEmbedding.InitializeAsync();
                embeddingService = localEmbedding;
                Console.WriteLine("✓ 임베딩 서비스 초기화 완료");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  임베딩 서비스 초기화 실패: {ex.Message}");
            Console.WriteLine("   임베딩 없이 계속 진행합니다.");
        }

        // 명령어 임포트
        int importedCount = 0;
        int totalCount = seedData.KnowledgeBase.Count;

        foreach (var item in seedData.KnowledgeBase)
        {
            importedCount++;
            Console.Write($"\r진행: {importedCount}/{totalCount} ({(importedCount * 100 / totalCount)}%)");

            try
            {
                // 임베딩 생성
                string? embeddingVector = null;
                if (embeddingService != null)
                {
                    try
                    {
                        var embedding = await embeddingService.GetEmbeddingAsync(item.Question);
                        embeddingVector = IEmbeddingService.SerializeVector(embedding);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\n⚠️  임베딩 생성 실패 ({item.Question}): {ex.Message}");
                    }
                }

                // DB에 삽입
                InsertCommand(connection, item, embeddingVector);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n⚠️  항목 삽입 실패 ({item.Question}): {ex.Message}");
            }
        }

        Console.WriteLine("\n✓ 모든 명령어가 임포트되었습니다.");

        // FTS 인덱스 최적화
        OptimizeFTS(connection);

        // 통계 출력
        PrintStatistics(connection);

        embeddingService?.Dispose();

        return importedCount;
    }

    private void CreateTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();

        // 메인 테이블
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
                is_success INTEGER DEFAULT 1,
                was_edited INTEGER DEFAULT 0,
                executed_at TEXT NOT NULL,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                embedding_vector TEXT,
                use_count INTEGER DEFAULT 0,
                display_order INTEGER DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_history_executed_at ON command_history(executed_at DESC);
            CREATE INDEX IF NOT EXISTS idx_history_server_profile ON command_history(server_profile);
            CREATE INDEX IF NOT EXISTS idx_history_is_success ON command_history(is_success);
            CREATE INDEX IF NOT EXISTS idx_history_use_count ON command_history(use_count DESC);
        ";
        command.ExecuteNonQuery();

        // FTS5 테이블
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

        // FTS 트리거
        command.CommandText = @"
            CREATE TRIGGER IF NOT EXISTS history_ai AFTER INSERT ON command_history BEGIN
                INSERT INTO command_history_fts(rowid, user_input, generated_command, explanation, output)
                VALUES (new.id, new.user_input, new.generated_command, new.explanation, new.output);
            END;

            CREATE TRIGGER IF NOT EXISTS history_ad AFTER DELETE ON command_history BEGIN
                INSERT INTO command_history_fts(command_history_fts, rowid, user_input, generated_command, explanation, output)
                VALUES ('delete', old.id, old.user_input, old.generated_command, old.explanation, old.output);
            END;

            CREATE TRIGGER IF NOT EXISTS history_au AFTER UPDATE ON command_history BEGIN
                INSERT INTO command_history_fts(command_history_fts, rowid, user_input, generated_command, explanation, output)
                VALUES ('delete', old.id, old.user_input, old.generated_command, old.explanation, old.output);
                INSERT INTO command_history_fts(rowid, user_input, generated_command, explanation, output)
                VALUES (new.id, new.user_input, new.generated_command, new.explanation, new.output);
            END;
        ";
        command.ExecuteNonQuery();

        Console.WriteLine("✓ 테이블 생성 완료");
    }

    private void InsertCommand(SqliteConnection connection, KnowledgeItem item, string? embeddingVector)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO command_history
                (user_input, generated_command, explanation, server_profile, is_success, executed_at, embedding_vector)
            VALUES
                (@userInput, @command, @explanation, @serverProfile, 1, @executedAt, @embedding)
        ";

        // 언어별로 서버 프로필 구분
        var serverProfile = $"__SEED_DATA_{item.Language.ToUpper()}__";

        command.Parameters.AddWithValue("@userInput", item.Question);
        command.Parameters.AddWithValue("@command", item.Command);
        command.Parameters.AddWithValue("@explanation", item.Explanation ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@serverProfile", serverProfile);
        command.Parameters.AddWithValue("@executedAt", DateTime.Now.ToString("O"));
        command.Parameters.AddWithValue("@embedding", embeddingVector ?? (object)DBNull.Value);

        command.ExecuteNonQuery();
    }

    private void OptimizeFTS(SqliteConnection connection)
    {
        Console.WriteLine("FTS 인덱스 최적화 중...");

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO command_history_fts(command_history_fts) VALUES('optimize')";
        command.ExecuteNonQuery();

        Console.WriteLine("✓ FTS 최적화 완료");
    }

    private void PrintStatistics(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();

        // 총 명령어 수
        command.CommandText = "SELECT COUNT(*) FROM command_history";
        var totalCount = (long)command.ExecuteScalar()!;

        // 임베딩이 있는 명령어 수
        command.CommandText = "SELECT COUNT(*) FROM command_history WHERE embedding_vector IS NOT NULL";
        var embeddedCount = (long)command.ExecuteScalar()!;

        // DB 파일 크기
        command.CommandText = "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size()";
        var dbSize = (long)command.ExecuteScalar()!;

        Console.WriteLine("\n=== 통계 ===");
        Console.WriteLine($"총 명령어: {totalCount}개");
        Console.WriteLine($"임베딩 포함: {embeddedCount}개 ({(embeddedCount * 100 / totalCount)}%)");
        Console.WriteLine($"DB 파일 크기: {dbSize / 1024 / 1024:F2} MB");
    }
}
