using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Nebula.McpServer;

/// <summary>
/// MCP 서버 호스트 - STDIO 전송 방식으로 Claude Code/Desktop과 통신
/// </summary>
public static class McpServerHost
{
    /// <summary>
    /// MCP 서버 실행
    /// </summary>
    public static async Task RunAsync(string[] args)
    {
        Console.Error.WriteLine("[MCP Server] Starting Nebula Terminal MCP Server...");

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // 로깅 설정 (stderr로 출력)
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            // MCP 서버 설정
            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly(typeof(McpServerHost).Assembly);

            // IPC 클라이언트 등록 (싱글톤)
            builder.Services.AddSingleton<IpcClient>();

            var app = builder.Build();

            // IPC 클라이언트 연결
            var ipcClient = app.Services.GetRequiredService<IpcClient>();
            var connected = await ipcClient.ConnectAsync();

            if (!connected)
            {
                Console.Error.WriteLine("[MCP Server] Warning: Could not connect to Nebula Terminal app. Make sure the app is running.");
            }
            else
            {
                Console.Error.WriteLine("[MCP Server] Connected to Nebula Terminal app via IPC.");
            }

            Console.Error.WriteLine("[MCP Server] Server started. Waiting for requests...");

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MCP Server] Fatal error: {ex.Message}");
            throw;
        }
    }
}
