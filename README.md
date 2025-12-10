# csharp-lsp-mcp

[![Build](https://github.com/HYMMA/csharp-lsp-mcp/actions/workflows/build.yml/badge.svg)](https://github.com/HYMMA/csharp-lsp-mcp/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/CSharpLspMcp.svg)](https://www.nuget.org/packages/CSharpLspMcp)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CSharpLspMcp.svg)](https://www.nuget.org/packages/CSharpLspMcp)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-0.5.0-blue)](https://modelcontextprotocol.io/)
[![GitHub release](https://img.shields.io/github/v/release/HYMMA/csharp-lsp-mcp)](https://github.com/HYMMA/csharp-lsp-mcp/releases)

An MCP (Model Context Protocol) server that provides C# and XAML language intelligence for AI assistants like Claude. It bridges the gap between LLMs and .NET development by exposing IntelliSense, diagnostics, and code analysis through the standardized MCP protocol.

## Features

### C# Language Intelligence (via csharp-ls)
- **Diagnostics** - Get compiler errors and warnings in real-time
- **Hover Information** - View type information and documentation
- **IntelliSense Completions** - Get context-aware code suggestions
- **Go to Definition** - Navigate to symbol definitions
- **Find References** - Locate all usages of a symbol
- **Document Symbols** - List all symbols in a file
- **Code Actions** - Access quick fixes and refactorings
- **Rename Preview** - Preview symbol renames across the workspace

### XAML Analysis (built-in)
- **Validation** - Check XAML for errors and issues
- **Binding Analysis** - Extract and analyze data bindings
- **Resource Inspection** - List and verify resource references
- **Name Discovery** - Find all x:Name declarations
- **Structure Visualization** - View element tree hierarchy
- **Binding Error Detection** - Identify binding problems
- **ViewModel Generation** - Generate ViewModels from bindings

## Prerequisites

1. **.NET 8.0 SDK** or later
2. **csharp-ls** - The C# Language Server

Install csharp-ls globally:
```bash
dotnet tool install --global csharp-ls
```

## Installation

### From Source

```bash
git clone https://github.com/HYMMA/csharp-lsp-mcp.git
cd csharp-lsp-mcp/csharp-lsp-mcp/src/CSharpLspMcp
dotnet build -c Release
```

### From NuGet (coming soon)

```bash
dotnet tool install --global CSharpLspMcp
```

## Configuration

### Claude Code

Add to your `~/.claude.json`:

```json
{
  "mcpServers": {
    "csharp": {
      "command": "path/to/csharp-lsp-mcp.exe"
    }
  }
}
```

Or if installed as a global tool:

```json
{
  "mcpServers": {
    "csharp": {
      "command": "csharp-lsp-mcp"
    }
  }
}
```

### Claude Desktop

Add to your Claude Desktop configuration:

```json
{
  "mcpServers": {
    "csharp": {
      "command": "csharp-lsp-mcp",
      "args": []
    }
  }
}
```

## Usage

Once configured, the MCP server provides the following tools to your AI assistant:

### Setting Up the Workspace

Before using C# tools, set the workspace directory:

```
Use csharp_set_workspace with path: "C:/path/to/your/solution"
```

### Example Interactions

**Get diagnostics for a file:**
```
Check for errors in Program.cs
```

**Get type information:**
```
What type is the variable at line 15, column 10 in MyClass.cs?
```

**Find all references:**
```
Find all usages of the GetCustomer method
```

**Analyze XAML bindings:**
```
Show me all data bindings in MainWindow.xaml
```

## Available Tools

| Tool | Description |
|------|-------------|
| `csharp_set_workspace` | Set the solution/project directory |
| `csharp_diagnostics` | Get compiler errors and warnings |
| `csharp_hover` | Get type info at a position |
| `csharp_completions` | Get IntelliSense completions |
| `csharp_definition` | Go to definition |
| `csharp_references` | Find all references |
| `csharp_symbols` | Get document symbols |
| `csharp_code_actions` | Get available code actions |
| `csharp_rename` | Preview symbol rename |
| `xaml_validate` | Validate XAML for errors |
| `xaml_bindings` | Extract data bindings |
| `xaml_resources` | List resource references |
| `xaml_names` | List x:Name declarations |
| `xaml_structure` | Show element tree |
| `xaml_find_binding_errors` | Find binding errors |
| `xaml_extract_viewmodel` | Generate ViewModel from bindings |

## Command Line Options

```
csharp-lsp-mcp [OPTIONS]

OPTIONS:
    -h, --help      Show help message
    -v, --version   Show version information
    -V, --verbose   Enable verbose logging (to stderr)

ENVIRONMENT VARIABLES:
    MCP_DEBUG=1     Enable trace-level logging
```

## Architecture

```
┌─────────────────┐     MCP Protocol      ┌──────────────────┐
│  Claude / LLM   │◄────────────────────►│  csharp-lsp-mcp  │
└─────────────────┘                       └────────┬─────────┘
                                                   │
                                    ┌──────────────┴──────────────┐
                                    │                             │
                              ┌─────▼─────┐               ┌───────▼───────┐
                              │ csharp-ls │               │  XAML Parser  │
                              │   (LSP)   │               │   (built-in)  │
                              └─────┬─────┘               └───────────────┘
                                    │
                              ┌─────▼─────┐
                              │  Roslyn   │
                              │ Compiler  │
                              └───────────┘
```

## Building from Source

```bash
# Clone the repository
git clone https://github.com/HYMMA/csharp-lsp-mcp.git
cd csharp-lsp-mcp

# Build
cd csharp-lsp-mcp/src/CSharpLspMcp
dotnet build -c Release

# Run tests (if available)
dotnet test

# Create a release build
dotnet publish -c Release -o ./publish
```

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for a history of changes.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [Model Context Protocol](https://modelcontextprotocol.io/) - The protocol specification
- [csharp-ls](https://github.com/razzmatazz/csharp-language-server) - The C# Language Server
- [Microsoft MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) - The official C# SDK for MCP

## Related Projects

- [Claude Code](https://claude.ai/code) - Anthropic's CLI for Claude
- [MCP Servers](https://github.com/modelcontextprotocol/servers) - Official MCP server implementations
