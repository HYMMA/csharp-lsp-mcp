using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace CSharpLspMcp.Xaml;

/// <summary>
/// Analyzes XAML files for errors, warnings, and common issues.
/// </summary>
public class XamlAnalyzer
{
    private readonly ILogger<XamlAnalyzer> _logger;
    private readonly XamlParser _parser;
    private readonly Dictionary<string, Assembly> _loadedAssemblies = new();
    private readonly Dictionary<string, ResolvedType?> _typeCache = new();

    // Common WPF controls for validation without needing assemblies
    private static readonly HashSet<string> WellKnownWpfTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Containers
        "Window", "Page", "UserControl", "Application", "ResourceDictionary",
        "Grid", "StackPanel", "WrapPanel", "DockPanel", "Canvas", "UniformGrid",
        "Border", "Viewbox", "ScrollViewer", "GroupBox", "Expander", "TabControl", "TabItem",
        
        // Controls
        "Button", "RepeatButton", "ToggleButton", "RadioButton", "CheckBox",
        "TextBox", "TextBlock", "RichTextBox", "PasswordBox", "Label",
        "ComboBox", "ComboBoxItem", "ListBox", "ListBoxItem", "ListView", "ListViewItem",
        "TreeView", "TreeViewItem", "DataGrid", "DataGridTextColumn", "DataGridTemplateColumn",
        "DataGridComboBoxColumn", "DataGridCheckBoxColumn",
        "Menu", "MenuItem", "ContextMenu", "ToolBar", "ToolBarTray", "StatusBar", "StatusBarItem",
        "Slider", "ProgressBar", "Calendar", "DatePicker", "MediaElement",
        "Image", "Frame", "WebBrowser", "DocumentViewer", "FlowDocumentReader",
        "Hyperlink", "InlineUIContainer", "BlockUIContainer",
        
        // Shapes
        "Rectangle", "Ellipse", "Line", "Polyline", "Polygon", "Path",
        
        // Resources & Styling
        "Style", "Setter", "Trigger", "DataTrigger", "EventTrigger", "MultiTrigger", "MultiDataTrigger",
        "ControlTemplate", "DataTemplate", "ItemsPanelTemplate", "HierarchicalDataTemplate",
        "Storyboard", "DoubleAnimation", "ColorAnimation", "ThicknessAnimation",
        "BeginStoryboard", "DoubleAnimationUsingKeyFrames", "ColorAnimationUsingKeyFrames",
        
        // Data
        "Binding", "MultiBinding", "PriorityBinding", "RelativeSource", "TemplateBinding",
        "CollectionViewSource", "ObjectDataProvider", "XmlDataProvider",
        
        // Layout
        "RowDefinition", "ColumnDefinition", "GridSplitter",
        
        // Other
        "ContentPresenter", "ItemsPresenter", "ContentControl", "ItemsControl",
        "AdornerDecorator", "Popup", "ToolTip",
        "Separator", "Thumb", "Track", "TickBar",
        "Run", "Span", "Bold", "Italic", "Underline", "Paragraph",
        "FlowDocument", "Section", "List", "ListItem",
        
        // Transforms
        "RotateTransform", "ScaleTransform", "SkewTransform", "TranslateTransform",
        "MatrixTransform", "TransformGroup",
        
        // Brushes
        "SolidColorBrush", "LinearGradientBrush", "RadialGradientBrush", "ImageBrush",
        "GradientStop",
        
        // Effects
        "DropShadowEffect", "BlurEffect",
        
