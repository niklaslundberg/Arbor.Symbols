using Arbor.Symbols.ConsoleClient;
using Arbor.Symbols.Core;

namespace Arbor.Symbols.UnitTests;

public class SymbolRequestScannerTests
{
    [Fact]
    public void CollectRequests_WithDefaultPatterns_FindsAssembly()
    {
        var scanDirectory = CreateScanDirectoryWithRealAssembly();

        try
        {
            var requests = SymbolRequestScanner.CollectRequests(scanDirectory, [], []);

            var assemblyFileName = Path.GetFileName(typeof(SymbolResourcePathHelper).Assembly.Location);
            requests.Should().Contain(r => r.RequestedFileName == assemblyFileName);
        }
        finally
        {
            Directory.Delete(scanDirectory, recursive: true);
        }
    }

    [Fact]
    public void CollectRequests_WithExcludePattern_RemovesMatchingFiles()
    {
        var scanDirectory = CreateScanDirectoryWithRealAssembly();

        try
        {
            var requests = SymbolRequestScanner.CollectRequests(scanDirectory, [], ["**/*.dll"]);

            requests.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(scanDirectory, recursive: true);
        }
    }

    [Fact]
    public void CollectRequests_WithIncludePattern_RestrictsToMatchingFiles()
    {
        var scanDirectory = CreateScanDirectoryWithRealAssembly();

        try
        {
            var requests = SymbolRequestScanner.CollectRequests(scanDirectory, ["**/*.pdb"], []);

            requests.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(scanDirectory, recursive: true);
        }
    }

    private static string CreateScanDirectoryWithRealAssembly()
    {
        var scanDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scanDirectory);

        var sourceAssemblyPath = typeof(SymbolResourcePathHelper).Assembly.Location;
        var destinationAssemblyPath = Path.Combine(scanDirectory, Path.GetFileName(sourceAssemblyPath));
        File.Copy(sourceAssemblyPath, destinationAssemblyPath);

        return scanDirectory;
    }
}
