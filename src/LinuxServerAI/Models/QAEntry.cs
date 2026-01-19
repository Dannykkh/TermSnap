using System;

namespace Nebula.Models;

/// <summary>
/// 질문-답변 항목 (사용자 정의 Q&A 데이터베이스)
/// </summary>
public class QAEntry
{
    /// <summary>
    /// 고유 ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 질문 (사용자가 입력)
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// 답변 (사용자가 입력)
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// 카테고리 (선택적)
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// 태그 (콤마로 구분)
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 수정 시간
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// 사용 횟수 (검색 히트 시 증가)
    /// </summary>
    public int UseCount { get; set; } = 0;

    /// <summary>
    /// 임베딩 벡터 (Base64 인코딩된 float 배열)
    /// </summary>
    public string? EmbeddingVector { get; set; }

    /// <summary>
    /// 활성화 여부
    /// </summary>
    public bool IsActive { get; set; } = true;
}
