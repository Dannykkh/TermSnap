using System.Collections.Generic;
using Newtonsoft.Json;

namespace TermSnap.Models;

/// <summary>
/// AI 명령어 생성 응답 (JSON 구조화)
/// </summary>
public class AICommandResponse
{
    /// <summary>
    /// 생성된 리눅스 명령어
    /// </summary>
    [JsonProperty("command")]
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// 명령어 설명 (한국어)
    /// </summary>
    [JsonProperty("explanation")]
    public string? Explanation { get; set; }

    /// <summary>
    /// 경고 메시지 (위험한 명령어일 경우)
    /// </summary>
    [JsonProperty("warning")]
    public string? Warning { get; set; }

    /// <summary>
    /// 대체 명령어 목록
    /// </summary>
    [JsonProperty("alternatives")]
    public List<string>? Alternatives { get; set; }

    /// <summary>
    /// 신뢰도 (0.0 ~ 1.0)
    /// </summary>
    [JsonProperty("confidence")]
    public double Confidence { get; set; } = 1.0;

    /// <summary>
    /// sudo 필요 여부
    /// </summary>
    [JsonProperty("requires_sudo")]
    public bool RequiresSudo { get; set; }

    /// <summary>
    /// 위험한 명령어 여부
    /// </summary>
    [JsonProperty("is_dangerous")]
    public bool IsDangerous { get; set; }

    /// <summary>
    /// 명령어 카테고리 (파일, 네트워크, 프로세스 등)
    /// </summary>
    [JsonProperty("category")]
    public string? Category { get; set; }

    /// <summary>
    /// 예상 실행 시간 (초)
    /// </summary>
    [JsonProperty("estimated_duration")]
    public int? EstimatedDuration { get; set; }

    /// <summary>
    /// 응답이 유효한지 확인
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Command);

    /// <summary>
    /// 경고가 있는지 확인
    /// </summary>
    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);

    /// <summary>
    /// 대체 명령어가 있는지 확인
    /// </summary>
    public bool HasAlternatives => Alternatives != null && Alternatives.Count > 0;
}

/// <summary>
/// AI 오류 분석 응답 (JSON 구조화)
/// </summary>
public class AIErrorAnalysisResponse
{
    /// <summary>
    /// 수정된 명령어
    /// </summary>
    [JsonProperty("fixed_command")]
    public string? FixedCommand { get; set; }

    /// <summary>
    /// 오류 원인 분석
    /// </summary>
    [JsonProperty("error_cause")]
    public string? ErrorCause { get; set; }

    /// <summary>
    /// 해결 방법 설명
    /// </summary>
    [JsonProperty("solution")]
    public string? Solution { get; set; }

    /// <summary>
    /// 수정 가능 여부
    /// </summary>
    [JsonProperty("is_fixable")]
    public bool IsFixable { get; set; } = true;

    /// <summary>
    /// 추가 조치가 필요한지
    /// </summary>
    [JsonProperty("requires_action")]
    public string? RequiresAction { get; set; }

    /// <summary>
    /// 응답이 유효한지 확인
    /// </summary>
    public bool IsValid => IsFixable && !string.IsNullOrWhiteSpace(FixedCommand);
}

/// <summary>
/// AI 명령어 설명 응답 (JSON 구조화)
/// </summary>
public class AIExplanationResponse
{
    /// <summary>
    /// 명령어 요약
    /// </summary>
    [JsonProperty("summary")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// 상세 설명
    /// </summary>
    [JsonProperty("details")]
    public string? Details { get; set; }

    /// <summary>
    /// 옵션 설명 목록
    /// </summary>
    [JsonProperty("options")]
    public List<OptionExplanation>? Options { get; set; }

    /// <summary>
    /// 주의사항
    /// </summary>
    [JsonProperty("cautions")]
    public List<string>? Cautions { get; set; }

    /// <summary>
    /// 관련 명령어
    /// </summary>
    [JsonProperty("related_commands")]
    public List<string>? RelatedCommands { get; set; }
}

/// <summary>
/// 명령어 옵션 설명
/// </summary>
public class OptionExplanation
{
    /// <summary>
    /// 옵션 (예: -r, --recursive)
    /// </summary>
    [JsonProperty("option")]
    public string Option { get; set; } = string.Empty;

    /// <summary>
    /// 설명
    /// </summary>
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;
}
