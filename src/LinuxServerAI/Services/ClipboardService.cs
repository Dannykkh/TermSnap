using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Nebula.Services;

/// <summary>
/// 클립보드 서비스 - 이미지 붙여넣기 지원
/// </summary>
public static class ClipboardService
{
    private static readonly string ImageCacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nebula", "ClipboardImages");

    /// <summary>
    /// 클립보드에 이미지가 있는지 확인
    /// </summary>
    public static bool HasImage()
    {
        try
        {
            return Clipboard.ContainsImage();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 클립보드에 텍스트가 있는지 확인
    /// </summary>
    public static bool HasText()
    {
        try
        {
            return Clipboard.ContainsText();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 클립보드에서 텍스트 가져오기
    /// </summary>
    public static string? GetText()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                return Clipboard.GetText();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 클립보드 이미지를 파일로 저장하고 경로 반환
    /// </summary>
    public static string? SaveClipboardImage()
    {
        try
        {
            if (!Clipboard.ContainsImage())
                return null;

            var image = Clipboard.GetImage();
            if (image == null)
                return null;

            // 캐시 폴더 생성
            if (!Directory.Exists(ImageCacheFolder))
            {
                Directory.CreateDirectory(ImageCacheFolder);
            }

            // 고유 파일명 생성
            var fileName = $"clipboard_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            var filePath = Path.Combine(ImageCacheFolder, fileName);

            // PNG로 저장
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(fileStream);
            }

            return filePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"클립보드 이미지 저장 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 클립보드 이미지를 BitmapSource로 가져오기
    /// </summary>
    public static BitmapSource? GetImage()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                return Clipboard.GetImage();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 이미지 캐시 폴더 정리 (오래된 파일 삭제)
    /// </summary>
    public static void CleanupOldImages(int daysToKeep = 7)
    {
        try
        {
            if (!Directory.Exists(ImageCacheFolder))
                return;

            var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
            
            foreach (var file in Directory.GetFiles(ImageCacheFolder, "clipboard_*.png"))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < cutoffDate)
                {
                    try
                    {
                        fileInfo.Delete();
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// 이미지 캐시 폴더 경로
    /// </summary>
    public static string CacheFolder => ImageCacheFolder;
}
