using System.Net;
using Arbor.Symbols.Core;
using Arbor.Symbols.Server;

namespace Arbor.Symbols.UnitTests;

public class OfficialSymbolClientTests
{
    private static readonly SymbolResourceRequest Request = new("MyLib.pdb", "ABC123", "MyLib.pdb");

    [Fact]
    public async Task TryDownloadAsync_SuccessResponse_ReturnsStreamWithContent()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("symbol-bytes") });
        var client = new OfficialSymbolClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") });

        await using var stream = await client.TryDownloadAsync(Request, TestContext.Current.CancellationToken);

        stream.Should().NotBeNull();
        using var reader = new StreamReader(stream!);
        (await reader.ReadToEndAsync()).Should().Be("symbol-bytes");
    }

    [Fact]
    public async Task TryDownloadAsync_NotFoundResponse_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new OfficialSymbolClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") });

        var stream = await client.TryDownloadAsync(Request, TestContext.Current.CancellationToken);

        stream.Should().BeNull();
    }

    [Fact]
    public async Task TryDownloadAsync_HttpRequestException_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("network error"));
        var client = new OfficialSymbolClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") });

        var stream = await client.TryDownloadAsync(Request, TestContext.Current.CancellationToken);

        stream.Should().BeNull();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
