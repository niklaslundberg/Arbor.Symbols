using System.Runtime.InteropServices;

namespace Arbor.Symbols.Core;

public static class SymbolCacheLocator
{
    public static string GetDefaultVisualStudioSymbolCacheDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Temp", "SymbolCache");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".vs", "symbols");
    }
}
