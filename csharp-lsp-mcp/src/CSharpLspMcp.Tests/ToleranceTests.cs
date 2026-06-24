using System.Threading;
using System.Threading.Tasks;
using CSharpLspMcp.Tools;
using Xunit;

namespace CSharpLspMcp.Tests;

/// <summary>
/// Covers the same-line position-tolerance ring (kb-4110 / AI-536): exact hit issues no extra
/// probes, a -1 near-miss resolves, and the ring is hard-clamped to the current line.
/// </summary>
public class ToleranceTests
{
    [Fact]
    public async Task TryWithTolerance_ExactHit_IssuesNoExtraProbes()
    {
        var calls = 0;
        var (result, winning) = await CSharpTools.TryWithToleranceAsync<string>(
            content: "var value = 1;",
            line: 0,
            character: 4,
            probe: (ch, _) =>
            {
                calls++;
                return Task.FromResult<string?>(ch == 4 ? "hit" : null);
            },
            ct: CancellationToken.None);

        Assert.Equal("hit", result);
        Assert.Equal(4, winning);
        Assert.Equal(1, calls); // exact hit => no extra queries on success
    }

    [Fact]
    public async Task TryWithTolerance_MinusOneNearMiss_Resolves()
    {
        var calls = 0;
        var (result, winning) = await CSharpTools.TryWithToleranceAsync<string>(
            content: "var value = 1;",
            line: 0,
            character: 5,
            probe: (ch, _) =>
            {
                calls++;
                return Task.FromResult<string?>(ch == 4 ? "hit" : null);
            },
            ct: CancellationToken.None);

        Assert.Equal("hit", result);
        Assert.Equal(4, winning); // -1 offset won
        Assert.Equal(2, calls);   // exact missed (1), -1 hit (2)
    }

    [Fact]
    public async Task TryWithTolerance_TrueMiss_ReturnsNullAndRequestedOffset()
    {
        var (result, winning) = await CSharpTools.TryWithToleranceAsync<string>(
            content: "var value = 1;",
            line: 0,
            character: 5,
            probe: (_, _) => Task.FromResult<string?>(null),
            ct: CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(5, winning);
    }

    [Fact]
    public void BuildToleranceRing_AtLineEnd_NeverCrossesBoundary()
    {
        // "var x;" has length 6; request sits at the line end.
        var ring = CSharpTools.BuildToleranceRing(character: 6, lineLength: 6);

        Assert.Equal(6, ring[0]);          // exact offset probed first
        Assert.All(ring, o => Assert.InRange(o, 0, 6));
        Assert.DoesNotContain(7, ring);    // never overshoots to (what would be) the next line
        Assert.True(ring.Count <= ToleranceDeltaCount);
    }

    [Fact]
    public void BuildToleranceRing_AtLineStart_NeverGoesNegative()
    {
        var ring = CSharpTools.BuildToleranceRing(character: 0, lineLength: 6);

        Assert.Equal(0, ring[0]);
        Assert.All(ring, o => Assert.InRange(o, 0, 6));
        Assert.DoesNotContain(-1, ring);
    }

    [Fact]
    public void BuildToleranceRing_MidLine_YieldsExactMinusOnePlusOneMinusTwo()
    {
        var ring = CSharpTools.BuildToleranceRing(character: 10, lineLength: 40);

        Assert.Equal(new[] { 10, 9, 11, 8 }, ring);
    }

    [Theory]
    [InlineData("abc\ndefgh\n", 0, 3)]
    [InlineData("abc\ndefgh\n", 1, 5)]
    [InlineData("abc\r\ndefgh", 0, 3)]  // CRLF terminator stripped
    [InlineData("abc\ndefgh\n", 9, 0)]  // line out of range => clamp source is 0
    public void GetLineLength_ReturnsCharCountExcludingTerminator(string content, int line, int expected)
    {
        Assert.Equal(expected, CSharpTools.GetLineLength(content, line));
    }

    private static int ToleranceDeltaCount => CSharpTools.ToleranceDeltas.Length;
}
