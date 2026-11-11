using System.Text;

namespace Cmdr.Tui;

/// <summary>Builds a command line that can be pasted into a shell: quoted for POSIX sh, or for the Windows C runtime (MSVCRT). Pure.</summary>
internal static class CommandLineFormatter
{
    private static readonly char[] WindowsDelimiters = { ' ', '\t', '\n', '\r', '"' };

    /// <summary>The program and its arguments, each quoted for the given platform and separated by spaces.</summary>
    public static string Format(string program, IReadOnlyList<string> args, bool windows)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(args);

        var parts = new List<string>(args.Count + 1) { Quote(program, windows) };
        foreach (string arg in args)
        {
            parts.Add(Quote(arg, windows));
        }

        return string.Join(' ', parts);
    }

    /// <summary>A token quoted for POSIX sh: bare when it only has safe characters, otherwise single-quoted.</summary>
    public static string QuotePosix(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (token.Length > 0 && token.All(IsPosixBare))
        {
            return token;
        }

        return "'" + token.Replace("'", "'\\''") + "'";
    }

    /// <summary>A token quoted for the MSVCRT argument parser used by most Windows programs.</summary>
    public static string QuoteWindows(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (token.Length > 0 && token.IndexOfAny(WindowsDelimiters) < 0)
        {
            return token;
        }

        // Backslashes count only before a quote or at the end (before the closing quote); both are doubled.
        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in token)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    private static string Quote(string token, bool windows) => windows ? QuoteWindows(token) : QuotePosix(token);

    private static bool IsPosixBare(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
            or '_' or '@' or '%' or '+' or '=' or ':' or ',' or '.' or '/' or '-';
}
