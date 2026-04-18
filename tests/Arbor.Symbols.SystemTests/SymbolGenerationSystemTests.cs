using System.Reflection.Metadata;
using Arbor.Symbols.Core;
using Arbor.Symbols.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Arbor.Symbols.SystemTests;

public class SymbolGenerationSystemTests
{
    /// <summary>
    /// Exercises the full ILSpy PDB generation pipeline by pointing the server at the
    /// Arbor.Symbols.Core assembly that is already present in the test output directory.
    /// Verifies that the response body is a valid portable PDB with at least one source document.
    /// </summary>
    [Fact]
    public async Task IlSpyGeneratedPdb_IsReturnedAndContainsSourceDocuments()
    {
        var assemblyPath = typeof(SymbolResourceRequest).Assembly.Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath)!;

        if (!SymbolResourcePathHelper.TryCreateAssociatedPdbRequest(assemblyPath, out var request))
        {
            Assert.Skip("Assembly does not have a CodeView debug directory entry; ILSpy generation cannot be tested.");
            return;
        }

        var cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            await using var factory = new SystemTestWebApplicationFactory(cacheRoot, assemblyDirectory);
            using var client = factory.CreateClient();

            var response = await client.GetAsync($"/{request.RelativePath}", TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            bytes.Should().NotBeEmpty();

            // Verify the response body is a valid portable PDB with source documents
            using var pdbStream = new MemoryStream(bytes);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
            var reader = provider.GetMetadataReader();
            reader.Documents.Count.Should().BePositive("a generated PDB must reference at least one source document");
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that a PDB served via ILSpy generation is cached on disk and that a
    /// second request returns identical content from the cache.
    /// </summary>
    [Fact]
    public async Task IlSpyGeneratedPdb_IsCachedAfterFirstRequest()
    {
        var assemblyPath = typeof(SymbolResourceRequest).Assembly.Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath)!;

        if (!SymbolResourcePathHelper.TryCreateAssociatedPdbRequest(assemblyPath, out var request))
        {
            Assert.Skip("Assembly does not have a CodeView debug directory entry; ILSpy generation cannot be tested.");
            return;
        }

        var cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            await using var factory = new SystemTestWebApplicationFactory(cacheRoot, assemblyDirectory);
            using var client = factory.CreateClient();

            var url = $"/{request.RelativePath}";
            var ct = TestContext.Current.CancellationToken;

            var firstResponse = await client.GetAsync(url, ct);
            firstResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            var cachePath = SymbolResourcePathHelper.GetCachePath(cacheRoot, request);
            File.Exists(cachePath).Should().BeTrue("generated PDB must be saved to cache after first request");

            // Second request should be served from cache with identical content
            var secondResponse = await client.GetAsync(url, ct);
            secondResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

            var firstBytes = await firstResponse.Content.ReadAsByteArrayAsync(ct);
            var secondBytes = await secondResponse.Content.ReadAsByteArrayAsync(ct);
            secondBytes.Should().Equal(firstBytes, "cached PDB must match the originally generated PDB");
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    private sealed class SystemTestWebApplicationFactory(string cacheDirectory, string assemblyDirectory)
        : WebApplicationFactory<Program>
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
                    AssemblySearchDirectories = [assemblyDirectory]
                };

                services.AddSingleton<IOptions<SymbolServerOptions>>(new OptionsWrapper<SymbolServerOptions>(options));
                services.AddSingleton(new SymbolStorage(options));

                // Return null for all requests so the handler falls through to ILSpy generation
                services.AddSingleton<IOfficialSymbolClient, AlwaysNotFoundSymbolClient>();
            });
        }
    }

    private sealed class AlwaysNotFoundSymbolClient : IOfficialSymbolClient
    {
        public Task<Stream?> TryDownloadAsync(SymbolResourceRequest request, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(null);
    }
}
