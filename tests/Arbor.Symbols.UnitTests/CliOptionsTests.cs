using Arbor.Symbols.ConsoleClient;

namespace Arbor.Symbols.UnitTests;

public class CliOptionsTests
{
    private const string DefaultServerUrl = "http://localhost:5000";
    private const string DefaultCacheDirectory = "/default/cache";

    [Fact]
    public void Parse_WithNoArguments_ReturnsFailure()
    {
        var result = CliOptions.Parse([], DefaultServerUrl, DefaultCacheDirectory);

        result.IsSuccess.Should().BeFalse();
        result.HelpRequested.Should().BeFalse();
        result.ErrorMessage.Should().Contain("scan-directory");
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Parse_WithHelpFlag_ReturnsHelpRequested(string helpFlag)
    {
        var result = CliOptions.Parse([helpFlag], DefaultServerUrl, DefaultCacheDirectory);

        result.HelpRequested.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Parse_WithQuestionMarkFlag_IsNotTreatedAsHelp()
    {
        var result = CliOptions.Parse(["-?"], DefaultServerUrl, DefaultCacheDirectory);

        result.HelpRequested.Should().BeFalse();
    }

    [Fact]
    public void Parse_WithOnlyScanDirectory_UsesDefaults()
    {
        var result = CliOptions.Parse(["/scan"], DefaultServerUrl, DefaultCacheDirectory);

        result.IsSuccess.Should().BeTrue();
        var options = result.Options!;
        options.ScanDirectory.Should().Be("/scan");
        options.ServerUrl.Should().Be(DefaultServerUrl);
        options.CacheDirectory.Should().Be(DefaultCacheDirectory);
        options.Force.Should().BeFalse();
        options.DryRun.Should().BeFalse();
        options.MaxConcurrency.Should().Be(CliOptions.DefaultMaxConcurrency);
        options.IncludePatterns.Should().BeEmpty();
        options.ExcludePatterns.Should().BeEmpty();
    }

    [Fact]
    public void Parse_WithAllOptions_ParsesEachValue()
    {
        var result = CliOptions.Parse(
            [
                "/scan",
                "--server", "http://symbols.example:5000",
                "--symbol-cache", "/cache",
                "--force",
                "--dry-run",
                "--max-concurrency", "16",
                "--include", "**/*.dll",
                "--include", "**/*.pdb",
                "--exclude", "**/*Tests*.dll"
            ],
            DefaultServerUrl,
            DefaultCacheDirectory);

        result.IsSuccess.Should().BeTrue();
        var options = result.Options!;
        options.ScanDirectory.Should().Be("/scan");
        options.ServerUrl.Should().Be("http://symbols.example:5000");
        options.CacheDirectory.Should().Be("/cache");
        options.Force.Should().BeTrue();
        options.DryRun.Should().BeTrue();
        options.MaxConcurrency.Should().Be(16);
        options.IncludePatterns.Should().Equal("**/*.dll", "**/*.pdb");
        options.ExcludePatterns.Should().Equal("**/*Tests*.dll");
    }

    [Fact]
    public void Parse_WithUnknownOption_ReturnsFailure()
    {
        var result = CliOptions.Parse(["/scan", "--bogus"], DefaultServerUrl, DefaultCacheDirectory);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("--bogus");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Parse_WithInvalidMaxConcurrency_ReturnsFailure(string value)
    {
        var result = CliOptions.Parse(["/scan", "--max-concurrency", value], DefaultServerUrl, DefaultCacheDirectory);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("--max-concurrency");
    }

    [Fact]
    public void Parse_WithMissingOptionValue_ReturnsFailure()
    {
        var result = CliOptions.Parse(["/scan", "--server"], DefaultServerUrl, DefaultCacheDirectory);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("--server");
    }

    [Fact]
    public void Parse_WithTwoPositionalArguments_ReturnsFailure()
    {
        var result = CliOptions.Parse(["/scan", "/other"], DefaultServerUrl, DefaultCacheDirectory);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("extra argument");
    }
}
