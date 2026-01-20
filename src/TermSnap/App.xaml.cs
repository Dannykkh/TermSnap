using System;
using System.Threading.Tasks;
using System.Windows;
using TermSnap.Core;
using TermSnap.Models;
using TermSnap.Services;

namespace TermSnap;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 전역 예외 처리
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 테마 초기화
        ThemeService.Instance.Initialize();

        // JSON → SQLite 마이그레이션 실행
        MigrateHistoryToDatabase();

        // AI Provider 및 임베딩 초기화
        await InitializeAIServicesAsync();
    }

    /// <summary>
    /// AI Provider 및 임베딩 서비스 초기화
    /// </summary>
    private async Task InitializeAIServicesAsync()
    {
        try
        {
            var config = ConfigService.Load();

            // AI Provider 초기화 (API 키가 설정된 경우)
            if (config.SelectedProvider != AIProviderType.None)
            {
                var modelConfig = config.GetModelConfig(config.SelectedProvider, config.SelectedModelId);
                if (modelConfig != null && modelConfig.IsConfigured)
                {
                    AIProviderManager.Instance.SetCurrentProvider(modelConfig);
                }
            }

            // 임베딩 서비스 초기화
            await AIProviderManager.Instance.InitializeEmbeddingAsync(config.Embedding);

            System.Diagnostics.Debug.WriteLine($"AI 서비스 초기화 완료 - 임베딩: {config.Embedding.Type}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AI 서비스 초기화 실패: {ex.Message}");
            // 초기화 실패해도 앱은 계속 실행
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 싱글톤 서비스들 정리
        try
        {
            AIProviderManager.Instance.Dispose();
            HistoryDatabaseService.Instance.Dispose();
            ServiceLocator.Instance.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"리소스 정리 중 오류: {ex.Message}");
        }

        base.OnExit(e);
    }

    /// <summary>
    /// 기존 JSON 히스토리를 SQLite DB로 마이그레이션
    /// </summary>
    private void MigrateHistoryToDatabase()
    {
        try
        {
            var config = ConfigService.Load();
            
            // JSON에 히스토리가 있고, DB가 비어있으면 마이그레이션
            if (config.CommandHistory.Items.Count > 0)
            {
                var dbService = HistoryDatabaseService.Instance;
                var dbCount = dbService.GetHistoryCount();
                
                if (dbCount == 0)
                {
                    // 마이그레이션 수행
                    dbService.MigrateFromJson(config.CommandHistory);
                    
                    // JSON 히스토리 비우고 저장 (중복 방지)
                    config.CommandHistory.Items.Clear();
                    ConfigService.Save(config);
                    
                    System.Diagnostics.Debug.WriteLine($"마이그레이션 완료: {config.CommandHistory.Items.Count}개 항목");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"마이그레이션 실패: {ex.Message}");
            // 마이그레이션 실패해도 앱은 계속 실행
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"예상치 못한 오류가 발생했습니다:\n{e.ExceptionObject}",
            "오류",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // 상세 오류 정보 수집
        var errorDetails = $"오류 유형: {e.Exception.GetType().Name}\n\n";
        errorDetails += $"메시지: {e.Exception.Message}\n\n";

        if (e.Exception.InnerException != null)
        {
            errorDetails += $"내부 오류: {e.Exception.InnerException.Message}\n\n";
        }

        errorDetails += $"스택 추적:\n{e.Exception.StackTrace}";

        // 로그 파일에 기록
        try
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TermSnap",
                "error.log");

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
            System.IO.File.AppendAllText(logPath,
                $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{errorDetails}\n{new string('=', 80)}\n");
        }
        catch { }

        MessageBox.Show(
            errorDetails,
            "오류 발생",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
