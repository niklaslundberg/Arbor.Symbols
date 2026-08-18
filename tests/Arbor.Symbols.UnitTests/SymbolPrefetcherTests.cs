using System.Net;
using Arbor.Symbols.ConsoleClient;
using Arbor.Symbols.Core;

namespace Arbor.Symbols.UnitTests;

public class SymbolPrefetcherTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private static readonly SymbolResourceRequest Request = new("MyLib.pdb", "ABC123", "MyLib.pdb");

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FileAlreadyCachedAndNotForced_SkipsWithoutContactingServer()
    {
        var destinationPath = SymbolResourcePathHelper.GetCachePath(_cacheRoot, Request);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllTextAsync(destinationPath, "already-cached", TestContext.Current.CancellationToken);
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("new-content") });

        var result = await SymbolPrefetcher.RunAsync(
            CreateClient(handler), [Request], _cacheRoot, force: false, dryRun: false, maxConcurrency: 1, TestContext.Current.CancellationToken);

        result.Should().Be(new PrefetchResult(Downloaded: 0, Skipped: 1, Failed: 0));
        handler.CallCount.Should().Be(0);
        (await File.ReadAllTextAsync(destinationPath, TestContext.Current.CancellationToken)).Should().Be("already-cached");
    }

    [Fact]
    public async Task RunAsync_FileAlreadyCachedAndForced_RedownloadsAndOverwrites()
    {
        var destinationPath = SymbolResourcePathHelper.GetCachePath(_cacheRoot, Request);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllTextAsync(destinationPath, "stale-content", TestContext.Current.CancellationToken);
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fresh-content") });

        var result = await SymbolPrefetcher.RunAsync(
            CreateClient(handler), [Request], _cacheRoot, force: true, dryRun: false, maxConcurrency: 1, TestContext.Current.CancellationToken);

        result.Should().Be(new PrefetchResult(Downloaded: 1, Skipped: 0, Failed: 0));
        handler.CallCount.Should().Be(1);
        (await File.ReadAllTextAsync(destinationPath, TestContext.Current.CancellationToken)).Should().Be("fresh-content");
    }

    [Fact]
    public async Task RunAsync_DryRun_ReportsWouldDownloadWithoutContactingServerOrWritingFiles()
    {
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("content") });

        var result = await SymbolPrefetcher.RunAsync(
            CreateClient(handler), [Request], _cacheRoot, force: false, dryRun: true, maxConcurrency: 1, TestContext.Current.CancellationToken);

        result.Should().Be(new PrefetchResult(Downloaded: 1, Skipped: 0, Failed: 0));
        handler.CallCount.Should().Be(0);
        File.Exists(SymbolResourcePathHelper.GetCachePath(_cacheRoot, Request)).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_SuccessfulDownload_WritesFileAndLeavesNoTemporaryFile()
    {
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("downloaded-content") });

        var result = await SymbolPrefetcher.RunAsync(
            CreateClient(handler), [Request], _cacheRoot, force: false, dryRun: false, maxConcurrency: 1, TestContext.Current.CancellationToken);

        result.Should().Be(new PrefetchResult(Downloaded: 1, Skipped: 0, Failed: 0));
        var destinationPath = SymbolResourcePathHelper.GetCachePath(_cacheRoot, Request);
        (await File.ReadAllTextAsync(destinationPath, TestContext.Current.CancellationToken)).Should().Be("downloaded-content");

        Directory.GetFiles(Path.GetDirectoryName(destinationPath)!)
            .Should().ContainSingle().Which.Should().Be(destinationPath);
    }

    [Fact]
    public async Task RunAsync_ServerReturnsError_CountsAsFailedAndWritesNoFile()
    {
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await SymbolPrefetcher.RunAsync(
            CreateClient(handler), [Request], _cacheRoot, force: false, dryRun: false, maxConcurrency: 1, TestContext.Current.CancellationToken);

        result.Should().Be(new PrefetchResult(Downloaded: 0, Skipped: 0, Failed: 1));
        File.Exists(SymbolResourcePathHelper.GetCachePath(_cacheRoot, Request)).Should().BeFalse();
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://example.test/") };

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(responder(request));
        }
    }
}
