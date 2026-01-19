using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nebula.Models;

namespace Nebula.Services;

/// <summary>
/// API 기반 텍스트 임베딩 서비스 - Gemini/OpenAI Embedding API 지원
/// </summary>
public class ApiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly AIProviderType _provider;
    private readonly string _apiKey;
    private readonly string _model;
    private bool _disposed = false;

    // Gemini Embedding 모델
    private const string GEMINI_EMBEDDING_MODEL = "text-embedding-004";
    private const string GEMINI_API_TEMPLATE = "https://generativelanguage.googleapis.com/v1beta/models/{0}:embedContent";

    // OpenAI Embedding 모델
    private const string OPENAI_EMBEDDING_MODEL = "text-embedding-3-small";
    private const string OPENAI_API_ENDPOINT = "https://api.openai.com/v1/embeddings";

    /// <summary>
    /// 임베딩 벡터 차원 수
    /// </summary>
    public int Dimensions => _provider switch
    {
        AIProviderType.Gemini => 768,
        AIProviderType.OpenAI => 1536,
        _ => 768
    };

    public string ServiceType => "API";

    public string ModelName => _model;

    public bool IsReady => true;

    public ApiEmbeddingService(AIProviderType provider, string apiKey)
    {
        _provider = provider;
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _httpClient = new HttpClient();

        _model = _provider switch
        {
            AIProviderType.Gemini => GEMINI_EMBEDDING_MODEL,
            AIProviderType.OpenAI => OPENAI_EMBEDDING_MODEL,
            _ => GEMINI_EMBEDDING_MODEL
        };

        if (_provider == AIProviderType.OpenAI)
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }
    }

    /// <summary>
    /// 텍스트를 임베딩 벡터로 변환
    /// </summary>
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<float>();

        return _provider switch
        {
            AIProviderType.Gemini => await GetGeminiEmbeddingAsync(text),
            AIProviderType.OpenAI => await GetOpenAIEmbeddingAsync(text),
            _ => await GetGeminiEmbeddingAsync(text)
        };
    }

    /// <summary>
    /// 여러 텍스트를 배치로 임베딩 변환
    /// </summary>
    public async Task<List<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            var embedding = await GetEmbeddingAsync(text);
            results.Add(embedding);
        }
        return results;
    }

    /// <summary>
    /// Gemini Embedding API 호출
    /// </summary>
    private async Task<float[]> GetGeminiEmbeddingAsync(string text)
    {
        try
        {
            var requestBody = new
            {
                model = $"models/{_model}",
                content = new
                {
                    parts = new[]
                    {
                        new { text = text }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var endpoint = string.Format(GEMINI_API_TEMPLATE, _model);
            var response = await _httpClient.PostAsync($"{endpoint}?key={_apiKey}", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini Embedding API 오류: {response.StatusCode}\n{errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseJson);

            var values = result["embedding"]?["values"]?.ToObject<float[]>();
            if (values == null || values.Length == 0)
            {
                throw new Exception("Gemini Embedding API로부터 유효한 응답을 받지 못했습니다.");
            }

            return values;
        }
        catch (Exception ex)
        {
            throw new Exception($"Gemini Embedding 호출 실패: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// OpenAI Embedding API 호출
    /// </summary>
    private async Task<float[]> GetOpenAIEmbeddingAsync(string text)
    {
        try
        {
            var requestBody = new
            {
                input = text,
                model = _model
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(OPENAI_API_ENDPOINT, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI Embedding API 오류: {response.StatusCode}\n{errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseJson);

            var values = result["data"]?[0]?["embedding"]?.ToObject<float[]>();
            if (values == null || values.Length == 0)
            {
                throw new Exception("OpenAI Embedding API로부터 유효한 응답을 받지 못했습니다.");
            }

            return values;
        }
        catch (Exception ex)
        {
            throw new Exception($"OpenAI Embedding 호출 실패: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _httpClient?.Dispose();
    }
}

/// <summary>
/// 이전 버전과의 호환성을 위한 별칭
/// </summary>
[Obsolete("ApiEmbeddingService를 사용하세요.")]
public class EmbeddingService : ApiEmbeddingService
{
    public EmbeddingService(AIProviderType provider, string apiKey) : base(provider, apiKey) { }

    /// <summary>
    /// 코사인 유사도 계산
    /// </summary>
    public static double CosineSimilarity(float[] vectorA, float[] vectorB)
    {
        return IEmbeddingService.CosineSimilarity(vectorA, vectorB);
    }

    /// <summary>
    /// 벡터를 Base64 문자열로 직렬화 (DB 저장용)
    /// </summary>
    public static string SerializeVector(float[] vector)
    {
        return IEmbeddingService.SerializeVector(vector);
    }

    /// <summary>
    /// Base64 문자열을 벡터로 역직렬화
    /// </summary>
    public static float[] DeserializeVector(string base64)
    {
        return IEmbeddingService.DeserializeVector(base64);
    }
}
