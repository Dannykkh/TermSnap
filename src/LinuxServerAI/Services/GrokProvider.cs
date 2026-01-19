using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nebula.Services;

/// <summary>
/// xAI Grok AI 제공자
/// </summary>
public class GrokProvider : IAIProvider
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly HttpClient _httpClient;
    private const string API_ENDPOINT = "https://api.x.ai/v1/chat/completions";

    public string ProviderName => "Grok";
    public string ModelName => _model;

    public GrokProvider(string apiKey, string model = "grok-beta")
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    /// <summary>
    /// 자연어를 리눅스 명령어로 변환
    /// </summary>
    public async Task<string> ConvertToLinuxCommand(string userRequest)
    {
        var systemPrompt = @"당신은 리눅스 서버 관리 전문가입니다.
사용자의 요청을 분석하고 적절한 리눅스 명령어를 생성해주세요.

중요한 규칙:
1. 명령어만 출력하세요 (설명 없이)
2. 위험한 명령어(rm -rf /, dd 등)는 거부하고 경고 메시지를 출력하세요
3. 여러 명령어가 필요하면 && 또는 ; 로 연결하세요
4. sudo가 필요한 경우 명령어에 포함하세요";

        return await GenerateContent(systemPrompt, userRequest);
    }

    /// <summary>
    /// 오류 분석 및 수정된 명령어 제안
    /// </summary>
    public async Task<string> AnalyzeErrorAndSuggestFix(string command, string errorMessage, string context = "")
    {
        var systemPrompt = "당신은 리눅스 서버 관리 전문가입니다. 오류를 분석하고 수정된 명령어를 제안해주세요.";
        var userPrompt = $@"리눅스 명령어 실행 중 오류가 발생했습니다.

실행한 명령어: {command}
오류 메시지: {errorMessage}
{(!string.IsNullOrWhiteSpace(context) ? $"추가 컨텍스트: {context}" : "")}

명령어만 출력하세요 (설명 없이).
오류를 수정할 수 없다면 'ERROR: 설명' 형식으로 응답하세요.";

        return await GenerateContent(systemPrompt, userPrompt);
    }

    /// <summary>
    /// 명령어 설명 생성
    /// </summary>
    public async Task<string> ExplainCommand(string command)
    {
        var systemPrompt = "당신은 리눅스 서버 관리 전문가입니다. 명령어를 초보자도 이해할 수 있도록 한국어로 설명해주세요.";
        var userPrompt = $@"다음 리눅스 명령어를 설명해주세요.

명령어: {command}

설명 규칙:
1. 명령어가 무엇을 하는지 간단하게 설명
2. 주요 옵션과 파라미터 설명
3. 실행 시 주의사항이 있다면 언급
4. 2-3문장으로 간결하게";

        return await GenerateContent(systemPrompt, userPrompt);
    }

    /// <summary>
    /// 대화 모드 - 일반 질의응답
    /// </summary>
    public async Task<string> ChatMode(string question, string? serverContext = null)
    {
        var systemPrompt = "당신은 리눅스 서버 관리 전문가 어시스턴트입니다. 사용자의 질문에 친절하고 정확하게 답변해주세요.";
        var userPrompt = $@"{(!string.IsNullOrWhiteSpace(serverContext) ? $"현재 서버 환경: {serverContext}\n\n" : "")}사용자 질문: {question}";

        return await GenerateContent(systemPrompt, userPrompt);
    }

    /// <summary>
    /// Grok API 호출
    /// </summary>
    private async Task<string> GenerateContent(string systemPrompt, string userPrompt)
    {
        try
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.3,
                max_tokens = 1024
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(API_ENDPOINT, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Grok API 오류: {response.StatusCode}\n{errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseJson);

            var text = result["choices"]?[0]?["message"]?["content"]?.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new Exception("Grok API로부터 유효한 응답을 받지 못했습니다.");
            }

            return text.Trim();
        }
        catch (Exception ex)
        {
            throw new Exception($"Grok API 호출 실패: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// API 키 유효성 검증
    /// </summary>
    public async Task<bool> ValidateApiKey()
    {
        try
        {
            await GenerateContent("You are a helpful assistant.", "test");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
