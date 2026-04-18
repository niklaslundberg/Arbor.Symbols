using Arbor.Symbols.Core;

namespace Arbor.Symbols.UnitTests;

public class SymbolResourcePathHelperTests
{
    [Fact]
    public void TryCreateAssemblyRequest_ForBuiltAssembly_ReturnsExpectedFileName()
    {
        var assemblyPath = typeof(SymbolResourcePathHelper).Assembly.Location;

        var result = SymbolResourcePathHelper.TryCreateAssemblyRequest(assemblyPath, out var request);

        Assert.True(result);
        Assert.Equal(Path.GetFileName(assemblyPath), request.RequestedFileName);
        Assert.Equal(request.RequestedFileName, request.ResourceFileName);
        Assert.NotEmpty(request.Identifier);
    }

    [Fact]
    public void GetCachePath_BuildsUnderRootDirectory()
    {
        var request = new SymbolResourceRequest("MyLib.pdb", "ABC123", "MyLib.pdb");
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var path = SymbolResourcePathHelper.GetCachePath(root, request);

        Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
    }
}
