using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CSharpLspMcp.Tools;
using Xunit;

namespace CSharpLspMcp.Tests;

/// <summary>
/// Covers the opt-in JSONL winning-offset telemetry sink (kb-4110 / AI-536): disabled by default,
/// correct outcome/delta classification, and lock-safe concurrent appends with no torn lines.
/// </summary>
public class ToleranceTelemetrySinkTests
{
    [Fact]
    public void Sink_WithNoPath_IsDisabledAndRecordIsNoOp()
    {
        var sink = new ToleranceTelemetrySink(null);

        Assert.False(sink.IsEnabled);
        sink.Record("csharp_hover", line: 1, requested: 2, winning: 2); // must not throw
    }

    [Fact]
    public void Sink_WithWhitespacePath_IsDisabled()
    {
        Assert.False(new ToleranceTelemetrySink("   ").IsEnabled);
    }

    [Theory]
    [InlineData(8, 8, "exact", 0)]
    [InlineData(8, 7, "tolerance", -1)]
    [InlineData(8, 9, "tolerance", 1)]
    public void BuildRecord_ClassifiesOutcomeAndDelta(int requested, int winning, string expectedOutcome, int expectedDelta)
    {
        var json = ToleranceTelemetrySink.BuildRecord(
            "csharp_definition", line: 12, requested: requested, winning: winning,
            timestamp: DateTimeOffset.UnixEpoch);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("csharp_definition", root.GetProperty("tool").GetString());
        Assert.Equal(12, root.GetProperty("line").GetInt32());
        Assert.Equal(requested, root.GetProperty("requested").GetInt32());
        Assert.Equal(winning, root.GetProperty("winning").GetInt32());
        Assert.Equal(expectedDelta, root.GetProperty("delta").GetInt32());
        Assert.Equal(expectedOutcome, root.GetProperty("outcome").GetString());
        Assert.Equal("1970-01-01T00:00:00.000Z", root.GetProperty("ts").GetString());
    }

    [Fact]
    public void BuildRecord_NullWinning_IsMissWithNullDelta()
    {
        var json = ToleranceTelemetrySink.BuildRecord(
            "csharp_references", line: 3, requested: 5, winning: null,
            timestamp: DateTimeOffset.UnixEpoch);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("miss", root.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("winning").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("delta").ValueKind);
    }

    [Fact]
    public void Record_AppendsOneWellFormedJsonLinePerCall()
    {
        var path = NewTempLogPath();
        try
        {
            var sink = new ToleranceTelemetrySink(path);
            Assert.True(sink.IsEnabled);

            sink.Record("csharp_hover", line: 0, requested: 4, winning: 4);
            sink.Record("csharp_definition", line: 1, requested: 9, winning: 8);

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.All(lines, l => Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(l).RootElement.ValueKind));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Record_IsLockSafeUnderConcurrency_NoTornLines()
    {
        var path = NewTempLogPath();
        try
        {
            var sink = new ToleranceTelemetrySink(path);
            const int writers = 8;
            const int perWriter = 50;

            var tasks = Enumerable.Range(0, writers).Select(w => Task.Run(() =>
            {
                for (var i = 0; i < perWriter; i++)
                    sink.Record("csharp_completions", line: w, requested: i, winning: i - 1);
            }));
            await Task.WhenAll(tasks);

            var lines = File.ReadAllLines(path);
            Assert.Equal(writers * perWriter, lines.Length);
            // Every line must be a complete, parseable JSON object (no interleaved/torn writes).
            Assert.All(lines, l => Assert.Equal("tolerance", JsonDocument.Parse(l).RootElement.GetProperty("outcome").GetString()));
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static string NewTempLogPath()
        => Path.Combine(Path.GetTempPath(), $"csharp-lsp-tolerance-{Guid.NewGuid():N}.jsonl");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* best-effort cleanup */ }
    }
}
