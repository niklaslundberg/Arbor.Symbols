using Arbor.Symbols.Core;
using Arbor.Symbols.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Arbor.Symbols.IntegrationTests;

public class SymbolEndpointTests
{
    [Fact]
    public async Task SymbolEndpoint_ReturnsContentFromOfficialClientAndCachesIt()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await using var factory = new TestWebApplicationFactory(cacheRoot);
        using var client = factory.CreateClient();

        var request = new SymbolResourceRequest("my.pdb", "ABCDEF", "my.pdb");
        var response = await client.GetAsync($"/{request.RelativePath}");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal("from-official", content);

        var cachePath = SymbolResourcePathHelper.GetCachePath(cacheRoot, request);
        Assert.True(File.Exists(cachePath));
    }

    private sealed class TestWebApplicationFactory(string cacheDirectory) : WebApplicationFactory<Program>
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
            Stream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("from-official"));
            return Task.FromResult<Stream?>(stream);
        }
    }
}
