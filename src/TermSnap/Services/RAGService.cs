using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TermSnap.Core;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// RAG 검색 결과
/// </summary>
public class RAGSearchResult
{
    public CommandHistory? CachedHistory { get; set; }
    public double Similarity { get; set; }
    public bool IsFromCache { get; set; }
    public string SearchMethod { get; set; } = "none"; // "fts5", "embedding", "none"
    public string? CachedCommand { get; set; }
    public string? CachedExplanation { get; set; }

    // AI JSON 응답 메타데이터
    public double Confidence { get; set; } = 1.0;
    public string? Warning { get; set; }
    public List<string>? Alternatives { get; set; }
    public bool RequiresSudo { get; set; }
    public bool IsDangerous { get; set; }
    public string? Category { get; set; }
    public int? EstimatedDuration { get; set; }

    /// <summary>
    /// CachedHistory에서 AI 메타데이터를 복사
    /// </summary>
    public void CopyMetadataFromHistory()
    {
        if (CachedHistory == null) return;

        Confidence = CachedHistory.Confidence;
        Warning = CachedHistory.Warning;
        Alternatives = CachedHistory.Alternatives;
        RequiresSudo = CachedHistory.RequiresSudo;
        IsDangerous = CachedHistory.IsDangerous;
        Category = CachedHistory.Category;
        EstimatedDuration = CachedHistory.EstimatedDuration;
    }
}

/// <summary>
/// 하이브리드 RAG 서비스 (싱글톤)
/// FTS5 키워드 검색 → Embedding 의미 검색 폴백
/// AIProviderManager에서 현재 Provider/EmbeddingService 가져옴
/// </summary>
public class RAGService
{
    private static readonly Lazy<RAGService> _instance =
        new(() => new RAGService(), isThreadSafe: true);

    public static RAGService Instance => _instance.Value;

    private readonly HistoryDatabaseService _historyDb;
    private readonly UsageStatisticsService _statisticsService;

    // 유사도 임계값
    private const double FTS5_MIN_SCORE = 0.5;  // FTS5 검색 최소 점수 (BM25 기반)
    private const double EMBEDDING_MIN_SIMILARITY = 0.75; // 임베딩 최소 유사도

    // 캐시 히트 판단 임계값
    private const double CACHE_HIT_THRESHOLD = 0.85; // 이 이상이면 캐시 결과 직접 사용

    private RAGService()
    {
        _historyDb = HistoryDatabaseService.Instance;
        _statisticsService = UsageStatisticsService.Instance;
    }

    /// <summary>
    /// 현재 Embedding 서비스 (AIProviderManager에서 가져옴)
    /// </summary>
    private IEmbeddingService? EmbeddingService => AIProviderManager.Instance.CurrentEmbeddingService;

    /// <summary>
    /// 현재 AI Provider (AIProviderManager에서 가져옴)
    /// </summary>
    private IAIProvider? AIProvider => AIProviderManager.Instance.CurrentProvider;

    /// <summary>
    /// RAG 기반 명령어 생성
    /// 1. FTS5로 키워드 검색
    /// 2. 결과 없으면 Embedding으로 의미 검색
    /// 3. 둘 다 없으면 AI로 새로 생성
    /// </summary>
    public async Task<RAGSearchResult> SearchOrGenerateCommand(string userInput, string? serverProfile = null)
    {
        var result = new RAGSearchResult();

        // 1단계: FTS5 키워드 검색
        var ftsResults = _historyDb.Search(userInput, limit: 5);
        var relevantFtsResult = ftsResults
            .Where(h => h.IsSuccess)
            .FirstOrDefault();

        if (relevantFtsResult != null)
        {
            // FTS5에서 결과 찾음 - 유사도 체크
            var similarity = CalculateTextSimilarity(userInput, relevantFtsResult.UserInput);

            if (similarity >= CACHE_HIT_THRESHOLD)
            {
                result.IsFromCache = true;
                result.CachedHistory = relevantFtsResult;
                result.CachedCommand = relevantFtsResult.GeneratedCommand;
                result.CachedExplanation = relevantFtsResult.Explanation;
                result.Similarity = similarity;
                result.SearchMethod = "fts5";

                // AI 메타데이터 복사
                result.CopyMetadataFromHistory();

                // 사용 횟수 증가
                _historyDb.IncrementUseCount(relevantFtsResult.Id);

                // 캐시 히트 통계 기록
                _statisticsService.RecordCacheHit();

                return result;
            }
        }

        // 2단계: Embedding 의미 검색 (폴백)
        var embeddingService = EmbeddingService;
        if (embeddingService != null)
        {
            try
            {
                var queryEmbedding = await embeddingService.GetEmbeddingAsync(userInput);
                var embeddingResults = _historyDb.SearchByEmbedding(
                    queryEmbedding, 
                    minSimilarity: EMBEDDING_MIN_SIMILARITY, 
                    limit: 5);

                var bestMatch = embeddingResults.FirstOrDefault();
                
                if (bestMatch.History != null && bestMatch.Similarity >= CACHE_HIT_THRESHOLD)
                {
                    result.IsFromCache = true;
                    result.CachedHistory = bestMatch.History;
                    result.CachedCommand = bestMatch.History.GeneratedCommand;
                    result.CachedExplanation = bestMatch.History.Explanation;
                    result.Similarity = bestMatch.Similarity;
                    result.SearchMethod = "embedding";

                    // AI 메타데이터 복사
                    result.CopyMetadataFromHistory();

                    // 사용 횟수 증가
                    _historyDb.IncrementUseCount(bestMatch.History.Id);

                    // 캐시 히트 통계 기록
                    _statisticsService.RecordCacheHit();

                    return result;
                }
            }
            catch (Exception ex)
            {
                // Embedding 검색 실패 시 무시하고 AI 생성으로 진행
                System.Diagnostics.Debug.WriteLine($"Embedding search failed: {ex.Message}");
            }
        }

        // 3단계: AI로 새로 생성
        result.IsFromCache = false;
        result.SearchMethod = "none";
        result.Similarity = 0;

        // 캐시 미스 통계 기록
        _statisticsService.RecordCacheMiss();

        return result;
    }

