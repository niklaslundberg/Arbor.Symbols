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
    [InlineData(".", "ABC123", "MyLib.pdb")]
    [InlineData("MyLib.pdb", ".", "MyLib.pdb")]
    [InlineData("MyLib.pdb", "ABC123", ".")]
    public void GetCachePath_SegmentIsDotOrDotDot_Throws(string requestedFileName, string identifier, string resourceFileName)
    {
        // "." and ".." contain no path-separator character, so they slip past the
        // separator check above. They're rejected explicitly rather than relying on
        // the root-escape check below: only the *first* segment being ".." actually
        // walks the resolved path above the root — ".." in the identifier or
        // resourceFileName position just cancels the segment before it and still
        // resolves under the root, which would otherwise let it slip through despite
        // aliasing a shorter, unintended cache path.
        var request = new SymbolResourceRequest(requestedFileName, identifier, resourceFileName);
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var act = () => SymbolResourcePathHelper.GetCachePath(root, request);

        act.Should().Throw<InvalidOperationException>();
    }
}
