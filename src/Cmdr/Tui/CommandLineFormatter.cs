using System.Text;
using Cmdr.Core.Config;

namespace Cmdr.Tui;

/// <summary>
/// Builds a command line that can be pasted into a shell: quoted for POSIX sh, or for PowerShell on Windows (so the
/// info pane and the copy show the same text, and that text means the same thing when pasted into PowerShell). Pure.
/// </summary>
/// <remarks>
/// Known limitation of the PowerShell form: Windows PowerShell 5.1, and PowerShell 7 in its Legacy native-argument
/// mode, drop empty-string arguments and mis-pass arguments that contain double quotes to native programs. PowerShell
/// 7.3 and later in the default mode pass both correctly.
/// </remarks>
internal static class CommandLineFormatter
{
    private const string TypographicSingleQuotes = "‘’‚‛";

    /// <summary>The command line of a command definition: the program and its arguments, quoted for the platform.</summary>
    public static string ForSpec(CommandSpec spec, bool powerShell)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return Format(spec.Program, spec.Args, powerShell);
    }

    /// <summary>The program and its arguments, each quoted for the given shell and separated by spaces.</summary>
    public static string Format(string program, IReadOnlyList<string> args, bool powerShell)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(args);

        var parts = new List<string>(args.Count + 1) { FormatProgram(program, powerShell) };
        foreach (string arg in args)
        {
            parts.Add(Quote(arg, powerShell));
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

    /// <summary>
    /// A token quoted for PowerShell: bare when it only has letters, digits and <c>_ . / \ : = + -</c>, otherwise
    /// single-quoted with every single quote doubled. The typographic single quotes (U+2018 to U+201B) are doubled too,
    /// because PowerShell treats them as quotes inside a single-quoted string.
    /// </summary>
    public static string QuotePowerShell(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (token.Length > 0 && token.All(IsPowerShellBare))
        {
            return token;
        }

        var sb = new StringBuilder(token.Length + 2).Append('\'');
        foreach (char c in token)
        {
            if (c == '\'' || TypographicSingleQuotes.Contains(c))
            {
                sb.Append(c);
            }

            sb.Append(c);
        }

        return sb.Append('\'').ToString();
    }

    /// <summary>
    /// The program as PowerShell must see it: bare when it needs no quoting, otherwise invoked with the call operator
    /// <c>&amp;</c> because PowerShell does not run a quoted string as a command.
    /// </summary>
    private static string FormatProgram(string program, bool powerShell)
    {
        if (!powerShell)
        {
            return QuotePosix(program);
        }

        string quoted = QuotePowerShell(program);
        return quoted.StartsWith('\'') ? "& " + quoted : quoted;
    }

    private static string Quote(string token, bool powerShell) => powerShell ? QuotePowerShell(token) : QuotePosix(token);

    private static bool IsPosixBare(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
            or '_' or '@' or '%' or '+' or '=' or ':' or ',' or '.' or '/' or '-';

    private static bool IsPowerShellBare(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
            or '_' or '.' or '/' or '\\' or ':' or '=' or '+' or '-';
}
