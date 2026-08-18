using Arbor.Symbols.Core;
using Arbor.Symbols.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Arbor.Symbols.UnitTests;

public class SymbolRequestHandlerTests
{
    private static readonly SymbolResourceRequest Request = new("my.pdb", "ABCDEF1234", "my.pdb");

    [Fact]
    public async Task HandleAsync_CacheHit_ServesFromStorageWithoutContactingOfficialClient()
    {
        var storage = new FakeSymbolStorage();
        storage.Seed(Request, "cached"u8.ToArray());
        var officialClient = new FakeOfficialSymbolClient(content: null);
        var statistics = new SymbolServerStatistics();

        var handler = CreateHandler(storage, officialClient, new FakeIlSpySymbolGenerator(false), statistics);

        var result = await handler.HandleAsync(Request.RequestedFileName, Request.Identifier, Request.ResourceFileName, CancellationToken.None);

        var (statusCode, body) = await ExecuteAsync(result);
        statusCode.Should().Be(StatusCodes.Status200OK);
        body.Should().Equal("cached"u8.ToArray());
        officialClient.WasCalled.Should().BeFalse();
        statistics.CacheHits.Should().Be(1);
        statistics.TotalRequests.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_OfficialDownloadSucceeds_SavesToStorageAndServesIt()
    {
        var storage = new FakeSymbolStorage();
        var officialClient = new FakeOfficialSymbolClient("downloaded"u8.ToArray());
        var statistics = new SymbolServerStatistics();

        var handler = CreateHandler(storage, officialClient, new FakeIlSpySymbolGenerator(false), statistics);

        var result = await handler.HandleAsync(Request.RequestedFileName, Request.Identifier, Request.ResourceFileName, CancellationToken.None);

        var (statusCode, body) = await ExecuteAsync(result);
        statusCode.Should().Be(StatusCodes.Status200OK);
        body.Should().Equal("downloaded"u8.ToArray());
        storage.SavedContent.Should().Equal("downloaded"u8.ToArray());
        statistics.OfficialDownloads.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_NothingAvailable_ReturnsNotFound()
    {
        var storage = new FakeSymbolStorage();
        var officialClient = new FakeOfficialSymbolClient(content: null);
        var statistics = new SymbolServerStatistics();

        var handler = CreateHandler(storage, officialClient, new FakeIlSpySymbolGenerator(false), statistics);

        var result = await handler.HandleAsync(Request.RequestedFileName, Request.Identifier, Request.ResourceFileName, CancellationToken.None);

        var (statusCode, _) = await ExecuteAsync(result);
        statusCode.Should().Be(StatusCodes.Status404NotFound);
        statistics.NotFound.Should().Be(1);
    }

    [Theory]
    [InlineData("..", "ABCDEF1234", "my.pdb")]
    [InlineData("my.pdb", "..", "my.pdb")]
    [InlineData("my.pdb", "ABCDEF1234", "..")]
    public async Task HandleAsync_PathEscapesCacheRoot_ReturnsBadRequestInsteadOfThrowing(
        string requestedFileName, string identifier, string resourceFileName)
    {
        var storage = new SymbolStorage(new SymbolServerOptions
        {
            CacheDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        });
        var officialClient = new FakeOfficialSymbolClient(content: null);
        var statistics = new SymbolServerStatistics();

        var handler = CreateHandler(storage, officialClient, new FakeIlSpySymbolGenerator(false), statistics);

        var result = await handler.HandleAsync(requestedFileName, identifier, resourceFileName, CancellationToken.None);

        var (statusCode, _) = await ExecuteAsync(result);
        statusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    private static SymbolRequestHandler CreateHandler(
        ISymbolStorage storage,
        IOfficialSymbolClient officialClient,
        IIlSpySymbolGenerator ilSpySymbolGenerator,
        SymbolServerStatistics statistics)
    {
        var options = Options.Create(new SymbolServerOptions { AssemblySearchDirectories = [] });
        return new SymbolRequestHandler(
            storage,
            officialClient,
            ilSpySymbolGenerator,
            options,
            NullLogger<SymbolRequestHandler>.Instance,
            statistics);
    }

    private static async Task<(int StatusCode, byte[] Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() }
        };

        await result.ExecuteAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var buffer = new MemoryStream();
        await context.Response.Body.CopyToAsync(buffer);
        return (context.Response.StatusCode, buffer.ToArray());
    }

    private sealed class FakeSymbolStorage : ISymbolStorage
    {
        private readonly Dictionary<string, byte[]> _entries = new(StringComparer.OrdinalIgnoreCase);

        public byte[]? SavedContent { get; private set; }

        public void Seed(SymbolResourceRequest request, byte[] content) => _entries[request.RelativePath] = content;

        public string GetPath(SymbolResourceRequest request) => request.RelativePath;

        public bool TryOpenRead(SymbolResourceRequest request, out Stream stream)
        {
            if (_entries.TryGetValue(request.RelativePath, out var bytes))
            {
                stream = new MemoryStream(bytes);
                return true;
            }

            stream = null!;
            return false;
        }

        public async Task SaveAsync(SymbolResourceRequest request, Stream source, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            SavedContent = buffer.ToArray();
            _entries[request.RelativePath] = SavedContent;
        }

        public IReadOnlyList<CachedSymbolEntry> GetCachedSymbols() => [];

        public long GetDiskUsageBytes() => 0;

        public bool TryDelete(SymbolResourceRequest request) => _entries.Remove(request.RelativePath);
    }

    private sealed class FakeOfficialSymbolClient(byte[]? content) : IOfficialSymbolClient
    {
        public bool WasCalled { get; private set; }

        public Task<Stream?> TryDownloadAsync(SymbolResourceRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult<Stream?>(content is null ? null : new MemoryStream(content));
        }
    }

    private sealed class FakeIlSpySymbolGenerator(bool succeed) : IIlSpySymbolGenerator
    {
        public Task<bool> TryGeneratePdbAsync(string assemblyPath, string outputPdbPath, CancellationToken cancellationToken)
            => Task.FromResult(succeed);
    }
}
