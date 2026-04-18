using Arbor.Symbols.Core;

namespace Arbor.Symbols.Server;

public sealed class SymbolServerOptions
{
    public const string SectionName = "SymbolServer";

    public string OfficialSymbolServerBaseUrl { get; set; } = "https://msdl.microsoft.com/download/symbols/";

    public string CacheDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "symbol-cache");

    public string[] AssemblySearchDirectories { get; set; } = [SymbolCacheLocator.GetDefaultVisualStudioSymbolCacheDirectory()];
}
