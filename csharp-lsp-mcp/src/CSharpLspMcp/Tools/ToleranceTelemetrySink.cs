using System.Text.Json;

namespace CSharpLspMcp.Tools;

/// <summary>
/// Opt-in, lock-safe JSONL sink that records same-line position-tolerance outcomes so the
/// <c>{0, -1, +1, -2}</c> ring can be validated against the real distribution of winning offsets
/// seen in production. It is activated only when the <see cref="PathEnvVar"/> environment variable
/// points at a writable file path; when unset, every call is a cheap no-op (a single null check),
/// so default behavior and the happy path are completely unchanged.
/// </summary>
/// <remarks>
/// One JSON object is appended per line (JSON Lines / NDJSON):
/// <code>
/// {"ts":"2026-06-24T16:00:00.000Z","tool":"csharp_definition","line":12,"requested":8,"winning":7,"delta":-1,"outcome":"tolerance"}
/// </code>
/// <para>
/// Downstream tooling builds a histogram by grouping on <c>delta</c> (or <c>outcome</c>): the count
/// of <c>exact</c> vs <c>tolerance</c> (and which delta won) vs <c>miss</c> answers whether the ring
/// is well chosen and whether any offset is dead weight.
/// </para>
/// <para>
/// Writes are lock-safe both in-process (a private monitor) and across processes (append-mode
/// <see cref="FileStream"/> opened with <see cref="FileShare.ReadWrite"/> plus a short bounded retry
/// on sharing/IO contention). Each record is written as a single line so concurrent appends never
/// interleave into a torn JSON object.
/// </para>
/// </remarks>
internal sealed class ToleranceTelemetrySink
{
    /// <summary>Environment variable holding the JSONL output path. Unset =&gt; sink disabled.</summary>
    public const string PathEnvVar = "CSHARP_LSP_TOLERANCE_LOG";

    private const int MaxAppendAttempts = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Compact, one object per line — never indent (would break the line-per-record contract).
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly string? _path;

    /// <summary>
    /// Creates a sink writing to <paramref name="path"/>. A null/whitespace path produces a disabled
    /// (no-op) sink. The target directory is created eagerly so the first <see cref="Record"/> never
    /// fails on a missing folder.
    /// </summary>
    public ToleranceTelemetrySink(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _path = null;
            return;
        }

        _path = path;

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Telemetry is best-effort: a bad path must never disable the tool. Disable the sink instead.
            _path = null;
        }
    }

    /// <summary>Builds a sink from <see cref="PathEnvVar"/>. Returns a disabled sink when unset.</summary>
    public static ToleranceTelemetrySink FromEnvironment()
        => new(Environment.GetEnvironmentVariable(PathEnvVar));

    /// <summary>True when a valid output path is configured and records will be written.</summary>
    public bool IsEnabled => _path is not null;

    /// <summary>
    /// Records one tolerance outcome. <paramref name="winning"/> is the winning character offset, or
    /// <c>null</c> when the lookup missed entirely after the full ring was probed. No-op when disabled.
    /// Best-effort: any IO failure is swallowed so telemetry never breaks a tool call.
    /// </summary>
    public void Record(string tool, int line, int requested, int? winning)
    {
        if (_path is null)
            return;

        try
        {
            Append(BuildRecord(tool, line, requested, winning, DateTimeOffset.UtcNow));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Swallow — see remarks. The ILogger path already surfaced the win.
        }
    }

    /// <summary>
    /// Serializes a single JSONL record. <c>outcome</c> is <c>"miss"</c> when <paramref name="winning"/>
    /// is null, <c>"exact"</c> when it equals <paramref name="requested"/>, otherwise <c>"tolerance"</c>;
    /// <c>delta</c> is <c>winning - requested</c> (null on a miss). Exposed for unit tests.
    /// </summary>
    internal static string BuildRecord(string tool, int line, int requested, int? winning, DateTimeOffset timestamp)
    {
        var outcome = winning switch
        {
            null => "miss",
            var w when w == requested => "exact",
            _ => "tolerance"
        };

        var record = new ToleranceRecord(
            Ts: timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Tool: tool,
            Line: line,
            Requested: requested,
            Winning: winning,
            Delta: winning is int w2 ? w2 - requested : null,
            Outcome: outcome);

        return JsonSerializer.Serialize(record, SerializerOptions);
    }

    private void Append(string jsonLine)
    {
        lock (_gate)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var stream = new FileStream(
                        _path!, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream);
                    writer.WriteLine(jsonLine);
                    writer.Flush();
                    return;
                }
                catch (IOException) when (attempt < MaxAppendAttempts)
                {
                    // Another process/thread holds the handle; back off briefly and retry.
                    Thread.Sleep(10 * attempt);
                }
            }
        }
    }

    private sealed record ToleranceRecord(
        string Ts,
        string Tool,
        int Line,
        int Requested,
        int? Winning,
        int? Delta,
        string Outcome);
}
