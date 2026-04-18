using Arbor.Symbols.Core;
using Serilog;

if (args.Length == 0)
{
    Console.WriteLine("Usage: Arbor.Symbols.ConsoleClient <scan-directory> [--server <url>] [--symbol-cache <path>]");
    return 1;
}

var scanDirectory = args[0];
if (!Directory.Exists(scanDirectory))
{
    Console.Error.WriteLine($"Directory '{scanDirectory}' does not exist.");
    return 2;
}

var serverUrl = "http://localhost:5000";
var cacheDirectory = SymbolCacheLocator.GetDefaultVisualStudioSymbolCacheDirectory();

for (var index = 1; index < args.Length; index++)
{
    if (string.Equals(args[index], "--server", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
    {
        serverUrl = args[++index];
        continue;
    }

    if (string.Equals(args[index], "--symbol-cache", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
    {
        cacheDirectory = args[++index];
    }
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var requests = CollectRequests(scanDirectory);
    Directory.CreateDirectory(cacheDirectory);

    using var httpClient = new HttpClient { BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/") };

    var downloaded = 0;
    var failed = 0;

    foreach (var request in requests)
    {
        var relativeUri = SymbolResourcePathHelper.BuildRelativeUri(request);
        var destinationPath = SymbolResourcePathHelper.GetCachePath(cacheDirectory, request);

        if (File.Exists(destinationPath))
        {
            continue;
        }

        var response = await httpClient.GetAsync(relativeUri);
        if (!response.IsSuccessStatusCode)
        {
            failed++;
            Log.Warning("Failed to fetch {RelativePath} ({StatusCode})", request.RelativePath, response.StatusCode);
            continue;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        await using var destination = File.Create(destinationPath);
        await using var source = await response.Content.ReadAsStreamAsync();
        await source.CopyToAsync(destination);

        downloaded++;
        Log.Information("Downloaded {RelativePath}", request.RelativePath);
    }

    Log.Information("Completed preload. Downloaded: {Downloaded}. Failed: {Failed}.", downloaded, failed);
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;

static IReadOnlyCollection<SymbolResourceRequest> CollectRequests(string scanDirectory)
{
    var requests = new Dictionary<string, SymbolResourceRequest>(StringComparer.OrdinalIgnoreCase);

    foreach (var assemblyPath in Directory.EnumerateFiles(scanDirectory, "*.dll", SearchOption.AllDirectories)
             .Concat(Directory.EnumerateFiles(scanDirectory, "*.exe", SearchOption.AllDirectories)))
    {
        if (SymbolResourcePathHelper.TryCreateAssemblyRequest(assemblyPath, out var assemblyRequest))
        {
            requests[assemblyRequest.RelativePath] = assemblyRequest;
        }

        if (SymbolResourcePathHelper.TryCreateAssociatedPdbRequest(assemblyPath, out var associatedPdbRequest))
        {
            requests[associatedPdbRequest.RelativePath] = associatedPdbRequest;
        }
    }

    foreach (var pdbPath in Directory.EnumerateFiles(scanDirectory, "*.pdb", SearchOption.AllDirectories))
    {
        if (SymbolResourcePathHelper.TryCreatePortablePdbRequest(pdbPath, out var pdbRequest))
        {
            requests[pdbRequest.RelativePath] = pdbRequest;
        }
    }

    return requests.Values.ToArray();
}
