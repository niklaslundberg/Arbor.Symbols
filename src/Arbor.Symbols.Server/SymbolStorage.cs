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

    public IReadOnlyList<CachedSymbolEntry> GetCachedSymbols()
    {
        var cacheDir = _options.CacheDirectory;
        if (!Directory.Exists(cacheDir))
        {
            return [];
        }

        var entries = new List<CachedSymbolEntry>();
        foreach (var fileNameDir in Directory.GetDirectories(cacheDir))
        {
            foreach (var identifierDir in Directory.GetDirectories(fileNameDir))
            {
                foreach (var filePath in Directory.GetFiles(identifierDir))
                {
                    var info = new FileInfo(filePath);
                    entries.Add(new CachedSymbolEntry(
                        Path.GetFileName(fileNameDir),
                        Path.GetFileName(identifierDir),
                        Path.GetFileName(filePath),
                        info.Length,
                        info.LastWriteTimeUtc));
                }
            }
        }

        return entries;
    }

    public long GetDiskUsageBytes()
    {
        var cacheDir = _options.CacheDirectory;
        if (!Directory.Exists(cacheDir))
        {
            return 0;
        }

        return Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    public bool TryDelete(SymbolResourceRequest request)
    {
        var path = GetPath(request);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);

        var identifierDir = Path.GetDirectoryName(path)!;
        if (Directory.Exists(identifierDir) && Directory.GetFileSystemEntries(identifierDir).Length == 0)
        {
            Directory.Delete(identifierDir);

            var fileNameDir = Path.GetDirectoryName(identifierDir)!;
            if (Directory.Exists(fileNameDir) && Directory.GetFileSystemEntries(fileNameDir).Length == 0)
            {
                Directory.Delete(fileNameDir);
            }
        }

        return true;
    }
}
