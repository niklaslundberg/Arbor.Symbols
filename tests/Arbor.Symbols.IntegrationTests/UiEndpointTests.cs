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

    private sealed class UiTestWebApplicationFactory(string cacheDirectory) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOfficialSymbolClient>();
                services.RemoveAll<SymbolStorage>();
                services.RemoveAll<IOptions<SymbolServerOptions>>();

                services.AddSingleton<IOptions<SymbolServerOptions>>(
                    new OptionsWrapper<SymbolServerOptions>(new SymbolServerOptions
                    {
                        CacheDirectory = cacheDirectory,
                        AssemblySearchDirectories = []
                    }));

                services.AddSingleton<SymbolStorage>(new SymbolStorage(new SymbolServerOptions
                {
                    CacheDirectory = cacheDirectory,
                    AssemblySearchDirectories = []
                }));

                services.AddSingleton<IOfficialSymbolClient, FakeOfficialSymbolClient>();
            });
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
