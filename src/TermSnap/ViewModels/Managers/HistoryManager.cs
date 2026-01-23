using System.Collections.Generic;

namespace TermSnap.ViewModels.Managers;

/// <summary>
/// 명령어 히스토리 관리자
/// </summary>
public class HistoryManager
{
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private string _savedInput = string.Empty;
    private const int MaxHistorySize = 100;

    /// <summary>
    /// 히스토리 개수
    /// </summary>
    public int Count => _commandHistory.Count;

    /// <summary>
    /// 명령어 추가
    /// </summary>
    public void Add(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        // 중복 제거 (마지막 명령어와 같으면 추가 안 함)
        if (_commandHistory.Count > 0 && _commandHistory[^1] == command)
            return;

        _commandHistory.Add(command);

        // 최대 크기 초과 시 오래된 것 삭제
        while (_commandHistory.Count > MaxHistorySize)
        {
            _commandHistory.RemoveAt(0);
        }

        ResetNavigation();
    }

    /// <summary>
    /// 히스토리에서 이전 명령어 (↑)
    /// </summary>
    public string? NavigateUp(string currentInput)
    {
        if (_commandHistory.Count == 0) return null;

        // 처음 탐색 시작할 때 현재 입력 저장
        if (_historyIndex == -1)
        {
            _savedInput = currentInput;
            _historyIndex = _commandHistory.Count;
        }

        if (_historyIndex > 0)
        {
            _historyIndex--;
            return _commandHistory[_historyIndex];
        }

        return _commandHistory.Count > 0 ? _commandHistory[0] : null;
    }

    /// <summary>
    /// 히스토리에서 다음 명령어 (↓)
    /// </summary>
    public string? NavigateDown()
    {
        if (_historyIndex == -1) return null;

        _historyIndex++;

        if (_historyIndex >= _commandHistory.Count)
        {
            // 마지막까지 내려왔으면 저장된 입력 복원
            _historyIndex = -1;
            return _savedInput;
        }

        return _commandHistory[_historyIndex];
    }

    /// <summary>
    /// 히스토리 인덱스 초기화
    /// </summary>
    public void ResetNavigation()
    {
        _historyIndex = -1;
        _savedInput = string.Empty;
    }

    /// <summary>
    /// 모든 히스토리 삭제
    /// </summary>
    public void Clear()
    {
        _commandHistory.Clear();
        ResetNavigation();
    }
}