    /// <summary>
    /// AI로 명령어 생성 후 DB에 저장 (JSON 구조화 응답 사용)
    /// </summary>
    public async Task<CommandHistory> GenerateAndSaveCommand(
        string userInput,
        string serverProfile)
    {
        var aiProvider = AIProvider;
        if (aiProvider == null)
        {
            throw new InvalidOperationException("AI Provider가 초기화되지 않았습니다.");
        }

        // AI로 명령어 생성 (JSON 구조화)
        var aiResponse = await aiProvider.ConvertToLinuxCommandAsync(userInput);

        // API 호출 통계 기록
        _statisticsService.RecordApiCall();

        // 히스토리 생성 (AI JSON 응답의 모든 필드 포함)
        var history = new CommandHistory(userInput, aiResponse.Command, serverProfile)
        {
            Explanation = aiResponse.Explanation,
            Confidence = aiResponse.Confidence,
            Warning = aiResponse.Warning,
            Alternatives = aiResponse.Alternatives,
            RequiresSudo = aiResponse.RequiresSudo,
            IsDangerous = aiResponse.IsDangerous,
            Category = aiResponse.Category,
            EstimatedDuration = aiResponse.EstimatedDuration,
            ExecutedAt = DateTime.Now
        };

        // Embedding 생성 및 저장
        string? embeddingVector = null;
        var embeddingService = EmbeddingService;
        if (embeddingService != null)
        {
            try
            {
                var embedding = await embeddingService.GetEmbeddingAsync(userInput);
                embeddingVector = IEmbeddingService.SerializeVector(embedding);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Embedding generation failed: {ex.Message}");
            }
        }

        // DB에 저장
        history.Id = _historyDb.AddHistory(history, embeddingVector);

        return history;
    }

    /// <summary>
    /// AI로 명령어 생성 (저장 없이 AICommandResponse 직접 반환)
    /// </summary>
    public async Task<AICommandResponse> GenerateCommandOnly(string userInput)
    {
        var aiProvider = AIProvider;
        if (aiProvider == null)
        {
            throw new InvalidOperationException("AI Provider가 초기화되지 않았습니다.");
        }

        return await aiProvider.ConvertToLinuxCommandAsync(userInput);
    }

    /// <summary>
    /// 기존 히스토리에 Embedding 추가 (배치 처리용)
    /// </summary>
    public async Task<int> GenerateEmbeddingsForExistingHistory(int batchSize = 50)
    {
        var embeddingService = EmbeddingService;
        if (embeddingService == null)
            return 0;

        var historiesWithoutEmbedding = _historyDb.GetHistoriesWithoutEmbedding(batchSize);
        var processedCount = 0;

        foreach (var history in historiesWithoutEmbedding)
        {
            try
            {
                var embedding = await embeddingService.GetEmbeddingAsync(history.UserInput);
                var vectorStr = IEmbeddingService.SerializeVector(embedding);
                _historyDb.UpdateEmbedding(history.Id, vectorStr);
                processedCount++;

                // API 레이트 리밋 방지
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to generate embedding for history {history.Id}: {ex.Message}");
            }
        }

        return processedCount;
    }

    /// <summary>
    /// 자주 사용하는 명령어 목록 조회
    /// </summary>
    public List<FrequentCommand> GetFrequentCommands(int limit = 20, string? serverProfile = null)
    {
        return _historyDb.GetFrequentCommands(limit, serverProfile);
    }

    /// <summary>
    /// 유사한 이전 질문 찾기 (제안용)
    /// </summary>
    public async Task<List<CommandHistory>> FindSimilarQuestions(string userInput, int limit = 5)
    {
        var results = new List<CommandHistory>();

        // FTS5 유사 검색
        var ftsResults = _historyDb.FindSimilar(userInput, limit);
        results.AddRange(ftsResults);

        // Embedding 검색 보완
        var embeddingService = EmbeddingService;
        if (embeddingService != null && results.Count < limit)
        {
            try
            {
                var queryEmbedding = await embeddingService.GetEmbeddingAsync(userInput);
                var embeddingResults = _historyDb.SearchByEmbedding(
                    queryEmbedding,
                    minSimilarity: 0.6, // 제안용이므로 임계값 낮춤
                    limit: limit);

                foreach (var (history, _) in embeddingResults)
                {
                    if (!results.Any(r => r.Id == history.Id))
                    {
                        results.Add(history);
                        if (results.Count >= limit) break;
                    }
                }
            }
            catch { /* 실패 시 FTS 결과만 사용 */ }
        }

        return results.Take(limit).ToList();
    }

    /// <summary>
    /// 텍스트 유사도 계산 (간단한 자카드 유사도)
    /// </summary>
    private static double CalculateTextSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0;

        var words1 = text1.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = text2.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (words1.Count == 0 && words2.Count == 0)
            return 1;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union == 0 ? 0 : (double)intersection / union;
    }
}
