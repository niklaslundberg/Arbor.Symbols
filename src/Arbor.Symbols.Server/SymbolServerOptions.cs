using Arbor.Symbols.Core;

namespace Arbor.Symbols.Server;

public sealed class SymbolServerOptions
{
    public const string SectionName = "SymbolServer";

    public string OfficialSymbolServerBaseUrl { get; set; } = "https://msdl.microsoft.com/download/symbols/";

    public string CacheDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "symbol-cache");

    public string[] AssemblySearchDirectories { get; set; } = [SymbolCacheLocator.GetDefaultVisualStudioSymbolCacheDirectory()];

    /// <summary>
    /// Caps how many ILSpy PDB-generation requests (CPU-bound, can take seconds each)
    /// run at once, so a burst of uncached-PDB requests can't exhaust the thread pool.
    /// </summary>
    public int MaxConcurrentIlSpyGenerations { get; set; } = 2;
}
