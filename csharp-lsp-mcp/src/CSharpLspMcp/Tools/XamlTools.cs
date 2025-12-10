using System.ComponentModel;
using System.Text;
using CSharpLspMcp.Xaml;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CSharpLspMcp.Tools;

/// <summary>
/// MCP tools for XAML analysis
/// </summary>
[McpServerToolType]
public class XamlTools
{
    private readonly ILogger<XamlTools> _logger;
    private readonly XamlAnalyzer _analyzer;
    private readonly XamlParser _parser;
    private string? _workspacePath;

    public XamlTools(ILogger<XamlTools> logger)
    {
        _logger = logger;
        _analyzer = new XamlAnalyzer(LoggerFactory.Create(b => b.AddConsole()).CreateLogger<XamlAnalyzer>());
        _parser = new XamlParser();
    }

    internal void SetWorkspace(string path)
    {
        _workspacePath = path;
    }

    [McpServerTool(Name = "xaml_validate")]
    [Description("Validate a XAML file for errors, warnings, and common issues. Checks type references, property names, resource keys, binding paths, and more.")]
    public async Task<string> ValidateAsync(
        [Description("Path to the XAML file")] string filePath,
        [Description("XAML content (optional, reads from file if not provided)")] string? content,
        [Description("Path to project directory for assembly-based validation (optional)")] string? projectPath,
        CancellationToken cancellationToken)
    {
        content ??= await File.ReadAllTextAsync(filePath, cancellationToken);
        projectPath ??= _workspacePath;

        var result = await _analyzer.AnalyzeAsync(filePath, content, projectPath, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"XAML Validation: {Path.GetFileName(filePath)}");
        sb.AppendLine(new string('=', 50));

        if (result.ParseResult != null)
        {
            sb.AppendLine($"\nClass: {result.ParseResult.ClassNamespace}.{result.ParseResult.ClassName}");
            sb.AppendLine($"Named Elements: {result.ParseResult.NamedElements.Count}");
            sb.AppendLine($"Resources: {result.ParseResult.Resources.Count}");
            sb.AppendLine($"Bindings: {result.ParseResult.Bindings.Count}");
        }

        if (result.Diagnostics.Count == 0)
        {
            sb.AppendLine("\nNo issues found!");
        }
        else
        {
            var errors = result.Diagnostics.Count(d => d.Severity == XamlDiagnosticSeverity.Error);
            var warnings = result.Diagnostics.Count(d => d.Severity == XamlDiagnosticSeverity.Warning);
            var info = result.Diagnostics.Count(d => d.Severity == XamlDiagnosticSeverity.Info);
            var hints = result.Diagnostics.Count(d => d.Severity == XamlDiagnosticSeverity.Hint);

            sb.AppendLine($"\nFound {result.Diagnostics.Count} issue(s): " +
                          $"{errors} error(s), {warnings} warning(s), {info} info, {hints} hint(s)\n");

            foreach (var diag in result.Diagnostics.OrderBy(d => d.Severity).ThenBy(d => d.Line))
            {
                var icon = diag.Severity switch
                {
                    XamlDiagnosticSeverity.Error => "ERROR",
                    XamlDiagnosticSeverity.Warning => "WARNING",
                    XamlDiagnosticSeverity.Info => "INFO",
                    _ => "HINT"
                };

                sb.AppendLine($"[{icon}] [{diag.Code}] Line {diag.Line}, Col {diag.Column}:");
                sb.AppendLine($"   {diag.Message}");
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "xaml_bindings")]
    [Description("Extract and analyze all data bindings in a XAML file. Shows binding paths, modes, converters, and potential issues.")]
    public async Task<string> GetBindingsAsync(
        [Description("Path to the XAML file")] string filePath,
        [Description("XAML content (optional)")] string? content,
        CancellationToken cancellationToken)
    {
        content ??= await File.ReadAllTextAsync(filePath, cancellationToken);

        var parseResult = _parser.Parse(content, filePath);

        var sb = new StringBuilder();
        sb.AppendLine($"Data Bindings in {Path.GetFileName(filePath)}");
        sb.AppendLine(new string('=', 50));

        if (parseResult.Bindings.Count == 0)
        {
            sb.AppendLine("\nNo data bindings found.");
            return sb.ToString();
        }

        sb.AppendLine($"\nFound {parseResult.Bindings.Count} binding(s):\n");

        foreach (var binding in parseResult.Bindings.OrderBy(b => b.Line))
        {
            sb.AppendLine($"* Line {binding.Line}: {binding.Path}");

            if (!string.IsNullOrEmpty(binding.Mode))
                sb.AppendLine($"    Mode: {binding.Mode}");
            if (!string.IsNullOrEmpty(binding.Converter))
                sb.AppendLine($"    Converter: {binding.Converter}");
            if (!string.IsNullOrEmpty(binding.ElementName))
                sb.AppendLine($"    ElementName: {binding.ElementName}");
        }

        var uniquePaths = parseResult.Bindings
            .Where(b => !string.IsNullOrEmpty(b.Path))
            .Select(b => b.Path.Split('.')[0])
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        if (uniquePaths.Count > 0)
        {
            sb.AppendLine($"\nUnique binding root properties ({uniquePaths.Count}):");
            foreach (var path in uniquePaths)
            {
                sb.AppendLine($"  - {path}");
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "xaml_resources")]
    [Description("List all resources defined in a XAML file and check for unused or missing resource references.")]
    public async Task<string> GetResourcesAsync(
        [Description("Path to the XAML file")] string filePath,
        [Description("XAML content (optional)")] string? content,
        CancellationToken cancellationToken)
    {
        content ??= await File.ReadAllTextAsync(filePath, cancellationToken);

        var parseResult = _parser.Parse(content, filePath);

        var sb = new StringBuilder();
        sb.AppendLine($"Resources in {Path.GetFileName(filePath)}");
        sb.AppendLine(new string('=', 50));

        sb.AppendLine($"\nDefined Resources ({parseResult.Resources.Count}):");
        if (parseResult.Resources.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var resource in parseResult.Resources.OrderBy(r => r.Key))
            {
                var extra = resource.IsStyle && resource.TargetType != null
                    ? $" (TargetType: {resource.TargetType})"
                    : "";
                sb.AppendLine($"  * {resource.Key} ({resource.Type}){extra} - Line {resource.Line}");
            }
        }

        var staticRefs = parseResult.ResourceReferences.Where(r => r.IsStatic).ToList();
        var dynamicRefs = parseResult.ResourceReferences.Where(r => !r.IsStatic).ToList();

        sb.AppendLine($"\nStaticResource References ({staticRefs.Count}):");
        if (staticRefs.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var refGroup in staticRefs.GroupBy(r => r.Key).OrderBy(g => g.Key))
            {
                var defined = parseResult.Resources.Any(r => r.Key == refGroup.Key);
                var status = defined ? "(found)" : "(not in file)";
                sb.AppendLine($"  * {refGroup.Key} ({refGroup.Count()} usage(s)) {status}");
            }
        }

        sb.AppendLine($"\nDynamicResource References ({dynamicRefs.Count}):");
        if (dynamicRefs.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var refGroup in dynamicRefs.GroupBy(r => r.Key).OrderBy(g => g.Key))
            {
                sb.AppendLine($"  * {refGroup.Key} ({refGroup.Count()} usage(s))");
            }
        }

