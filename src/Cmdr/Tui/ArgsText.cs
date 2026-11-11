namespace Cmdr.Tui;

/// <summary>The args field of the command dialog: one argument per line. Pure.</summary>
/// <remarks>
/// A final empty argument is written as a trailing newline, so that <see cref="Parse"/> can tell it from the newline
/// that ends the last line: <c>["a", ""]</c> is written as <c>"a\n\n"</c>. Empty text means no arguments.
/// </remarks>
internal static class ArgsText
{
    /// <summary>Joins the arguments with newlines.</summary>
    public static string Format(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string text = string.Join("\n", args);
        if (args.Count > 0 && args[^1].Length == 0)
        {
            text += "\n";
        }

        return text;
    }

    /// <summary>
    /// Splits the text on newlines. A trailing <c>\r</c> is removed from each line. A single empty line at the end
    /// (the newline that ends the last line) is ignored; other empty lines are empty arguments.
    /// </summary>
    public static IReadOnlyList<string> Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        var args = text.Split('\n')
            .Select(line => line.EndsWith('\r') ? line[..^1] : line)
            .ToList();

        if (args[^1].Length == 0)
        {
            args.RemoveAt(args.Count - 1);
        }

        return args;
    }
}
