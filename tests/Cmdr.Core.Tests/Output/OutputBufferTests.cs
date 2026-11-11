using Cmdr.Core.Output;
using static Cmdr.Core.Tests.Output.OutputTestHelpers;

namespace Cmdr.Core.Tests.Output;

public class OutputBufferTests
{
    [Fact]
    public void NewBufferIsEmpty()
    {
        var buffer = new OutputBuffer(3);
        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, buffer.FirstLineNumber);
        Assert.Equal(0, buffer.NextLineNumber);
        Assert.Empty(buffer.GetLines(0, 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveCapacityIsRejected(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutputBuffer(capacity));
    }

    [Fact]
    public void AppendKeepsOrder()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("a"), Line("b") });
        buffer.Append(new[] { Line("c") });
        Assert.Equal(new[] { "a", "b", "c" }, BufferTexts(buffer));
        Assert.Equal(3, buffer.Count);
        Assert.Equal(3, buffer.NextLineNumber);
    }

    [Fact]
    public void OldestLinesAreDroppedWhenFull()
    {
        var buffer = new OutputBuffer(3);
        buffer.Append(new[] { Line("0"), Line("1"), Line("2"), Line("3"), Line("4") });

        Assert.Equal(3, buffer.Count);
        Assert.Equal(2, buffer.FirstLineNumber);
        Assert.Equal(5, buffer.NextLineNumber);
        Assert.Equal(new[] { "2", "3", "4" }, BufferTexts(buffer));
    }

    [Fact]
    public void RingWrapsCorrectlyAcrossManyAppends()
    {
        var buffer = new OutputBuffer(3);
        for (int i = 0; i < 17; i++)
        {
            buffer.Append(new[] { Line(i.ToString()) });
        }

        Assert.Equal(14, buffer.FirstLineNumber);
        Assert.Equal(new[] { "14", "15", "16" }, BufferTexts(buffer));
        Assert.Equal(new[] { "15", "16" }, Texts(buffer.GetLines(1, 10)));
    }

    [Fact]
    public void GetLinesReturnsRequestedRange()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("a"), Line("b"), Line("c"), Line("d") });
        Assert.Equal(new[] { "b", "c" }, Texts(buffer.GetLines(1, 2)));
    }

    [Fact]
    public void GetLinesClampsOutOfRangeRequests()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("a"), Line("b"), Line("c") });

        Assert.Equal(new[] { "c" }, Texts(buffer.GetLines(2, 100)));
        Assert.Equal(new[] { "a", "b" }, Texts(buffer.GetLines(-1, 3)));
        Assert.Equal(new[] { "a" }, Texts(buffer.GetLines(-5, 6)));
        Assert.Empty(buffer.GetLines(3, 5));
        Assert.Empty(buffer.GetLines(100, 5));
        Assert.Empty(buffer.GetLines(0, 0));
        Assert.Empty(buffer.GetLines(0, -4));
        Assert.Empty(buffer.GetLines(-10, 5));
    }

    [Fact]
    public void GetLinesNeverThrowsForLargeStartOrCount()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("a") });
        Assert.Equal(new[] { "a" }, Texts(buffer.GetLines(0, int.MaxValue)));
        Assert.Empty(buffer.GetLines(int.MinValue, int.MaxValue));
        Assert.Empty(buffer.GetLines(int.MaxValue, int.MaxValue));
    }

    [Fact]
    public void PartialLinesFollowCompletedLinesStdOutBeforeStdErr()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("done") });
        buffer.SetPartial(OutputSource.StdErr, Line("err-partial", OutputSource.StdErr));
        buffer.SetPartial(OutputSource.StdOut, Line("out-partial"));

        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { "done", "out-partial", "err-partial" }, BufferTexts(buffer));
        Assert.Equal(OutputSource.StdErr, buffer.GetLines(2, 1)[0].Source);
    }

    [Fact]
    public void PartialCanBeReplacedAndCleared()
    {
        var buffer = new OutputBuffer(10);
        buffer.SetPartial(OutputSource.StdOut, Line("a"));
        buffer.SetPartial(OutputSource.StdOut, Line("ab"));
        Assert.Equal(new[] { "ab" }, BufferTexts(buffer));

        buffer.SetPartial(OutputSource.StdOut, null);
        Assert.Empty(buffer.GetLines(0, 10));
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void PartialsDoNotCountTowardLineNumbers()
    {
        var buffer = new OutputBuffer(10);
        buffer.SetPartial(OutputSource.StdOut, Line("partial"));
        Assert.Equal(0, buffer.NextLineNumber);
        Assert.Equal(0, buffer.FirstLineNumber);
    }

    [Fact]
    public void SetPartialRejectsNonStreamSources()
    {
        var buffer = new OutputBuffer(10);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetPartial(OutputSource.System, Line("x")));
    }

    [Fact]
    public void GetCompletedLinesFromReturnsLinesAtOrAfterNumber()
    {
        var buffer = new OutputBuffer(10);
        buffer.Append(new[] { Line("0"), Line("1"), Line("2"), Line("3") });
        buffer.SetPartial(OutputSource.StdOut, Line("partial"));

        Assert.Equal(new[] { "2", "3" }, Texts(buffer.GetCompletedLinesFrom(2)));
        Assert.Equal(new[] { "0", "1", "2", "3" }, Texts(buffer.GetCompletedLinesFrom(0)));
        Assert.Empty(buffer.GetCompletedLinesFrom(4));
        Assert.Empty(buffer.GetCompletedLinesFrom(100));
    }

    [Fact]
    public void GetCompletedLinesFromClampsToFirstRetainedLine()
    {
        var buffer = new OutputBuffer(2);
        buffer.Append(new[] { Line("0"), Line("1"), Line("2") });
        Assert.Equal(new[] { "1", "2" }, Texts(buffer.GetCompletedLinesFrom(-5)));
        Assert.Equal(new[] { "1", "2" }, Texts(buffer.GetCompletedLinesFrom(0)));
    }

    [Fact]
    public void GetCompletedLinesFromExcludesPartials()
    {
        var buffer = new OutputBuffer(10);
        buffer.SetPartial(OutputSource.StdErr, Line("partial", OutputSource.StdErr));
        Assert.Empty(buffer.GetCompletedLinesFrom(0));
    }

    [Fact]
    public void AbsoluteNumbersStayStableWhileScrolling()
    {
        var buffer = new OutputBuffer(3);
        buffer.Append(new[] { Line("0"), Line("1"), Line("2") });
        long anchor = 1;
        buffer.Append(new[] { Line("3") });
        Assert.Equal("1", Texts(buffer.GetCompletedLinesFrom(anchor))[0]);

        buffer.Append(new[] { Line("4") });
        Assert.Equal("2", Texts(buffer.GetCompletedLinesFrom(anchor))[0]);
    }

    [Fact]
    public void VersionIncrementsOnEveryMutation()
    {
        var buffer = new OutputBuffer(3);
        long v0 = buffer.Version;

        buffer.Append(new[] { Line("a") });
        long v1 = buffer.Version;
        Assert.True(v1 > v0);

        buffer.SetPartial(OutputSource.StdOut, Line("p"));
        long v2 = buffer.Version;
        Assert.True(v2 > v1);

        buffer.SetPartial(OutputSource.StdOut, null);
        long v3 = buffer.Version;
        Assert.True(v3 > v2);

        buffer.Clear();
        Assert.True(buffer.Version > v3);
    }

    [Fact]
    public void ReadsDoNotChangeVersion()
    {
        var buffer = new OutputBuffer(3);
        buffer.Append(new[] { Line("a") });
        long version = buffer.Version;
        _ = buffer.GetLines(0, 10);
        _ = buffer.GetCompletedLinesFrom(0);
        _ = buffer.Count;
        Assert.Equal(version, buffer.Version);
    }

    [Fact]
    public void ClearDropsEverythingAndAdvancesFirstLineNumber()
    {
        var buffer = new OutputBuffer(5);
        buffer.Append(new[] { Line("a"), Line("b") });
        buffer.SetPartial(OutputSource.StdOut, Line("p"));

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.GetLines(0, 10));
        Assert.Empty(buffer.GetCompletedLinesFrom(0));
        Assert.Equal(2, buffer.FirstLineNumber);
        Assert.Equal(2, buffer.NextLineNumber);

        buffer.Append(new[] { Line("c") });
        Assert.Equal(new[] { "c" }, BufferTexts(buffer));
        Assert.Equal(2, buffer.FirstLineNumber);
        Assert.Equal(3, buffer.NextLineNumber);
    }

    [Fact]
    public void AppendOfEmptyListDoesNotChangeVersion()
    {
        var buffer = new OutputBuffer(3);
        long version = buffer.Version;
        buffer.Append(Array.Empty<OutputLine>());
        Assert.Equal(version, buffer.Version);
    }
}
