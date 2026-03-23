using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace CSharpLspMcp.Lsp;

public class LspClient : IAsyncDisposable
{
    private readonly ILogger<LspClient> _logger;
    private readonly SolutionFilter _solutionFilter;
    private Process? _lspProcess;
    private StreamReader? _outputReader;
    private int _requestId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, PublishDiagnosticsParams> _diagnosticsCache = new();
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;
    private bool _isInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string? _filteredWorkspacePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public event Action<PublishDiagnosticsParams>? DiagnosticsReceived;

    public LspClient(ILogger<LspClient> logger, SolutionFilter solutionFilter)
    {
        _logger = logger;
        _solutionFilter = solutionFilter;
    }

    public bool IsRunning => _isInitialized && _lspProcess != null && !_lspProcess.HasExited;

    /// <summary>
    /// Stops the LSP server and resets state so it can be restarted with StartAsync.
    /// </summary>
    public async Task StopAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (!_isInitialized && _lspProcess == null)
                return;

            _logger.LogInformation("Stopping LSP server...");

            _readLoopCts?.Cancel();

            if (_readLoopTask != null)
            {
                try
                {
                    await _readLoopTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch { }
            }

            if (_lspProcess != null && !_lspProcess.HasExited)
            {
                try
                {
                    await SendRequestAsync<object>("shutdown", null, CancellationToken.None);
                    await SendNotificationAsync("exit", null, CancellationToken.None);

                    if (!_lspProcess.WaitForExit(3000))
                        _lspProcess.Kill();
                }
                catch
                {
                    try { _lspProcess.Kill(); } catch { }
                }
            }

            _lspProcess?.Dispose();
            _readLoopCts?.Dispose();

            _lspProcess = null;
            _outputReader = null;
            _readLoopCts = null;
            _readLoopTask = null;
            _isInitialized = false;
            _filteredWorkspacePath = null;
            _requestId = 0;
            _pendingRequests.Clear();
            _diagnosticsCache.Clear();

            _solutionFilter.Cleanup();

            _logger.LogInformation("LSP server stopped.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<bool> StartAsync(string? workspacePath = null, CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
                return true;

            // Try to find csharp-ls
            var lspPath = await FindLspServerAsync(cancellationToken);
            if (lspPath == null)
            {
                _logger.LogError("Could not find csharp-ls. Install it with: dotnet tool install --global csharp-ls");
                return false;
            }

            // Filter the solution to exclude unsupported project types
            var effectiveWorkspacePath = workspacePath;
            if (workspacePath != null)
            {
                _filteredWorkspacePath = _solutionFilter.GetFilteredWorkspacePath(workspacePath);
                if (_filteredWorkspacePath != workspacePath)
                {
                    _logger.LogInformation("Using filtered workspace: {Path}", _filteredWorkspacePath);
                    effectiveWorkspacePath = _filteredWorkspacePath;
                }
            }

            _logger.LogInformation("Starting LSP server: {Path}", lspPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = lspPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };

            _lspProcess = new Process { StartInfo = startInfo };
            _lspProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogWarning("LSP stderr: {Message}", e.Data);
            };

            if (!_lspProcess.Start())
            {
                _logger.LogError("Failed to start LSP process");
                return false;
            }

            _logger.LogDebug("LSP process started, PID: {Pid}", _lspProcess.Id);

            // Get the stdout reader BEFORE starting async stderr reads
            _outputReader = _lspProcess.StandardOutput;
            _lspProcess.BeginErrorReadLine();

            // Small delay to let the process start
            await Task.Delay(100, cancellationToken);

            if (_lspProcess.HasExited)
            {
                _logger.LogError("LSP process exited immediately with code: {ExitCode}", _lspProcess.ExitCode);
                return false;
            }

            _readLoopCts = new CancellationTokenSource();
            _readLoopTask = Task.Run(() => ReadLoopAsync(_readLoopCts.Token), _readLoopCts.Token);

            // Initialize the LSP server with the (potentially filtered) workspace
            var initResult = await InitializeAsync(effectiveWorkspacePath, cancellationToken);
            if (initResult == null)
            {
                _logger.LogError("LSP initialization failed");
                await CleanupFailedStartAsync();
                return false;
            }

            _logger.LogInformation("LSP server initialized: {ServerName} {Version}",
                initResult.ServerInfo?.Name ?? "Unknown",
                initResult.ServerInfo?.Version ?? "Unknown");

            // Send initialized notification
            await SendNotificationAsync("initialized", new { }, cancellationToken);

            _isInitialized = true;
            return true;
        }
        catch
        {
            await CleanupFailedStartAsync();
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<string?> FindLspServerAsync(CancellationToken cancellationToken)
    {
        // Check common locations for csharp-ls
        var isWindows = OperatingSystem.IsWindows();
        var exeExtension = isWindows ? ".exe" : "";

        var possiblePaths = new[]
        {
            "csharp-ls", // In PATH (Windows handles .exe automatically)
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", $"csharp-ls{exeExtension}"),
            "/usr/local/bin/csharp-ls",
            "/usr/bin/csharp-ls"
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync(cancellationToken);
                    if (process.ExitCode == 0)
                        return path;
                }
            }
            catch
            {
                // Try next path
            }
        }

