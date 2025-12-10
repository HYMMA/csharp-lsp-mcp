using System.Text.Json.Serialization;

namespace CSharpLspMcp.Lsp;

#region Base Protocol Types

public record JsonRpcMessage
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";
}

public record JsonRpcRequest : JsonRpcMessage
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    public object? Params { get; init; }
}

public record JsonRpcResponse : JsonRpcMessage
{
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("result")]
    public object? Result { get; init; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; init; }
}

public record JsonRpcNotification : JsonRpcMessage
{
    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    public object? Params { get; init; }
}

public record JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("data")]
    public object? Data { get; init; }
}

#endregion

#region LSP Core Types

public record Position
{
    [JsonPropertyName("line")]
    public int Line { get; init; }

    [JsonPropertyName("character")]
    public int Character { get; init; }
}

public record Range
{
    [JsonPropertyName("start")]
    public required Position Start { get; init; }

    [JsonPropertyName("end")]
    public required Position End { get; init; }
}

public record Location
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("range")]
    public required Range Range { get; init; }
}

public record TextDocumentIdentifier
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

public record VersionedTextDocumentIdentifier : TextDocumentIdentifier
{
    [JsonPropertyName("version")]
    public int Version { get; init; }
}

public record TextDocumentItem
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("languageId")]
    public required string LanguageId { get; init; }

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public record TextDocumentPositionParams
{
    [JsonPropertyName("textDocument")]
    public required TextDocumentIdentifier TextDocument { get; init; }

    [JsonPropertyName("position")]
    public required Position Position { get; init; }
}

#endregion

#region Initialization

public record InitializeParams
{
    [JsonPropertyName("processId")]
    public int? ProcessId { get; init; }

    [JsonPropertyName("rootUri")]
    public string? RootUri { get; init; }

    [JsonPropertyName("rootPath")]
    public string? RootPath { get; init; }

    [JsonPropertyName("capabilities")]
    public required ClientCapabilities Capabilities { get; init; }

    [JsonPropertyName("workspaceFolders")]
    public WorkspaceFolder[]? WorkspaceFolders { get; init; }
}

public record ClientCapabilities
{
    [JsonPropertyName("textDocument")]
    public TextDocumentClientCapabilities? TextDocument { get; init; }

    [JsonPropertyName("workspace")]
    public WorkspaceClientCapabilities? Workspace { get; init; }
}

public record TextDocumentClientCapabilities
{
    [JsonPropertyName("synchronization")]
    public TextDocumentSyncClientCapabilities? Synchronization { get; init; }

    [JsonPropertyName("completion")]
    public CompletionClientCapabilities? Completion { get; init; }

    [JsonPropertyName("hover")]
    public HoverClientCapabilities? Hover { get; init; }

    [JsonPropertyName("publishDiagnostics")]
    public PublishDiagnosticsClientCapabilities? PublishDiagnostics { get; init; }
}

public record TextDocumentSyncClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    public bool? DynamicRegistration { get; init; }

    [JsonPropertyName("willSave")]
    public bool? WillSave { get; init; }

    [JsonPropertyName("didSave")]
    public bool? DidSave { get; init; }
}

public record CompletionClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    public bool? DynamicRegistration { get; init; }

    [JsonPropertyName("completionItem")]
    public CompletionItemCapabilities? CompletionItem { get; init; }
}

public record CompletionItemCapabilities
{
    [JsonPropertyName("snippetSupport")]
    public bool? SnippetSupport { get; init; }

    [JsonPropertyName("documentationFormat")]
    public string[]? DocumentationFormat { get; init; }
}

public record HoverClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    public bool? DynamicRegistration { get; init; }

    [JsonPropertyName("contentFormat")]
    public string[]? ContentFormat { get; init; }
}

public record PublishDiagnosticsClientCapabilities
{
    [JsonPropertyName("relatedInformation")]
    public bool? RelatedInformation { get; init; }
}

public record WorkspaceClientCapabilities
{
    [JsonPropertyName("workspaceFolders")]
    public bool? WorkspaceFolders { get; init; }
}

