namespace Logvu.Tui;

/// <summary>The shaping of copied text, shared by the detail's copy and the timeline's copy of an event. Pure.</summary>
internal static class CopyText
{
    /// <summary>
    /// Removes the leading spaces that the non-blank lines of a text share: the smallest count of them is taken from every line. A text
    /// with no non-blank line, or none indented, is returned as it is.
    /// </summary>
    /// <param name="text">The text, its lines joined by <c>\n</c>.</param>
    public static string Unindent(string text)
    {
        string[] lines = text.Split('\n');
        int common = int.MaxValue;
        foreach (string line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            common = Math.Min(common, LeadingSpaces(line));
        }

        if (common is int.MaxValue or 0)
        {
            return text;
        }

        return string.Join('\n', lines.Select(line => line[Math.Min(common, LeadingSpaces(line))..]));
    }

    private static int LeadingSpaces(string line)
    {
        int count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }
}
