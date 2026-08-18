using Arbor.Symbols.Core;

namespace Arbor.Symbols.Server;

public interface ISymbolStorage
{
    string GetPath(SymbolResourceRequest request);

    bool TryOpenRead(SymbolResourceRequest request, out Stream stream);

    Task SaveAsync(SymbolResourceRequest request, Stream source, CancellationToken cancellationToken);

    IReadOnlyList<CachedSymbolEntry> GetCachedSymbols();

    long GetDiskUsageBytes();

    bool TryDelete(SymbolResourceRequest request);
}
