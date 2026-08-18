using Arbor.Symbols.Core;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Arbor.Symbols.ConsoleClient;

public static class SymbolRequestScanner
{
    public static readonly IReadOnlyList<string> DefaultIncludePatterns = ["**/*.dll", "**/*.exe", "**/*.pdb"];

    public static IReadOnlyCollection<SymbolResourceRequest> CollectRequests(
        string scanDirectory,
        IReadOnlyList<string> includePatterns,
        IReadOnlyList<string> excludePatterns)
    {
        var matcher = new Matcher();
        matcher.AddIncludePatterns(includePatterns.Count > 0 ? includePatterns : DefaultIncludePatterns);

        if (excludePatterns.Count > 0)
        {
            matcher.AddExcludePatterns(excludePatterns);
        }

        var matchResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(scanDirectory)));

        var requests = new Dictionary<string, SymbolResourceRequest>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in matchResult.Files)
        {
            var fullPath = Path.Combine(scanDirectory, match.Path);
            var extension = Path.GetExtension(fullPath);

            if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (SymbolResourcePathHelper.TryCreateAssemblyRequest(fullPath, out var assemblyRequest))
                {
                    requests[assemblyRequest.RelativePath] = assemblyRequest;
                }

                if (SymbolResourcePathHelper.TryCreateAssociatedPdbRequest(fullPath, out var associatedPdbRequest))
                {
                    requests[associatedPdbRequest.RelativePath] = associatedPdbRequest;
                }
            }
            else if (extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase) &&
                     SymbolResourcePathHelper.TryCreatePortablePdbRequest(fullPath, out var pdbRequest))
            {
                requests[pdbRequest.RelativePath] = pdbRequest;
            }
        }

        return requests.Values.ToArray();
    }
}
