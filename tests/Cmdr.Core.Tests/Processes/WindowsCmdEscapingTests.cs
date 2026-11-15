using Cmdr.Core.Processes;

namespace Cmdr.Core.Tests.Processes;

/// <summary>Expected values were derived by hand from the cross-spawn escaping rules.</summary>
public class WindowsCmdEscapingTests
{
    [Fact]
    public void PlainArgumentIsQuotedAndCaretEscaped()
    {
        Assert.Equal(@"^""hello^""", WindowsCmdEscaping.EscapeArgument("hello"));
    }

    [Fact]
    public void SpaceIsCaretEscapedInsideQuotes()
    {
        Assert.Equal(@"^""a^ b^""", WindowsCmdEscaping.EscapeArgument("a b"));
    }

    [Fact]
    public void EmbeddedQuoteIsBackslashEscaped()
    {
        // a"b -> a\"b -> "a\"b" -> ^"a\^"b^"
        Assert.Equal(@"^""a\^""b^""", WindowsCmdEscaping.EscapeArgument("a\"b"));
    }

    [Fact]
    public void BackslashBeforeQuoteIsDoubledAndQuoteEscaped()
    {
        // a\" -> a\\\" -> "a\\\"" -> ^"a\\\^"^"
        Assert.Equal(@"^""a\\\^""^""", WindowsCmdEscaping.EscapeArgument("a\\\""));
    }

    [Fact]
    public void TrailingBackslashIsDoubledSoItDoesNotEscapeTheClosingQuote()
    {
        Assert.Equal(@"^""C:\dir\\^""", WindowsCmdEscaping.EscapeArgument(@"C:\dir\"));
    }

    [Fact]
    public void TrailingBackslashAfterSpaceIsDoubled()
    {
        // a b\ -> "a b\\" -> ^"a^ b\\^"
        Assert.Equal(@"^""a^ b\\^""", WindowsCmdEscaping.EscapeArgument(@"a b\"));
    }

    [Fact]
    public void EmptyArgumentBecomesEscapedEmptyQuotes()
    {
        Assert.Equal(@"^""^""", WindowsCmdEscaping.EscapeArgument(""));
    }

    [Fact]
    public void PercentIsCaretEscaped()
    {
        Assert.Equal(@"^""50^%^""", WindowsCmdEscaping.EscapeArgument("50%"));
    }

    [Fact]
    public void AmpersandIsCaretEscaped()
    {
        Assert.Equal(@"^""a^&b^""", WindowsCmdEscaping.EscapeArgument("a&b"));
    }

    [Fact]
    public void InnerBackslashesAreLiteral()
    {
        Assert.Equal(@"^""a\b\c^""", WindowsCmdEscaping.EscapeArgument(@"a\b\c"));
    }

    [Fact]
    public void CommandEscapesOnlyMetacharacters()
    {
        Assert.Equal(@"C:\Program^ Files\nodejs\npm.cmd", WindowsCmdEscaping.EscapeCommand(@"C:\Program Files\nodejs\npm.cmd"));
    }

    [Fact]
    public void BuildCmdArgumentsCombinesCommandAndEscapedArguments()
    {
        string result = WindowsCmdEscaping.BuildCmdArguments(
            @"C:\Program Files\nodejs\npm.cmd",
            new[] { "install", "a b" });

        Assert.Equal(@"/d /v:off /s /c ""C:\Program^ Files\nodejs\npm.cmd ^""install^"" ^""a^ b^""""", result);
    }

    [Fact]
    public void BuildCmdArgumentsWithNoArgumentsEndsAfterCommand()
    {
        Assert.Equal(@"/d /v:off /s /c ""C:\tools\run.bat""", WindowsCmdEscaping.BuildCmdArguments(@"C:\tools\run.bat", Array.Empty<string>()));
    }

    [Fact]
    public void BuildCmdArgumentsTurnsDelayedExpansionOff()
    {
        // With delayed expansion on, cmd would expand !var! in the arguments; /v:off must come before /s /c.
        Assert.StartsWith(@"/d /v:off /s /c """, WindowsCmdEscaping.BuildCmdArguments(@"C:\tools\run.bat", new[] { "!PATH!" }));
    }

    [Theory]
    [InlineData(@"C:\proj\node_modules\.bin\vite.cmd")]
    [InlineData(@"C:\proj\NODE_MODULES/.bin/vite.CMD")]
    public void BuildCmdArgumentsDoubleEscapesArgumentsForNodeModulesShims(string shim)
    {
        string result = WindowsCmdEscaping.BuildCmdArguments(shim, new[] { "a b" });

        Assert.EndsWith(@" ^^^""a^^^ b^^^""""", result);
    }

    [Fact]
    public void BuildCmdArgumentsDoesNotDoubleEscapeOtherBatchFiles()
    {
        string result = WindowsCmdEscaping.BuildCmdArguments(@"C:\Users\me\AppData\Roaming\npm\pnpm.cmd", new[] { "a b" });

        Assert.EndsWith(@" ^""a^ b^""""", result);
    }
}
