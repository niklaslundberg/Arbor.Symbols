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

    [Theory]
    [InlineData("Sub/MyLib.pdb", "ABC123", "MyLib.pdb")]
    [InlineData("MyLib.pdb", "ABC/123", "MyLib.pdb")]
    [InlineData("MyLib.pdb", "ABC123", "Sub/MyLib.pdb")]
    public void GetCachePath_SegmentContainsPathSeparator_Throws(string requestedFileName, string identifier, string resourceFileName)
    {
        var request = new SymbolResourceRequest(requestedFileName, identifier, resourceFileName);
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var act = () => SymbolResourcePathHelper.GetCachePath(root, request);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("..", "ABC123", "MyLib.pdb")]
    [InlineData("MyLib.pdb", "..", "MyLib.pdb")]
    [InlineData("MyLib.pdb", "ABC123", "..")]
    public void GetCachePath_SegmentResolvesOutsideCacheRoot_ThrowsEvenWithoutASeparatorCharacter(
        string requestedFileName, string identifier, string resourceFileName)
    {
        // ".." contains no path-separator character, so it slips past the separator
        // check; the root-escape check must still catch it once the path is resolved.
        var request = new SymbolResourceRequest(requestedFileName, identifier, resourceFileName);
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var act = () => SymbolResourcePathHelper.GetCachePath(root, request);

        act.Should().Throw<InvalidOperationException>();
    }
}
