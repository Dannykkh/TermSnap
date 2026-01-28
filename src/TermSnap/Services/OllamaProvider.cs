using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// Ollama 로컬 AI 제공자 (OpenAI 호환 API 사용)
/// </summary>
public class OllamaProvider : IAIProvider
{
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly HttpClient _httpClient;

    public string ProviderName => "Ollama";
    public string ModelName => _model;

    /// <summary>
    /// Ollama Provider 생성자
    /// </summary>
    /// <param name="model">모델 이름 (예: qwen3, deepseek-r1, llama3.3)</param>
    /// <param name="baseUrl">Ollama 서버 URL (기본값: http://localhost:11434)</param>
    public OllamaProvider(string model = "qwen3:30b-a3b", string? baseUrl = null)
    {
        _model = model ?? "llama3.2";
        _baseUrl = baseUrl?.TrimEnd('/') ?? "http://localhost:11434";
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5) // 로컬 모델은 느릴 수 있음
        };
    }

    #region JSON 구조화 응답 메서드

    /// <summary>
    /// 자연어를 리눅스 명령어로 변환 (JSON 구조화 응답)
    /// </summary>
    public async Task<AICommandResponse> ConvertToLinuxCommandAsync(string userRequest)
    {
        var systemPrompt = @"당신은 리눅스 서버 관리 전문가입니다.
사용자의 요청을 분석하고 적절한 리눅스 명령어를 JSON 형식으로 생성해주세요.

중요한 규칙:
1. 반드시 JSON 형식으로만 응답하세요
2. 위험한 명령어(rm -rf /, dd 등)는 is_dangerous를 true로 설정하고 warning에 경고 메시지를 작성하세요
3. 여러 명령어가 필요하면 && 또는 ; 로 연결하세요
4. sudo가 필요한 경우 requires_sudo를 true로 설정하세요";

        var userPrompt = $@"사용자 요청: {userRequest}

다음 JSON 형식으로만 응답하세요:
{{
  ""command"": ""생성된 리눅스 명령어"",
  ""explanation"": ""명령어가 하는 일을 한국어로 간단히 설명"",
  ""warning"": ""경고 메시지 (없으면 null)"",
  ""alternatives"": [""대체 명령어1"", ""대체 명령어2""],
  ""confidence"": 0.95,
  ""requires_sudo"": false,
  ""is_dangerous"": false,
  ""category"": ""파일/네트워크/프로세스/시스템/패키지 중 하나"",
  ""estimated_duration"": 5
}}";

        var responseText = await GenerateContent(systemPrompt, userPrompt);
        return ParseCommandResponse(responseText);
    }

    /// <summary>
    /// 오류 분석 및 수정된 명령어 제안 (JSON 구조화 응답)
    /// </summary>
    public async Task<AIErrorAnalysisResponse> AnalyzeErrorAsync(string command, string errorMessage, string context = "")
    {
        var systemPrompt = "당신은 리눅스 서버 관리 전문가입니다. 오류를 분석하고 JSON 형식으로 응답하세요.";
        var userPrompt = $@"리눅스 명령어 실행 중 오류가 발생했습니다.

실행한 명령어: {command}
오류 메시지: {errorMessage}
{(!string.IsNullOrWhiteSpace(context) ? $"추가 컨텍스트: {context}" : "")}

다음 JSON 형식으로만 응답하세요:
{{
  ""fixed_command"": ""수정된 명령어 (수정 불가능하면 null)"",
  ""error_cause"": ""오류 원인 분석 (한국어)"",
  ""solution"": ""해결 방법 설명 (한국어)"",
  ""is_fixable"": true,
  ""requires_action"": ""추가 조치가 필요하면 설명 (없으면 null)""
}}";

        var responseText = await GenerateContent(systemPrompt, userPrompt);
        return ParseErrorAnalysisResponse(responseText);
    }

    /// <summary>
    /// 명령어 설명 생성 (JSON 구조화 응답)
    /// </summary>
    public async Task<AIExplanationResponse> ExplainCommandAsync(string command)
    {
        var systemPrompt = "당신은 리눅스 서버 관리 전문가입니다. 명령어를 초보자도 이해할 수 있도록 한국어로 설명해주세요.";
        var userPrompt = $@"다음 리눅스 명령어를 설명해주세요.

명령어: {command}

다음 JSON 형식으로만 응답하세요:
{{
  ""summary"": ""명령어가 하는 일을 한 문장으로 요약"",
  ""details"": ""상세 설명 (2-3문장)"",
  ""options"": [
    {{""option"": ""-r"", ""description"": ""재귀적으로 처리""}},
    {{""option"": ""-f"", ""description"": ""강제 실행""}}
  ],
  ""cautions"": [""주의사항1"", ""주의사항2""],
  ""related_commands"": [""관련 명령어1"", ""관련 명령어2""]
}}";

        var responseText = await GenerateContent(systemPrompt, userPrompt);
        return ParseExplanationResponse(responseText);
    }

    #endregion

    #region 하위 호환성 메서드

    /// <summary>
    /// 자연어를 리눅스 명령어로 변환 (하위 호환성)
    /// </summary>
    public async Task<string> ConvertToLinuxCommand(string userRequest)
    {
        var response = await ConvertToLinuxCommandAsync(userRequest);
        return response.Command;
    }

    /// <summary>
    /// 오류 분석 및 수정된 명령어 제안 (하위 호환성)
    /// </summary>
    public async Task<string> AnalyzeErrorAndSuggestFix(string command, string errorMessage, string context = "")
    {
        var response = await AnalyzeErrorAsync(command, errorMessage, context);
        if (!response.IsFixable || string.IsNullOrEmpty(response.FixedCommand))
        {
            return $"ERROR: {response.ErrorCause}";
        }
        return response.FixedCommand;
    }

    /// <summary>
    /// 명령어 설명 생성 (하위 호환성)
    /// </summary>
    public async Task<string> ExplainCommand(string command)
    {
        var response = await ExplainCommandAsync(command);
        return response.Summary + (string.IsNullOrEmpty(response.Details) ? "" : $"\n{response.Details}");
    }

    #endregion

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
    /// Ollama API 호출 (OpenAI 호환 엔드포인트)
    /// </summary>
    private async Task<string> GenerateContent(string systemPrompt, string userPrompt)
    {
        try
        {
            // OpenAI 호환 API 엔드포인트 사용
            var endpoint = $"{_baseUrl}/v1/chat/completions";

            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.3,
                stream = false
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Ollama API 오류: {response.StatusCode}\n{errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseJson);

            var text = result["choices"]?[0]?["message"]?["content"]?.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new Exception("Ollama API로부터 유효한 응답을 받지 못했습니다.");
            }

            return text.Trim();
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Ollama 서버에 연결할 수 없습니다. Ollama가 실행 중인지 확인하세요.\n{_baseUrl}\n{ex.Message}", ex);
        }
        catch (TaskCanceledException)
        {
            throw new Exception("Ollama 응답 시간 초과. 모델이 로드 중이거나 서버가 응답하지 않습니다.");
        }
        catch (Exception ex)
        {
            throw new Exception($"Ollama API 호출 실패: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Ollama 서버 연결 상태 확인
    /// </summary>
    public async Task<bool> ValidateApiKey()
    {
        try
        {
            // Ollama 버전 체크 엔드포인트로 연결 확인
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/version");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 설치된 모델 목록 조회
    /// </summary>
    public async Task<List<OllamaModel>> GetAvailableModelsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");

            if (!response.IsSuccessStatusCode)
            {
                return new List<OllamaModel>();
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(json);
            var models = new List<OllamaModel>();

            if (result["models"] is JArray modelArray)
            {
                foreach (var model in modelArray)
                {
                    models.Add(new OllamaModel
                    {
                        Name = model["name"]?.ToString() ?? "",
                        Size = model["size"]?.Value<long>() ?? 0,
                        ModifiedAt = model["modified_at"]?.ToString() ?? ""
                    });
                }
            }

            return models;
        }
        catch
        {
            return new List<OllamaModel>();
        }
    }

    /// <summary>
    /// Ollama 서버가 실행 중인지 확인 (정적 메서드)
    /// </summary>
    public static async Task<bool> IsServerRunningAsync(string baseUrl = "http://localhost:11434")
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync($"{baseUrl}/api/version");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    #region JSON 파싱 헬퍼

    private static string ExtractJson(string text)
    {
        var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        var jsonMatch = Regex.Match(text, @"\{[\s\S]*\}");
        if (jsonMatch.Success)
        {
            return jsonMatch.Value;
        }

        return text;
    }

    private static AICommandResponse ParseCommandResponse(string text)
    {
        try
        {
            var json = ExtractJson(text);
            var response = JsonConvert.DeserializeObject<AICommandResponse>(json);
            return response ?? new AICommandResponse { Command = text };
        }
        catch
        {
            return new AICommandResponse { Command = text.Trim(), Confidence = 0.5 };
        }
    }

    private static AIErrorAnalysisResponse ParseErrorAnalysisResponse(string text)
    {
        try
        {
            var json = ExtractJson(text);
            var response = JsonConvert.DeserializeObject<AIErrorAnalysisResponse>(json);
            return response ?? new AIErrorAnalysisResponse { ErrorCause = text };
        }
        catch
        {
            return new AIErrorAnalysisResponse { IsFixable = false, ErrorCause = text.Trim() };
        }
    }

    private static AIExplanationResponse ParseExplanationResponse(string text)
    {
        try
        {
            var json = ExtractJson(text);
            var response = JsonConvert.DeserializeObject<AIExplanationResponse>(json);
            return response ?? new AIExplanationResponse { Summary = text };
        }
        catch
        {
            return new AIExplanationResponse { Summary = text.Trim() };
        }
    }

    #endregion
}

/// <summary>
/// Ollama 모델 정보
/// </summary>
public class OllamaModel
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ModifiedAt { get; set; } = string.Empty;

    /// <summary>
    /// 사람이 읽기 쉬운 크기 (예: 4.1 GB)
    /// </summary>
    public string SizeFormatted => FormatSize(Size);

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.#} {sizes[order]}";
    }
}
