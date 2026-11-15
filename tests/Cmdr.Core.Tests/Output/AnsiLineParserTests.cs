using System.Collections.Concurrent;
using Cmdr.Core.Output;
using static Cmdr.Core.Tests.Output.OutputTestHelpers;

namespace Cmdr.Core.Tests.Output;

public class AnsiLineParserTests
{
    [Fact]
    public void PlainLinesAreSplitOnNewline()
    {
        Assert.Equal(new[] { "one", "two", "" }, Texts(Parse("one\ntwo\n\n")));
    }

    [Fact]
    public void FlushCompletesNonEmptyPartialLine()
    {
        Assert.Equal(new[] { "abc" }, Texts(Parse("abc")));
    }

    [Fact]
    public void CrLfCompletesLineOnce()
    {
        Assert.Equal(new[] { "abc", "def" }, Texts(Parse("abc\r\ndef\r\n")));
    }

    [Fact]
    public void CrLfSplitAcrossChunksDoesNotClearLine()
    {
        Assert.Equal(new[] { "abc", "def" }, Texts(Parse("abc\r", "\ndef")));
    }

    [Fact]
    public void BareCarriageReturnOverwritesLine()
    {
        Assert.Equal(new[] { "xy" }, Texts(Parse("abc\rxy\n")));
    }

    [Fact]
    public void BareCarriageReturnAcrossChunkBoundaryOverwritesLine()
    {
        Assert.Equal(new[] { "xy" }, Texts(Parse("abc\r", "xy\n")));
    }

    [Fact]
    public void ProgressBarStyleOverwriteKeepsLatestContent()
    {
        Assert.Equal(new[] { "100%" }, Texts(Parse("  0%\r 50%\r100%\n")));
    }

    [Fact]
    public void DoubleCarriageReturnThenLineFeedKeepsContent()
    {
        Assert.Equal(new[] { "abc" }, Texts(Parse("abc\r\r\n")));
    }

    [Fact]
    public void CarriageReturnKeepsContentUntilNextCharacter()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var lines = new List<OutputLine>();
        parser.Feed("  0%\r", lines);
        Assert.Equal("  0%", parser.Partial?.PlainText);

