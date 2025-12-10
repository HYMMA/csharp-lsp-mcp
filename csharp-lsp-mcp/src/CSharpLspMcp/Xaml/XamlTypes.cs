namespace CSharpLspMcp.Xaml;

public enum XamlDiagnosticSeverity
{
    Error,
    Warning,
    Info,
    Hint
}

public record XamlDiagnostic
{
    public required string Message { get; init; }
    public required XamlDiagnosticSeverity Severity { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public string? Code { get; init; }
    public string? Source { get; init; }
}

public record XamlElement
{
    public required string Name { get; init; }
    public required string? Namespace { get; init; }
    public required string? ClrNamespace { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public List<XamlAttribute> Attributes { get; init; } = new();
    public List<XamlElement> Children { get; init; } = new();
    public XamlElement? Parent { get; init; }
}

public record XamlAttribute
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public bool IsAttached { get; init; }
    public string? OwnerType { get; init; }
    public string? PropertyName { get; init; }
}

public record XamlBinding
{
    public required string Path { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public string? Mode { get; init; }
    public string? Converter { get; init; }
    public string? ElementName { get; init; }
    public string? Source { get; init; }
    public string? RelativeSource { get; init; }
}

public record XamlResource
{
    public required string Key { get; init; }
    public required string Type { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public bool IsStyle { get; init; }
    public string? TargetType { get; init; }
}

public record XamlResourceReference
{
    public required string Key { get; init; }
    public required bool IsStatic { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
}

public record XamlNamedElement
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
}

public record XamlParseResult
{
    public XamlElement? Root { get; init; }
    public List<XamlDiagnostic> ParseErrors { get; init; } = new();
    public List<XamlNamedElement> NamedElements { get; init; } = new();
    public List<XamlResource> Resources { get; init; } = new();
    public List<XamlResourceReference> ResourceReferences { get; init; } = new();
    public List<XamlBinding> Bindings { get; init; } = new();
    public Dictionary<string, string> Namespaces { get; init; } = new();
    public string? ClassName { get; init; }
    public string? ClassNamespace { get; init; }
}

public record XamlValidationResult
{
    public required string FilePath { get; init; }
    public List<XamlDiagnostic> Diagnostics { get; init; } = new();
    public XamlParseResult? ParseResult { get; init; }
}

public record ResolvedType
{
    public required string FullName { get; init; }
    public required string AssemblyName { get; init; }
    public List<string> Properties { get; init; } = new();
    public List<string> Events { get; init; } = new();
    public List<string> AttachedProperties { get; init; } = new();
    public string? BaseType { get; init; }
    public bool IsControl { get; init; }
}
