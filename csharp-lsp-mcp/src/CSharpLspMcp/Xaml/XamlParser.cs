using System.Text.RegularExpressions;
using System.Xml;

namespace CSharpLspMcp.Xaml;

/// <summary>
/// Tolerant XAML parser that extracts structure even from invalid/incomplete XAML
/// </summary>
public class XamlParser
{
    private static readonly Regex ClrNamespaceRegex = new(
        @"clr-namespace:(?<ns>[^;]+)(?:;assembly=(?<asm>[^""']+))?",
        RegexOptions.Compiled);

    private static readonly Regex BindingRegex = new(
        @"\{(?:Binding|x:Bind)\s*(?<content>[^}]*)\}",
        RegexOptions.Compiled);

    private static readonly Regex StaticResourceRegex = new(
        @"\{StaticResource\s+(?<key>[^}]+)\}",
        RegexOptions.Compiled);

    private static readonly Regex DynamicResourceRegex = new(
        @"\{DynamicResource\s+(?<key>[^}]+)\}",
        RegexOptions.Compiled);

    private static readonly Regex BindingPathRegex = new(
        @"(?:^|,)\s*(?:Path\s*=\s*)?(?<path>[A-Za-z_][A-Za-z0-9_\.]*)",
        RegexOptions.Compiled);

