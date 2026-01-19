using System.Threading.Tasks;

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
    /// 자연어를 리눅스 명령어로 변환
    /// </summary>
    Task<string> ConvertToLinuxCommand(string userRequest);
    
    /// <summary>
    /// 오류 분석 및 수정된 명령어 제안
    /// </summary>
    Task<string> AnalyzeErrorAndSuggestFix(string command, string errorMessage, string context = "");
    
    /// <summary>
    /// 명령어 설명 생성
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
