using Arbor.Symbols.Core;

namespace Arbor.Symbols.Server;

public sealed class SymbolStorage
{
    private readonly SymbolServerOptions _options;

    public SymbolStorage(SymbolServerOptions options)
    {
        _options = options;
    }

    public string GetPath(SymbolResourceRequest request) => SymbolResourcePathHelper.GetCachePath(_options.CacheDirectory, request);

    public bool TryOpenRead(SymbolResourceRequest request, out Stream stream)
    {
        var path = GetPath(request);
        if (!File.Exists(path))
        {
            stream = default!;
            return false;
        }

        stream = File.OpenRead(path);
        return true;
    }

    public async Task SaveAsync(SymbolResourceRequest request, Stream source, CancellationToken cancellationToken)
    {
        var path = GetPath(request);
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var destination = File.Create(path);
        await source.CopyToAsync(destination, cancellationToken);
    }
}
