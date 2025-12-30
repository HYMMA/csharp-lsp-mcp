using System.Reflection;
using System.Text;
using CSharpLspMcp.Lsp;
using Xunit;

namespace CSharpLspMcp.Tests;

public class LspClientTests
{
    [Fact]
    public async Task ReadContentLengthAsync_ReadsLengthAndPayload()
    {
        var payload = Encoding.UTF8.GetBytes("{\"a\":\"✓\"}");
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        var stream = new MemoryStream(header.Concat(payload).ToArray());

        var readLength = await InvokeReadContentLengthAsync(stream);
        Assert.Equal(payload.Length, readLength);

        var readBuffer = new byte[payload.Length];
        var readOk = await InvokeReadExactAsync(stream, readBuffer, payload.Length);
        Assert.True(readOk);
        Assert.Equal(payload, readBuffer);
    }

    [Fact]
    public async Task ReadExactAsync_ReturnsFalseOnShortStream()
    {
        var stream = new MemoryStream(new byte[] { 1, 2 });
        var readBuffer = new byte[3];

        var readOk = await InvokeReadExactAsync(stream, readBuffer, readBuffer.Length);

        Assert.False(readOk);
    }

    private static Task<int?> InvokeReadContentLengthAsync(Stream stream)
    {
        var method = typeof(LspClient).GetMethod(
            "ReadContentLengthAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var task = (Task<int?>)method.Invoke(null, new object?[] { stream, CancellationToken.None })!;
        return task;
    }

    private static Task<bool> InvokeReadExactAsync(Stream stream, byte[] buffer, int length)
    {
        var method = typeof(LspClient).GetMethod(
            "ReadExactAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var task = (Task<bool>)method.Invoke(null, new object?[] { stream, buffer, length, CancellationToken.None })!;
        return task;
    }
}
