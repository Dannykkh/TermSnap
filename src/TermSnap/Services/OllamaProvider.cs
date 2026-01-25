using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
