using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace AssetStudioCLI.Mcp
{
    internal static class McpServer
    {
        public static bool IsMcpCommand(string[] args)
        {
            return args.Length > 0 && (args[0] == "mcp" || args[0] == "--mcp");
        }

        public static async Task RunAsync(string[] args)
        {
            // stdout carries the JSON-RPC stream; anything the CLI prints must go to stderr
            Console.SetOut(Console.Error);

            var builder = Host.CreateApplicationBuilder(args[1..]);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
                    {
                        Name = "assetstudio",
                        Title = "AssetStudioMod",
                        Version = version,
                    };
                    options.ServerInstructions =
                        "Tools for extracting assets (textures, sprites, audio, models, text, Live2D) from Unity games. " +
                        "Typical flow: inspect_assets on the game's data folder, list_assets with filters to find what you need, " +
                        "then export_assets / export_models / export_live2d into an output folder. " +
                        "Each call reloads the input files, so pass asset_types to keep loading fast on large games. Calls run one at a time.";
                })
                .WithStdioServerTransport()
                .WithToolsFromAssembly(typeof(AssetStudioTools).Assembly);

            await builder.Build().RunAsync();
        }
    }
}