        // Geometry
        "RectangleGeometry", "EllipseGeometry", "LineGeometry", "PathGeometry",
        "GeometryGroup", "CombinedGeometry", "PathFigure", "LineSegment", "ArcSegment", "BezierSegment"
    };

    // Common binding path typos
    private static readonly Dictionary<string, string> CommonTypos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Visibilty"] = "Visibility",
        ["Visibile"] = "Visibility", 
        ["IsEnabeld"] = "IsEnabled",
        ["IsEnabel"] = "IsEnabled",
        ["Backgroud"] = "Background",
        ["Foregroud"] = "Foreground",
        ["Conent"] = "Content",
        ["Heigth"] = "Height",
        ["Widht"] = "Width",
        ["Maring"] = "Margin",
        ["Pading"] = "Padding",
        ["ItemSouce"] = "ItemsSource",
        ["ItemSource"] = "ItemsSource",
        ["SelectedItme"] = "SelectedItem",
        ["Commnad"] = "Command",
        ["CommandParamter"] = "CommandParameter",
        ["DataContex"] = "DataContext",
        ["TextWraping"] = "TextWrapping",
        ["Strech"] = "Stretch",
        ["Orientaiton"] = "Orientation",
        ["Horiontal"] = "Horizontal",
        ["Verticla"] = "Vertical",
        ["MaxHeigth"] = "MaxHeight",
        ["MinHeigth"] = "MinHeight",
        ["MaxWidht"] = "MaxWidth",
        ["MinWidht"] = "MinWidth",
        ["FontSzie"] = "FontSize",
        ["FontWieght"] = "FontWeight",
        ["ClickMode"] = "ClickMode",
        ["ToolTop"] = "ToolTip"
    };

    // Valid binding modes
    private static readonly HashSet<string> ValidBindingModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "OneWay", "TwoWay", "OneTime", "OneWayToSource", "Default"
    };

    // Valid update source triggers
    private static readonly HashSet<string> ValidUpdateSourceTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Default", "PropertyChanged", "LostFocus", "Explicit"
    };

    public XamlAnalyzer(ILogger<XamlAnalyzer> logger)
    {
        _logger = logger;
        _parser = new XamlParser();
    }

    public async Task<XamlValidationResult> AnalyzeAsync(
        string filePath, 
        string content, 
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        var result = new XamlValidationResult { FilePath = filePath };
        var diagnostics = new List<XamlDiagnostic>();

        try
        {
            // Try to load project assemblies for better type resolution
            if (!string.IsNullOrEmpty(projectPath))
            {
                await Task.Run(() => LoadProjectAssemblies(projectPath), cancellationToken);
            }

            // Parse the XAML
            var parseResult = _parser.Parse(content, filePath);
            result = result with { ParseResult = parseResult };

            // Add any parse errors
            diagnostics.AddRange(parseResult.ParseErrors);

            // Validate types
            if (parseResult.Root != null)
            {
                ValidateElement(parseResult.Root, parseResult.Namespaces, diagnostics);
            }

            // Validate bindings
            foreach (var binding in parseResult.Bindings)
            {
                ValidateBinding(binding, diagnostics);
            }

            // Validate resource references
            ValidateResourceReferences(parseResult, diagnostics);

            // Check for duplicate names
            ValidateNameUniqueness(parseResult.NamedElements, diagnostics);

        }
        catch (Exception ex)
        {
            diagnostics.Add(new XamlDiagnostic
            {
                Message = $"Analysis error: {ex.Message}",
                Severity = XamlDiagnosticSeverity.Error,
                Line = 1,
                Column = 1,
                Code = "XAML000"
            });
        }

        return result with { Diagnostics = diagnostics };
    }

    private void LoadProjectAssemblies(string projectPath)
    {
        try
        {
            var binPath = Path.Combine(projectPath, "bin");
            if (!Directory.Exists(binPath))
                return;

            foreach (var config in new[] { "Debug", "Release" })
            {
                var configPath = Path.Combine(binPath, config);
                if (!Directory.Exists(configPath))
                    continue;

                // Look for target framework folders
                foreach (var tfmDir in Directory.GetDirectories(configPath))
                {
                    LoadAssembliesFromDirectory(tfmDir);
                }

                // Also check directly in config folder
                LoadAssembliesFromDirectory(configPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading project assemblies from {Path}", projectPath);
        }
    }

    private void LoadAssembliesFromDirectory(string path)
    {
        try
        {
            foreach (var dll in Directory.GetFiles(path, "*.dll"))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(dll);
                    if (!_loadedAssemblies.ContainsKey(name))
                    {
                        var assembly = Assembly.LoadFrom(dll);
                        _loadedAssemblies[name] = assembly;
                        _logger.LogDebug("Loaded assembly: {Name}", name);
                    }
                }
                catch
                {
                    // Skip assemblies that can't be loaded
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Error scanning directory {Path}", path);
        }
    }

    private void ValidateElement(
        XamlElement element, 
        Dictionary<string, string> namespaces, 
        List<XamlDiagnostic> diagnostics)
    {
        // Check if element type is valid
        if (!IsValidType(element.Name, element.ClrNamespace, namespaces))
        {
            // Don't warn about property elements (e.g., Grid.RowDefinitions)
            if (!element.Name.Contains('.'))
            {
                diagnostics.Add(new XamlDiagnostic
                {
                    Message = $"Unknown type '{element.Name}'. Verify the type exists and namespace is correct.",
                    Severity = XamlDiagnosticSeverity.Warning,
                    Line = element.Line,
                    Column = element.Column,
                    Code = "XAML001"
                });
            }
        }

        // Validate attributes
        foreach (var attr in element.Attributes)
        {
            ValidateAttribute(element, attr, diagnostics);
        }

        // Recurse into children
        foreach (var child in element.Children)
        {
            ValidateElement(child, namespaces, diagnostics);
        }
    }

    private void ValidateAttribute(XamlElement element, XamlAttribute attr, List<XamlDiagnostic> diagnostics)
    {
        // Check for common typos in property names
        foreach (var (typo, correct) in CommonTypos)
        {
            if (attr.Name.Equals(typo, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new XamlDiagnostic
                {
                    Message = $"Possible typo: '{attr.Name}'. Did you mean '{correct}'?",
                    Severity = XamlDiagnosticSeverity.Warning,
                    Line = attr.Line,
                    Column = attr.Column,
                    Code = "XAML003"
                });
            }
        }
    }

    private void ValidateBinding(XamlBinding binding, List<XamlDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(binding.Path))
        {
            diagnostics.Add(new XamlDiagnostic
            {
                Message = "Binding has empty Path. If intentional, use '{Binding}' without Path.",
                Severity = XamlDiagnosticSeverity.Info,
                Line = binding.Line,
                Column = binding.Column,
                Code = "XAML004"
            });
            return;
        }

        // Check for typos in binding path
        var pathSegments = binding.Path.Split('.');
        foreach (var segment in pathSegments)
        {
            foreach (var (typo, correct) in CommonTypos)
            {
                if (segment.Equals(typo, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new XamlDiagnostic
                    {
                        Message = $"Possible typo in binding path: '{segment}'. Did you mean '{correct}'?",
                        Severity = XamlDiagnosticSeverity.Warning,
                        Line = binding.Line,
                        Column = binding.Column,
                        Code = "XAML003"
                    });
                }
            }
        }

        // Validate binding mode
        if (!string.IsNullOrEmpty(binding.Mode) && !ValidBindingModes.Contains(binding.Mode))
        {
            diagnostics.Add(new XamlDiagnostic
            {
                Message = $"Invalid binding mode '{binding.Mode}'. Valid modes: {string.Join(", ", ValidBindingModes)}",
                Severity = XamlDiagnosticSeverity.Error,
                Line = binding.Line,
                Column = binding.Column,
                Code = "XAML002"
            });
        }
    }

    private void ValidateResourceReferences(XamlParseResult parseResult, List<XamlDiagnostic> diagnostics)
    {
        var definedKeys = parseResult.Resources.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in parseResult.ResourceReferences)
        {
            // Only warn about StaticResource since DynamicResource can be defined elsewhere
            if (reference.IsStatic && !definedKeys.Contains(reference.Key))
            {
                // Don't warn about system resources or theme resources
                if (!IsSystemResource(reference.Key))
                {
                    diagnostics.Add(new XamlDiagnostic
                    {
                        Message = $"StaticResource '{reference.Key}' not found in this file. " +
                                  "It may be defined in App.xaml, a merged dictionary, or a parent scope.",
                        Severity = XamlDiagnosticSeverity.Info,
                        Line = reference.Line,
                        Column = reference.Column,
                        Code = "XAML005"
                    });
                }
            }
        }
    }

    private void ValidateNameUniqueness(List<XamlNamedElement> namedElements, List<XamlDiagnostic> diagnostics)
    {
        var nameGroups = namedElements.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var group in nameGroups.Where(g => g.Count() > 1))
        {
            foreach (var element in group.Skip(1))
            {
                diagnostics.Add(new XamlDiagnostic
                {
                    Message = $"Duplicate x:Name '{element.Name}'. Names must be unique within a XAML scope.",
                    Severity = XamlDiagnosticSeverity.Error,
                    Line = element.Line,
                    Column = element.Column,
                    Code = "XAML006"
                });
            }
        }
    }

    private bool IsValidType(string typeName, string? clrNamespace, Dictionary<string, string> namespaces)
    {
        // Well-known WPF types are always valid
        if (WellKnownWpfTypes.Contains(typeName))
            return true;

        // If we have a CLR namespace, try to resolve the type
        if (!string.IsNullOrEmpty(clrNamespace))
        {
            var cacheKey = $"{clrNamespace}.{typeName}";
            if (_typeCache.TryGetValue(cacheKey, out var cached))
                return cached != null;

            // Try to find in loaded assemblies
            foreach (var assembly in _loadedAssemblies.Values)
            {
                var type = assembly.GetType($"{clrNamespace}.{typeName}");
                if (type != null)
                {
                    _typeCache[cacheKey] = new ResolvedType
                    {
                        FullName = type.FullName!,
                        AssemblyName = assembly.GetName().Name!
                    };
                    return true;
                }
            }

            _typeCache[cacheKey] = null;
        }

        // If we can't validate, assume it's okay (avoid false positives)
        return true;
    }

    private static bool IsSystemResource(string key)
    {
        // Common system/theme resource prefixes
        return key.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("{x:Static", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("{DynamicResource", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("Brush") ||
               key.Contains("Color") ||
               key.Contains("Font") ||
               key.Contains("Theme");
    }
}
