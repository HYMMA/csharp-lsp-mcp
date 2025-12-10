# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2025-12-11

### Added
- Initial release of csharp-lsp-mcp
- C# language intelligence via csharp-ls integration
  - `csharp_set_workspace` - Set the workspace/solution directory
  - `csharp_diagnostics` - Get compiler errors and warnings
  - `csharp_hover` - Get type information at a position
  - `csharp_completions` - Get IntelliSense completions
  - `csharp_definition` - Go to definition
  - `csharp_references` - Find all references
  - `csharp_symbols` - Get document symbols
  - `csharp_code_actions` - Get available code actions
  - `csharp_rename` - Preview symbol rename
- Built-in XAML analysis tools
  - `xaml_validate` - Validate XAML for errors and issues
  - `xaml_bindings` - Extract and analyze data bindings
  - `xaml_resources` - List and check resource references
  - `xaml_names` - List x:Name declarations
  - `xaml_structure` - Show element tree structure
  - `xaml_find_binding_errors` - Find binding errors
  - `xaml_extract_viewmodel` - Generate ViewModel from bindings
- Support for MCP protocol version 2024-11-05
- Compatible with Claude Code and Claude Desktop
- Verbose logging support via `--verbose` flag or `MCP_DEBUG=1` environment variable

### Dependencies
- ModelContextProtocol 0.5.0-preview.1
- .NET 8.0
- csharp-ls (external dependency)

[Unreleased]: https://github.com/HYMMA/csharp-lsp-mcp/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/HYMMA/csharp-lsp-mcp/releases/tag/v1.0.0
