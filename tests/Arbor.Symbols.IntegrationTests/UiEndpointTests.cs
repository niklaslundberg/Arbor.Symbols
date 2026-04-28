using Arbor.Symbols.Core;
using Arbor.Symbols.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Arbor.Symbols.IntegrationTests;

public class UiEndpointTests
{
    [Fact]
    public async Task DashboardEndpoint_ReturnsHtml()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await using var factory = new UiTestWebApplicationFactory(cacheRoot);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/ui", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.Should().Contain("Arbor.Symbols Dashboard");
        html.Should().Contain("Statistics");
        html.Should().Contain("Disk Usage");
        html.Should().Contain("Cached Symbols");
    }

    [Fact]
    public async Task DashboardEndpoint_ShowsCachedSymbolAfterDownload()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await using var factory = new UiTestWebApplicationFactory(cacheRoot);
        using var client = factory.CreateClient();

        var request = new SymbolResourceRequest("my.pdb", "ABCDEF1234", "my.pdb");
        var symbolResponse = await client.GetAsync($"/{request.RelativePath}", TestContext.Current.CancellationToken);
        symbolResponse.EnsureSuccessStatusCode();

        var uiResponse = await client.GetAsync("/ui", TestContext.Current.CancellationToken);
        uiResponse.EnsureSuccessStatusCode();

        var html = await uiResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.Should().Contain("my.pdb");
    }

    [Fact]
    public async Task DeleteCacheEntry_ReturnsOkAndRemovesFile()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await using var factory = new UiTestWebApplicationFactory(cacheRoot);
        using var client = factory.CreateClient();

        var request = new SymbolResourceRequest("my.pdb", "ABCDEF1234", "my.pdb");

        var symbolResponse = await client.GetAsync($"/{request.RelativePath}", TestContext.Current.CancellationToken);
        symbolResponse.EnsureSuccessStatusCode();

        var cachePath = SymbolResourcePathHelper.GetCachePath(cacheRoot, request);
        File.Exists(cachePath).Should().BeTrue();

        var deleteResponse = await client.DeleteAsync(
            $"/ui/cache/{Uri.EscapeDataString(request.RequestedFileName)}/{Uri.EscapeDataString(request.Identifier)}/{Uri.EscapeDataString(request.ResourceFileName)}",
            TestContext.Current.CancellationToken);

        deleteResponse.EnsureSuccessStatusCode();
        File.Exists(cachePath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteCacheEntry_ReturnsNotFoundForMissingEntry()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await using var factory = new UiTestWebApplicationFactory(cacheRoot);
        using var client = factory.CreateClient();

        var deleteResponse = await client.DeleteAsync(
            "/ui/cache/nonexistent.pdb/AAAA/nonexistent.pdb",
            TestContext.Current.CancellationToken);

        deleteResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DashboardEndpoint_ReflectsStatisticsAfterRequests()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await using var factory = new UiTestWebApplicationFactory(cacheRoot);
        using var client = factory.CreateClient();

        // First request: downloaded from official (cache miss)
        var request = new SymbolResourceRequest("my.pdb", "ABCDEF1234", "my.pdb");
        await client.GetAsync($"/{request.RelativePath}", TestContext.Current.CancellationToken);

        // Second request: served from cache (cache hit)
        await client.GetAsync($"/{request.RelativePath}", TestContext.Current.CancellationToken);

        var uiResponse = await client.GetAsync("/ui", TestContext.Current.CancellationToken);
        uiResponse.EnsureSuccessStatusCode();

        var html = await uiResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Total requests = 2, cache hits = 1, downloads = 1
        // Use targeted patterns that include surrounding context from the dashboard HTML
        html.Should().MatchRegex(@"<div class=""value"">2</div>\s*<div class=""label"">Total Requests</div>");
        html.Should().MatchRegex(@"<div class=""value"">1</div>\s*<div class=""label"">Cache Hits</div>");
        html.Should().MatchRegex(@"<div class=""value"">1</div>\s*<div class=""label"">Downloaded</div>");
    }

    private sealed class UiTestWebApplicationFactory(string cacheDirectory) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOfficialSymbolClient>();
                services.RemoveAll<SymbolStorage>();
                services.RemoveAll<IOptions<SymbolServerOptions>>();

                var options = new SymbolServerOptions
                {
                    CacheDirectory = cacheDirectory,
                    AssemblySearchDirectories = []
                };

                services.AddSingleton<IOptions<SymbolServerOptions>>(new OptionsWrapper<SymbolServerOptions>(options));
                services.AddSingleton(new SymbolStorage(options));

                services.AddSingleton<IOfficialSymbolClient, FakeOfficialSymbolClient>();
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();

            if (Directory.Exists(cacheDirectory))
            {
                try
                {
                    Directory.Delete(cacheDirectory, recursive: true);
                }
                catch (IOException ex)
                {
                    // Best-effort cleanup: log to debug output so CI logs capture it if cleanup fails.
                    System.Diagnostics.Debug.WriteLine($"Failed to clean up test cache directory '{cacheDirectory}': {ex.Message}");
                }
            }
        }
    }

    private sealed class FakeOfficialSymbolClient : IOfficialSymbolClient
    {
        public Task<Stream?> TryDownloadAsync(SymbolResourceRequest request, CancellationToken cancellationToken)
        {
            Stream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-symbol-data"));
            return Task.FromResult<Stream?>(stream);
        }
    }
}
