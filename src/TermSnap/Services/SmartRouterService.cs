using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// 스마트 라우터 서비스 - 작업 복잡도 분석 및 최적 모델 선택
/// </summary>
public class SmartRouterService
{
    // 복잡도 판단용 키워드
    private static readonly HashSet<string> SimpleKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "format", "convert", "list", "show", "display", "print", "hello", "hi",
        "what is", "how to", "simple", "basic", "quick", "rename", "copy",
        "move", "delete", "create file", "read file", "변환", "목록", "보여", "출력"
    };

    private static readonly HashSet<string> ComplexKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "architecture", "design", "security", "vulnerability", "optimize", "refactor",
        "migrate", "scale", "performance", "complex", "advanced", "analyze",
        "strategy", "plan", "review", "audit", "integration", "아키텍처", "설계",
        "보안", "취약점", "최적화", "리팩토링", "마이그레이션", "분석", "전략"
    };

    private static readonly HashSet<string> MediumKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "implement", "fix", "bug", "error", "debug", "test", "add", "update",
        "modify", "change", "feature", "function", "method", "class", "구현",
        "수정", "버그", "오류", "디버그", "테스트", "추가", "기능"
    };

    private readonly List<AIModelConfig> _availableModels = new();

    public SmartRouterService()
    {
        RefreshAvailableModels();
    }

    /// <summary>
    /// 사용 가능한 모델 목록 새로고침
    /// </summary>
    public void RefreshAvailableModels()
    {
        _availableModels.Clear();

        try
        {
            var config = ConfigService.Load();
            if (config?.AIModels != null)
            {
                foreach (var model in config.AIModels.Where(m => m.IsConfigured))
                {
                    _availableModels.Add(model);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SmartRouterService] RefreshAvailableModels failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 작업 복잡도 분석
    /// </summary>
    public TaskComplexity AnalyzeComplexity(string taskDescription)
    {
        if (string.IsNullOrWhiteSpace(taskDescription))
            return TaskComplexity.Simple;

        var lowerTask = taskDescription.ToLower();

        // 복잡도 점수 계산
        int score = 0;

        // 복잡한 키워드 체크 (+2점)
        foreach (var keyword in ComplexKeywords)
        {
            if (lowerTask.Contains(keyword.ToLower()))
                score += 2;
        }

        // 중간 키워드 체크 (+1점)
        foreach (var keyword in MediumKeywords)
        {
            if (lowerTask.Contains(keyword.ToLower()))
                score += 1;
        }

        // 단순 키워드 체크 (-1점)
        foreach (var keyword in SimpleKeywords)
        {
            if (lowerTask.Contains(keyword.ToLower()))
                score -= 1;
        }

        // 텍스트 길이 기반 추가 점수
        if (taskDescription.Length > 500) score += 2;
        else if (taskDescription.Length > 200) score += 1;

        // 코드 블록 포함 시 +1
        if (taskDescription.Contains("```")) score += 1;

        // 여러 파일/컴포넌트 언급 시 +1
        if (Regex.IsMatch(lowerTask, @"(multiple|several|many|files?|components?|여러|다수)"))
            score += 1;

        // 점수 → 복잡도 변환
        return score switch
        {
            >= 4 => TaskComplexity.Complex,
            >= 1 => TaskComplexity.Medium,
            _ => TaskComplexity.Simple
        };
    }

    /// <summary>
    /// 복잡도에 따른 권장 모델 티어
    /// </summary>
    public ModelTier GetRecommendedTier(TaskComplexity complexity)
    {
        return complexity switch
        {
            TaskComplexity.Simple => ModelTier.Fast,
            TaskComplexity.Medium => ModelTier.Balanced,
            TaskComplexity.Complex => ModelTier.Powerful,
            _ => ModelTier.Balanced
        };
    }

    /// <summary>
    /// 작업에 최적의 AI Provider 선택
    /// </summary>
    public IAIProvider? SelectProvider(string taskDescription)
    {
        var complexity = AnalyzeComplexity(taskDescription);
        var recommendedTier = GetRecommendedTier(complexity);

        return SelectProviderByTier(recommendedTier);
    }

    /// <summary>
    /// 티어 기반 Provider 선택
    /// </summary>
    public IAIProvider? SelectProviderByTier(ModelTier tier)
    {
        // 해당 티어의 모델 찾기
        var model = _availableModels.FirstOrDefault(m => m.Tier == tier);

        // 없으면 인접 티어에서 찾기
        if (model == null)
        {
            model = tier switch
            {
                ModelTier.Fast => _availableModels.FirstOrDefault(m => m.Tier == ModelTier.Balanced)
                                  ?? _availableModels.FirstOrDefault(m => m.Tier == ModelTier.Powerful),
                ModelTier.Powerful => _availableModels.FirstOrDefault(m => m.Tier == ModelTier.Balanced)
                                      ?? _availableModels.FirstOrDefault(m => m.Tier == ModelTier.Fast),
                _ => _availableModels.FirstOrDefault()
            };
        }

        // 그래도 없으면 아무 모델이나
        model ??= _availableModels.FirstOrDefault();

        if (model == null)
            return null;

        return AIProviderFactory.CreateProviderFromConfig(model);
    }

    /// <summary>
    /// 특정 역할에 최적의 Provider 선택
    /// </summary>
    public IAIProvider? SelectProviderForRole(AgentRole role)
    {
        var tier = AgentRoleInfo.GetRecommendedTier(role);
        return SelectProviderByTier(tier);
    }

    /// <summary>
    /// 사용 가능한 모델 목록 조회
    /// </summary>
    public IReadOnlyList<AIModelConfig> GetAvailableModels() => _availableModels.AsReadOnly();

    /// <summary>
    /// 특정 티어의 모델이 있는지 확인
    /// </summary>
    public bool HasTier(ModelTier tier) => _availableModels.Any(m => m.Tier == tier);

    /// <summary>
    /// 복잡도 분석 결과 요약
    /// </summary>
    public string GetComplexityAnalysisSummary(string taskDescription)
    {
        var complexity = AnalyzeComplexity(taskDescription);
        var tier = GetRecommendedTier(complexity);
        var provider = SelectProviderByTier(tier);

        return $"Complexity: {complexity}, Tier: {tier}, Model: {provider?.ModelName ?? "N/A"}";
    }
}

/// <summary>
/// 스마트 라우터 확장 메서드
/// </summary>
public static class SmartRouterExtensions
{
    /// <summary>
    /// 복잡도를 문자열로 변환
    /// </summary>
    public static string ToDisplayString(this TaskComplexity complexity) => complexity switch
    {
        TaskComplexity.Simple => "Simple (Fast)",
        TaskComplexity.Medium => "Medium (Balanced)",
        TaskComplexity.Complex => "Complex (Powerful)",
        _ => complexity.ToString()
    };

    /// <summary>
    /// 티어를 문자열로 변환
    /// </summary>
    public static string ToDisplayString(this ModelTier tier) => tier switch
    {
        ModelTier.Fast => "Fast (Cost-effective)",
        ModelTier.Balanced => "Balanced",
        ModelTier.Powerful => "Powerful (Premium)",
        _ => tier.ToString()
    };
}
