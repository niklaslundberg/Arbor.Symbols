using Arbor.Symbols.ConsoleClient;
using Arbor.Symbols.Core;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

var parseResult = CliOptions.Parse(args, "http://localhost:5000", SymbolCacheLocator.GetDefaultVisualStudioSymbolCacheDirectory());

if (parseResult.HelpRequested)
{
    PrintHelp();
    return 0;
}

if (!parseResult.IsSuccess)
{
    Console.Error.WriteLine(parseResult.ErrorMessage);
    Console.Error.WriteLine();
    PrintHelp();
    return 1;
}

var options = parseResult.Options!;

if (!Directory.Exists(options.ScanDirectory))
{
    Console.Error.WriteLine($"Directory '{options.ScanDirectory}' does not exist.");
    return 2;
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

using var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    Log.Warning("Cancellation requested, finishing in-flight downloads...");
    cancellationTokenSource.Cancel();
};

try
{
    var requests = SymbolRequestScanner.CollectRequests(options.ScanDirectory, options.IncludePatterns, options.ExcludePatterns);
    Log.Information("Found {Count} symbol artifact(s) to consider under {ScanDirectory}", requests.Count, options.ScanDirectory);

    Directory.CreateDirectory(options.CacheDirectory);

    var services = new ServiceCollection();
    services.AddHttpClient("SymbolServer", client => client.BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/"))
        .AddStandardResilienceHandler();

    await using var serviceProvider = services.BuildServiceProvider();
    var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("SymbolServer");

    var result = await SymbolPrefetcher.RunAsync(
        httpClient,
        requests,
        options.CacheDirectory,
        options.Force,
        options.DryRun,
        options.MaxConcurrency,
        cancellationTokenSource.Token);

    var verb = options.DryRun ? "Would download" : "Downloaded";
    Log.Information(
        "Completed preload. {Verb}: {Downloaded}. Skipped (cached): {Skipped}. Failed: {Failed}.",
        verb, result.Downloaded, result.Skipped, result.Failed);

    return result.Failed > 0 ? 3 : 0;
}
catch (OperationCanceledException)
{
    Log.Warning("Preload cancelled.");
    return 130;
}
finally
{
    await Log.CloseAndFlushAsync();
}

void PrintHelp()
{
    var defaultCacheDirectory = SymbolCacheLocator.GetDefaultVisualStudioSymbolCacheDirectory();

    Console.WriteLine(
        $"""
         Arbor.Symbols.ConsoleClient

         Prefetches debugger symbols for local .dll/.exe/.pdb files: scans a
         directory tree, derives debugger-compatible symbol identifiers (PE
         timestamp+size for assemblies, GUID+age/stamp for PDBs), requests each
         one from an Arbor.Symbols.Server instance, and stores the results in a
         Visual Studio-compatible symbol cache layout:

             <symbol-cache>/<fileName>/<identifier>/<fileName>

         USAGE
             Arbor.Symbols.ConsoleClient <scan-directory> [options]

         ARGUMENTS
             <scan-directory>         Directory to scan recursively for .dll,
                                       .exe, and .pdb files. Required.

         OPTIONS
             --server <url>           Base URL of the Arbor.Symbols.Server
                                       instance. Default: http://localhost:5000

             --symbol-cache <path>    Destination symbol cache directory.
                                       Default on this OS: {defaultCacheDirectory}

             --force                  Re-download and overwrite files that
                                       already exist in the symbol cache.
                                       Default: off (existing files are skipped).

             --dry-run                Report what would be downloaded without
                                       contacting the server or writing files.

             --max-concurrency <n>    Maximum number of symbol requests to run
                                       in parallel. Default: {CliOptions.DefaultMaxConcurrency}

             --include <glob>         Only scan files matching this glob
                                       pattern, relative to <scan-directory>
                                       (e.g. "**/*.dll"). Repeatable. Defaults
                                       to "**/*.dll", "**/*.exe", "**/*.pdb".

             --exclude <glob>         Skip files matching this glob pattern
                                       (e.g. "**/*Tests*.dll"). Repeatable.
                                       Applied after --include.

             -h, --help               Show this help and exit.

         NETWORK RESILIENCE
             HTTP requests to the server use the standard Microsoft.Extensions.Http.Resilience
             pipeline (Polly-based): automatic retry with exponential backoff,
             a per-attempt and total-request timeout, and a circuit breaker.
             404 responses (symbol not found) are not treated as transient
             failures and do not trigger retries.

         EXIT CODES
             0    Completed, no failed downloads.
             1    Invalid or missing command-line arguments.
             2    <scan-directory> does not exist.
             3    Completed, but one or more symbols failed to download.
             130  Cancelled (Ctrl+C).

         EXAMPLES
             Arbor.Symbols.ConsoleClient C:\MyApp\bin\Release
             Arbor.Symbols.ConsoleClient /srv/myapp --server http://symbols.internal:5000
             Arbor.Symbols.ConsoleClient . --dry-run --include "**/*.dll" --exclude "**/*Tests*.dll"
             Arbor.Symbols.ConsoleClient . --force --max-concurrency 16
         """);
}