        var usedKeys = parseResult.ResourceReferences.Select(r => r.Key).ToHashSet();
        var unusedResources = parseResult.Resources
            .Where(r => !usedKeys.Contains(r.Key) && !r.IsStyle)
            .ToList();

        if (unusedResources.Count > 0)
        {
            sb.AppendLine($"\nPotentially Unused Resources ({unusedResources.Count}):");
            foreach (var resource in unusedResources)
            {
                sb.AppendLine($"  * {resource.Key} ({resource.Type})");
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "xaml_names")]
    [Description("List all x:Name declarations in a XAML file. Useful for finding named elements and checking for duplicates.")]
    public async Task<string> GetNamesAsync(
        [Description("Path to the XAML file")] string filePath,
        [Description("XAML content (optional)")] string? content,
        CancellationToken cancellationToken)
    {
        content ??= await File.ReadAllTextAsync(filePath, cancellationToken);

        var parseResult = _parser.Parse(content, filePath);

        var sb = new StringBuilder();
        sb.AppendLine($"Named Elements in {Path.GetFileName(filePath)}");
        sb.AppendLine(new string('=', 50));

        if (parseResult.NamedElements.Count == 0)
        {
            sb.AppendLine("\nNo named elements (x:Name) found.");
            return sb.ToString();
        }

        sb.AppendLine($"\nFound {parseResult.NamedElements.Count} named element(s):\n");

        var byType = parseResult.NamedElements
            .GroupBy(n => n.Type)
            .OrderBy(g => g.Key);

        foreach (var group in byType)
        {
            sb.AppendLine($"{group.Key}:");
            foreach (var element in group.OrderBy(e => e.Name))
            {
                sb.AppendLine($"  * {element.Name} (line {element.Line})");
            }
        }

        var duplicates = parseResult.NamedElements
            .GroupBy(n => n.Name)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count > 0)
        {
            sb.AppendLine("\nDuplicate Names Found:");
            foreach (var dup in duplicates)
            {
                sb.AppendLine($"  * '{dup.Key}' appears {dup.Count()} times at lines: " +
                              string.Join(", ", dup.Select(d => d.Line)));
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "xaml_structure")]
    [Description("Show the element tree structure of a XAML file. Displays hierarchy of controls and their types.")]
    public async Task<string> GetStructureAsync(
        [Description("Path to the XAML file")] string filePath,
        [Description("XAML content (optional)")] string? content,
        [Description("Maximum depth to display (default: 10)")] int maxDepth = 10,
        CancellationToken cancellationToken = default)
    {
        content ??= await File.ReadAllTextAsync(filePath, cancellationToken);

        var parseResult = _parser.Parse(content, filePath);

        var sb = new StringBuilder();
        sb.AppendLine($"Element Structure: {Path.GetFileName(filePath)}");
        sb.AppendLine(new string('=', 50));

        if (parseResult.Root == null)
        {
            sb.AppendLine("\nCould not parse XAML structure.");
            if (parseResult.ParseErrors.Count > 0)
            {
                sb.AppendLine("Parse errors:");
                foreach (var error in parseResult.ParseErrors)
                {
                    sb.AppendLine($"  * Line {error.Line}: {error.Message}");
                }
            }
            return sb.ToString();
        }

        sb.AppendLine();
        PrintElementTree(parseResult.Root, sb, 0, maxDepth);

        return sb.ToString();
    }

