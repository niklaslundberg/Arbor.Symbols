using Arbor.Symbols.Core;

namespace Arbor.Symbols.Server;

public interface IOfficialSymbolClient
{
    Task<Stream?> TryDownloadAsync(SymbolResourceRequest request, CancellationToken cancellationToken);
}
