# csharp-lsp-mcp

An MCP (Model Context Protocol) server that provides C# and XAML language intelligence by integrating with `csharp-ls` (the lightweight C# Language Server) and built-in XAML analysis.

This enables Claude and other MCP-compatible AI assistants to get real-time compiler diagnostics, IntelliSense completions, type information, and more when working with C# code, plus comprehensive XAML validation and analysis for WPF/WinUI projects.

## Features

### C# Features (via csharp-ls)
- **Diagnostics** - Get compiler errors and warnings in real-time
- **Hover** - Type information and documentation at any position
- **Completions** - IntelliSense suggestions
- **Go to Definition** - Find where symbols are defined
- **Find References** - Find all usages of a symbol
- **Document Symbols** - List all classes, methods, properties in a file
- **Code Actions** - Quick fixes and refactoring suggestions
- **Rename** - Preview symbol renames across the workspace

### XAML Features (built-in)
- **Validation** - Check type references, property names, resource keys
- **Binding Analysis** - Extract and validate data bindings
- **Resource Tracking** - Find unused and missing resource references  
- **Name Checking** - Detect duplicate x:Name declarations
- **Structure View** - Visualize element hierarchy
- **ViewModel Extraction** - Generate interfaces from XAML bindings

## Prerequisites

### 1. Install .NET 8 SDK

Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0)

### 2. Install csharp-ls

```bash
dotnet tool install --global csharp-ls
```

Verify installation:
```bash
csharp-ls --version
```

## Installation

### Option 1: Build from source

```bash
git clone <repo-url>
cd csharp-lsp-mcp
dotnet build -c Release
```

The executable will be at:
- Windows: `src/CSharpLspMcp/bin/Release/net8.0/csharp-lsp-mcp.exe`
- Linux/macOS: `src/CSharpLspMcp/bin/Release/net8.0/csharp-lsp-mcp`

### Option 2: Install as .NET tool (after publishing to NuGet)

```bash
dotnet tool install --global csharp-lsp-mcp
```

## Configuration with Claude

Add to your Claude Desktop configuration (`claude_desktop_config.json`):

### Windows

```json
{
  "mcpServers": {
    "csharp": {
      "command": "C:\\path\\to\\csharp-lsp-mcp.exe"
    }
  }
}
```

### macOS/Linux

```json
{
  "mcpServers": {
    "csharp": {
      "command": "/path/to/csharp-lsp-mcp"
    }
  }
}
```

### Using dotnet run

```json
{
  "mcpServers": {
    "csharp": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/csharp-lsp-mcp/src/CSharpLspMcp"]
    }
  }
}
```

## Available Tools

### C# Tools (via csharp-ls)

### `csharp_set_workspace`

Set the workspace directory (solution or project folder). Call this first!

```json
{
  "path": "/path/to/your/solution"
}
```

### `csharp_diagnostics`

Get compiler errors and warnings for a C# file.

```json
{
  "filePath": "/path/to/File.cs",
  "content": "optional - file content if not on disk"
}
```

### `csharp_hover`

Get type information at a specific position.

```json
{
  "filePath": "/path/to/File.cs",
  "line": 10,
  "character": 15
}
```

### `csharp_completions`

Get IntelliSense completions.

```json
{
  "filePath": "/path/to/File.cs",
  "line": 10,
  "character": 15,
  "maxResults": 20
}
```

### `csharp_definition`

Find where a symbol is defined.

```json
{
  "filePath": "/path/to/File.cs",
  "line": 10,
  "character": 15
}
```

### `csharp_references`

Find all references to a symbol.

```json
{
  "filePath": "/path/to/File.cs",
  "line": 10,
  "character": 15,
  "includeDeclaration": true
}
```

### `csharp_symbols`

Get all symbols in a document.

```json
{
  "filePath": "/path/to/File.cs"
}
```

### `csharp_code_actions`

Get available quick fixes and refactorings.

```json
{
  "filePath": "/path/to/File.cs",
  "startLine": 10,
  "startCharacter": 0,
  "endLine": 10,
  "endCharacter": 20
}
```

### `csharp_rename`

Preview a symbol rename across the workspace.

```json
{
  "filePath": "/path/to/File.cs",
  "line": 10,
  "character": 15,
  "newName": "NewSymbolName"
}
```

---

## XAML Tools

### `xaml_validate`

Validate a XAML file for errors, warnings, and common issues.

```json
{
  "filePath": "/path/to/MainWindow.xaml",
  "projectPath": "/path/to/project"
}
```

