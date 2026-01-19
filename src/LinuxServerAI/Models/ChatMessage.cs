using System;

namespace Nebula.Models;

/// <summary>
/// 채팅 메시지 모델
/// </summary>
public class ChatMessage
{
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public DateTime Timestamp { get; set; }
    public MessageType Type { get; set; }

    public ChatMessage()
    {
        Timestamp = DateTime.Now;
    }

    public ChatMessage(string content, bool isUser, MessageType type = MessageType.Normal)
    {
        Content = content;
        IsUser = isUser;
        Type = type;
        Timestamp = DateTime.Now;
    }
}

public enum MessageType
{
    Normal,      // 일반 메시지
    Command,     // 실행된 명령어
    Success,     // 성공 결과
    Error,       // 오류 메시지
    Warning,     // 경고 메시지
    Info         // 정보 메시지
}
