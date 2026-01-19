using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nebula.Services;

/// <summary>
/// 임베딩 서비스 인터페이스
/// 텍스트를 벡터로 변환하는 기능 제공
/// </summary>
public interface IEmbeddingService : IDisposable
{
    /// <summary>
    /// 임베딩 벡터 차원 수
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// 서비스 타입 (Local, API)
    /// </summary>
    string ServiceType { get; }

    /// <summary>
    /// 모델 이름
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// 서비스 준비 완료 여부
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// 텍스트를 임베딩 벡터로 변환
    /// </summary>
    Task<float[]> GetEmbeddingAsync(string text);

    /// <summary>
    /// 여러 텍스트를 배치로 임베딩 변환
    /// </summary>
    Task<List<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts);

    /// <summary>
    /// 코사인 유사도 계산
    /// </summary>
    static double CosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length || vectorA.Length == 0)
            return 0;

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            magnitudeA += vectorA[i] * vectorA[i];
            magnitudeB += vectorB[i] * vectorB[i];
        }

        magnitudeA = Math.Sqrt(magnitudeA);
        magnitudeB = Math.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0;

        return dotProduct / (magnitudeA * magnitudeB);
    }

    /// <summary>
    /// 벡터를 Base64 문자열로 직렬화 (DB 저장용)
    /// </summary>
    static string SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Base64 문자열을 벡터로 역직렬화
    /// </summary>
    static float[] DeserializeVector(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return Array.Empty<float>();

        var bytes = Convert.FromBase64String(base64);
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }
}
