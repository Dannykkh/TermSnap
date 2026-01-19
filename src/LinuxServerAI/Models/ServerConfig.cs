using System;

namespace Nebula.Models;

/// <summary>
/// SSH 서버 연결 설정 (프로필)
/// </summary>
public class ServerConfig
{
    public string ProfileName { get; set; } = "기본 서버";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;

    // 암호화된 비밀번호 저장
    public string EncryptedPassword { get; set; } = string.Empty;

    public string PrivateKeyPath { get; set; } = string.Empty;

    // SSH 키의 passphrase (암호화 저장)
    public string EncryptedPassphrase { get; set; } = string.Empty;

    public AuthenticationType AuthType { get; set; } = AuthenticationType.Password;

    // 마지막 연결 시간
    public DateTime LastConnected { get; set; } = DateTime.MinValue;

    // 즐겨찾기 여부
    public bool IsFavorite { get; set; } = false;

    // 서버 메모 (MySQL 접속 정보, 중요 경로 등)
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// 설정이 유효한지 확인
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username))
            return false;

        if (AuthType == AuthenticationType.Password && string.IsNullOrWhiteSpace(EncryptedPassword))
            return false;

        if (AuthType == AuthenticationType.PrivateKey && string.IsNullOrWhiteSpace(PrivateKeyPath))
            return false;

        return true;
    }

    /// <summary>
    /// 프로필 복사본 생성
    /// </summary>
    public ServerConfig Clone()
    {
        return new ServerConfig
        {
            ProfileName = ProfileName,
            Host = Host,
            Port = Port,
            Username = Username,
            EncryptedPassword = EncryptedPassword,
            PrivateKeyPath = PrivateKeyPath,
            EncryptedPassphrase = EncryptedPassphrase,
            AuthType = AuthType,
            LastConnected = LastConnected,
            IsFavorite = IsFavorite,
            Notes = Notes
        };
    }

    public override string ToString()
    {
        return $"{ProfileName} ({Username}@{Host}:{Port})";
    }
}

public enum AuthenticationType
{
    Password,
    PrivateKey
}
