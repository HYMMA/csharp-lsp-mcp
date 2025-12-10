# Contributing to csharp-lsp-mcp

Thank you for your interest in contributing to csharp-lsp-mcp! This document provides guidelines and instructions for contributing.

## Code of Conduct

By participating in this project, you agree to maintain a respectful and inclusive environment for everyone.

## How to Contribute

### Reporting Bugs

Before submitting a bug report:
1. Check the [existing issues](https://github.com/HYMMA/csharp-lsp-mcp/issues) to avoid duplicates
2. Ensure you're using the latest version
3. Verify that csharp-ls is properly installed (`dotnet tool list -g`)

When submitting a bug report, include:
- Your operating system and version
- .NET SDK version (`dotnet --version`)
- csharp-ls version (`csharp-ls --version`)
- Steps to reproduce the issue
- Expected vs actual behavior
- Relevant log output (run with `--verbose` or `MCP_DEBUG=1`)

### Suggesting Features

Feature requests are welcome! Please:
1. Check existing issues for similar suggestions
2. Clearly describe the use case
3. Explain how it would benefit users

### Pull Requests

1. **Fork the repository** and create your branch from `main`
2. **Follow the coding style** used in the project
3. **Add tests** if applicable
4. **Update documentation** for any new features
5. **Update CHANGELOG.md** with your changes
6. **Ensure the build passes** before submitting

#### Development Setup

```bash
# Clone your fork
git clone https://github.com/YOUR_USERNAME/csharp-lsp-mcp.git
cd csharp-lsp-mcp

# Install dependencies
dotnet restore

# Build
cd csharp-lsp-mcp/src/CSharpLspMcp
dotnet build

# Run tests
dotnet test
```

#### Commit Messages

Follow conventional commit format:
- `feat:` New feature
- `fix:` Bug fix
- `docs:` Documentation changes
- `refactor:` Code refactoring
- `test:` Adding or updating tests
- `chore:` Maintenance tasks

Examples:
```
feat: add support for workspace/symbol requests
fix: handle null reference in diagnostic handler
docs: update README with new configuration options
```

### Coding Guidelines

#### C# Style
- Use C# 12 features where appropriate
- Follow Microsoft's [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful variable and method names
- Add XML documentation for public APIs

#### Project Structure
```
csharp-lsp-mcp/
├── src/
│   └── CSharpLspMcp/
│       ├── Lsp/           # LSP client implementation
│       ├── Tools/         # MCP tool implementations
│       └── Program.cs     # Entry point
├── tests/                  # Unit and integration tests
└── docs/                   # Additional documentation
```

#### Adding New Tools

1. Create a new method in the appropriate tool class (`CSharpTools.cs` or `XamlTools.cs`)
2. Add the `[McpServerTool]` attribute with a descriptive name
3. Add the `[Description]` attribute explaining what the tool does
4. Document parameters with `[Description]` attributes
5. Update README.md with the new tool
6. Add tests for the new functionality

Example:
```csharp
[McpServerTool(Name = "csharp_my_new_tool")]
[Description("Description of what this tool does")]
public async Task<string> MyNewToolAsync(
    [Description("Parameter description")] string param1,
    CancellationToken cancellationToken)
{
    // Implementation
}
```

## Testing

### Running Tests
```bash
dotnet test
```

### Manual Testing

1. Build the project in Debug mode
2. Configure Claude Code to use your local build
3. Test the tools interactively

## Release Process

Releases are managed by maintainers. The process:
1. Update version in `.csproj`
2. Update CHANGELOG.md
3. Create a git tag
4. Push to trigger CI/CD

## Getting Help

- Open an [issue](https://github.com/HYMMA/csharp-lsp-mcp/issues) for questions
- Check existing documentation and issues first

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
