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
    [InlineData("\u001B[mx")]
    [InlineData("\u001B[0mx")]
    [InlineData("\u001B[00mx")]
    public void EmptyOrZeroParameterResets(string input)
    {
        Assert.Equal(SpanStyle.Default, StyleOf(input));
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
    [InlineData("\u001B[38;5;abcmx")]
    [InlineData("\u001B[38;7;1mx")]
    [InlineData("\u001B[38:5:300mx")]
    [InlineData("\u001B[38:2:1:2mx")]
    [InlineData("\u001B[38:2:1:2:3:4:5mx")]
    [InlineData("\u001B[38;99999999999;1;2;3mx")]
    public void InvalidColorSpecsAreIgnored(string input)
    {
        Assert.Equal(AnsiColor.Default, StyleOf(input).Foreground);
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
        Assert.Equal(SpanStyle.Default, StyleOf("\u001B[21;38x;99;108mx"));
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
}
