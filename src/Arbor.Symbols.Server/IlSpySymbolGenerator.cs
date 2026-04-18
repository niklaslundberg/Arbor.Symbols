using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;

namespace Arbor.Symbols.Server;

public sealed class IlSpySymbolGenerator : IIlSpySymbolGenerator
{
    public Task<bool> TryGeneratePdbAsync(string assemblyPath, string outputPdbPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var file = new PEFile(assemblyPath);

        if (!PortablePdbWriter.HasCodeViewDebugDirectoryEntry(file))
        {
            return Task.FromResult(false);
        }

        var targetFrameworkId = file.DetectTargetFrameworkId();
        var resolver = new UniversalAssemblyResolver(assemblyPath, throwOnError: false, targetFrameworkId);
        var decompilerSettings = new DecompilerSettings(LanguageVersion.Latest);

        var decompiler = new CSharpDecompiler(file, resolver, decompilerSettings);

        var outputDirectory = Path.GetDirectoryName(outputPdbPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        using var stream = File.Create(outputPdbPath);
        PortablePdbWriter.WritePdb(file, decompiler, decompilerSettings, stream);

        return Task.FromResult(true);
    }
}
