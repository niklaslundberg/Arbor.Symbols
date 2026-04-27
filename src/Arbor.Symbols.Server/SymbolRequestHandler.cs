using Arbor.Symbols.Core;
using Microsoft.Extensions.Options;

namespace Arbor.Symbols.Server;

public sealed class SymbolRequestHandler
{
    private readonly SymbolStorage _storage;
    private readonly IOfficialSymbolClient _officialSymbolClient;
    private readonly IIlSpySymbolGenerator _ilSpySymbolGenerator;
    private readonly SymbolServerOptions _options;
    private readonly ILogger<SymbolRequestHandler> _logger;
    private readonly SymbolServerStatistics _statistics;

    public SymbolRequestHandler(
        SymbolStorage storage,
        IOfficialSymbolClient officialSymbolClient,
        IIlSpySymbolGenerator ilSpySymbolGenerator,
        IOptions<SymbolServerOptions> options,
        ILogger<SymbolRequestHandler> logger,
        SymbolServerStatistics statistics)
    {
        _storage = storage;
        _officialSymbolClient = officialSymbolClient;
        _ilSpySymbolGenerator = ilSpySymbolGenerator;
        _options = options.Value;
        _logger = logger;
        _statistics = statistics;
    }

    public async Task<IResult> HandleAsync(string requestedFileName, string identifier, string resourceFileName, CancellationToken cancellationToken)
    {
        var request = new SymbolResourceRequest(requestedFileName, identifier, resourceFileName);

        if (_storage.TryOpenRead(request, out var cachedStream))
        {
            _logger.LogInformation("Serving cached symbol {RelativePath}", request.RelativePath);
            _statistics.RecordCacheHit();
            return Results.Stream(cachedStream, GetContentType(resourceFileName));
        }

        await using var officialStream = await _officialSymbolClient.TryDownloadAsync(request, cancellationToken);
        if (officialStream is not null)
        {
            await using var memoryStream = new MemoryStream();
            await officialStream.CopyToAsync(memoryStream, cancellationToken);
            var bytes = memoryStream.ToArray();
            await using var copySource = new MemoryStream(bytes);
            await _storage.SaveAsync(request, copySource, cancellationToken);

            _logger.LogInformation("Downloaded symbol from Microsoft symbol server {RelativePath}", request.RelativePath);
            _statistics.RecordOfficialDownload();
            return Results.File(bytes, GetContentType(resourceFileName));
        }

        if (resourceFileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
            await TryGenerateAndStorePdbAsync(request, cancellationToken))
        {
            if (_storage.TryOpenRead(request, out var generatedPdbStream))
            {
                _logger.LogInformation("Generated symbol using ILSpy {RelativePath}", request.RelativePath);
                _statistics.RecordIlSpyGeneration();
                return Results.Stream(generatedPdbStream, GetContentType(resourceFileName));
            }
        }

        _logger.LogWarning("Symbol request not found {RelativePath}", request.RelativePath);
        _statistics.RecordNotFound();
        return Results.NotFound();
    }

    private async Task<bool> TryGenerateAndStorePdbAsync(SymbolResourceRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Attempting ILSpy PDB generation for {RelativePath} across {DirectoryCount} search director(ies)",
            request.RelativePath, _options.AssemblySearchDirectories.Length);

        foreach (var directory in _options.AssemblySearchDirectories)
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogDebug("Assembly search directory does not exist, skipping: {Directory}", directory);
                continue;
            }

            var baseName = Path.GetFileNameWithoutExtension(request.ResourceFileName);

            foreach (var extension in new[] { ".dll", ".exe" })
            {
                var candidateAssemblyPath = Path.Combine(directory, baseName + extension);

                if (!File.Exists(candidateAssemblyPath))
                {
                    continue;
                }

                _logger.LogDebug("Found candidate assembly {CandidatePath}, checking identifier match", candidateAssemblyPath);

                if (!SymbolResourcePathHelper.TryCreateAssociatedPdbRequest(candidateAssemblyPath, out var pdbRequest))
                {
                    _logger.LogDebug("Could not read PDB identifier from {CandidatePath}, skipping", candidateAssemblyPath);
                    continue;
                }

                if (!string.Equals(pdbRequest.Identifier, request.Identifier, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(pdbRequest.RequestedFileName, request.RequestedFileName, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "Identifier mismatch for {CandidatePath}: expected {ExpectedId}/{ExpectedName}, found {ActualId}/{ActualName}",
                        candidateAssemblyPath,
                        request.Identifier, request.RequestedFileName,
                        pdbRequest.Identifier, pdbRequest.RequestedFileName);
                    continue;
                }

                _logger.LogInformation("Matched assembly {CandidatePath} for {RelativePath}, generating PDB via ILSpy",
                    candidateAssemblyPath, request.RelativePath);

                var destinationPath = _storage.GetPath(request);
                var generated = await _ilSpySymbolGenerator.TryGeneratePdbAsync(candidateAssemblyPath, destinationPath, cancellationToken);
                if (generated)
                {
                    return true;
                }

                _logger.LogWarning("ILSpy PDB generation failed for {CandidatePath}", candidateAssemblyPath);
            }
        }

        _logger.LogDebug("No matching assembly found for ILSpy PDB generation of {RelativePath}", request.RelativePath);
        return false;
    }

    private static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).Equals(".pdb", StringComparison.OrdinalIgnoreCase)
            ? "application/octet-stream"
            : "application/x-msdownload";
    }
}