    public XamlParseResult Parse(string content, string? filePath = null)
    {
        var result = new XamlParseResult
        {
            ParseErrors = new List<XamlDiagnostic>(),
            NamedElements = new List<XamlNamedElement>(),
            Resources = new List<XamlResource>(),
            ResourceReferences = new List<XamlResourceReference>(),
            Bindings = new List<XamlBinding>(),
            Namespaces = new Dictionary<string, string>()
        };

        try
        {
            using var stringReader = new StringReader(content);
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = false,
                DtdProcessing = DtdProcessing.Ignore
            };

            using var reader = XmlReader.Create(stringReader, settings);
            var elementStack = new Stack<XamlElement>();
            XamlElement? root = null;

            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        var element = ParseElement(reader, result, elementStack.Count > 0 ? elementStack.Peek() : null);

                        if (root == null)
                        {
                            root = element;
                            // Extract x:Class from root
                            var classAttr = element.Attributes.FirstOrDefault(a => 
                                a.Name == "Class" || a.Name == "x:Class");
                            if (classAttr != null)
                            {
                                var parts = classAttr.Value.Split('.');
                                result = result with
                                {
                                    ClassName = parts[^1],
                                    ClassNamespace = parts.Length > 1 
                                        ? string.Join(".", parts[..^1]) 
                                        : null
                                };
                            }
                        }
                        else if (elementStack.Count > 0)
                        {
                            elementStack.Peek().Children.Add(element);
                        }

                        // Extract namespaces from root element
                        if (elementStack.Count == 0)
                        {
                            ExtractNamespaces(reader, result);
                        }

                        // Check for x:Name or Name
                        var nameAttr = element.Attributes.FirstOrDefault(a => 
                            a.Name == "x:Name" || a.Name == "Name");
                        if (nameAttr != null)
                        {
                            result.NamedElements.Add(new XamlNamedElement
                            {
                                Name = nameAttr.Value,
                                Type = element.Name,
                                Line = element.Line,
                                Column = element.Column
                            });
                        }

                        // Check for x:Key (resource)
                        var keyAttr = element.Attributes.FirstOrDefault(a => a.Name == "x:Key");
                        if (keyAttr != null)
                        {
                            var targetTypeAttr = element.Attributes.FirstOrDefault(a => a.Name == "TargetType");
                            result.Resources.Add(new XamlResource
                            {
                                Key = keyAttr.Value,
                                Type = element.Name,
                                Line = element.Line,
                                Column = element.Column,
                                IsStyle = element.Name == "Style",
                                TargetType = targetTypeAttr?.Value
                            });
                        }

                        // Parse bindings and resource references from attributes
                        foreach (var attr in element.Attributes)
                        {
                            ParseBindingsAndResources(attr.Value, attr.Line, attr.Column, result);
                        }

                        if (!reader.IsEmptyElement)
                        {
                            elementStack.Push(element);
                        }
                        break;

                    case XmlNodeType.EndElement:
                        if (elementStack.Count > 0)
                        {
                            elementStack.Pop();
                        }
                        break;

                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                        // Check text content for bindings too
                        var lineInfo = reader as IXmlLineInfo;
                        ParseBindingsAndResources(
                            reader.Value, 
                            lineInfo?.LineNumber ?? 0, 
                            lineInfo?.LinePosition ?? 0, 
                            result);
                        break;
                }
            }

            return result with { Root = root };
        }
        catch (XmlException ex)
        {
            result.ParseErrors.Add(new XamlDiagnostic
            {
                Message = $"XML parse error: {ex.Message}",
                Severity = XamlDiagnosticSeverity.Error,
                Line = ex.LineNumber,
                Column = ex.LinePosition,
                Code = "XAML001",
                Source = "XamlParser"
            });

            // Try to recover partial information using regex fallback
            result = TryRecoverWithRegex(content, result);

            return result;
        }
    }

    private XamlElement ParseElement(XmlReader reader, XamlParseResult result, XamlElement? parent)
    {
        var lineInfo = reader as IXmlLineInfo;
        var line = lineInfo?.LineNumber ?? 0;
        var column = lineInfo?.LinePosition ?? 0;

        var element = new XamlElement
        {
            Name = reader.LocalName,
            Namespace = reader.NamespaceURI,
            ClrNamespace = GetClrNamespace(reader.NamespaceURI, result.Namespaces),
            Line = line,
            Column = column,
            Parent = parent
        };

        // Parse attributes
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                var attrName = reader.Name;
                var isAttached = attrName.Contains('.');

                string? ownerType = null;
                string? propertyName = attrName;

                if (isAttached)
                {
                    var parts = attrName.Split('.', 2);
                    ownerType = parts[0];
                    propertyName = parts[1];
                }

                element.Attributes.Add(new XamlAttribute
                {
                    Name = reader.Name,
                    Value = reader.Value,
                    Line = lineInfo?.LineNumber ?? 0,
                    Column = lineInfo?.LinePosition ?? 0,
                    IsAttached = isAttached,
                    OwnerType = ownerType,
                    PropertyName = propertyName
                });
            }
            reader.MoveToElement();
        }

        return element;
    }

    private void ExtractNamespaces(XmlReader reader, XamlParseResult result)
    {
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Name.StartsWith("xmlns"))
                {
                    var prefix = reader.Name == "xmlns" ? "" : reader.LocalName;
                    result.Namespaces[prefix] = reader.Value;
                }
            }
            reader.MoveToElement();
        }
    }

    private string? GetClrNamespace(string xmlNamespace, Dictionary<string, string> namespaces)
    {
        var match = ClrNamespaceRegex.Match(xmlNamespace);
        if (match.Success)
        {
            return match.Groups["ns"].Value;
        }

        // Check for common WPF namespaces
        return xmlNamespace switch
        {
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation" => "System.Windows.Controls",
            "http://schemas.microsoft.com/winfx/2006/xaml" => "System.Windows.Markup",
            _ => null
        };
    }

    private void ParseBindingsAndResources(string value, int line, int column, XamlParseResult result)
    {
        // Find bindings
        foreach (Match match in BindingRegex.Matches(value))
        {
            var bindingContent = match.Groups["content"].Value;
            var pathMatch = BindingPathRegex.Match(bindingContent);
            
            var binding = new XamlBinding
            {
                Path = pathMatch.Success ? pathMatch.Groups["path"].Value : "",
                Line = line,
                Column = column + match.Index
            };

            // Extract other binding properties
            if (bindingContent.Contains("Mode="))
            {
                var modeMatch = Regex.Match(bindingContent, @"Mode\s*=\s*(\w+)");
                if (modeMatch.Success)
                    binding = binding with { Mode = modeMatch.Groups[1].Value };
            }

            if (bindingContent.Contains("Converter="))
            {
                var converterMatch = Regex.Match(bindingContent, @"Converter\s*=\s*\{StaticResource\s+(\w+)\}");
                if (converterMatch.Success)
                    binding = binding with { Converter = converterMatch.Groups[1].Value };
            }

            if (bindingContent.Contains("ElementName="))
            {
                var elementMatch = Regex.Match(bindingContent, @"ElementName\s*=\s*(\w+)");
                if (elementMatch.Success)
                    binding = binding with { ElementName = elementMatch.Groups[1].Value };
            }

            result.Bindings.Add(binding);
        }

        // Find StaticResource references
        foreach (Match match in StaticResourceRegex.Matches(value))
        {
            result.ResourceReferences.Add(new XamlResourceReference
            {
                Key = match.Groups["key"].Value.Trim(),
                IsStatic = true,
                Line = line,
                Column = column + match.Index
            });
        }

        // Find DynamicResource references
        foreach (Match match in DynamicResourceRegex.Matches(value))
        {
            result.ResourceReferences.Add(new XamlResourceReference
            {
                Key = match.Groups["key"].Value.Trim(),
                IsStatic = false,
                Line = line,
                Column = column + match.Index
            });
        }
    }

    private XamlParseResult TryRecoverWithRegex(string content, XamlParseResult result)
    {
        var lines = content.Split('\n');

        // Try to find x:Class
        var classMatch = Regex.Match(content, @"x:Class\s*=\s*[""']([^""']+)[""']");
        if (classMatch.Success)
        {
            var parts = classMatch.Groups[1].Value.Split('.');
            result = result with
            {
                ClassName = parts[^1],
                ClassNamespace = parts.Length > 1 ? string.Join(".", parts[..^1]) : null
            };
        }

        // Find all x:Name declarations
        var nameMatches = Regex.Matches(content, @"x:Name\s*=\s*[""']([^""']+)[""']");
        foreach (Match match in nameMatches)
        {
            var lineNum = GetLineNumber(content, match.Index);
            // Try to find the element type
            var beforeMatch = content[..match.Index];
            var elementMatch = Regex.Match(beforeMatch, @"<(\w+)(?:\s|$)[^>]*$");
            
            result.NamedElements.Add(new XamlNamedElement
            {
                Name = match.Groups[1].Value,
                Type = elementMatch.Success ? elementMatch.Groups[1].Value : "Unknown",
                Line = lineNum,
                Column = match.Index - content.LastIndexOf('\n', match.Index) - 1
            });
        }

        // Find all bindings
        for (int i = 0; i < lines.Length; i++)
        {
            ParseBindingsAndResources(lines[i], i + 1, 1, result);
        }

        return result;
    }

    private int GetLineNumber(string content, int index)
    {
        return content[..index].Count(c => c == '\n') + 1;
    }
}