### `xaml_bindings`

Extract and analyze all data bindings in a XAML file.

```json
{
  "filePath": "/path/to/MainWindow.xaml"
}
```

### `xaml_resources`

List all resources and check for unused or missing references.

```json
{
  "filePath": "/path/to/MainWindow.xaml"
}
```

### `xaml_names`

List all x:Name declarations and check for duplicates.

```json
{
  "filePath": "/path/to/MainWindow.xaml"
}
```

### `xaml_structure`

Show the element tree structure of a XAML file.

```json
{
  "filePath": "/path/to/MainWindow.xaml",
  "maxDepth": 5
}
```

### `xaml_find_binding_errors`

Find potential binding errors like invalid ElementName references.

```json
{
  "filePath": "/path/to/MainWindow.xaml"
}
```

### `xaml_extract_viewmodel`

Generate a C# interface based on the bindings in XAML.

```json
{
  "filePath": "/path/to/MainWindow.xaml",
  "interfaceName": "IMainViewModel"
}
```

## Usage Examples

### Example conversation with Claude:

**You:** I'm working on a C# project at `/home/user/MyProject`. Can you check my Program.cs for errors?

**Claude:** *Uses `csharp_set_workspace` and `csharp_diagnostics`*

Let me analyze your code... I found 2 issues:

1. **ERROR** Line 15, Col 10: 'MyClass' does not contain a definition for 'Foo'
2. **WARNING** Line 22, Col 5: Variable 'x' is declared but never used

---

**You:** What's the type of the variable on line 15?

**Claude:** *Uses `csharp_hover`*

The variable `result` is of type `Task<IEnumerable<Customer>>` - it's an async task that returns a collection of Customer objects.

## Running Tests

```bash
cd csharp-lsp-mcp
dotnet test
```

## Debugging

Enable verbose logging:

```bash
csharp-lsp-mcp --verbose
```

Or set environment variable:
```bash
export MCP_DEBUG=1
csharp-lsp-mcp
```

Logs are written to stderr to avoid interfering with the MCP protocol on stdout.

## Architecture

```
┌─────────────┐     MCP Protocol      ┌──────────────┐     LSP Protocol     ┌────────────┐
│   Claude    │◄──────────────────────►│  csharp-lsp  │◄────────────────────►│  csharp-ls │
│             │        (stdio)         │     -mcp     │       (stdio)        │            │
└─────────────┘                        └──────────────┘                      └────────────┘
                                              │
                                              ▼
                                       ┌──────────────┐
                                       │  Your C#     │
                                       │  Project     │
                                       └──────────────┘
```

## Project Structure

```
csharp-lsp-mcp/
├── CSharpLspMcp.sln
├── README.md
└── src/
    ├── CSharpLspMcp/
    │   ├── CSharpLspMcp.csproj
    │   ├── Program.cs
    │   ├── Lsp/
    │   │   ├── LspClient.cs        # LSP client implementation
    │   │   └── LspTypes.cs         # LSP protocol types
    │   ├── Mcp/
    │   │   ├── McpServer.cs        # MCP server implementation
    │   │   └── McpTypes.cs         # MCP protocol types
    │   └── Tools/
    │       └── CSharpLspToolHandler.cs  # Tool implementations
    └── CSharpLspMcp.Tests/
        ├── CSharpLspMcp.Tests.csproj
        ├── McpServerTests.cs
        ├── LspTypesTests.cs
        └── ToolHandlerTests.cs
```

## Troubleshooting

### "Could not find csharp-ls"

Make sure `csharp-ls` is installed and in your PATH:
```bash
dotnet tool install --global csharp-ls
```

On Windows, you may need to restart your terminal or add `%USERPROFILE%\.dotnet\tools` to your PATH.

### "LSP initialization failed"

- Ensure your workspace contains a `.sln` or `.csproj` file
- Try running `dotnet restore` in your project directory
- Check that your project targets a supported .NET version

### No diagnostics appearing

- The LSP server needs time to analyze your project
- Large solutions may take longer to initialize
- Try calling `csharp_set_workspace` first, then wait a moment before requesting diagnostics

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Add tests for new functionality
4. Submit a pull request

## License

MIT License - see LICENSE file for details.

## Acknowledgments

- [csharp-ls](https://github.com/razzmatazz/csharp-language-server) - The C# language server this project uses
- [Model Context Protocol](https://modelcontextprotocol.io/) - The protocol specification
- [Anthropic](https://www.anthropic.com/) - Creators of Claude and MCP
