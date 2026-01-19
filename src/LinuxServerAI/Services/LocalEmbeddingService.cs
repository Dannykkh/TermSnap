using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Nebula.Services;

/// <summary>
/// 로컬 ONNX 임베딩 서비스
/// paraphrase-multilingual-MiniLM-L12-v2 모델 사용 (한글 지원)
/// API 호출 없이 로컬에서 빠르게 임베딩 생성
/// </summary>
public class LocalEmbeddingService : IEmbeddingService
{
    private InferenceSession? _session;
    private Dictionary<string, int>? _vocab;
    private bool _disposed = false;
    private bool _isReady = false;

    // 모델 정보
    private const string MODEL_NAME = "paraphrase-multilingual-MiniLM-L12-v2";
    private const int EMBEDDING_DIM = 384;
    private const int MAX_SEQ_LENGTH = 128;

    // 모델 파일 경로
    private static readonly string ModelDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Nebula", "models", MODEL_NAME
    );

    private static readonly string ModelPath = Path.Combine(ModelDirectory, "model.onnx");
    private static readonly string VocabPath = Path.Combine(ModelDirectory, "vocab.txt");
    private static readonly string TokenizerConfigPath = Path.Combine(ModelDirectory, "tokenizer_config.json");

    // 특수 토큰
    private const string PAD_TOKEN = "[PAD]";
    private const string UNK_TOKEN = "[UNK]";
    private const string CLS_TOKEN = "[CLS]";
    private const string SEP_TOKEN = "[SEP]";

    private int _padTokenId = 0;
    private int _unkTokenId = 100;
    private int _clsTokenId = 101;
    private int _sepTokenId = 102;

    public int Dimensions => EMBEDDING_DIM;
    public string ServiceType => "Local";
    public string ModelName => MODEL_NAME;
    public bool IsReady => _isReady;

    /// <summary>
    /// 모델 파일 존재 여부 확인
    /// </summary>
    public static bool IsModelAvailable()
    {
        return File.Exists(ModelPath) && File.Exists(VocabPath);
    }

    /// <summary>
    /// 모델 디렉토리 경로
    /// </summary>
    public static string GetModelDirectory() => ModelDirectory;

    /// <summary>
    /// 초기화 (모델 로드)
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isReady) return;

        if (!IsModelAvailable())
        {
            throw new FileNotFoundException(
                $"모델 파일을 찾을 수 없습니다.\n" +
                $"경로: {ModelDirectory}\n" +
                $"DownloadModelAsync()를 호출하여 모델을 다운로드하세요.");
        }

        await Task.Run(() =>
        {
            // ONNX 세션 옵션 설정
            var sessionOptions = new SessionOptions();
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            sessionOptions.InterOpNumThreads = Environment.ProcessorCount;
            sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;

            // 모델 로드
            _session = new InferenceSession(ModelPath, sessionOptions);

            // 어휘 사전 로드
            LoadVocabulary();
        });

        _isReady = true;
    }

    /// <summary>
    /// 어휘 사전 로드
    /// </summary>
    private void LoadVocabulary()
    {
        _vocab = new Dictionary<string, int>();
        var lines = File.ReadAllLines(VocabPath, Encoding.UTF8);

        for (int i = 0; i < lines.Length; i++)
        {
            var token = lines[i].Trim();
            if (!string.IsNullOrEmpty(token))
            {
                _vocab[token] = i;
            }
        }

        // 특수 토큰 ID 설정
        if (_vocab.TryGetValue(PAD_TOKEN, out var padId)) _padTokenId = padId;
        if (_vocab.TryGetValue(UNK_TOKEN, out var unkId)) _unkTokenId = unkId;
        if (_vocab.TryGetValue(CLS_TOKEN, out var clsId)) _clsTokenId = clsId;
        if (_vocab.TryGetValue(SEP_TOKEN, out var sepId)) _sepTokenId = sepId;
    }

    /// <summary>
    /// 텍스트를 임베딩 벡터로 변환
    /// </summary>
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        if (!_isReady || _session == null || _vocab == null)
        {
            throw new InvalidOperationException("서비스가 초기화되지 않았습니다. InitializeAsync()를 먼저 호출하세요.");
        }

        if (string.IsNullOrWhiteSpace(text))
            return new float[EMBEDDING_DIM];

        return await Task.Run(() =>
        {
            // 1. 토큰화
            var (inputIds, attentionMask) = Tokenize(text);

            // 2. 입력 텐서 생성
            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, attentionMask.Length });
            var tokenTypeIdsTensor = new DenseTensor<long>(new long[inputIds.Length], new[] { 1, inputIds.Length });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
            };

            // 3. 추론 실행
            using var results = _session.Run(inputs);

            // 4. 출력에서 임베딩 추출 (Mean Pooling)
            var output = results.First();
            var outputTensor = output.AsTensor<float>();

            return MeanPooling(outputTensor, attentionMask);
        });
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
    /// WordPiece 토큰화 (간소화 버전)
    /// </summary>
    private (long[] inputIds, long[] attentionMask) Tokenize(string text)
    {
        var tokens = new List<int> { _clsTokenId };

        // 기본 토큰화: 공백 및 특수문자로 분리
        var words = TokenizeBasic(text);

        foreach (var word in words)
        {
            // WordPiece 토큰화
            var wordTokens = TokenizeWordPiece(word);
            tokens.AddRange(wordTokens);

            if (tokens.Count >= MAX_SEQ_LENGTH - 1)
                break;
        }

        tokens.Add(_sepTokenId);

        // 패딩
        var inputIds = new long[MAX_SEQ_LENGTH];
        var attentionMask = new long[MAX_SEQ_LENGTH];

        for (int i = 0; i < tokens.Count && i < MAX_SEQ_LENGTH; i++)
        {
            inputIds[i] = tokens[i];
            attentionMask[i] = 1;
        }

        return (inputIds, attentionMask);
    }

    /// <summary>
    /// 기본 토큰화 (공백, 구두점 분리)
    /// </summary>
    private List<string> TokenizeBasic(string text)
    {
        var result = new List<string>();
        var currentWord = new StringBuilder();

        foreach (var c in text.ToLower())
        {
            if (char.IsWhiteSpace(c))
            {
                if (currentWord.Length > 0)
                {
                    result.Add(currentWord.ToString());
                    currentWord.Clear();
                }
            }
            else if (char.IsPunctuation(c) || char.IsSymbol(c))
            {
                if (currentWord.Length > 0)
                {
                    result.Add(currentWord.ToString());
                    currentWord.Clear();
                }
                result.Add(c.ToString());
            }
            else
            {
                currentWord.Append(c);
            }
        }

        if (currentWord.Length > 0)
        {
            result.Add(currentWord.ToString());
        }

        return result;
    }

    /// <summary>
    /// WordPiece 토큰화
    /// </summary>
    private List<int> TokenizeWordPiece(string word)
    {
        var tokens = new List<int>();

        if (_vocab!.TryGetValue(word, out var wordId))
        {
            tokens.Add(wordId);
            return tokens;
        }

        // 서브워드 분해
        int start = 0;
        while (start < word.Length)
        {
            int end = word.Length;
            int? curId = null;
            string? curToken = null;

            while (start < end)
            {
                var substr = start > 0 ? "##" + word.Substring(start, end - start) : word.Substring(start, end - start);

                if (_vocab.TryGetValue(substr, out var subId))
                {
                    curId = subId;
                    curToken = substr;
                    break;
                }
                end--;
            }

            if (curId == null)
            {
                // 알 수 없는 토큰
                tokens.Add(_unkTokenId);
                start++;
            }
            else
            {
                tokens.Add(curId.Value);
                start = end;
            }
        }

        return tokens;
    }

    /// <summary>
    /// Mean Pooling (문장 임베딩 생성)
    /// </summary>
    private float[] MeanPooling(Tensor<float> output, long[] attentionMask)
    {
        var dims = output.Dimensions.ToArray();
        var seqLen = dims[1];
        var hiddenSize = dims[2];

        var result = new float[hiddenSize];
        float validTokenCount = 0;

        for (int i = 0; i < seqLen; i++)
        {
            if (attentionMask[i] == 1)
            {
                for (int j = 0; j < hiddenSize; j++)
                {
                    result[j] += output[0, i, j];
                }
                validTokenCount++;
            }
        }

        if (validTokenCount > 0)
        {
            for (int j = 0; j < hiddenSize; j++)
            {
                result[j] /= validTokenCount;
            }
        }

        // L2 정규화
        float norm = 0;
        for (int j = 0; j < hiddenSize; j++)
        {
            norm += result[j] * result[j];
        }
        norm = (float)Math.Sqrt(norm);

        if (norm > 0)
        {
            for (int j = 0; j < hiddenSize; j++)
            {
                result[j] /= norm;
            }
        }

        return result;
    }

    /// <summary>
    /// 모델 다운로드
    /// </summary>
    public static async Task<bool> DownloadModelAsync(IProgress<(string status, int percent)>? progress = null)
    {
        try
        {
            // 디렉토리 생성
            Directory.CreateDirectory(ModelDirectory);

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(30);

            // Hugging Face에서 다운로드할 파일들
            var files = new[]
            {
                ("model.onnx", "https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2/resolve/main/onnx/model.onnx"),
                ("vocab.txt", "https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2/resolve/main/vocab.txt"),
                ("tokenizer_config.json", "https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2/resolve/main/tokenizer_config.json")
            };

            for (int i = 0; i < files.Length; i++)
            {
                var (fileName, url) = files[i];
                var filePath = Path.Combine(ModelDirectory, fileName);

                if (File.Exists(filePath))
                {
                    progress?.Report(($"{fileName} 이미 존재", (i + 1) * 100 / files.Length));
                    continue;
                }

                progress?.Report(($"{fileName} 다운로드 중...", i * 100 / files.Length));

                try
                {
                    using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;

                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (totalBytes > 0)
                        {
                            var filePercent = (int)(totalRead * 100 / totalBytes);
                            var overallPercent = (i * 100 + filePercent) / files.Length;
                            progress?.Report(($"{fileName}: {totalRead / 1024 / 1024}MB / {totalBytes / 1024 / 1024}MB", overallPercent));
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    // ONNX 파일이 해당 경로에 없을 수 있음 - 대체 경로 시도
                    if (fileName == "model.onnx")
                    {
                        throw new Exception(
                            $"모델 다운로드 실패. ONNX 모델을 수동으로 다운로드해주세요.\n\n" +
                            $"1. https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2 방문\n" +
                            $"2. 'Files' 탭에서 ONNX 모델 찾기 또는 변환 필요\n" +
                            $"3. 다운로드 후 {ModelDirectory} 폴더에 저장\n\n" +
                            $"오류: {ex.Message}", ex);
                    }
                    throw;
                }

                progress?.Report(($"{fileName} 완료", (i + 1) * 100 / files.Length));
            }

            progress?.Report(("다운로드 완료!", 100));
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"모델 다운로드 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 모델 삭제
    /// </summary>
    public static void DeleteModel()
    {
        if (Directory.Exists(ModelDirectory))
        {
            Directory.Delete(ModelDirectory, true);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isReady = false;

        _session?.Dispose();
        _session = null;
        _vocab = null;
    }
}
