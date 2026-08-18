using Arbor.Symbols.Core;

namespace Arbor.Symbols.Server;

public sealed class SymbolStorage : ISymbolStorage
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

        // Write to a temp file and rename into place so concurrent requests for the
        // same missing symbol (or a reader hitting TryOpenRead mid-write) can never
        // observe a truncated/interleaved file at the final path.
        var temporaryPath = Path.Combine(directory ?? string.Empty, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var destination = File.Create(temporaryPath))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public IReadOnlyList<CachedSymbolEntry> GetCachedSymbols()
    {
        var cacheDir = _options.CacheDirectory;
        if (!Directory.Exists(cacheDir))
        {
            return [];
        }

        var entries = new List<CachedSymbolEntry>();
        try
        {
            foreach (var fileNameDir in Directory.EnumerateDirectories(cacheDir))
            {
                try
                {
                    foreach (var identifierDir in Directory.EnumerateDirectories(fileNameDir))
                    {
                        try
                        {
                            foreach (var filePath in Directory.EnumerateFiles(identifierDir))
                            {
                                try
                                {
                                    var info = new FileInfo(filePath);
                                    entries.Add(new CachedSymbolEntry(
                                        Path.GetFileName(fileNameDir),
                                        Path.GetFileName(identifierDir),
                                        Path.GetFileName(filePath),
                                        info.Length,
                                        info.LastWriteTimeUtc));
                                }
                                catch (FileNotFoundException) { }
                                catch (IOException) { }
                                catch (UnauthorizedAccessException) { }
                            }
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return entries;
    }

    public long GetDiskUsageBytes()
    {
        var cacheDir = _options.CacheDirectory;
        if (!Directory.Exists(cacheDir))
        {
            return 0;
        }

        long total = 0;
        foreach (var f in Directory.EnumerateFiles(cacheDir, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(f).Length;
            }
            catch (FileNotFoundException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return total;
    }

    public bool TryDelete(SymbolResourceRequest request)
    {
        var path = GetPath(request);

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        var identifierDir = Path.GetDirectoryName(path)!;
        if (TryDeleteEmptyDirectory(identifierDir))
        {
            var fileNameDir = Path.GetDirectoryName(identifierDir)!;
            TryDeleteEmptyDirectory(fileNameDir);
        }

        return true;
    }

    private static bool TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path) || Directory.GetFileSystemEntries(path).Length != 0)
            {
                return false;
            }

            Directory.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
