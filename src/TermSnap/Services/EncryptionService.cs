using System;
using System.Security.Cryptography;
using System.Text;

namespace TermSnap.Services;

/// <summary>
/// Windows DPAPI를 사용한 암호화 서비스
/// </summary>
public static class EncryptionService
{
    // DPAPI는 Windows 사용자 계정에 종속되어 안전하게 암호화/복호화
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Nebula_v1.0_SecureKey");

    /// <summary>
    /// 문자열을 암호화 (Windows DPAPI 사용)
    /// </summary>
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = ProtectedData.Protect(
                plainBytes,
                Entropy,
                DataProtectionScope.CurrentUser
            );

            return Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            throw new Exception($"암호화 실패: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 암호화된 문자열을 복호화
    /// </summary>
    public static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return string.Empty;

        try
        {
            byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
            byte[] plainBytes = ProtectedData.Unprotect(
                encryptedBytes,
                Entropy,
                DataProtectionScope.CurrentUser
            );

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            throw new Exception($"복호화 실패: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 문자열이 암호화된 것인지 확인
    /// </summary>
    public static bool IsEncrypted(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            // Base64 형식인지 확인
            Convert.FromBase64String(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
