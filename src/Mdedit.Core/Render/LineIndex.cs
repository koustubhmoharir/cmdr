using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// The source lines of a text, for counting the lines between two offsets. A line break is a line feed, a carriage return
/// followed by a line feed (one break), or a carriage return on its own.
/// </summary>
internal sealed class LineIndex
{
    private readonly ImmutableArray<int> _starts;

    /// <summary>Indexes the line starts of a text.</summary>
    /// <param name="text">The source text.</param>
    public LineIndex(string text)
    {
        var starts = ImmutableArray.CreateBuilder<int>();
        starts.Add(0);
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n' || (text[index] == '\r' && (index + 1 >= text.Length || text[index + 1] != '\n')))
            {
                starts.Add(index + 1);
            }
        }

        _starts = starts.ToImmutable();
    }

    /// <summary>The number of the line that holds an offset, counted from 0. A line break belongs to the line it ends.</summary>
    /// <param name="offset">An offset in the text, at least 0.</param>
    public int LineOf(int offset)
    {
        int low = 0;
        int high = _starts.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (_starts[middle] <= offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low - 1;
    }
}