        return null;
    }

    private async Task<InitializeResult?> InitializeAsync(string? workspacePath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("InitializeAsync: Sending initialize request to LSP server...");
        var rootUri = workspacePath != null ? new Uri(workspacePath).ToString() : null;

        var initParams = new InitializeParams
        {
            ProcessId = Environment.ProcessId,
            RootUri = rootUri,
            RootPath = workspacePath,
            Capabilities = new ClientCapabilities
            {
                TextDocument = new TextDocumentClientCapabilities
                {
                    Synchronization = new TextDocumentSyncClientCapabilities
                    {
                        DynamicRegistration = false,
                        WillSave = false,
                        DidSave = true
                    },
                    Completion = new CompletionClientCapabilities
                    {
                        DynamicRegistration = false,
                        CompletionItem = new CompletionItemCapabilities
                        {
                            SnippetSupport = true,
                            DocumentationFormat = new[] { "markdown", "plaintext" }
                        }
                    },
                    Hover = new HoverClientCapabilities
                    {
                        DynamicRegistration = false,
                        ContentFormat = new[] { "markdown", "plaintext" }
                    },
                    PublishDiagnostics = new PublishDiagnosticsClientCapabilities
                    {
                        RelatedInformation = true
                    }
                },
                Workspace = new WorkspaceClientCapabilities
                {
                    WorkspaceFolders = true
                }
            },
            WorkspaceFolders = workspacePath != null
                ? new[] { new WorkspaceFolder { Uri = rootUri!, Name = Path.GetFileName(workspacePath) } }
                : null
        };

        _logger.LogDebug("InitializeAsync: Workspace folders: {Folders}", workspacePath);
        var response = await SendRequestAsync<InitializeResult>("initialize", initParams, cancellationToken);
        _logger.LogInformation("InitializeAsync: Received response from LSP server");
        return response;
    }

    public async Task OpenDocumentAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(filePath).ToString();
        var param = new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "csharp",
                Version = 1,
                Text = content
            }
        };

        await SendNotificationAsync("textDocument/didOpen", param, cancellationToken);
        _logger.LogDebug("Opened document: {Uri}", uri);
    }

    public async Task UpdateDocumentAsync(string filePath, string content, int version, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(filePath).ToString();
        var param = new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier
            {
                Uri = uri,
                Version = version
            },
            ContentChanges = new[] { new TextDocumentContentChangeEvent { Text = content } }
        };

        await SendNotificationAsync("textDocument/didChange", param, cancellationToken);
        _logger.LogDebug("Updated document: {Uri} (version {Version})", uri, version);
    }

    public async Task CloseDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(filePath).ToString();
        var param = new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri }
        };

        await SendNotificationAsync("textDocument/didClose", param, cancellationToken);
        _diagnosticsCache.TryRemove(uri, out _);
        _logger.LogDebug("Closed document: {Uri}", uri);
    }

    public PublishDiagnosticsParams? GetCachedDiagnostics(string filePath)
    {
        var uri = new Uri(filePath).ToString();
        _diagnosticsCache.TryGetValue(uri, out var diagnostics);
        return diagnostics;
    }

    public async Task<PublishDiagnosticsParams?> WaitForDiagnosticsAsync(string filePath, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(filePath).ToString();
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_diagnosticsCache.TryGetValue(uri, out var diagnostics))
                return diagnostics;

            await Task.Delay(100, cancellationToken);
        }

        return GetCachedDiagnostics(filePath);
    }

    public async Task<Hover?> GetHoverAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        var param = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = new Uri(filePath).ToString() },
            Position = new Position { Line = line, Character = character }
        };

        return await SendRequestAsync<Hover>("textDocument/hover", param, cancellationToken);
    }

    public async Task<CompletionItem[]?> GetCompletionsAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        var param = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = new Uri(filePath).ToString() },
            Position = new Position { Line = line, Character = character },
            Context = new CompletionContext { TriggerKind = CompletionTriggerKind.Invoked }
        };

        var result = await SendRequestAsync<JsonElement>("textDocument/completion", param, cancellationToken);
        if (result.ValueKind == JsonValueKind.Undefined || result.ValueKind == JsonValueKind.Null)
            return null;

        // Response can be CompletionItem[] or CompletionList
        if (result.ValueKind == JsonValueKind.Array)
            return result.Deserialize<CompletionItem[]>(JsonOptions);

        var list = result.Deserialize<CompletionList>(JsonOptions);
        return list?.Items;
    }

    public async Task<Location[]?> GetDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        var param = new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = new Uri(filePath).ToString() },
            Position = new Position { Line = line, Character = character }
        };

        var result = await SendRequestAsync<JsonElement>("textDocument/definition", param, cancellationToken);
        if (result.ValueKind == JsonValueKind.Undefined || result.ValueKind == JsonValueKind.Null)
            return null;

        // Response can be Location, Location[], or null
        if (result.ValueKind == JsonValueKind.Array)
            return result.Deserialize<Location[]>(JsonOptions);

        var single = result.Deserialize<Location>(JsonOptions);
        return single != null ? new[] { single } : null;
    }

    public async Task<Location[]?> GetReferencesAsync(string filePath, int line, int character, bool includeDeclaration = true, CancellationToken cancellationToken = default)
    {
        var param = new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = new Uri(filePath).ToString() },
            Position = new Position { Line = line, Character = character },
            Context = new ReferenceContext { IncludeDeclaration = includeDeclaration }
        };

        return await SendRequestAsync<Location[]>("textDocument/references", param, cancellationToken);
    }

    public async Task<object?> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var param = new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = new Uri(filePath).ToString() }
        };

        var result = await SendRequestAsync<JsonElement>("textDocument/documentSymbol", param, cancellationToken);
        if (result.ValueKind == JsonValueKind.Undefined || result.ValueKind == JsonValueKind.Null)
            return null;

        // Try to determine if it's DocumentSymbol[] or SymbolInformation[]
        if (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0)
        {
            var first = result[0];
            if (first.TryGetProperty("selectionRange", out _))
                return result.Deserialize<DocumentSymbol[]>(JsonOptions);
            else
                return result.Deserialize<SymbolInformation[]>(JsonOptions);
        }

        return Array.Empty<DocumentSymbol>();
    }

    public async Task<CodeAction[]?> GetCodeActionsAsync(string filePath, Range range, Diagnostic[] diagnostics, CancellationToken cancellationToken = default)
    {
        var param = new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = new Uri(filePath).ToString() },
            Range = range,
            Context = new CodeActionContext { Diagnostics = diagnostics }
        };

        return await SendRequestAsync<CodeAction[]>("textDocument/codeAction", param, cancellationToken);
    }

    public async Task<WorkspaceEdit?> RenameSymbolAsync(string filePath, int line, int character, string newName, CancellationToken cancellationToken = default)
    {
        var param = new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = new Uri(filePath).ToString() },
            Position = new Position { Line = line, Character = character },
            NewName = newName
        };

        return await SendRequestAsync<WorkspaceEdit>("textDocument/rename", param, cancellationToken);
    }

    private async Task<T?> SendRequestAsync<T>(string method, object? @params, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _requestId);
        var request = new JsonRpcRequest
        {
            Id = id,
            Method = method,
            Params = @params
        };

        var tcs = new TaskCompletionSource<JsonElement>();
        _pendingRequests[id] = tcs;

        try
        {
            _logger.LogDebug("SendRequestAsync: Sending request id={Id} method={Method}", id, method);
            await SendMessageAsync(request, cancellationToken);
            _logger.LogDebug("SendRequestAsync: Request sent, waiting for response...");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(120));

            var result = await tcs.Task.WaitAsync(cts.Token);

            if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
                return default;

            return result.Deserialize<T>(JsonOptions);
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
        }
    }

    private async Task SendNotificationAsync(string method, object? @params, CancellationToken cancellationToken)
    {
        var notification = new JsonRpcNotification
        {
            Method = method,
            Params = @params
        };

        await SendMessageAsync(notification, cancellationToken);
    }

    private async Task SendMessageAsync(object message, CancellationToken cancellationToken)
    {
        if (_lspProcess?.StandardInput?.BaseStream == null)
            throw new InvalidOperationException("LSP client not started");

        var json = JsonSerializer.Serialize(message, JsonOptions);
        // Write bytes directly to avoid encoding issues (BOM, etc.)
        var content = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";
        var bytes = Encoding.UTF8.GetBytes(content);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _lspProcess.StandardInput.BaseStream.WriteAsync(bytes, cancellationToken);
            await _lspProcess.StandardInput.BaseStream.FlushAsync(cancellationToken);
            _logger.LogTrace("Sent: {Message}", json);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Sends a success response to a server-initiated request (e.g. client/registerCapability).
    /// Without this, the server blocks waiting for a response and stops processing further requests.
    /// </summary>
    private async Task SendServerResponseAsync(int requestId)
    {
        // Build JSON manually to ensure "result":null is always present.
        // Using anonymous types + JsonOptions would strip null via WhenWritingNull.
        var json = $"{{\"jsonrpc\":\"2.0\",\"id\":{requestId},\"result\":null}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {bytes.Length}\r\n\r\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);

        _logger.LogDebug("Sending response to server request id={Id}", requestId);

        await _writeLock.WaitAsync(CancellationToken.None);
        try
        {
            if (_lspProcess?.StandardInput?.BaseStream == null)
                return;
            await _lspProcess.StandardInput.BaseStream.WriteAsync(headerBytes, CancellationToken.None);
            await _lspProcess.StandardInput.BaseStream.WriteAsync(bytes, CancellationToken.None);
            await _lspProcess.StandardInput.BaseStream.FlushAsync(CancellationToken.None);
            _logger.LogTrace("Sent server response: {Message}", json);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("ReadLoopAsync: Starting read loop");
        if (_outputReader == null)
        {
            _logger.LogError("ReadLoopAsync: Output reader is null!");
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("ReadLoopAsync: Waiting for content length header...");
                var contentLength = await ReadContentLengthAsync(_outputReader, cancellationToken);
                if (contentLength == null)
                {
                    _logger.LogWarning("ReadLoopAsync: Stream closed (null content length)");
                    return; // Stream closed
                }

                _logger.LogDebug("ReadLoopAsync: Content-Length: {Length}", contentLength.Value);
                if (contentLength.Value <= 0)
                    continue;

                var buffer = new char[contentLength.Value];
                var totalRead = 0;
                while (totalRead < contentLength.Value)
                {
                    var read = await _outputReader.ReadAsync(buffer.AsMemory(totalRead, contentLength.Value - totalRead), cancellationToken);
                    if (read == 0)
                    {
                        _logger.LogWarning("ReadLoopAsync: Stream closed while reading payload");
                        return;
                    }
                    totalRead += read;
                }

                var json = new string(buffer, 0, totalRead);
                _logger.LogTrace("Received: {Message}", json);
                _logger.LogDebug("ReadLoopAsync: Received message, length={Length}", json.Length);

                ProcessMessage(json);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("ReadLoopAsync: Cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LSP read loop");
            }
        }
        _logger.LogDebug("ReadLoopAsync: Exiting read loop");
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool hasId = root.TryGetProperty("id", out var idElement);
            bool hasMethod = root.TryGetProperty("method", out var methodElement);

            if (hasId && hasMethod)
            {
                // Server-sent REQUEST (has both id and method) — needs a response
                var serverRequestId = idElement.GetInt32();
                var method = methodElement.GetString();
                _logger.LogDebug("Received server request: id={Id} method={Method}", serverRequestId, method);

                // Send an empty success response so the server doesn't block
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendServerResponseAsync(serverRequestId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send response to server request id={Id}", serverRequestId);
                    }
                });
            }
            else if (hasId)
            {
                // Response to one of our requests
                var id = idElement.GetInt32();
                if (_pendingRequests.TryGetValue(id, out var tcs))
                {
                    if (root.TryGetProperty("error", out var error))
                    {
                        var errorMsg = error.GetProperty("message").GetString();
                        _logger.LogError("LSP error: {Error}", errorMsg);
                        tcs.TrySetException(new Exception($"LSP error: {errorMsg}"));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        tcs.TrySetResult(result.Clone());
                    }
                    else
                    {
                        tcs.TrySetResult(default);
                    }
                }
                else
                {
                    _logger.LogDebug("Received response for unknown request id={Id}", id);
                }
            }
            else if (hasMethod)
            {
                // Notification from server (has method but no id)
                var method = methodElement.GetString();
                if (method == "textDocument/publishDiagnostics" && root.TryGetProperty("params", out var @params))
                {
                    var diagnostics = @params.Deserialize<PublishDiagnosticsParams>(JsonOptions);
                    if (diagnostics != null)
                    {
                        _diagnosticsCache[diagnostics.Uri] = diagnostics;
                        DiagnosticsReceived?.Invoke(diagnostics);
                        _logger.LogDebug("Received {Count} diagnostics for {Uri}",
                            diagnostics.Diagnostics.Length, diagnostics.Uri);
                    }
                }
                else
                {
                    _logger.LogDebug("Received notification: {Method}", method);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing LSP message");
        }
    }

    private static async Task<int?> ReadContentLengthAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        int contentLength = 0;
        string? line;

        // Read header lines until we hit the empty line separator (\r\n\r\n)
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrEmpty(line))
                break; // Empty line = end of headers

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(line.Substring(15).Trim(), out var length))
                    contentLength = length;
            }
        }

        if (line == null)
            return null; // Stream closed

        return contentLength;
    }

    private async Task CleanupFailedStartAsync()
    {
        _readLoopCts?.Cancel();

        if (_readLoopTask != null)
        {
            try
            {
                await _readLoopTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch { }
        }

        if (_lspProcess != null && !_lspProcess.HasExited)
        {
            try
            {
                _lspProcess.Kill();
            }
            catch { }
        }

        _lspProcess?.Dispose();
        _readLoopCts?.Dispose();

        _outputReader = null;
        _readLoopCts = null;
        _readLoopTask = null;
        _lspProcess = null;
    }

    public async ValueTask DisposeAsync()
    {
        _readLoopCts?.Cancel();

        if (_readLoopTask != null)
        {
            try
            {
                await _readLoopTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch { }
        }

        if (_lspProcess != null && !_lspProcess.HasExited)
        {
            try
            {
                // Send shutdown request
                await SendRequestAsync<object>("shutdown", null, CancellationToken.None);
                await SendNotificationAsync("exit", null, CancellationToken.None);

                if (!_lspProcess.WaitForExit(3000))
                    _lspProcess.Kill();
            }
            catch { }
        }

        _lspProcess?.Dispose();
        _readLoopCts?.Dispose();
        _initLock.Dispose();
        _writeLock.Dispose();
    }
}
