using System.Text.Json;
using CSharpLspMcp.Lsp;
using Xunit;

namespace CSharpLspMcp.Tests;

public class LspTypesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Position_SerializesCorrectly()
    {
        // Arrange
        var position = new Position { Line = 10, Character = 5 };

        // Act
        var json = JsonSerializer.Serialize(position, JsonOptions);

        // Assert
        Assert.Contains("\"line\":10", json);
        Assert.Contains("\"character\":5", json);
    }

    [Fact]
    public void Position_DeserializesCorrectly()
    {
        // Arrange
        var json = """{"line":42,"character":13}""";

        // Act
        var position = JsonSerializer.Deserialize<Position>(json, JsonOptions);

        // Assert
        Assert.NotNull(position);
        Assert.Equal(42, position.Line);
        Assert.Equal(13, position.Character);
    }

    [Fact]
    public void Diagnostic_SerializesWithAllFields()
    {
        // Arrange
        var diagnostic = new Diagnostic
        {
            Range = new Lsp.Range
            {
                Start = new Position { Line = 1, Character = 0 },
                End = new Position { Line = 1, Character = 10 }
            },
            Severity = DiagnosticSeverity.Error,
            Code = "CS1002",
            Source = "csharp",
            Message = "Expected ';'"
        };

        // Act
        var json = JsonSerializer.Serialize(diagnostic, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<Diagnostic>(json, JsonOptions);

        // Assert
        Assert.NotNull(roundTrip);
        Assert.Equal(DiagnosticSeverity.Error, roundTrip.Severity);
        Assert.Equal("CS1002", roundTrip.Code?.ToString());
        Assert.Equal("Expected ';'", roundTrip.Message);
    }

    [Fact]
    public void CompletionItem_HandlesOptionalFields()
    {
        // Arrange
        var item = new CompletionItem
        {
            Label = "ToString",
            Kind = CompletionItemKind.Method,
            Detail = "string Object.ToString()"
        };

        // Act
        var json = JsonSerializer.Serialize(item, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<CompletionItem>(json, JsonOptions);

        // Assert
        Assert.NotNull(roundTrip);
        Assert.Equal("ToString", roundTrip.Label);
        Assert.Equal(CompletionItemKind.Method, roundTrip.Kind);
        Assert.Equal("string Object.ToString()", roundTrip.Detail);
        Assert.Null(roundTrip.Documentation);
        Assert.Null(roundTrip.InsertText);
    }

    [Fact]
    public void TextDocumentItem_CreatesValidDocument()
    {
        // Arrange & Act
        var doc = new TextDocumentItem
        {
            Uri = "file:///c:/test/Program.cs",
            LanguageId = "csharp",
            Version = 1,
            Text = "using System;"
        };

        var json = JsonSerializer.Serialize(doc, JsonOptions);

        // Assert
        Assert.Contains("\"uri\":\"file:///c:/test/Program.cs\"", json);
        Assert.Contains("\"languageId\":\"csharp\"", json);
        Assert.Contains("\"version\":1", json);
    }

    [Fact]
    public void InitializeParams_SerializesCapabilities()
    {
        // Arrange
        var @params = new InitializeParams
        {
            ProcessId = 12345,
            RootUri = "file:///c:/test",
            Capabilities = new ClientCapabilities
            {
                TextDocument = new TextDocumentClientCapabilities
                {
                    Completion = new CompletionClientCapabilities
                    {
                        CompletionItem = new CompletionItemCapabilities
                        {
                            SnippetSupport = true,
                            DocumentationFormat = new[] { "markdown" }
                        }
                    }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(@params, JsonOptions);

        // Assert
        Assert.Contains("\"processId\":12345", json);
        Assert.Contains("\"snippetSupport\":true", json);
        Assert.Contains("\"markdown\"", json);
    }

    [Fact]
    public void Location_ParsesUri()
    {
        // Arrange
        var json = """
        {
            "uri": "file:///c:/src/Program.cs",
            "range": {
                "start": {"line": 5, "character": 10},
                "end": {"line": 5, "character": 20}
            }
        }
        """;

        // Act
        var location = JsonSerializer.Deserialize<Location>(json, JsonOptions);

        // Assert
        Assert.NotNull(location);
        Assert.Equal("file:///c:/src/Program.cs", location.Uri);
        Assert.Equal(5, location.Range.Start.Line);
        Assert.Equal(10, location.Range.Start.Character);
    }

    [Fact]
    public void DocumentSymbol_HandlesNesting()
    {
        // Arrange
        var symbol = new DocumentSymbol
        {
            Name = "MyClass",
            Kind = SymbolKind.Class,
            Range = new Lsp.Range
            {
                Start = new Position { Line = 0, Character = 0 },
                End = new Position { Line = 100, Character = 0 }
            },
            SelectionRange = new Lsp.Range
            {
                Start = new Position { Line = 0, Character = 13 },
                End = new Position { Line = 0, Character = 20 }
            },
            Children = new[]
            {
                new DocumentSymbol
                {
                    Name = "MyMethod",
                    Kind = SymbolKind.Method,
                    Range = new Lsp.Range
                    {
                        Start = new Position { Line = 10, Character = 0 },
                        End = new Position { Line = 20, Character = 0 }
                    },
                    SelectionRange = new Lsp.Range
                    {
                        Start = new Position { Line = 10, Character = 12 },
                        End = new Position { Line = 10, Character = 20 }
                    }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(symbol, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<DocumentSymbol>(json, JsonOptions);

        // Assert
        Assert.NotNull(roundTrip);
        Assert.Equal("MyClass", roundTrip.Name);
        Assert.NotNull(roundTrip.Children);
        Assert.Single(roundTrip.Children);
        Assert.Equal("MyMethod", roundTrip.Children[0].Name);
    }
}
