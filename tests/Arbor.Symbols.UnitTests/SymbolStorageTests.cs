using Arbor.Symbols.Core;
using Arbor.Symbols.Server;

namespace Arbor.Symbols.UnitTests;

public class SymbolStorageTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }

    private SymbolStorage CreateStorage() => new(new SymbolServerOptions { CacheDirectory = _cacheRoot });

    [Fact]
    public void GetCachedSymbols_EmptyCache_ReturnsEmpty()
    {
        var storage = CreateStorage();

        storage.GetCachedSymbols().Should().BeEmpty();
    }

    [Fact]
    public async Task GetCachedSymbols_AfterSave_ReturnsMatchingEntry()
    {
        var storage = CreateStorage();
        var request = new SymbolResourceRequest("MyLib.pdb", "ABC123", "MyLib.pdb");

        await storage.SaveAsync(request, new MemoryStream("hello"u8.ToArray()), CancellationToken.None);

        var entries = storage.GetCachedSymbols();

        entries.Should().ContainSingle();
        var entry = entries[0];
        entry.RequestedFileName.Should().Be("MyLib.pdb");
        entry.Identifier.Should().Be("ABC123");
        entry.ResourceFileName.Should().Be("MyLib.pdb");
        entry.SizeBytes.Should().Be(5);
    }

    [Fact]
    public void GetDiskUsageBytes_EmptyCache_ReturnsZero()
    {
        var storage = CreateStorage();

        storage.GetDiskUsageBytes().Should().Be(0);
    }

    [Fact]
    public async Task GetDiskUsageBytes_SumsAllCachedFileSizes()
    {
        var storage = CreateStorage();

        await storage.SaveAsync(new SymbolResourceRequest("A.pdb", "1", "A.pdb"), new MemoryStream(new byte[10]), CancellationToken.None);
        await storage.SaveAsync(new SymbolResourceRequest("B.pdb", "2", "B.pdb"), new MemoryStream(new byte[25]), CancellationToken.None);

        storage.GetDiskUsageBytes().Should().Be(35);
    }

    [Fact]
    public void TryDelete_MissingEntry_ReturnsFalse()
    {
        var storage = CreateStorage();
        var request = new SymbolResourceRequest("Missing.pdb", "ABC123", "Missing.pdb");

        storage.TryDelete(request).Should().BeFalse();
    }

    [Fact]
    public async Task TryDelete_PresentEntry_RemovesFileAndReturnsTrue()
    {
        var storage = CreateStorage();
        var request = new SymbolResourceRequest("MyLib.pdb", "ABC123", "MyLib.pdb");
        await storage.SaveAsync(request, new MemoryStream("hello"u8.ToArray()), CancellationToken.None);
        var path = storage.GetPath(request);

        storage.TryDelete(request).Should().BeTrue();

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task TryDelete_LastEntryInDirectory_CleansUpEmptyIdentifierAndFileNameDirectories()
    {
        var storage = CreateStorage();
        var request = new SymbolResourceRequest("MyLib.pdb", "ABC123", "MyLib.pdb");
        await storage.SaveAsync(request, new MemoryStream("hello"u8.ToArray()), CancellationToken.None);
        var path = storage.GetPath(request);
        var identifierDir = Path.GetDirectoryName(path)!;
        var fileNameDir = Path.GetDirectoryName(identifierDir)!;

        storage.TryDelete(request).Should().BeTrue();

        Directory.Exists(identifierDir).Should().BeFalse();
        Directory.Exists(fileNameDir).Should().BeFalse();
    }

    [Fact]
    public async Task TryDelete_SiblingEntryRemainsInFileNameDirectory_LeavesFileNameDirectoryInPlace()
    {
        var storage = CreateStorage();
        var deleted = new SymbolResourceRequest("MyLib.pdb", "ABC123", "MyLib.pdb");
        var sibling = new SymbolResourceRequest("MyLib.pdb", "DEF456", "MyLib.pdb");
        await storage.SaveAsync(deleted, new MemoryStream("hello"u8.ToArray()), CancellationToken.None);
        await storage.SaveAsync(sibling, new MemoryStream("world"u8.ToArray()), CancellationToken.None);
        var fileNameDir = Path.GetDirectoryName(Path.GetDirectoryName(storage.GetPath(deleted)))!;

        storage.TryDelete(deleted).Should().BeTrue();

        Directory.Exists(fileNameDir).Should().BeTrue();
        File.Exists(storage.GetPath(sibling)).Should().BeTrue();
    }
}
