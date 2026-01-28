using System.Threading.Tasks;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// AI 제공자 공통 인터페이스
/// </summary>
public interface IAIProvider
{
    /// <summary>
    /// 제공자 이름
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 모델 이름
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// 자연어를 리눅스 명령어로 변환 (JSON 구조화 응답)
    /// </summary>
    Task<AICommandResponse> ConvertToLinuxCommandAsync(string userRequest);

    /// <summary>
    /// 자연어를 리눅스 명령어로 변환 (하위 호환성 - 문자열 반환)
    /// </summary>
    Task<string> ConvertToLinuxCommand(string userRequest);

    /// <summary>
    /// 오류 분석 및 수정된 명령어 제안 (JSON 구조화 응답)
    /// </summary>
    Task<AIErrorAnalysisResponse> AnalyzeErrorAsync(string command, string errorMessage, string context = "");

    /// <summary>
    /// 오류 분석 및 수정된 명령어 제안 (하위 호환성 - 문자열 반환)
    /// </summary>
    Task<string> AnalyzeErrorAndSuggestFix(string command, string errorMessage, string context = "");

    /// <summary>
    /// 명령어 설명 생성 (JSON 구조화 응답)
    /// </summary>
    Task<AIExplanationResponse> ExplainCommandAsync(string command);

    /// <summary>
    /// 명령어 설명 생성 (하위 호환성 - 문자열 반환)
    /// </summary>
    Task<string> ExplainCommand(string command);

    /// <summary>
    /// 대화 모드 - 일반 질의응답
    /// </summary>
    Task<string> ChatMode(string question, string? serverContext = null);

    /// <summary>
    /// API 키 유효성 검증
    /// </summary>
    Task<bool> ValidateApiKey();
}
