using Arbor.Symbols.Core;

namespace Arbor.Symbols.UnitTests;

public class SymbolResourcePathHelperTests
{
    [Fact]
    public void TryCreateAssemblyRequest_ForBuiltAssembly_ReturnsExpectedFileName()
    {
        var assemblyPath = typeof(SymbolResourcePathHelper).Assembly.Location;

        var result = SymbolResourcePathHelper.TryCreateAssemblyRequest(assemblyPath, out var request);

        result.Should().BeTrue();
        request.RequestedFileName.Should().Be(Path.GetFileName(assemblyPath));
        request.ResourceFileName.Should().Be(request.RequestedFileName);
        request.Identifier.Should().NotBeEmpty();
    }

    [Fact]
    public void GetCachePath_BuildsUnderRootDirectory()
    {
        var request = new SymbolResourceRequest("MyLib.pdb", "ABC123", "MyLib.pdb");
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var path = SymbolResourcePathHelper.GetCachePath(root, request);

        Path.GetFullPath(path).Should().StartWithEquivalentOf(Path.GetFullPath(root));
    }
}