        parser.Feed(" 5", lines);
        Assert.Equal(" 5", parser.Partial?.PlainText);
    }

    [Fact]
    public void CarriageReturnThenSgrThenNewlineCompletesWithExistingContent()
    {
        Assert.Equal(new[] { "abc" }, Texts(Parse("abc\r\u001B[0m\n")));
    }

    [Fact]
    public void CarriageReturnAtEndOfStreamKeepsContent()
    {
        Assert.Equal(new[] { "100%" }, Texts(Parse("100%\r")));
    }

    [Fact]
    public void BackspaceWhileOverwritePendingIsNoOp()
    {
        Assert.Equal(new[] { "abc" }, Texts(Parse("abc\r\b\n")));
        Assert.Equal(new[] { "x" }, Texts(Parse("abc\r\bx\n")));
    }

    [Fact]
    public void TabWhileOverwritePendingClearsLineFirst()
    {
        Assert.Equal(new[] { "        x" }, Texts(Parse("abc\r\tx\n")));
    }

    [Fact]
    public void OverwriteTakesStyleOfNewCharacter()
    {
        List<OutputLine> lines = Parse("abc\r\u001B[31mx\n");
        Assert.Equal("x", lines[0].PlainText);
        Assert.Equal(AnsiColor.FromIndex(1), lines[0].Spans[0].Style.Foreground);
    }

    [Fact]
    public void OverwriteAcrossChunkBoundaryWithSgrBetween()
    {
        Assert.Equal(new[] { "xy" }, Texts(Parse("abc\r\u001B[", "0mxy\n")));
    }

    [Fact]
    public void TabExpandsToNextMultipleOfEight()
    {
        Assert.Equal(new[] { "a       b" }, Texts(Parse("a\tb\n")));
    }

    [Fact]
    public void TabAtColumnZeroIsEightSpaces()
    {
        Assert.Equal(new[] { "        x" }, Texts(Parse("\tx\n")));
    }

    [Fact]
    public void TabAtColumnMultipleOfEightIsEightSpaces()
    {
        Assert.Equal(new[] { "abcdefgh        x" }, Texts(Parse("abcdefgh\tx\n")));
    }

    [Theory]
    [InlineData("a\u009Dtitle\u0007VISIBLE\n")]
    [InlineData("a\u001B]title\u009CVISIBLE\n")]
    [InlineData("a\u0090payload\u009CVISIBLE\n")]
    public void C1StringSequencesAreStrippedAcrossChunkBoundaries(string input)
    {
        for (int split = 0; split <= input.Length; split++)
        {
            Assert.Equal(new[] { "aVISIBLE" }, Texts(Parse(input[..split], input[split..])));
        }

        Assert.Equal(new[] { "aVISIBLE" }, Texts(Parse(input.Select(c => c.ToString()).ToArray())));
    }

    [Fact]
    public void ForcedLineBreakDoesNotSplitSurrogatePair()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 3);
        var lines = new List<OutputLine>();
        parser.Feed("ab\uD83D", lines);
        parser.Feed("\uDE00X\n", lines);

        Assert.Equal(new[] { "ab", "😀X" }, Texts(lines));
        Assert.All(lines, line => Assert.Equal(line.PlainText, System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(line.PlainText))));

        var tiny = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 1);
        var tinyLines = new List<OutputLine>();
        tiny.Feed("😀x\n", tinyLines);
        Assert.Equal(new[] { "😀", "x" }, Texts(tinyLines));
    }

    [Fact]
    public void ForcedBreakMovesAZwjSequenceStartedByASupplementaryScalarWhole()
    {
        // 👩‍💻 is U+1F469 ZWJ U+1F4BB; the break falls between the high surrogate of 💻 and its low surrogate.
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 5);
        var lines = new List<OutputLine>();
        parser.Feed("ab👩\u200D💻X\n", lines);

        // "👩‍💻X" is six units, over the limit, so X starts a line of its own.
        Assert.Equal(new[] { "ab", "👩\u200D💻", "X" }, Texts(lines));
    }

    [Fact]
    public void ForcedBreakMovesAZwjSequenceStartedByASupplementaryScalarWhenFedOneUnitAtATime()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 5);
        var lines = new List<OutputLine>();
        foreach (char c in "ab👩\u200D💻X\n")
        {
            parser.Feed(c.ToString(), lines);
        }

        Assert.Equal(new[] { "ab", "👩\u200D💻", "X" }, Texts(lines));
    }

    [Fact]
    public void ForcedBreakKeepsARegionalIndicatorPairWholeWhenItStartsAtTheLimit()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 4);
        var lines = new List<OutputLine>();
        parser.Feed("ab🇮🇳X\n", lines);

        Assert.Equal(new[] { "ab", "🇮🇳", "X" }, Texts(lines));
    }

    [Fact]
    public void ForcedBreakKeepsARegionalIndicatorPairWholeWhenFedOneUnitAtATime()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 4);
        var lines = new List<OutputLine>();
        foreach (char c in "ab🇮🇳X\n")
        {
            parser.Feed(c.ToString(), lines);
        }

        Assert.Equal(new[] { "ab", "🇮🇳", "X" }, Texts(lines));
    }

    [Fact]
    public void ForcedBreakAtTheDefaultLimitMovesAZwjSequenceStartedAtTheLimit()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var lines = new List<OutputLine>();
        parser.Feed(new string('a', 65_533) + "👩\u200D💻X\n", lines);

        Assert.Equal(2, lines.Count);
        Assert.Equal(new string('a', 65_533), lines[0].PlainText);
        Assert.Equal("👩\u200D💻X", lines[1].PlainText);
    }

    [Fact]
    public void SupplementaryScalarThatStartsANewGraphemeAtTheLimitBreaksBeforeItsHighSurrogate()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 4);
        var lines = new List<OutputLine>();
        parser.Feed("abcd😀\n", lines);

        Assert.Equal(new[] { "abcd", "😀" }, Texts(lines));
    }

    [Fact]
    public void SupplementaryScalarThatStartsANewGraphemeAtTheLimitWhenFedOneUnitAtATime()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 4);
        var lines = new List<OutputLine>();
        foreach (char c in "abcd😀\n")
        {
            parser.Feed(c.ToString(), lines);
        }

        Assert.Equal(new[] { "abcd", "😀" }, Texts(lines));
    }

    [Fact]
    public void LoneHighSurrogateAtTheLimitFollowedByANonLowCharacterDoesNotCrashAndExceedsTheLimitByOne()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 4);
        var lines = new List<OutputLine>();
        parser.Feed("abcd\uD83DX\n", lines);

        Assert.Equal(new[] { "abcd\uD83D", "X" }, Texts(lines));
        Assert.Equal(5, lines[0].PlainText.Length);
    }

    [Fact]
    public void LoneHighSurrogateAtTheLimitFollowedByAnotherHighSurrogateDoesNotExceedTheLimitByMoreThanOne()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 4);
        var lines = new List<OutputLine>();
        parser.Feed("abcd\uD83D\uD83D\uDE00\n", lines);

        Assert.Equal(new[] { "abcd\uD83D", "😀" }, Texts(lines));
        Assert.Equal(5, lines[0].PlainText.Length);
    }

    [Fact]
    public void LongGraphemeThatIsTheWholeLineIsSplitBeforeASurrogatePairNotInsideIt()
    {
        // 😀 ZWJ 💻 is one grapheme longer than the limit. It is split between scalars, never inside 💻.
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 3);
        var lines = new List<OutputLine>();
        parser.Feed("\uD83D\uDE00\u200D\uD83D\uDCBB\n", lines);

        Assert.Equal(new[] { "\uD83D\uDE00\u200D", "\uD83D\uDCBB" }, Texts(lines));
    }

    [Fact]
    public void LongGraphemeThatIsTheWholeLineIsSplitBeforeASurrogatePairWhenTheHighSurrogateWasAtTheLimit()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 4);
        var lines = new List<OutputLine>();
        parser.Feed("\uD83D\uDE00\u200D\uD83D\uDCBBX\n", lines);

        Assert.Equal(new[] { "\uD83D\uDE00\u200D", "\uD83D\uDCBBX" }, Texts(lines));
    }

    [Fact]
    public void ForcedLineBreakMovesABaseWithItsCombiningMarkToTheNextLine()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 3);
        var lines = new List<OutputLine>();
        parser.Feed("abe", lines);
        parser.Feed("\u0301X\n", lines); // the mark arrives in a later chunk

        Assert.Equal(new[] { "ab", "e\u0301X" }, Texts(lines));
    }

    [Fact]
    public void ForcedLineBreakAtTheDefaultLimitKeepsTheCombiningMark()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var lines = new List<OutputLine>();
        parser.Feed(new string('a', 65_535) + "e\u0301X\n", lines);

        Assert.Equal(2, lines.Count);
        Assert.Equal(65_535, lines[0].PlainText.Length);
        Assert.Equal("e\u0301X", lines[1].PlainText);
    }

    [Fact]
    public void ForcedLineBreakMovesAZwjSequenceWhole_InItsStyles()
    {
        // 👩‍💻 is U+1F469 ZWJ U+1F4BB: five UTF-16 code units, one grapheme.
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 6);
        List<OutputLine> lines = new();
        parser.Feed("ab\u001B[31m\uD83D\uDC69\u200D\uD83D", lines);
        parser.Feed("\uDCBB\u001B[0mX\n", lines);

        Assert.Equal(new[] { "ab", "\uD83D\uDC69\u200D\uD83D\uDCBBX" }, Texts(lines));
        Assert.Equal(new[] { "\uD83D\uDC69\u200D\uD83D\uDCBB", "X" }, lines[1].Spans.Select(span => span.Text));
        Assert.NotEqual(SpanStyle.Default, lines[1].Spans[0].Style);
        Assert.Equal(SpanStyle.Default, lines[1].Spans[1].Style);
    }

    [Fact]
    public void GraphemeThatIsTheWholeLineIsSplitAtTheLimit()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 2);
        var lines = new List<OutputLine>();
        parser.Feed("e\u0301\u0302\n", lines);

        Assert.Equal(new[] { "e\u0301", "\u0302" }, Texts(lines));
    }

    [Fact]
    public void BackspaceRemovesCompleteSupplementaryCharacter()
    {
        Assert.Equal(new[] { "X" }, Texts(Parse("😀", "\bX\n")));
        List<OutputLine> styled = Parse("\u001B[31m\uD83D\u001B[0m\uDE00\bX\n");
        Assert.Equal("X", styled[0].PlainText);
        Assert.Single(styled[0].Spans);
        Assert.Equal(SpanStyle.Default, styled[0].Spans[0].Style);
    }


    [Fact]
    public void BackspaceRemovesLastCharacter()
    {
        Assert.Equal(new[] { "c" }, Texts(Parse("ab\b\bc\n")));
    }

    [Fact]
    public void BackspaceOnEmptyLineIsIgnored()
    {
        Assert.Equal(new[] { "x" }, Texts(Parse("\b\bx\n")));
    }

    [Fact]
    public void OtherC0ControlsAndDelAreDropped()
    {
        Assert.Equal(new[] { "abcde" }, Texts(Parse("a\u0000b\u0007c\u001Fd\u007Fe\n")));
    }

    [Fact]
    public void NonAsciiTextIsKept()
    {
        Assert.Equal(new[] { "héllo 日本" }, Texts(Parse("héllo 日本\n")));
    }

    [Theory]
    [InlineData("\u001B[1m", TextAttrs.Bold)]
    [InlineData("\u001B[2m", TextAttrs.Dim)]
    [InlineData("\u001B[3m", TextAttrs.Italic)]
    [InlineData("\u001B[4m", TextAttrs.Underline)]
    [InlineData("\u001B[5m", TextAttrs.Blink)]
    [InlineData("\u001B[7m", TextAttrs.Inverse)]
    [InlineData("\u001B[9m", TextAttrs.Strikethrough)]
    public void AttributeOnCodesSetAttribute(string sgr, TextAttrs expected)
    {
        Assert.Equal(expected, StyleOf(sgr + "x").Attrs);
    }

    [Theory]
    [InlineData("1;22", TextAttrs.None)]
    [InlineData("2;22", TextAttrs.None)]
    [InlineData("3;23", TextAttrs.None)]
    [InlineData("4;24", TextAttrs.None)]
    [InlineData("5;25", TextAttrs.None)]
    [InlineData("7;27", TextAttrs.None)]
    [InlineData("9;29", TextAttrs.None)]
    public void AttributeOffCodesClearAttribute(string sgr, TextAttrs expected)
    {
        Assert.Equal(expected, StyleOf($"\u001B[{sgr}mx").Attrs);
    }

    [Fact]
    public void Code22ClearsBoldAndDimButNotOthers()
    {
        Assert.Equal(TextAttrs.Italic, StyleOf("\u001B[1;2;3;22mx").Attrs);
    }

    [Fact]
    public void AttributesCombine()
    {
        Assert.Equal(TextAttrs.Bold | TextAttrs.Underline, StyleOf("\u001B[1;4mx").Attrs);
    }

    [Fact]
    public void SixteenColorsMapToIndices()
    {
        for (int i = 0; i < 8; i++)
        {
            SpanStyle fg = StyleOf($"\u001B[{30 + i}mx");
            SpanStyle bg = StyleOf($"\u001B[{40 + i}mx");
            SpanStyle brightFg = StyleOf($"\u001B[{90 + i}mx");
            SpanStyle brightBg = StyleOf($"\u001B[{100 + i}mx");

            Assert.Equal(AnsiColor.FromIndex((byte)i), fg.Foreground);
            Assert.Equal(AnsiColor.FromIndex((byte)i), bg.Background);
            Assert.Equal(AnsiColor.FromIndex((byte)(i + 8)), brightFg.Foreground);
            Assert.Equal(AnsiColor.FromIndex((byte)(i + 8)), brightBg.Background);
        }
    }

    [Fact]
    public void ColorsAreIndependentOfEachOther()
    {
        SpanStyle style = StyleOf("\u001B[31;42mx");
        Assert.Equal(AnsiColor.FromIndex(1), style.Foreground);
        Assert.Equal(AnsiColor.FromIndex(2), style.Background);
    }

    [Theory]
    [InlineData("\u001B[38;5;200mx", true, 200)]
    [InlineData("\u001B[38:5:9mx", true, 9)]
    [InlineData("\u001B[48;5;16mx", false, 16)]
    [InlineData("\u001B[48:5:255mx", false, 255)]
    [InlineData("\u001B[38;5;0mx", true, 0)]
    public void IndexedColorsUse256Palette(string input, bool foreground, byte index)
    {
        SpanStyle style = StyleOf(input);
        AnsiColor color = foreground ? style.Foreground : style.Background;
        Assert.Equal(AnsiColor.FromIndex(index), color);
    }

    [Theory]
    [InlineData("\u001B[38;2;1;2;3mx", true)]
    [InlineData("\u001B[38:2:1:2:3mx", true)]
    [InlineData("\u001B[38:2::1:2:3mx", true)]
    [InlineData("\u001B[38:2:0:1:2:3mx", true)]
    [InlineData("\u001B[48;2;1;2;3mx", false)]
    [InlineData("\u001B[48:2:1:2:3mx", false)]
    [InlineData("\u001B[48:2::1:2:3mx", false)]
    [InlineData("\u001B[48:2:0:1:2:3mx", false)]
    public void RgbColorsAcceptSemicolonAndColonForms(string input, bool foreground)
    {
        SpanStyle style = StyleOf(input);
        AnsiColor color = foreground ? style.Foreground : style.Background;
        Assert.Equal(AnsiColor.FromRgb(1, 2, 3), color);
    }

    [Fact]
    public void RgbColorWithColorspaceIdUsesLastThreeValues()
    {
        Assert.Equal(AnsiColor.FromRgb(10, 20, 30), StyleOf("\u001B[38:2:7:10:20:30mx").Foreground);
    }

    [Fact]
    public void DefaultColorCodesResetColors()
    {
        SpanStyle style = StyleOf("\u001B[31;42;39;49mx");
        Assert.Equal(AnsiColor.Default, style.Foreground);
        Assert.Equal(AnsiColor.Default, style.Background);
    }

    [Fact]
    public void Code39And49ResetOnlyTheirOwnColor()
    {
        SpanStyle style = StyleOf("\u001B[31;42;39mx");
        Assert.Equal(AnsiColor.Default, style.Foreground);
        Assert.Equal(AnsiColor.FromIndex(2), style.Background);
    }

    [Fact]
    public void ZeroResetsEverything()
    {
        Assert.Equal(SpanStyle.Default, StyleOf("\u001B[1;3;31;44;0mx"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("00")]
    public void EmptyOrZeroParameterResets(string reset)
    {
        // Bold red first, so a reset that does nothing leaves the text after it styled.
        List<OutputLine> lines = Parse($"\u001B[1;31mabc\u001B[{reset}mxyz\n");

        Assert.Single(lines);
        Assert.Equal(new[] { "abc", "xyz" }, lines[0].Spans.Select(s => s.Text));
        SpanStyle before = lines[0].Spans[0].Style;
        Assert.Equal(TextAttrs.Bold, before.Attrs);
        Assert.Equal(AnsiColor.FromIndex(1), before.Foreground);
        Assert.Equal(SpanStyle.Default, lines[0].Spans[1].Style);
    }

    [Fact]
    public void EmptyParameterInListIsZero()
    {
        // "1;;31" is bold, reset, then red: the reset clears bold.
        SpanStyle style = StyleOf("\u001B[1;;31mx");
        Assert.Equal(TextAttrs.None, style.Attrs);
        Assert.Equal(AnsiColor.FromIndex(1), style.Foreground);
    }

    [Theory]
    [InlineData("\u001B[38;5;256mx")]
    [InlineData("\u001B[38;5;999mx")]
    [InlineData("\u001B[38;2;256;0;0mx")]
    [InlineData("\u001B[38;2;0;0;300mx")]
    [InlineData("\u001B[38;2;1;2mx")]
    [InlineData("\u001B[38;5mx")]
    [InlineData("\u001B[38;5;1:2mx")]
    [InlineData("\u001B[38;7;1mx")]
    [InlineData("\u001B[38:5:300mx")]
    [InlineData("\u001B[38:2:1:2mx")]
    [InlineData("\u001B[38:2:1:2:3:4:5mx")]
    [InlineData("\u001B[38;99999999999;1;2;3mx")]
    public void InvalidColorSpecsAreIgnored(string input)
    {
        List<OutputLine> lines = Parse(input);

        Assert.Single(lines);
        Assert.Equal("x", lines[0].PlainText);
        Assert.All(lines[0].Spans, span => Assert.Equal(AnsiColor.Default, span.Style.Foreground));
    }

    [Fact]
    public void InvalidColorSpecDoesNotStopLaterCodes()
    {
        SpanStyle style = StyleOf("\u001B[38;5;300;1mx");
        Assert.Equal(AnsiColor.Default, style.Foreground);
        Assert.Equal(TextAttrs.Bold, style.Attrs);
    }

    [Fact]
    public void ValidColorConsumesItsParametersOnly()
    {
        SpanStyle style = StyleOf("\u001B[38;2;1;2;3;4mx");
        Assert.Equal(AnsiColor.FromRgb(1, 2, 3), style.Foreground);
        Assert.Equal(TextAttrs.Underline, style.Attrs);
    }

    [Fact]
    public void UnknownCodesAreIgnored()
    {
        // Syntactically valid SGR: a letter would end the sequence early.
        List<OutputLine> lines = Parse("\u001B[21;99;108mx\n");

        Assert.Single(lines);
        Assert.Equal("x", lines[0].PlainText);
        Assert.Single(lines[0].Spans);
        Assert.Equal(SpanStyle.Default, lines[0].Spans[0].Style);
    }

    [Fact]
    public void UnknownCodeDoesNotDisturbKnownCodes()
    {
        SpanStyle style = StyleOf("\u001B[21;1;99;31mx");
        Assert.Equal(TextAttrs.Bold, style.Attrs);
        Assert.Equal(AnsiColor.FromIndex(1), style.Foreground);
    }

    [Fact]
    public void SgrStylePersistsAcrossLines()
    {
        List<OutputLine> lines = Parse("\u001B[32mone\ntwo\u001B[0m\nthree\n");
        Assert.Equal(AnsiColor.FromIndex(2), lines[1].Spans[0].Style.Foreground);
        Assert.Equal(SpanStyle.Default, lines[2].Spans[0].Style);
    }

    [Fact]
    public void SgrStylePersistsAcrossFlush()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var lines = new List<OutputLine>();
        parser.Feed("\u001B[4mhi", lines);
        parser.Flush(lines);
        parser.Feed("there\n", lines);
        Assert.Equal(TextAttrs.Underline, lines[1].Spans[0].Style.Attrs);
    }

    [Fact]
    public void AdjacentSpansWithEqualStyleAreMerged()
    {
        List<OutputLine> lines = Parse("\u001B[31mab\u001B[31mcd\u001B[0mef\n");
        Assert.Equal(2, lines[0].Spans.Count);
        Assert.Equal("abcd", lines[0].Spans[0].Text);
        Assert.Equal("ef", lines[0].Spans[1].Text);
    }

    [Fact]
    public void SgrThatChangesNothingDoesNotSplitSpan()
    {
        List<OutputLine> lines = Parse("ab\u001B[0mcd\n");
        Assert.Single(lines[0].Spans);
    }

    [Fact]
    public void NoEmptySpansAreProduced()
    {
        List<OutputLine> lines = Parse("\u001B[31m\u001B[32mx\u001B[33m\u001B[0m\n");
        Assert.Single(lines[0].Spans);
        Assert.Equal("x", lines[0].Spans[0].Text);
    }

    [Fact]
    public void BackspaceRemovingStyledRunLeavesNoEmptySpan()
    {
        List<OutputLine> lines = Parse("a\u001B[31mb\b\u001B[0mc\n");
        Assert.Equal("ac", lines[0].PlainText);
        Assert.Single(lines[0].Spans);
        Assert.Equal(SpanStyle.Default, lines[0].Spans[0].Style);
    }

    [Fact]
    public void NonSgrCsiSequencesAreDropped()
    {
        Assert.Equal(new[] { "abc" }, Texts(Parse("\u001B[2Kabc\n")));
        Assert.Equal(new[] { "abc" }, Texts(Parse("\u001B[?25labc\n")));
        Assert.Equal(new[] { "abc" }, Texts(Parse("\u001B[2 qabc\n")));
        Assert.Equal(new[] { "abc" }, Texts(Parse("\u001B[1;2Habc\n")));
    }

    [Fact]
    public void NonSgrCsiDoesNotChangeStyle()
    {
        SpanStyle style = StyleOf("\u001B[31m\u001B[2Kx");
        Assert.Equal(AnsiColor.FromIndex(1), style.Foreground);
    }

    [Fact]
    public void Csi8BitIntroducerIsRecognised()
    {
        SpanStyle style = StyleOf("\u009B31mx");
        Assert.Equal(AnsiColor.FromIndex(1), style.Foreground);
    }

    [Fact]
    public void OscTerminatedByBelIsDropped()
    {
        Assert.Equal(new[] { "text" }, Texts(Parse("\u001B]0;title\u0007text\n")));
    }

    [Fact]
    public void OscTerminatedByStIsDropped()
    {
        Assert.Equal(new[] { "text" }, Texts(Parse("\u001B]2;title\u001B\\text\n")));
    }

    [Fact]
    public void CharsetAndKeypadSequencesAreDropped()
    {
        Assert.Equal(new[] { "ab" }, Texts(Parse("\u001B(Ba\u001B=b\n")));
    }

    [Fact]
    public void UnterminatedOscAtNewlineDoesNotSwallowFollowingOutput()
    {
        Assert.Equal(new[] { "", "next" }, Texts(Parse("\u001B]0;unterminated\nnext\n")));
    }

    [Fact]
    public void IncompleteEscapeAtEndOfStreamIsDiscarded()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var lines = new List<OutputLine>();
        parser.Feed("x\u001B[31", lines);
        parser.Flush(lines);
        Assert.Equal(new[] { "x" }, Texts(lines));

        parser.Feed("y\n", lines);
        Assert.Equal(new[] { "x", "y" }, Texts(lines));
        Assert.Equal(SpanStyle.Default, lines[1].Spans[0].Style);
    }

    [Fact]
    public void MalformedCsiIsAbandonedAndFollowingTextKept()
    {
        // A C0 control inside a CSI abandons the sequence; the 'm' after it is ordinary text.
        Assert.Equal(new[] { "mX" }, Texts(Parse("\u001B[3\u0001mX\n")));
    }

    [Fact]
    public void EscapeInsideCsiRestartsEscape()
    {
        SpanStyle style = StyleOf("\u001B[3\u001B[32mx");
        Assert.Equal(AnsiColor.FromIndex(2), style.Foreground);
    }

    [Fact]
    public void MalformedEscapeBeforeNewlineStillCompletesLine()
    {
        Assert.Equal(new[] { "a", "b" }, Texts(Parse("a\u001B\nb\n")));
    }

    [Fact]
    public void EscapeSequenceSplitAtEveryPositionGivesSameResult()
    {
        const string input = "a\u001B[1;31mb\u001B]0;title\u0007c\u001B(Bd\u001B[2Ke\u001B[38;2;1;2;3mf\u001B]2;t\u001B\\g\r\nh\u001B[0m\ti\n";
        string[] expected = Describe(Parse(input));

        for (int split = 0; split <= input.Length; split++)
        {
            string first = input[..split];
            string second = input[split..];
            Assert.Equal(expected, Describe(Parse(first, second)));
        }
    }

    [Theory]
    [InlineData("\u001BPpayload\u001B\\")]
    [InlineData("\u001BPpayload\u0007")]
    [InlineData("\u001BXpayload\u001B\\")]
    [InlineData("\u001B^payload\u0007")]
    [InlineData("\u001B_payload\u001B\\")]
    [InlineData("\u0090payload\u001B\\")]
    [InlineData("\u0090payload\u0007")]
    [InlineData("\u0098payload\u0007")]
    [InlineData("\u009Epayload\u001B\\")]
    [InlineData("\u009Fpayload\u0007")]
    public void StringSequencesAreDroppedUpToTerminator(string sequence)
    {
        Assert.Equal(new[] { "ab" }, Texts(Parse("a" + sequence + "b\n")));
    }

    [Fact]
    public void EscapeInsideStringStartsNewSequence()
    {
        // ESC not followed by a backslash ends the string and starts a new sequence, as for OSC.
        List<OutputLine> lines = Parse("\u001BPq\u001B[31mx\u0007y\n");
        Assert.Equal("xy", lines[0].PlainText);
        Assert.Equal(AnsiColor.FromIndex(1), lines[0].Spans[0].Style.Foreground);
    }

    [Fact]
    public void UnterminatedStringAtNewlineDoesNotSwallowFollowingOutput()
    {
        Assert.Equal(new[] { "", "next" }, Texts(Parse("\u001BPnever ends\nnext\n")));
        Assert.Equal(new[] { "", "next" }, Texts(Parse("\u0090never ends\nnext\n")));
    }

    [Fact]
    public void AllStringSequenceTypesSplitAtEveryPositionGiveSameResult()
    {
        const string input =
            "a\u001BPq#0;2;1;2;3\u001B\\b\u001BXsos\u0007c\u001B^pm\u001B\\d\u001B_apc\u0007e" +
            "\u0090dcs\u0007f\u0098sos\u0007g\u009Epm\u0007h\u009Fapc\u0007i\u001B]0;t\u0007j\u001B[31mk\n";
        string[] expected = Describe(Parse(input));
        Assert.Equal(new[] { "abcdefghijk" }, Texts(Parse(input)));

        for (int split = 0; split <= input.Length; split++)
        {
            Assert.Equal(expected, Describe(Parse(input[..split], input[split..])));
        }
    }

    [Fact]
    public void EscapeSequenceSplitIntoSingleCharacterChunksGivesSameResult()
    {
        const string input = "a\u001B[1;31mb\u001B]0;title\u0007c\u001B(Bd\u001B[38:5:200me\r\n\u001B[?25lf\rg\n";
        string[] expected = Describe(Parse(input));

        var parser = new AnsiLineParser(OutputSource.StdOut);
        var lines = new List<OutputLine>();
        foreach (char c in input)
        {
            parser.Feed(c.ToString(), lines);
        }

        parser.Flush(lines);
        Assert.Equal(expected, Describe(lines));
    }

    [Fact]
    public void CarriageReturnAtEveryChunkBoundaryGivesSameResult()
    {
        const string input = "abc\r\ndef\rxy\r\n\r\n";
        string[] expected = Describe(Parse(input));

        for (int split = 0; split <= input.Length; split++)
        {
            Assert.Equal(expected, Describe(Parse(input[..split], input[split..])));
        }
    }

    [Fact]
    public void LineReachingMaxLengthIsCompleted()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 5);
        var lines = new List<OutputLine>();
        parser.Feed("abcdefghijk\n", lines);
        Assert.Equal(new[] { "abcde", "fghij", "k" }, Texts(lines));
    }

    [Fact]
    public void LineOfExactlyMaxLengthFollowedByNewlineIsOneLine()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 5);
        var lines = new List<OutputLine>();
        parser.Feed("abcde\n", lines);
        Assert.Equal(new[] { "abcde" }, Texts(lines));
        Assert.Null(parser.Partial);
    }

    [Fact]
    public void LineOneOverMaxLengthLeavesPartialOfOne()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 5);
        var lines = new List<OutputLine>();
        parser.Feed("abcdef", lines);
        Assert.Equal(new[] { "abcde" }, Texts(lines));
        Assert.Equal("f", parser.Partial?.PlainText);
    }

    [Fact]
    public void MaxLengthCountsTabExpansion()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 4);
        var lines = new List<OutputLine>();
        parser.Feed("a\tb\n", lines);
        Assert.Equal(new[] { "a   ", "    ", "b" }, Texts(lines));
    }

    [Fact]
    public void PartialReflectsIncompleteLine()
    {
        var parser = new AnsiLineParser(OutputSource.StdErr);
        var lines = new List<OutputLine>();
        Assert.Null(parser.Partial);

        parser.Feed("ab\u001B[31mc", lines);
        Assert.Empty(lines);
        OutputLine? partial = parser.Partial;
        Assert.NotNull(partial);
        Assert.Equal("abc", partial.PlainText);
        Assert.Equal(OutputSource.StdErr, partial.Source);
        Assert.Equal(2, partial.Spans.Count);

        parser.Feed("\n", lines);
        Assert.Null(parser.Partial);
        Assert.Single(lines);
    }

    [Fact]
    public void PartialIsNullAfterCrLfCompletesLine()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var lines = new List<OutputLine>();
        parser.Feed("abc\r", lines);
        Assert.NotNull(parser.Partial);
        parser.Feed("\n", lines);
        Assert.Null(parser.Partial);
        Assert.Equal(new[] { "abc" }, Texts(lines));
    }

    [Fact]
    public void FlushOnEmptyPartialCompletesNothing()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var lines = new List<OutputLine>();
        parser.Feed("done\n", lines);
        parser.Flush(lines);
        Assert.Single(lines);
    }

    [Fact]
    public void CompletedLinesCarrySource()
    {
        var parser = new AnsiLineParser(OutputSource.StdErr);
        var lines = new List<OutputLine>();
        parser.Feed("x\n", lines);
        Assert.Equal(OutputSource.StdErr, lines[0].Source);
    }

    [Fact]
    public void ConstructorRejectsNonPositiveMaxLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnsiLineParser(OutputSource.StdOut, 0));
    }

    [Theory]
    [InlineData("ab\tX\n", 8, 6)]
    [InlineData("日\tX\n", 8, 6)]
    [InlineData("\U0001F600\tX\n", 8, 6)]
    [InlineData("e\u0301\tX\n", 8, 7)]
    [InlineData("12345678\tX\n", 16, 8)]
    [InlineData("abc\rd\tX\n", 8, 7)]
    [InlineData("日日\b\tX\n", 8, 6)]
    public void TabStopIsMeasuredInDisplayColumns(string input, int expectedColumn, int expectedSpaces)
    {
        (int column, int spaces) = TabStop(input);
        Assert.Equal(expectedColumn, column);
        Assert.Equal(expectedSpaces, spaces);
    }

    [Fact]
    public void ConsecutiveTabsStopEveryEightColumns()
    {
        string text = Texts(Parse("\t\t\t\tX\n"))[0];
        Assert.Equal(32, DisplayWidth.Of(text[..^1]));
        Assert.Equal('X', text[^1]);
    }

    [Fact]
    public void TabAfterWideCharactersAcrossChunksKeepsColumn()
    {
        List<OutputLine> lines = Parse("日", "日", "\t", "X\n");
        Assert.Equal(new[] { "日日    X" }, Texts(lines));
    }

    [Fact]
    public void TabAfterForcedBreakInsideSpacesStaysConsistent()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 6);
        var lines = new List<OutputLine>();
        parser.Feed("abcd\tX\tY", lines);
        parser.Flush(lines);
        Assert.Equal(new[] { "abcd  ", "  X   ", "  Y" }, Texts(lines));
    }

    [Theory]
    [InlineData("ab\tX\n")]
    [InlineData("日\tX\tY\n")]
    [InlineData("\U0001F600\tX\n")]
    [InlineData("e\u0301\tX\t\tY\n")]
    [InlineData("abc\rd\tX\n")]
    [InlineData("日日\b\tX\n")]
    [InlineData("\t\t\t\tX\n")]
    public void TabsSplitAtEveryPositionGiveSameResult(string input)
    {
        string[] expected = Describe(Parse(input));
        for (int split = 0; split <= input.Length; split++)
        {
            Assert.Equal(expected, Describe(Parse(input[..split], input[split..])));
        }
    }

    /// <summary>What the eager path gives for the same input: the completed line built by <c>BuildLine</c>.</summary>
    private static string[] EagerDescription(string input) => Describe(Parse(input + "\n"));

    [Fact]
    public void PartialSnapshotIsUnchangedByLaterAppends()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var sink = new List<OutputLine>();
        parser.Feed("abc", sink);
        OutputLine? first = parser.Partial;

        parser.Feed("def", sink);

        Assert.Equal("abc", first!.PlainText);
        Assert.Equal("abcdef", parser.Partial!.PlainText);
        Assert.Empty(sink);
    }

    [Fact]
    public void PartialSnapshotIsUnchangedByBackspaceAndRewrite()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var sink = new List<OutputLine>();
        parser.Feed("\u001b[31mab\u001b[0mc", sink);
        OutputLine? before = parser.Partial;
        string[] expected = EagerDescription("\u001b[31mab\u001b[0mc");

        parser.Feed("\b\b\b\u001b[32mZ", sink);

        Assert.Equal(expected, Describe(new[] { before! }));
        Assert.Equal(new[] { "Z" }, Texts(new[] { parser.Partial! }));
        Assert.Equal(EagerDescription("\u001b[32mZ"), Describe(new[] { parser.Partial! }));
    }

    [Fact]
    public void PartialSnapshotIsUnchangedByCarriageReturnOverwrite()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var sink = new List<OutputLine>();
        parser.Feed("abc", sink);
        OutputLine? before = parser.Partial;

        // The old text stays visible until new content arrives.
        parser.Feed("\r", sink);
        Assert.Equal("abc", parser.Partial!.PlainText);

        parser.Feed("xy", sink);

        Assert.Equal("abc", before!.PlainText);
        Assert.Equal("xy", parser.Partial!.PlainText);
        Assert.Empty(sink);
    }

    [Fact]
    public void PartialSnapshotIsUnchangedByForcedBreakAtMaxLength()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 4);
        var sink = new List<OutputLine>();
        parser.Feed("abcd", sink);
        OutputLine? before = parser.Partial;

        parser.Feed("e", sink);

        Assert.Equal("abcd", before!.PlainText);
        Assert.Equal(new[] { "abcd" }, Texts(sink));
        Assert.Equal("e", parser.Partial!.PlainText);
    }

    [Fact]
    public void PartialSnapshotIsUnchangedByForcedBreakThatSplitsSurrogatePair()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 3);
        var sink = new List<OutputLine>();
        parser.Feed("ab\uD83D", sink);
        OutputLine? before = parser.Partial;

        parser.Feed("\uDE00", sink);

        Assert.Equal("ab\uD83D", before!.PlainText);
        Assert.Equal(new[] { "ab" }, Texts(sink));
        Assert.Equal("\uD83D\uDE00", parser.Partial!.PlainText);
    }

    [Fact]
    public void PartialSnapshotIsUnchangedWhenLineCompletes()
    {
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var sink = new List<OutputLine>();
        parser.Feed("\u001b[1mbold\u001b[0m plain", sink);
        OutputLine? before = parser.Partial;
        string[] expected = EagerDescription("\u001b[1mbold\u001b[0m plain");

        parser.Feed("\nnext", sink);

        Assert.Equal(expected, Describe(new[] { before! }));
        Assert.Equal(expected, Describe(sink));
        Assert.Equal("next", parser.Partial!.PlainText);
    }

    [Fact]
    public void PartialSnapshotMatchesEagerPathForStyledInput()
    {
        const string input = "\u001b[31mred\u001b[0m \u001b[1;4mbold\u001b[22;24m \u001b[38;2;1;2;3mrgb\u001b[0m\tTAB\u001b[7m\u001b[41mX";
        string[] expected = EagerDescription(input);

        // Fed one character at a time, with a snapshot taken after every character.
        var parser = new AnsiLineParser(OutputSource.StdOut);
        var sink = new List<OutputLine>();
        OutputLine? last = null;
        foreach (char c in input)
        {
            parser.Feed(c.ToString(), sink);
            last = parser.Partial;
        }

        Assert.Empty(sink);
        Assert.Equal(expected, Describe(new[] { last! }));
    }

    [Fact]
    public void ConcurrentReadersSeeConsistentPartialSnapshots()
    {
        const int count = 4000;
        var parser = new AnsiLineParser(OutputSource.StdOut, maxLineLength: 1_000_000);
        var published = new ConcurrentQueue<(int Length, OutputLine Line)>();

        Task writer = Task.Run(() =>
        {
            var sink = new List<OutputLine>();
            for (int i = 1; i <= count; i++)
            {
                parser.Feed("a", sink);
                published.Enqueue((i, parser.Partial!));
            }
        });

        var seen = new List<(int Length, OutputLine Line)>();
        Task reader = Task.Run(() =>
        {
            while (true)
            {
                if (published.TryDequeue(out var item))
                {
                    CheckSnapshot(item.Length, item.Line);
                    seen.Add(item);
                }
                else if (writer.IsCompleted && published.IsEmpty)
                {
                    // Checked in this order, so nothing enqueued before the writer finished is missed.
                    break;
                }
                else
                {
                    Thread.Yield();
                }
            }
        });

        Assert.True(Task.WhenAll(writer, reader).Wait(TimeSpan.FromSeconds(10)), "readers or writer did not finish");

        // A second pass over everything read, now that the writer has finished.
        foreach (var (length, line) in seen)
        {
            CheckSnapshot(length, line);
        }

        Assert.Equal(count, seen.Count);
    }

    private static void CheckSnapshot(int length, OutputLine line)
    {
        string text = line.PlainText;
        Assert.Equal(length, text.Length);
        Assert.Equal(-1, text.AsSpan().IndexOfAnyExcept('a'));
        Assert.Equal(text, string.Concat(line.Spans.Select(s => s.Text)));
    }

    /// <summary>The display column where the 'X' after the single tab starts, and the number of spaces the tab wrote.</summary>
    private static (int Column, int Spaces) TabStop(string input)
    {
        string text = Texts(Parse(input))[0];
        int x = text.IndexOf('X', StringComparison.Ordinal);
        Assert.True(x >= 0);
        string before = text[..x];
        int spaces = before.Length - before.TrimEnd(' ').Length;
        return (DisplayWidth.Of(before), spaces);
    }
}
