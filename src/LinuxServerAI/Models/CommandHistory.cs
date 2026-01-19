using System;
using System.Collections.Generic;
using System.Linq;

namespace Nebula.Models;

/// <summary>
/// 명령어 실행 히스토리
/// </summary>
public class CommandHistory
{
    // SQLite용 ID (0이면 아직 저장되지 않음)
    public long Id { get; set; } = 0;

    // 기존 호환성을 위한 문자열 ID
    [Obsolete("Use Id instead")]
    public string LegacyId { get; set; } = Guid.NewGuid().ToString();

    public string UserInput { get; set; } = string.Empty;
    public string GeneratedCommand { get; set; } = string.Empty;
    public string? OriginalCommand { get; set; }
    public string? Explanation { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public string ServerProfile { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public bool WasEdited { get; set; } = false;
    public DateTime ExecutedAt { get; set; } = DateTime.Now;

    // 기존 필드명 호환성 (마이그레이션용)
    [Obsolete("Use UserInput instead")]
    public string UserRequest
    {
        get => UserInput;
        set => UserInput = value;
    }

    [Obsolete("Use ServerProfile instead")]
    public string ProfileName
    {
        get => ServerProfile;
        set => ServerProfile = value;
    }

    public CommandHistory()
    {
    }

    public CommandHistory(string userInput, string command, string serverProfile)
    {
        UserInput = userInput;
        GeneratedCommand = command;
        ServerProfile = serverProfile;
    }

    public override string ToString()
    {
        return $"[{ExecutedAt:HH:mm:ss}] {GeneratedCommand}";
    }
}

/// <summary>
/// 명령어 히스토리 컬렉션
/// </summary>
public class CommandHistoryCollection
{
    public List<CommandHistory> Items { get; set; } = new();
    public int MaxHistorySize { get; set; } = 1000;

    public void Add(CommandHistory history)
    {
        Items.Insert(0, history); // 최신 항목을 맨 위에

        // 최대 크기 초과 시 오래된 항목 제거
        if (Items.Count > MaxHistorySize)
        {
            Items = Items.Take(MaxHistorySize).ToList();
        }
    }

    public List<CommandHistory> GetByProfile(string serverProfile)
    {
        return Items.Where(h => h.ServerProfile == serverProfile).ToList();
    }

    public List<CommandHistory> Search(string keyword)
    {
        keyword = keyword.ToLower();
        return Items.Where(h =>
            h.UserInput.ToLower().Contains(keyword) ||
            h.GeneratedCommand.ToLower().Contains(keyword) ||
            (h.Explanation?.ToLower().Contains(keyword) ?? false)
        ).ToList();
    }

    public List<CommandHistory> GetSuccessfulCommands()
    {
        return Items.Where(h => h.IsSuccess).ToList();
    }

    public void Clear()
    {
        Items.Clear();
    }

    public void ClearProfile(string serverProfile)
    {
        Items.RemoveAll(h => h.ServerProfile == serverProfile);
    }
}