    [McpServerTool(Name = "xaml_find_binding_errors")]
    [Description("Find potential binding errors in XAML. Checks for ElementName references to non-existent names, converter references, and suspicious binding patterns.")]
    public async Task<string> FindBindingErrorsAsync(
        [Description("Path to the XAML file")] string filePath,
        [Description("XAML content (optional)")] string? content,
        CancellationToken cancellationToken)
    {
        content ??= await File.ReadAllTextAsync(filePath, cancellationToken);

        var parseResult = _parser.Parse(content, filePath);

        var sb = new StringBuilder();
        sb.AppendLine($"Binding Error Analysis: {Path.GetFileName(filePath)}");
        sb.AppendLine(new string('=', 50));

        var issues = new List<string>();

        var namedElements = parseResult.NamedElements.Select(n => n.Name).ToHashSet();
        foreach (var binding in parseResult.Bindings.Where(b => !string.IsNullOrEmpty(b.ElementName)))
        {
            if (!namedElements.Contains(binding.ElementName!))
            {
                issues.Add($"[ERROR] Line {binding.Line}: ElementName=\"{binding.ElementName}\" references non-existent element");
            }
        }

        var resourceKeys = parseResult.Resources.Select(r => r.Key).ToHashSet();
        foreach (var binding in parseResult.Bindings.Where(b => !string.IsNullOrEmpty(b.Converter)))
        {
            if (!resourceKeys.Contains(binding.Converter!))
            {
                issues.Add($"[WARNING] Line {binding.Line}: Converter=\"{binding.Converter}\" not found in file resources");
            }
        }

        foreach (var binding in parseResult.Bindings)
        {
            if (binding.Path.Contains(".."))
            {
                issues.Add($"[WARNING] Line {binding.Line}: Suspicious path with '..': {binding.Path}");
            }

            if (binding.Path.EndsWith("."))
            {
                issues.Add($"[ERROR] Line {binding.Line}: Path ends with '.': {binding.Path}");
            }

            if (binding.Path.StartsWith("[") && !binding.Path.Contains("]"))
            {
                issues.Add($"[ERROR] Line {binding.Line}: Unclosed indexer in path: {binding.Path}");
            }
        }

        foreach (var binding in parseResult.Bindings)
        {
            if (string.IsNullOrEmpty(binding.Path) &&
                string.IsNullOrEmpty(binding.ElementName) &&
                !string.IsNullOrEmpty(binding.Mode))
            {
                issues.Add($"[WARNING] Line {binding.Line}: Binding with Mode but no Path specified");
            }
        }

        if (issues.Count == 0)
        {
            sb.AppendLine("\nNo binding errors detected!");
        }
        else
        {
            sb.AppendLine($"\nFound {issues.Count} potential issue(s):\n");
            foreach (var issue in issues)
            {
                sb.AppendLine(issue);
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "xaml_extract_viewmodel")]
    [Description("Extract the expected ViewModel interface from XAML bindings. Generates a C# interface with properties needed by the XAML bindings.")]
    public async Task<string> ExtractViewModelAsync(
        [Description("Path to the XAML file")] string filePath,
        [Description("XAML content (optional)")] string? content,
        [Description("Name for generated interface (default: IViewModel)")] string interfaceName = "IViewModel",
        CancellationToken cancellationToken = default)
    {
        content ??= await File.ReadAllTextAsync(filePath, cancellationToken);

        var parseResult = _parser.Parse(content, filePath);

        var sb = new StringBuilder();
        sb.AppendLine($"// Generated ViewModel interface from {Path.GetFileName(filePath)}");
        sb.AppendLine($"// Bindings found: {parseResult.Bindings.Count}");
        sb.AppendLine();

        var properties = parseResult.Bindings
            .Where(b => !string.IsNullOrEmpty(b.Path))
            .Select(b => b.Path.Split('.')[0])
            .Where(p => !p.StartsWith("[") && !p.StartsWith("("))
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        var propertyTypes = new Dictionary<string, string>();
        foreach (var binding in parseResult.Bindings)
        {
            if (string.IsNullOrEmpty(binding.Path)) continue;

            var rootProp = binding.Path.Split('.')[0];

            if (binding.Path.Contains("Command") || rootProp.EndsWith("Command"))
            {
                propertyTypes[rootProp] = "ICommand";
            }
            else if (binding.Path.Contains("Items") || rootProp.EndsWith("Items") ||
                     rootProp.EndsWith("Collection") || rootProp.EndsWith("List"))
            {
                propertyTypes[rootProp] = "IEnumerable";
            }
            else if (rootProp.StartsWith("Is") || rootProp.StartsWith("Has") ||
                     rootProp.StartsWith("Can") || rootProp.EndsWith("Enabled") ||
                     rootProp.EndsWith("Visible"))
            {
                propertyTypes[rootProp] = "bool";
            }
            else if (rootProp.EndsWith("Count") || rootProp.EndsWith("Index") ||
                     rootProp.EndsWith("Number"))
            {
                propertyTypes[rootProp] = "int";
            }
            else if (rootProp.EndsWith("Date") || rootProp.EndsWith("Time"))
            {
                propertyTypes[rootProp] = "DateTime";
            }
            else if (!propertyTypes.ContainsKey(rootProp))
            {
                propertyTypes[rootProp] = "object";
            }
        }

        sb.AppendLine($"public interface {interfaceName}");
        sb.AppendLine("{");

        foreach (var prop in properties)
        {
            var propType = propertyTypes.GetValueOrDefault(prop, "object");
            sb.AppendLine($"    {propType} {prop} {{ get; }}");
        }

        sb.AppendLine("}");

        sb.AppendLine();
        sb.AppendLine("// Note: Types are inferred from naming conventions.");
        sb.AppendLine("// Please review and adjust as needed.");

        return sb.ToString();
    }

    private void PrintElementTree(XamlElement element, StringBuilder sb, int depth, int maxDepth)
    {
        if (depth >= maxDepth)
        {
            sb.AppendLine($"{new string(' ', depth * 2)}...");
            return;
        }

        var indent = new string(' ', depth * 2);
        var name = element.Attributes.FirstOrDefault(a => a.Name == "x:Name" || a.Name == "Name")?.Value;
        var nameStr = name != null ? $" (x:Name=\"{name}\")" : "";

        sb.AppendLine($"{indent}* {element.Name}{nameStr}");

        foreach (var child in element.Children.Where(c => !c.Name.Contains(".")))
        {
            PrintElementTree(child, sb, depth + 1, maxDepth);
        }
    }
}
