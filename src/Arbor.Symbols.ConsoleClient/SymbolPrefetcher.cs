using Arbor.Symbols.Core;
using Serilog;

namespace Arbor.Symbols.ConsoleClient;

public sealed record PrefetchResult(int Downloaded, int Skipped, int Failed);

public static class SymbolPrefetcher
{
    public static async Task<PrefetchResult> RunAsync(
        HttpClient httpClient,
        IReadOnlyCollection<SymbolResourceRequest> requests,
        string cacheDirectory,
        bool force,
        bool dryRun,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        var downloaded = 0;
        var skipped = 0;
        var failed = 0;

        var parallelOptions = new ParallelOptions
        {
            // CliOptions already rejects < 1 for the CLI's own --max-concurrency, but this
            // is a public API any caller can invoke directly, and ParallelOptions itself
            // throws for 0/negative — clamp defensively rather than propagate that.
            MaxDegreeOfParallelism = Math.Max(1, maxConcurrency),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(requests, parallelOptions, async (request, token) =>
        {
            var destinationPath = SymbolResourcePathHelper.GetCachePath(cacheDirectory, request);

            if (!force && File.Exists(destinationPath))
            {
                Interlocked.Increment(ref skipped);
                return;
            }

            if (dryRun)
            {
                Log.Information("[dry-run] Would fetch {RelativePath}", request.RelativePath);
                Interlocked.Increment(ref downloaded);
                return;
            }

            var relativeUri = SymbolResourcePathHelper.BuildRelativeUri(request);

            try
            {
                using var response = await httpClient.GetAsync(relativeUri, HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode)
                {
                    Interlocked.Increment(ref failed);
                    Log.Warning("Failed to fetch {RelativePath} ({StatusCode})", request.RelativePath, response.StatusCode);
                    return;
                }

                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                // Download to a temp file and rename into place so an interrupted copy
                // (exception/cancellation/process exit) can never leave a partial file
                // at destinationPath, which a later run would otherwise treat as cached.
                var temporaryPath = Path.Combine(destinationDirectory ?? string.Empty, $"{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.download");

                try
                {
                    await using (var destination = File.Create(temporaryPath))
                    await using (var source = await response.Content.ReadAsStreamAsync(token))
                    {
                        await source.CopyToAsync(destination, token);
                    }

                    File.Move(temporaryPath, destinationPath, overwrite: true);
                }
                finally
                {
                    File.Delete(temporaryPath);
                }

                Interlocked.Increment(ref downloaded);
                Log.Information("Downloaded {RelativePath}", request.RelativePath);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                Log.Warning(ex, "Error fetching {RelativePath}", request.RelativePath);
            }
        });

        return new PrefetchResult(downloaded, skipped, failed);
    }
}
