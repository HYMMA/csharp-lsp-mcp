using CSharpLspMcp.Lsp;
using CSharpLspMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CSharpLspMcp;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Handle version flag
        if (args.Contains("--version") || args.Contains("-v"))
        {
            Console.WriteLine("csharp-lsp-mcp version 1.0.0");
            return 0;
        }

        // Handle help flag
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Configure logging to stderr so it doesn't interfere with MCP protocol
            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            // Set log level based on environment or args
            var logLevel = LogLevel.Warning;
            if (args.Contains("--verbose") || args.Contains("-V"))
                logLevel = LogLevel.Debug;
            if (Environment.GetEnvironmentVariable("MCP_DEBUG") == "1")
                logLevel = LogLevel.Trace;

            builder.Logging.SetMinimumLevel(logLevel);

            // Register LSP client as singleton
            builder.Services.AddSingleton<LspClient>();

            // Configure MCP server with official SDK
            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation
                    {
                        Name = "csharp-lsp-mcp",
                        Version = "1.0.0"
                    };
                })
                .WithStdioServerTransport()
                .WithTools<CSharpTools>()
                .WithTools<XamlTools>();

            var app = builder.Build();

            await app.RunAsync();

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Server crashed: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"csharp-lsp-mcp - MCP Server for C# and XAML Language Intelligence

USAGE:
    csharp-lsp-mcp [OPTIONS]

OPTIONS:
    -h, --help      Show this help message
    -v, --version   Show version information
    -V, --verbose   Enable verbose logging (to stderr)

ENVIRONMENT VARIABLES:
    MCP_DEBUG=1     Enable trace-level logging

DESCRIPTION:
    This MCP server provides C# language intelligence via csharp-ls and
    built-in XAML analysis for WPF/WinUI projects.

    Built with the official Microsoft Model Context Protocol SDK.

PREREQUISITES:
    Install csharp-ls: dotnet tool install --global csharp-ls

USAGE WITH CLAUDE:
    Add to your Claude configuration:
    {
      ""mcpServers"": {
        ""csharp"": {
          ""command"": ""csharp-lsp-mcp""
        }
      }
    }

AVAILABLE TOOLS:
  C# Tools (require csharp-ls):
    csharp_set_workspace  - Set the workspace/solution directory
    csharp_diagnostics    - Get compiler errors and warnings
    csharp_hover          - Get type info at a position
    csharp_completions    - Get IntelliSense completions
    csharp_definition     - Go to definition
    csharp_references     - Find all references
    csharp_symbols        - Get document symbols
    csharp_code_actions   - Get available code actions
    csharp_rename         - Preview symbol rename

  XAML Tools (built-in):
    xaml_validate         - Validate XAML for errors and issues
    xaml_bindings         - Extract and analyze data bindings
    xaml_resources        - List and check resource references
    xaml_names            - List x:Name declarations
    xaml_structure        - Show element tree structure
    xaml_find_binding_errors - Find binding errors
    xaml_extract_viewmodel   - Generate ViewModel from bindings
");
    }
}
