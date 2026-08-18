namespace Arbor.Symbols.ConsoleClient;

public sealed record CliOptions(
    string ScanDirectory,
    string ServerUrl,
    string CacheDirectory,
    bool Force,
    bool DryRun,
    int MaxConcurrency,
    IReadOnlyList<string> IncludePatterns,
    IReadOnlyList<string> ExcludePatterns)
{
    public const int DefaultMaxConcurrency = 8;

    public static CliParseResult Parse(string[] args, string defaultServerUrl, string defaultCacheDirectory)
    {
        if (args.Any(static a => a is "--help" or "-h"))
        {
            return CliParseResult.Help();
        }

        if (args.Length == 0)
        {
            return CliParseResult.Failure("Missing required argument: <scan-directory>");
        }

        string? scanDirectory = null;
        var serverUrl = defaultServerUrl;
        var cacheDirectory = defaultCacheDirectory;
        var force = false;
        var dryRun = false;
        var maxConcurrency = DefaultMaxConcurrency;
        var includePatterns = new List<string>();
        var excludePatterns = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];

            switch (arg)
            {
                case "--server":
                {
                    if (!TryTakeValue(args, ref index, arg, out var value, out var error))
                    {
                        return CliParseResult.Failure(error);
                    }

                    serverUrl = value;
                    break;
                }
                case "--symbol-cache":
                {
                    if (!TryTakeValue(args, ref index, arg, out var value, out var error))
                    {
                        return CliParseResult.Failure(error);
                    }

                    cacheDirectory = value;
                    break;
                }
                case "--max-concurrency":
                {
                    if (!TryTakeValue(args, ref index, arg, out var value, out var error))
                    {
                        return CliParseResult.Failure(error);
                    }

                    if (!int.TryParse(value, out maxConcurrency) || maxConcurrency < 1)
                    {
                        return CliParseResult.Failure($"--max-concurrency must be a positive integer, got '{value}'.");
                    }

                    break;
                }
                case "--include":
                {
                    if (!TryTakeValue(args, ref index, arg, out var value, out var error))
                    {
                        return CliParseResult.Failure(error);
                    }

                    includePatterns.Add(value);
                    break;
                }
                case "--exclude":
                {
                    if (!TryTakeValue(args, ref index, arg, out var value, out var error))
                    {
                        return CliParseResult.Failure(error);
                    }

                    excludePatterns.Add(value);
                    break;
                }
                case "--force":
                    force = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        return CliParseResult.Failure($"Unknown option '{arg}'.");
                    }

                    if (scanDirectory is not null)
                    {
                        return CliParseResult.Failure($"Unexpected extra argument '{arg}'. Only one scan directory is supported.");
                    }

                    scanDirectory = arg;
                    break;
            }
        }

        if (scanDirectory is null)
        {
            return CliParseResult.Failure("Missing required argument: <scan-directory>");
        }

        var options = new CliOptions(
            scanDirectory,
            serverUrl,
            cacheDirectory,
            force,
            dryRun,
            maxConcurrency,
            includePatterns,
            excludePatterns);

        return CliParseResult.Success(options);
    }

    private static bool TryTakeValue(string[] args, ref int index, string optionName, out string value, out string error)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = $"Option '{optionName}' requires a value.";
            return false;
        }

        value = args[++index];
        error = string.Empty;
        return true;
    }
}
