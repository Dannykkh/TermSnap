using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap.ViewModels.Managers;

/// <summary>
/// 로컬 스니펫 관리자
/// </summary>
public class SnippetManager
{
    /// <summary>
    /// 스니펫 목록 (UI 바인딩용)
    /// </summary>
    public ObservableCollection<CommandSnippet> Snippets { get; } = new();

    /// <summary>
    /// 스니펫 로드
    /// </summary>
    public void Load()
    {
        try
        {
            var config = ConfigService.Load();
            var snippets = config.LocalSnippets ?? new System.Collections.Generic.List<CommandSnippet>();

            Snippets.Clear();
            foreach (var snippet in snippets.OrderByDescending(s => s.UseCount).ThenByDescending(s => s.LastUsedAt))
            {
                Snippets.Add(snippet);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"로컬 스니펫 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 스니펫 저장
    /// </summary>
    public void Save()
    {
        try
        {
            var config = ConfigService.Load();
            config.LocalSnippets = Snippets.ToList();
            ConfigService.Save(config);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"로컬 스니펫 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 스니펫 추가
    /// </summary>
    public void Add(CommandSnippet snippet)
    {
        Snippets.Insert(0, snippet);
        Save();
    }

    /// <summary>
    /// 스니펫 삭제
    /// </summary>
    public void Remove(CommandSnippet snippet)
    {
        Snippets.Remove(snippet);
        Save();
    }

    /// <summary>
    /// 스니펫 사용 (사용 횟수 증가 및 재정렬)
    /// </summary>
    public void Use(CommandSnippet snippet)
    {
        snippet.IncrementUseCount();
        Save();

        // 정렬 업데이트
        var sorted = Snippets.OrderByDescending(s => s.UseCount).ThenByDescending(s => s.LastUsedAt).ToList();
        Snippets.Clear();
        foreach (var s in sorted)
        {
            Snippets.Add(s);
        }
    }
}
