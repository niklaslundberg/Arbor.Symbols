namespace Arbor.Symbols.ConsoleClient;

public sealed class CliParseResult
{
    private CliParseResult(CliOptions? options, string? errorMessage, bool helpRequested)
    {
        Options = options;
        ErrorMessage = errorMessage;
        HelpRequested = helpRequested;
    }

    public CliOptions? Options { get; }

    public string? ErrorMessage { get; }

    public bool HelpRequested { get; }

    public bool IsSuccess => Options is not null;

    public static CliParseResult Success(CliOptions options) => new(options, null, false);

    public static CliParseResult Failure(string errorMessage) => new(null, errorMessage, false);

    public static CliParseResult Help() => new(null, null, true);
}
