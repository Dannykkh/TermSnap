using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TermSnap.McpServer;

namespace TermSnap;

/// <summary>
/// 앱 진입점 - MCP 모드와 일반 WPF 모드 분기
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // --mcp 플래그가 있으면 MCP 서버 모드로 실행 (별도 스레드)
        if (args.Contains("--mcp") || args.Contains("-mcp"))
        {
            Console.Error.WriteLine("[TermSnap] Starting in MCP server mode...");
            // MCP 서버는 STA가 필요 없으므로 동기적으로 실행
            return RunMcpServerAsync(args).GetAwaiter().GetResult();
        }

        // 일반 WPF 앱 실행 (STA 스레드에서)
        var app = new App();
        app.InitializeComponent();

        // 폴더 경로 인수 감지 (- 로 시작하지 않는 실존 디렉토리)
        var folderArg = args.FirstOrDefault(a => !a.StartsWith("-") && Directory.Exists(a));
        if (folderArg != null)
            app.StartupFolderPath = Path.GetFullPath(folderArg);

        return app.Run();
    }

    private static async Task<int> RunMcpServerAsync(string[] args)
    {
        await McpServerHost.RunAsync(args);
        return 0;
    }
}
