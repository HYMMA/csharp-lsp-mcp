using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CSharpLspMcp.Lsp;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CSharpLspMcp.Tools;

/// <summary>
/// MCP tools for C# language server functionality
/// </summary>
[McpServerToolType]
public class CSharpTools
{
    private readonly ILogger<CSharpTools> _logger;
    private readonly LspClient _lspClient;
    private readonly Dictionary<string, DocumentState> _openDocuments = new();
    private string? _workspacePath;

    public CSharpTools(ILogger<CSharpTools> logger, LspClient lspClient)
    {
        _logger = logger;
        _lspClient = lspClient;
    }

    [McpServerTool(Name = "csharp_set_workspace")]
    [Description("Set the workspace/solution directory for the C# LSP. Call this first before other operations.")]
    public async Task<string> SetWorkspaceAsync(
        [Description("Path to the solution/project directory")] string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(path))
                return $"Error: Directory does not exist: {path}";

            _workspacePath = path;

            var started = await _lspClient.StartAsync(path, cancellationToken);

            if (!started)
                return "Error: Failed to start LSP server. Make sure csharp-ls is installed: dotnet tool install --global csharp-ls";

            return $"Workspace set to: {path}\nLSP server started successfully.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting workspace to {Path}", path);
            return $"Error setting workspace: {ex.Message}";
        }
    }

    [McpServerTool(Name = "csharp_diagnostics")]
    [Description("Get compiler errors and warnings for C# code. Opens the document in the LSP if not already open.")]
    public async Task<string> GetDiagnosticsAsync(
        [Description("Absolute path to the C# file")] string filePath,
        [Description("Content of the file (optional, reads from disk if not provided)")] string? content,
        CancellationToken cancellationToken)
    {
        try
        {
            var absolutePath = GetAbsolutePath(filePath);
            await EnsureDocumentOpenAsync(absolutePath, content, cancellationToken);

            var diagnostics = await _lspClient.WaitForDiagnosticsAsync(absolutePath, TimeSpan.FromSeconds(5), cancellationToken);

            if (diagnostics == null || diagnostics.Diagnostics.Length == 0)
                return "No diagnostics found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {diagnostics.Diagnostics.Length} diagnostic(s):\n");

            foreach (var diag in diagnostics.Diagnostics.OrderBy(d => d.Range.Start.Line))
            {
                var severity = diag.Severity switch
                {
                    DiagnosticSeverity.Error => "ERROR",
                    DiagnosticSeverity.Warning => "WARNING",
                    DiagnosticSeverity.Information => "INFO",
                    DiagnosticSeverity.Hint => "HINT",
                    _ => "UNKNOWN"
                };

                sb.AppendLine($"[{severity}] Line {diag.Range.Start.Line + 1}, Col {diag.Range.Start.Character + 1}:");
                sb.AppendLine($"  {diag.Message}");
                if (diag.Code != null)
                    sb.AppendLine($"  Code: {diag.Code}");
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting diagnostics for {FilePath}", filePath);
            return $"Error getting diagnostics: {ex.Message}";
        }
    }

    [McpServerTool(Name = "csharp_hover")]
    [Description("Get type information and documentation at a specific position in C# code")]
    public async Task<string> GetHoverAsync(
        [Description("Absolute path to the C# file")] string filePath,
        [Description("0-based line number")] int line,
        [Description("0-based character position")] int character,
        [Description("Content of the file (optional)")] string? content,
        CancellationToken cancellationToken)
    {
        var absolutePath = GetAbsolutePath(filePath);
        await EnsureDocumentOpenAsync(absolutePath, content, cancellationToken);

        var hover = await _lspClient.GetHoverAsync(absolutePath, line, character, cancellationToken);
        if (hover == null)
            return "No hover information available at this position.";

        return FormatHoverContent(hover.Contents);
    }

    [McpServerTool(Name = "csharp_completions")]
    [Description("Get IntelliSense completions at a specific position")]
    public async Task<string> GetCompletionsAsync(
        [Description("Absolute path to the C# file")] string filePath,
        [Description("0-based line number")] int line,
        [Description("0-based character position")] int character,
        [Description("Content of the file (optional)")] string? content,
        [Description("Maximum number of completions to return (default: 20)")] int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        var absolutePath = GetAbsolutePath(filePath);
        await EnsureDocumentOpenAsync(absolutePath, content, cancellationToken);

        var completions = await _lspClient.GetCompletionsAsync(absolutePath, line, character, cancellationToken);
        if (completions == null || completions.Length == 0)
            return "No completions available.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {completions.Length} completion(s):\n");

        foreach (var item in completions.Take(maxResults))
        {
            var kind = item.Kind?.ToString() ?? "Unknown";
            sb.AppendLine($"• {item.Label} ({kind})");
            if (!string.IsNullOrEmpty(item.Detail))
                sb.AppendLine($"  {item.Detail}");
        }

        if (completions.Length > maxResults)
            sb.AppendLine($"\n... and {completions.Length - maxResults} more");

        return sb.ToString();
    }

    [McpServerTool(Name = "csharp_definition")]
    [Description("Go to definition - find where a symbol is defined")]
    public async Task<string> GetDefinitionAsync(
        [Description("Absolute path to the C# file")] string filePath,
        [Description("0-based line number")] int line,
        [Description("0-based character position")] int character,
        [Description("Content of the file (optional)")] string? content,
        CancellationToken cancellationToken)
    {
        var absolutePath = GetAbsolutePath(filePath);
        await EnsureDocumentOpenAsync(absolutePath, content, cancellationToken);

        var locations = await _lspClient.GetDefinitionAsync(absolutePath, line, character, cancellationToken);
        if (locations == null || locations.Length == 0)
            return "No definition found.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {locations.Length} definition(s):\n");

        foreach (var loc in locations)
        {
            var path = new Uri(loc.Uri).LocalPath;
            sb.AppendLine($"• {path}");
            sb.AppendLine($"  Line {loc.Range.Start.Line + 1}, Col {loc.Range.Start.Character + 1}");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "csharp_references")]
    [Description("Find all references to a symbol")]
    public async Task<string> GetReferencesAsync(
        [Description("Absolute path to the C# file")] string filePath,
        [Description("0-based line number")] int line,
        [Description("0-based character position")] int character,
        [Description("Content of the file (optional)")] string? content,
        [Description("Include the declaration in results (default: true)")] bool includeDeclaration = true,
        CancellationToken cancellationToken = default)
    {
        var absolutePath = GetAbsolutePath(filePath);
        await EnsureDocumentOpenAsync(absolutePath, content, cancellationToken);

        var locations = await _lspClient.GetReferencesAsync(absolutePath, line, character, includeDeclaration, cancellationToken);
        if (locations == null || locations.Length == 0)
            return "No references found.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {locations.Length} reference(s):\n");

        var grouped = locations.GroupBy(l => new Uri(l.Uri).LocalPath);
        foreach (var group in grouped)
        {
            sb.AppendLine($"• {group.Key}");
            foreach (var loc in group.OrderBy(l => l.Range.Start.Line))
            {
                sb.AppendLine($"  Line {loc.Range.Start.Line + 1}, Col {loc.Range.Start.Character + 1}");
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "csharp_symbols")]
    [Description("Get all symbols (classes, methods, properties, etc.) in a document")]
    public async Task<string> GetSymbolsAsync(
        [Description("Absolute path to the C# file")] string filePath,
        [Description("Content of the file (optional)")] string? content,
        CancellationToken cancellationToken)
    {
        var absolutePath = GetAbsolutePath(filePath);
        await EnsureDocumentOpenAsync(absolutePath, content, cancellationToken);

        var symbols = await _lspClient.GetDocumentSymbolsAsync(absolutePath, cancellationToken);
        if (symbols == null)
            return "No symbols found.";

        var sb = new StringBuilder();

        if (symbols is DocumentSymbol[] docSymbols)
        {
            sb.AppendLine("Document Symbols:\n");
            FormatDocumentSymbols(sb, docSymbols, 0);
        }
        else if (symbols is SymbolInformation[] symInfos)
        {
            sb.AppendLine($"Found {symInfos.Length} symbol(s):\n");
            foreach (var sym in symInfos)
            {
                sb.AppendLine($"• {sym.Name} ({sym.Kind})");
                if (!string.IsNullOrEmpty(sym.ContainerName))
                    sb.AppendLine($"  Container: {sym.ContainerName}");
                sb.AppendLine($"  Line {sym.Location.Range.Start.Line + 1}");
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "csharp_code_actions")]
    [Description("Get available code actions (quick fixes, refactorings) for a range")]
    public async Task<string> GetCodeActionsAsync(
        [Description("Absolute path to the C# file")] string filePath,
        [Description("0-based start line")] int startLine,
        [Description("0-based start character")] int startCharacter,
        [Description("0-based end line")] int endLine,
        [Description("0-based end character")] int endCharacter,
        [Description("Content of the file (optional)")] string? content,
        CancellationToken cancellationToken)
    {
        var absolutePath = GetAbsolutePath(filePath);
        await EnsureDocumentOpenAsync(absolutePath, content, cancellationToken);

        var range = new Lsp.Range
        {
            Start = new Position { Line = startLine, Character = startCharacter },
            End = new Position { Line = endLine, Character = endCharacter }
        };

        var diagnostics = _lspClient.GetCachedDiagnostics(absolutePath);
        var relevantDiagnostics = diagnostics?.Diagnostics
            .Where(d => d.Range.Start.Line >= startLine && d.Range.End.Line <= endLine)
            .ToArray() ?? Array.Empty<Diagnostic>();

        var actions = await _lspClient.GetCodeActionsAsync(absolutePath, range, relevantDiagnostics, cancellationToken);
        if (actions == null || actions.Length == 0)
            return "No code actions available.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {actions.Length} code action(s):\n");

        foreach (var action in actions)
        {
            sb.AppendLine($"• {action.Title}");
            if (!string.IsNullOrEmpty(action.Kind))
                sb.AppendLine($"  Kind: {action.Kind}");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "csharp_rename")]
    [Description("Rename a symbol across the workspace")]
    public async Task<string> RenameAsync(
        [Description("Absolute path to the C# file")] string filePath,
        [Description("0-based line number")] int line,
        [Description("0-based character position")] int character,
        [Description("The new name for the symbol")] string newName,
        [Description("Content of the file (optional)")] string? content,
        CancellationToken cancellationToken)
    {
        var absolutePath = GetAbsolutePath(filePath);
        await EnsureDocumentOpenAsync(absolutePath, content, cancellationToken);

        var edit = await _lspClient.RenameSymbolAsync(absolutePath, line, character, newName, cancellationToken);
        if (edit == null || edit.Changes == null || edit.Changes.Count == 0)
            return "Cannot rename symbol at this position.";

        var sb = new StringBuilder();
        sb.AppendLine($"Rename to '{newName}' would affect:\n");

        var totalEdits = 0;
        foreach (var (uri, edits) in edit.Changes)
        {
            var path = new Uri(uri).LocalPath;
            sb.AppendLine($"• {path}: {edits.Length} edit(s)");
            totalEdits += edits.Length;
        }

        sb.AppendLine($"\nTotal: {totalEdits} edit(s) in {edit.Changes.Count} file(s)");
        sb.AppendLine("\nNote: This is a preview. Apply the rename in your editor to make changes.");

        return sb.ToString();
    }

    internal void SetWorkspacePath(string path)
    {
        _workspacePath = path;
    }

    private async Task EnsureDocumentOpenAsync(string filePath, string? content, CancellationToken ct)
    {
        if (_workspacePath == null)
        {
            var dir = Path.GetDirectoryName(filePath);
            while (dir != null)
            {
                if (Directory.GetFiles(dir, "*.sln").Any() || Directory.GetFiles(dir, "*.csproj").Any())
                {
                    await _lspClient.StartAsync(dir, ct);
                    _workspacePath = dir;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }

            if (_workspacePath == null)
            {
                _workspacePath = Path.GetDirectoryName(filePath);
                await _lspClient.StartAsync(_workspacePath, ct);
            }
        }

        content ??= await File.ReadAllTextAsync(filePath, ct);

        if (_openDocuments.TryGetValue(filePath, out var state))
        {
            if (state.Content != content)
            {
                state.Content = content;
                state.Version++;
                await _lspClient.UpdateDocumentAsync(filePath, content, state.Version, ct);
            }
        }
        else
        {
            await _lspClient.OpenDocumentAsync(filePath, content, ct);
            _openDocuments[filePath] = new DocumentState { Content = content, Version = 1 };
        }
    }

    private string GetAbsolutePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        if (_workspacePath != null)
            return Path.Combine(_workspacePath, path);

        return Path.GetFullPath(path);
    }

    private static string FormatHoverContent(object contents)
    {
        if (contents is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? "";

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("value", out var value))
                    return value.GetString() ?? "";
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        sb.AppendLine(item.GetString());
                    else if (item.TryGetProperty("value", out var val))
                        sb.AppendLine(val.GetString());
                }
                return sb.ToString();
            }
        }

        return contents.ToString() ?? "";
    }

    private void FormatDocumentSymbols(StringBuilder sb, DocumentSymbol[] symbols, int indent)
    {
        var prefix = new string(' ', indent * 2);
        foreach (var sym in symbols)
        {
            sb.AppendLine($"{prefix}• {sym.Name} ({sym.Kind}) - Line {sym.Range.Start.Line + 1}");
            if (!string.IsNullOrEmpty(sym.Detail))
                sb.AppendLine($"{prefix}  {sym.Detail}");

            if (sym.Children != null && sym.Children.Length > 0)
                FormatDocumentSymbols(sb, sym.Children, indent + 1);
        }
    }

    private class DocumentState
    {
        public required string Content { get; set; }
        public int Version { get; set; }
    }
}
