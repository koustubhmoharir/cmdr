using Logvu.Core.Parsing;

namespace Logvu.Core.Tests.Parsing;

public sealed class LogLevelTests
{
    [Theory]
    [InlineData("TRACE", LogLevel.Trace)]
    [InlineData("Verbose", LogLevel.Trace)]
    [InlineData("all", LogLevel.Trace)]
    [InlineData("FINEST", LogLevel.Trace)]
    [InlineData("DEBUG", LogLevel.Debug)]
    [InlineData("fine", LogLevel.Debug)]
    [InlineData("INFO", LogLevel.Info)]
    [InlineData("Information", LogLevel.Info)]
    [InlineData("notice", LogLevel.Info)]
    [InlineData("WARN", LogLevel.Warn)]
    [InlineData("warning", LogLevel.Warn)]
    [InlineData("ERROR", LogLevel.Error)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("FATAL", LogLevel.Fatal)]
    [InlineData("Critical", LogLevel.Fatal)]
    [InlineData("EMERGENCY", LogLevel.Fatal)]
    [InlineData("alert", LogLevel.Fatal)]
    [InlineData("Severe", LogLevel.Fatal)]
    public void Known_spellings_map_case_insensitively(string text, LogLevel expected) =>
        Assert.Equal(expected, LogLevels.Normalize(text));

    [Theory]
    [InlineData("INFO+2", LogLevel.Info)]
    [InlineData("DEBUG-4", LogLevel.Debug)]
    [InlineData("WARN+2", LogLevel.Warn)]
    [InlineData("ERROR-1", LogLevel.Error)]
    [InlineData("  error  ", LogLevel.Error)]
    public void Slog_offsets_and_surrounding_space_map_to_the_base_level(string text, LogLevel expected) =>
        Assert.Equal(expected, LogLevels.Normalize(text));

    [Theory]
    [InlineData("CUSTOM")]
    [InlineData("CUSTOM+2")]
    [InlineData("INFO+")]
    [InlineData("+2")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_else_is_unknown(string? text) =>
        Assert.Equal(LogLevel.Unknown, LogLevels.Normalize(text));
}