public record WorkspaceFolder
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

public record InitializeResult
{
    [JsonPropertyName("capabilities")]
    public required ServerCapabilities Capabilities { get; init; }

    [JsonPropertyName("serverInfo")]
    public ServerInfo? ServerInfo { get; init; }
}

public record ServerCapabilities
{
    [JsonPropertyName("textDocumentSync")]
    public object? TextDocumentSync { get; init; }

    [JsonPropertyName("completionProvider")]
    public CompletionOptions? CompletionProvider { get; init; }

    [JsonPropertyName("hoverProvider")]
    public bool? HoverProvider { get; init; }

    [JsonPropertyName("definitionProvider")]
    public bool? DefinitionProvider { get; init; }

    [JsonPropertyName("referencesProvider")]
    public bool? ReferencesProvider { get; init; }

    [JsonPropertyName("documentSymbolProvider")]
    public bool? DocumentSymbolProvider { get; init; }

    [JsonPropertyName("codeActionProvider")]
    public bool? CodeActionProvider { get; init; }

    [JsonPropertyName("renameProvider")]
    public bool? RenameProvider { get; init; }
}

public record CompletionOptions
{
    [JsonPropertyName("triggerCharacters")]
    public string[]? TriggerCharacters { get; init; }

    [JsonPropertyName("resolveProvider")]
    public bool? ResolveProvider { get; init; }
}

public record ServerInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

#endregion

#region Document Sync

public record DidOpenTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public required TextDocumentItem TextDocument { get; init; }
}

public record DidChangeTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public required VersionedTextDocumentIdentifier TextDocument { get; init; }

    [JsonPropertyName("contentChanges")]
    public required TextDocumentContentChangeEvent[] ContentChanges { get; init; }
}

public record TextDocumentContentChangeEvent
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public record DidCloseTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public required TextDocumentIdentifier TextDocument { get; init; }
}

public record DidSaveTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public required TextDocumentIdentifier TextDocument { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

#endregion

#region Diagnostics

public record PublishDiagnosticsParams
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("diagnostics")]
    public required Diagnostic[] Diagnostics { get; init; }
}

public record Diagnostic
{
    [JsonPropertyName("range")]
    public required Range Range { get; init; }

    [JsonPropertyName("severity")]
    public DiagnosticSeverity? Severity { get; init; }

    [JsonPropertyName("code")]
    public object? Code { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("relatedInformation")]
    public DiagnosticRelatedInformation[]? RelatedInformation { get; init; }
}

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

public record DiagnosticRelatedInformation
{
    [JsonPropertyName("location")]
    public required Location Location { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

#endregion

#region Completion

public record CompletionParams : TextDocumentPositionParams
{
    [JsonPropertyName("context")]
    public CompletionContext? Context { get; init; }
}

public record CompletionContext
{
    [JsonPropertyName("triggerKind")]
    public CompletionTriggerKind TriggerKind { get; init; }

    [JsonPropertyName("triggerCharacter")]
    public string? TriggerCharacter { get; init; }
}

public enum CompletionTriggerKind
{
    Invoked = 1,
    TriggerCharacter = 2,
    TriggerForIncompleteCompletions = 3
}

public record CompletionList
{
    [JsonPropertyName("isIncomplete")]
    public bool IsIncomplete { get; init; }

    [JsonPropertyName("items")]
    public required CompletionItem[] Items { get; init; }
}

public record CompletionItem
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("kind")]
    public CompletionItemKind? Kind { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("documentation")]
    public object? Documentation { get; init; }

    [JsonPropertyName("insertText")]
    public string? InsertText { get; init; }

    [JsonPropertyName("insertTextFormat")]
    public InsertTextFormat? InsertTextFormat { get; init; }

    [JsonPropertyName("filterText")]
    public string? FilterText { get; init; }

    [JsonPropertyName("sortText")]
    public string? SortText { get; init; }
}

