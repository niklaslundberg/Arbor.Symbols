using System.Diagnostics;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;

namespace Arbor.Symbols.Server;

public sealed class IlSpySymbolGenerator(ILogger<IlSpySymbolGenerator> logger) : IIlSpySymbolGenerator
{
    private static readonly TimeSpan SlowGenerationThreshold = TimeSpan.FromSeconds(5);

    public Task<bool> TryGeneratePdbAsync(string assemblyPath, string outputPdbPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var file = new PEFile(assemblyPath);

        if (!PortablePdbWriter.HasCodeViewDebugDirectoryEntry(file))
        {
            logger.LogDebug("Skipping PDB generation for {AssemblyPath}: no CodeView debug directory entry", assemblyPath);
            return Task.FromResult(false);
        }

        logger.LogInformation("Starting PDB generation for {AssemblyPath}", assemblyPath);

        var targetFrameworkId = file.DetectTargetFrameworkId();
        var resolver = new UniversalAssemblyResolver(assemblyPath, throwOnError: false, targetFrameworkId);
        var decompilerSettings = new DecompilerSettings(LanguageVersion.Latest);

        var decompiler = new CSharpDecompiler(file, resolver, decompilerSettings);

        var outputDirectory = Path.GetDirectoryName(outputPdbPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var stopwatch = Stopwatch.StartNew();
        using var stream = File.Create(outputPdbPath);
        PortablePdbWriter.WritePdb(file, decompiler, decompilerSettings, stream);
        stopwatch.Stop();

        if (stopwatch.Elapsed >= SlowGenerationThreshold)
        {
            logger.LogWarning(
                "PDB generation for {AssemblyPath} completed in {ElapsedMs}ms (exceeded {ThresholdMs}ms threshold)",
                assemblyPath,
                stopwatch.ElapsedMilliseconds,
                (long)SlowGenerationThreshold.TotalMilliseconds);
        }
        else
        {
            logger.LogInformation(
                "PDB generation for {AssemblyPath} completed in {ElapsedMs}ms",
                assemblyPath,
                stopwatch.ElapsedMilliseconds);
        }

        return Task.FromResult(true);
    }
}
