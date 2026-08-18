using System.Diagnostics;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.Extensions.Options;

namespace Arbor.Symbols.Server;

public sealed class IlSpySymbolGenerator : IIlSpySymbolGenerator, IDisposable
{
    private static readonly TimeSpan SlowGenerationThreshold = TimeSpan.FromSeconds(5);

    private readonly ILogger<IlSpySymbolGenerator> _logger;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public IlSpySymbolGenerator(ILogger<IlSpySymbolGenerator> logger, IOptions<SymbolServerOptions> options)
    {
        _logger = logger;
        var maxConcurrentGenerations = Math.Max(1, options.Value.MaxConcurrentIlSpyGenerations);
        _concurrencyLimiter = new SemaphoreSlim(maxConcurrentGenerations, maxConcurrentGenerations);
    }

    public async Task<bool> TryGeneratePdbAsync(string assemblyPath, string outputPdbPath, CancellationToken cancellationToken)
    {
        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => GeneratePdb(assemblyPath, outputPdbPath, cancellationToken), cancellationToken);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    public void Dispose() => _concurrencyLimiter.Dispose();

    private bool GeneratePdb(string assemblyPath, string outputPdbPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var file = new PEFile(assemblyPath);

        if (!PortablePdbWriter.HasCodeViewDebugDirectoryEntry(file))
        {
            _logger.LogDebug("Skipping PDB generation for {AssemblyPath}: no CodeView debug directory entry", assemblyPath);
            return false;
        }

        _logger.LogInformation("Starting PDB generation for {AssemblyPath}", assemblyPath);

        var targetFrameworkId = file.DetectTargetFrameworkId();
        var resolver = new UniversalAssemblyResolver(assemblyPath, throwOnError: false, targetFrameworkId);
        var decompilerSettings = new DecompilerSettings(LanguageVersion.Latest);

        var decompiler = new CSharpDecompiler(file, resolver, decompilerSettings);

        var outputDirectory = Path.GetDirectoryName(outputPdbPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // Write to a temp file and rename into place so two concurrent generation
        // requests for the same missing PDB (or a reader hitting the cache mid-write)
        // can never observe a truncated/interleaved file at the final path.
        var temporaryPath = Path.Combine(outputDirectory ?? string.Empty, $"{Path.GetFileName(outputPdbPath)}.{Guid.NewGuid():N}.tmp");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                new PortablePdbWriter().WritePdb(file, decompiler, decompilerSettings, stream);
            }

            File.Move(temporaryPath, outputPdbPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        stopwatch.Stop();

        if (stopwatch.Elapsed >= SlowGenerationThreshold)
        {
            _logger.LogWarning(
                "PDB generation for {AssemblyPath} completed in {ElapsedMs}ms (exceeded {ThresholdMs}ms threshold)",
                assemblyPath,
                stopwatch.ElapsedMilliseconds,
                (long)SlowGenerationThreshold.TotalMilliseconds);
        }
        else
        {
            _logger.LogInformation(
                "PDB generation for {AssemblyPath} completed in {ElapsedMs}ms",
                assemblyPath,
                stopwatch.ElapsedMilliseconds);
        }

        return true;
    }
}