public enum CompletionItemKind
{
    Text = 1, Method = 2, Function = 3, Constructor = 4, Field = 5,
    Variable = 6, Class = 7, Interface = 8, Module = 9, Property = 10,
    Unit = 11, Value = 12, Enum = 13, Keyword = 14, Snippet = 15,
    Color = 16, File = 17, Reference = 18, Folder = 19, EnumMember = 20,
    Constant = 21, Struct = 22, Event = 23, Operator = 24, TypeParameter = 25
}

public enum InsertTextFormat
{
    PlainText = 1,
    Snippet = 2
}

#endregion

#region Hover

public record HoverParams : TextDocumentPositionParams { }

public record Hover
{
    [JsonPropertyName("contents")]
    public required object Contents { get; init; }

    [JsonPropertyName("range")]
    public Range? Range { get; init; }
}

public record MarkupContent
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

#endregion

#region Definition & References

public record DefinitionParams : TextDocumentPositionParams { }

public record ReferenceParams : TextDocumentPositionParams
{
    [JsonPropertyName("context")]
    public required ReferenceContext Context { get; init; }
}

public record ReferenceContext
{
    [JsonPropertyName("includeDeclaration")]
    public bool IncludeDeclaration { get; init; }
}

#endregion

#region Document Symbols

public record DocumentSymbolParams
{
    [JsonPropertyName("textDocument")]
    public required TextDocumentIdentifier TextDocument { get; init; }
}

public record DocumentSymbol
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("kind")]
    public SymbolKind Kind { get; init; }

    [JsonPropertyName("range")]
    public required Range Range { get; init; }

    [JsonPropertyName("selectionRange")]
    public required Range SelectionRange { get; init; }

    [JsonPropertyName("children")]
    public DocumentSymbol[]? Children { get; init; }
}

public record SymbolInformation
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("kind")]
    public SymbolKind Kind { get; init; }

    [JsonPropertyName("location")]
    public required Location Location { get; init; }

    [JsonPropertyName("containerName")]
    public string? ContainerName { get; init; }
}

public enum SymbolKind
{
    File = 1, Module = 2, Namespace = 3, Package = 4, Class = 5,
    Method = 6, Property = 7, Field = 8, Constructor = 9, Enum = 10,
    Interface = 11, Function = 12, Variable = 13, Constant = 14, String = 15,
    Number = 16, Boolean = 17, Array = 18, Object = 19, Key = 20,
    Null = 21, EnumMember = 22, Struct = 23, Event = 24, Operator = 25,
    TypeParameter = 26
}

#endregion

#region Code Actions

public record CodeActionParams
{
    [JsonPropertyName("textDocument")]
    public required TextDocumentIdentifier TextDocument { get; init; }

    [JsonPropertyName("range")]
    public required Range Range { get; init; }

    [JsonPropertyName("context")]
    public required CodeActionContext Context { get; init; }
}

public record CodeActionContext
{
    [JsonPropertyName("diagnostics")]
    public required Diagnostic[] Diagnostics { get; init; }

    [JsonPropertyName("only")]
    public string[]? Only { get; init; }
}

public record CodeAction
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("diagnostics")]
    public Diagnostic[]? Diagnostics { get; init; }

    [JsonPropertyName("edit")]
    public WorkspaceEdit? Edit { get; init; }

    [JsonPropertyName("command")]
    public Command? Command { get; init; }
}

public record Command
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("command")]
    public required string CommandIdentifier { get; init; }

    [JsonPropertyName("arguments")]
    public object[]? Arguments { get; init; }
}

public record WorkspaceEdit
{
    [JsonPropertyName("changes")]
    public Dictionary<string, TextEdit[]>? Changes { get; init; }
}

public record TextEdit
{
    [JsonPropertyName("range")]
    public required Range Range { get; init; }

    [JsonPropertyName("newText")]
    public required string NewText { get; init; }
}

#endregion

#region Rename

public record RenameParams : TextDocumentPositionParams
{
    [JsonPropertyName("newName")]
    public required string NewName { get; init; }
}

#endregion
