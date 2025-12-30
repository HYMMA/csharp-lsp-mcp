using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace CSharpLspMcp.Lsp;

/// <summary>
/// Filters Visual Studio solution files to exclude unsupported project types
/// that would cause csharp-ls to fail or timeout during initialization.
/// </summary>
public partial class SolutionFilter
{
    private readonly ILogger<SolutionFilter> _logger;
    private string? _filteredSlnPath;

    /// <summary>
    /// Default project file extensions that should be excluded from solutions
    /// when loading with csharp-ls.
    /// </summary>
    public static readonly HashSet<string> DefaultExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wixproj",   // WiX installer projects
        ".sqlproj",   // SQL Server database projects
        ".vcxproj",   // C++ projects
        ".vcproj",    // Legacy C++ projects
        ".vbproj",    // VB.NET projects (csharp-ls doesn't support VB)
        ".fsproj",    // F# projects (csharp-ls doesn't support F#)
        ".shproj",    // Shared projects (no direct compilation)
        ".sfproj",    // Service Fabric projects
        ".dcproj",    // Docker Compose projects
        ".esproj",    // JavaScript/TypeScript projects
        ".njsproj",   // Node.js projects
        ".pyproj",    // Python projects
    };

    public SolutionFilter(ILogger<SolutionFilter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Finds the first .sln file in the specified directory.
    /// </summary>
    public string? FindSolutionFile(string workspacePath)
    {
        if (!Directory.Exists(workspacePath))
            return null;

        var slnFiles = Directory.GetFiles(workspacePath, "*.sln", SearchOption.TopDirectoryOnly);
        // Prefer non-filtered solutions
        return slnFiles.FirstOrDefault(f => !f.EndsWith(".filtered.sln", StringComparison.OrdinalIgnoreCase))
            ?? slnFiles.FirstOrDefault();
    }

    /// <summary>
    /// Creates a filtered copy of the solution file, excluding unsupported project types.
    /// The filtered solution uses absolute paths so it can be placed in a temp directory.
    /// </summary>
    /// <param name="originalSlnPath">Path to the original .sln file</param>
    /// <param name="excludedExtensions">Extensions to exclude (uses defaults if null)</param>
    /// <returns>Path to the filtered solution file, or null if no filtering was needed</returns>
    public string? CreateFilteredSolution(string originalSlnPath, ISet<string>? excludedExtensions = null)
    {
        excludedExtensions ??= DefaultExcludedExtensions;

        if (!File.Exists(originalSlnPath))
        {
            _logger.LogWarning("Solution file not found: {Path}", originalSlnPath);
            return null;
        }

        var slnContent = File.ReadAllText(originalSlnPath);
        var slnDir = Path.GetDirectoryName(originalSlnPath)!;
        var slnName = Path.GetFileNameWithoutExtension(originalSlnPath);

        // Find all project entries and determine which to exclude
        var projectPattern = ProjectLineRegex();
        var matches = projectPattern.Matches(slnContent);

        var excludedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectPathReplacements = new Dictionary<string, string>();

        foreach (Match match in matches)
        {
            var projectPath = match.Groups["path"].Value;
            var projectGuid = match.Groups["guid"].Value;
            var extension = Path.GetExtension(projectPath);

            if (excludedExtensions.Contains(extension))
            {
                excludedProjects.Add(projectPath);
                excludedGuids.Add(projectGuid);
                _logger.LogInformation("Excluding unsupported project: {Path} ({Extension})", projectPath, extension);
            }
            else
            {
                // Convert relative path to absolute for included projects
                var absolutePath = Path.GetFullPath(Path.Combine(slnDir, projectPath));
                if (projectPath != absolutePath)
                {
                    projectPathReplacements[projectPath] = absolutePath;
                }
            }
        }

        if (excludedProjects.Count == 0)
        {
            _logger.LogDebug("No projects need filtering in solution: {Path}", originalSlnPath);
            return null; // No filtering needed
        }

        // Remove project entries and their configuration sections, convert paths to absolute
        var filteredContent = FilterSolutionContent(slnContent, excludedGuids, projectPathReplacements);

        // Write filtered solution to temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), "csharp-lsp-mcp", slnName);
        Directory.CreateDirectory(tempDir);

        var filteredSlnPath = Path.Combine(tempDir, $"{slnName}.sln");
        File.WriteAllText(filteredSlnPath, filteredContent, Encoding.UTF8);

        _filteredSlnPath = filteredSlnPath;

        _logger.LogInformation(
            "Created filtered solution at {Path}, excluded {Count} unsupported project(s)",
            filteredSlnPath, excludedProjects.Count);

        return filteredSlnPath;
    }

    private string FilterSolutionContent(string content, HashSet<string> excludedGuids, Dictionary<string, string> pathReplacements)
    {
        var lines = content.Split('\n');
        var result = new StringBuilder();
        var skipUntilEndProject = false;
        var configSectionDepth = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Handle multi-line project blocks (rare but possible)
            if (skipUntilEndProject)
            {
                if (line.Trim().StartsWith("EndProject", StringComparison.Ordinal))
                    skipUntilEndProject = false;
                continue;
            }

            // Check if this is a project line to exclude
            var projectMatch = ProjectLineRegex().Match(line);
            if (projectMatch.Success)
            {
                var guid = projectMatch.Groups["guid"].Value;
                if (excludedGuids.Contains(guid))
                {
                    // Check if EndProject is on the same logical block
                    if (!line.Contains("EndProject"))
                        skipUntilEndProject = true;
                    continue;
                }

                // Replace relative paths with absolute paths
                var relativePath = projectMatch.Groups["path"].Value;
                if (pathReplacements.TryGetValue(relativePath, out var absolutePath))
                {
                    line = line.Replace($"\"{relativePath}\"", $"\"{absolutePath}\"");
                }
            }

            // Handle Global section entries that reference excluded projects
            if (line.Trim().StartsWith("GlobalSection(", StringComparison.Ordinal))
            {
                configSectionDepth++;
            }
            else if (line.Trim() == "EndGlobalSection")
            {
                configSectionDepth--;
            }

            // Skip lines in config sections that reference excluded GUIDs
            if (configSectionDepth > 0)
            {
                var shouldSkip = excludedGuids.Any(guid =>
                    line.Contains(guid, StringComparison.OrdinalIgnoreCase));

                if (shouldSkip)
                    continue;
            }

            result.AppendLine(line);
        }

        return result.ToString();
    }

    /// <summary>
    /// Gets a workspace path that can be used with csharp-ls.
    /// If the workspace contains a solution with unsupported projects, creates a filtered solution
    /// in a temp directory with absolute paths and returns that path.
    /// </summary>
    public string GetFilteredWorkspacePath(string workspacePath, ISet<string>? excludedExtensions = null)
    {
        var slnPath = FindSolutionFile(workspacePath);
        if (slnPath == null)
        {
            _logger.LogDebug("No solution file found in {Path}, using original workspace", workspacePath);
            return workspacePath;
        }

        var filteredSlnPath = CreateFilteredSolution(slnPath, excludedExtensions);
        if (filteredSlnPath == null)
        {
            _logger.LogDebug("No filtering needed for solution, using original workspace");
            return workspacePath;
        }

        // Return the directory containing the filtered solution
        return Path.GetDirectoryName(filteredSlnPath)!;
    }

    /// <summary>
    /// Cleans up any temporary filtered solutions.
    /// </summary>
    public void Cleanup()
    {
        try
        {
            if (_filteredSlnPath != null)
            {
                var dir = Path.GetDirectoryName(_filteredSlnPath);
                if (dir != null && Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogDebug("Cleaned up filtered solution directory: {Path}", dir);
                }
                _filteredSlnPath = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up filtered solution directory");
        }
    }

    [GeneratedRegex(
        @"^Project\s*\(\s*""\{[^}]+\}""\s*\)\s*=\s*""[^""]+""\s*,\s*""(?<path>[^""]+)""\s*,\s*""\{(?<guid>[^}]+)\}""",
        RegexOptions.Multiline)]
    private static partial Regex ProjectLineRegex();
}
