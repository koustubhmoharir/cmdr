using System.Text;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>
/// Builds a command line that can be pasted into a shell: quoted for POSIX sh, or for PowerShell on Windows. The info pane
/// shows the program and its arguments (<see cref="ForEntry"/>); the copy also sets the working directory and the environment
/// that cmdr sets for the command, for that command only (<see cref="ForCopy"/>). Pure.
/// </summary>
/// <remarks>
/// Known limitation of the PowerShell form: Windows PowerShell 5.1, and PowerShell 7 in its Legacy native-argument
/// mode, drop empty-string arguments and mis-pass arguments that contain double quotes to native programs. PowerShell
/// 7.3 and later in the default mode pass both correctly, except to batch files (and cmd.exe), which always get the legacy
/// passing. A command whose program is a batch file and whose arguments would be mangled by it is therefore copied as the
/// command cmdr runs through cmd.exe (see <see cref="WindowsBatchCommandLine"/>), held in an environment variable so that
/// PowerShell does not pass it as an argument: cmd.exe expands the variable before it parses the line, so cmdr's cmd
/// escaping applies unchanged.
/// </remarks>
internal static class CommandLineFormatter
{
    private const string TypographicSingleQuotes = "‘’‚‛";

    /// <summary>The variable that holds the cmd.exe command in the batch-file form.</summary>
    internal const string BatchVariable = "CMDR_CMD";

    /// <summary>The argument that PowerShell's binder reads as its stop-parsing token.</summary>
    private const string PowerShellStopParsing = "--%";

    /// <summary>
    /// The reserved words of POSIX sh. In command position, sh reads one of these as syntax (<c>if</c>, <c>do</c>), so a
    /// program with that name is quoted, which makes sh read it as a command name.
    /// </summary>
    private static readonly HashSet<string> PosixReservedWords = new(StringComparer.Ordinal)
    {
        "case", "coproc", "do", "done", "elif", "else", "esac", "fi", "for", "function", "if", "in", "select", "then", "time",
        "until", "while",
    };

    /// <summary>
    /// The command line of a command: the program and its arguments, quoted for the platform. In PowerShell, a batch-file
    /// program whose arguments need it gets the cmd.exe form (see the remarks on this class).
    /// </summary>
    public static string ForEntry(CommandEntry entry, bool powerShell)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return Format(entry.Spec.Program, entry.Spec.Args, powerShell, powerShell ? BatchCommandFor(entry) : null);
    }

    /// <summary>
    /// The command line that the copy puts on the clipboard: the command with the working directory and the environment that
    /// cmdr sets for it, wrapped so that they apply to the command only (see <see cref="Wrap"/>). Falls back to
    /// <see cref="ForEntry"/> when the working directory cannot be resolved, as the info pane does.
    /// </summary>
    /// <param name="entry">The command to copy.</param>
    /// <param name="powerShell">The platform: POSIX sh, or PowerShell on Windows.</param>
    /// <param name="environment">The process environment the values are applied to.</param>
    /// <param name="pathSeparator">The value of <c>${pathsep}</c>.</param>
    public static string ForCopy(CommandEntry entry, bool powerShell, IReadOnlyDictionary<string, string?> environment, char pathSeparator)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(environment);

        LaunchSpec spec;
        try
        {
            spec = LaunchSpec.From(GroupTree.SpecChain(entry.Group), entry.Spec, entry.Source.Dir);
        }
        catch (Exception ex) when (DetailsText.IsPathError(ex))
        {
            return ForEntry(entry, powerShell);
        }

        IReadOnlyList<KeyValuePair<string, string?>> env =
            EffectiveEnv.Of(spec.EnvLevels, spec.ConfigDir, environment, powerShell, pathSeparator);

        if (!powerShell)
        {
            return Wrap(spec.WorkingDirectory, env, Format(spec.Program, spec.Args, powerShell: false), powerShell: false);
        }

        // The batch-file form: the cmd.exe command goes in a variable, so that PowerShell does not pass it as an argument, and
        // cmd.exe expands the variable before it parses the line.
        string? batchCommand = WindowsBatchCommandLine.ForCopy(spec);
        if (batchCommand is null)
        {
            return Wrap(spec.WorkingDirectory, env, Format(spec.Program, spec.Args, powerShell: true), powerShell: true);
        }

        env = WithVariable(env, BatchVariable, batchCommand);
        return Wrap(spec.WorkingDirectory, env, $"cmd /d /v:off /c '%{BatchVariable}%'", powerShell: true);
    }

    /// <summary>
    /// Wraps a command so that it runs in <paramref name="workingDirectory"/> with the environment <paramref name="env"/>, and
    /// nothing else changes: the parent shell keeps its directory and variables. An entry with a null value removes the variable.
    /// POSIX runs the command in a subshell, with <c>env</c> for the variables; PowerShell saves the variables, sets them, runs
    /// the command and restores them in a <c>finally</c> block. Pure.
    /// </summary>
    /// <param name="workingDirectory">The directory the command runs in.</param>
    /// <param name="env">The variables to set (or remove, with a null value), in order.</param>
    /// <param name="command">An already formatted command line.</param>
    /// <param name="powerShell">The shell: PowerShell, or POSIX sh.</param>
    internal static string Wrap(string workingDirectory, IReadOnlyList<KeyValuePair<string, string?>> env, string command, bool powerShell)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(command);

        return powerShell
            ? WrapPowerShell(workingDirectory, env, command)
            : WrapPosix(workingDirectory, env, command);
    }

    private static string WrapPosix(string workingDirectory, IReadOnlyList<KeyValuePair<string, string?>> env, string command)
    {
        string cd = "cd " + QuotePosix(workingDirectory) + " && ";
        if (env.Count == 0)
        {
            return "(" + cd + command + ")";
        }

        // Removals come first, then assignments, each in the order of the environment.
        var parts = new List<string> { "env" };
        foreach (KeyValuePair<string, string?> entry in env.Where(entry => entry.Value is null))
        {
            parts.Add("-u");
            parts.Add(QuotePosix(entry.Key));
        }

        foreach (KeyValuePair<string, string?> entry in env.Where(entry => entry.Value is not null))
        {
            parts.Add(QuotePosix(entry.Key + "=" + entry.Value));
        }

        parts.Add(command);
        return "(" + cd + string.Join(' ', parts) + ")";
    }

    private static string WrapPowerShell(string workingDirectory, IReadOnlyList<KeyValuePair<string, string?>> env, string command)
    {
        string location = "Push-Location -LiteralPath " + QuotePowerShell(workingDirectory);
        if (env.Count == 0)
        {
            return $"& {{ {location}; try {{ {command} }} finally {{ Pop-Location }} }}";
        }

        // Every name is a .NET string argument, so each one is always quoted, unlike the bare form of QuotePowerShell.
        string names = string.Join(", ", env.Select(entry => SingleQuoted(entry.Key)));
        string sets = string.Concat(env.Select(entry =>
            $"[Environment]::SetEnvironmentVariable({SingleQuoted(entry.Key)}, {PowerShellValue(entry.Value)}); "));
        return $"& {{ $saved = @{{}}; foreach ($name in @({names})) {{ $saved[$name] = [Environment]::GetEnvironmentVariable($name) }}; " +
            $"{location}; try {{ {sets}{command} }} finally {{ foreach ($name in $saved.Keys) {{ " +
            "[Environment]::SetEnvironmentVariable($name, $saved[$name]) }; Pop-Location } }";
    }

    /// <summary>A PowerShell value for a .NET call: a single-quoted string, or <c>$null</c> to remove the variable.</summary>
    private static string PowerShellValue(string? value) => value is null ? "$null" : SingleQuoted(value);

    /// <summary>The copy of <paramref name="env"/> with <paramref name="name"/> set to <paramref name="value"/>: the existing entry is replaced in place, otherwise it is added last.</summary>
    private static List<KeyValuePair<string, string?>> WithVariable(
        IReadOnlyList<KeyValuePair<string, string?>> env,
        string name,
        string value)
    {
        var result = env.ToList();
        int index = result.FindIndex(entry => string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase));
        var variable = new KeyValuePair<string, string?>(name, value);
        if (index < 0)
        {
            result.Add(variable);
        }
        else
        {
            result[index] = variable;
        }

        return result;
    }

    /// <summary>
    /// The program and its arguments, each quoted for the given shell and separated by spaces. With a
    /// <paramref name="batchCommand"/> (PowerShell only), the line runs that command through cmd.exe instead.
    /// </summary>
    public static string Format(string program, IReadOnlyList<string> args, bool powerShell, string? batchCommand)
    {
        if (powerShell && batchCommand is not null)
        {
            // The variable is saved and restored, so a value the user had is kept; assigning $null removes it again if it was absent.
            return $"& {{ $saved = $env:{BatchVariable}; $env:{BatchVariable} = {QuotePowerShell(batchCommand)}; " +
                $"try {{ cmd /d /v:off /c '%{BatchVariable}%' }} finally {{ $env:{BatchVariable} = $saved }} }}";
        }

        return Format(program, args, powerShell);
    }

    /// <summary>The cmd.exe command of a batch-file command that needs it (see <see cref="WindowsBatchCommandLine"/>), or null.</summary>
    private static string? BatchCommandFor(CommandEntry entry)
    {
        try
        {
            LaunchSpec spec = LaunchSpec.From(GroupTree.SpecChain(entry.Group), entry.Spec, entry.Source.Dir);
            return WindowsBatchCommandLine.ForCopy(spec);
        }
        catch (ArgumentException)
        {
            return null; // the command would not start either; show the plain form
        }
    }

    /// <summary>The program and its arguments, each quoted for the given shell and separated by spaces.</summary>
    public static string Format(string program, IReadOnlyList<string> args, bool powerShell)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(args);

        if (powerShell && args.Contains(PowerShellStopParsing))
        {
            // PowerShell's binder takes an argument whose value is --% as the stop-parsing token even when it is quoted, so
            // the arguments go in one array expression, whose elements the binder does not inspect.
            return $"& {QuotePowerShell(program)} @({string.Join(", ", args.Select(arg => QuotePowerShell(arg)))})";
        }

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

        return SingleQuoted(token);
    }

    /// <summary>The token in single quotes, with every single quote and typographic single quote doubled.</summary>
    private static string SingleQuoted(string token)
    {
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
    /// The program as PowerShell must see it: bare when PowerShell would read it as a command name, otherwise quoted and invoked
    /// with the call operator <c>&amp;</c>, because PowerShell does not run a quoted string as a command. Bare needs only safe
    /// characters and a start that is not an expression: a letter or <c>_</c>, a drive or a path (<c>.\</c>, <c>..\</c>,
    /// <c>\</c>, <c>/</c>), and not a keyword (<c>foreach</c> would start a loop, <c>123</c> is a number, <c>-x</c> an operator).
    /// </summary>
    private static string FormatProgram(string program, bool powerShell)
    {
        if (!powerShell)
        {
            // sh reads an assignment such as X=1 in command position as an assignment, not as a command; quoting it makes it one.
            bool syntax = PosixReservedWords.Contains(program) || program.Contains('=');
            return syntax ? "'" + program.Replace("'", "'\\''") + "'" : QuotePosix(program);
        }

        string quoted = QuotePowerShell(program);
        bool bare = !quoted.StartsWith('\'') && StartsLikeACommand(program) && !PowerShellKeywords.Contains(program);
        return bare ? quoted : "& " + (quoted.StartsWith('\'') ? quoted : "'" + quoted + "'");
    }

    private static bool StartsLikeACommand(string program) =>
        char.IsLetter(program[0])
        || program[0] == '_'
        || program[0] is '\\' or '/'
        || program.StartsWith(@".\", StringComparison.Ordinal)
        || program.StartsWith("./", StringComparison.Ordinal)
        || program.StartsWith(@"..\", StringComparison.Ordinal)
        || program.StartsWith("../", StringComparison.Ordinal);

    /// <summary>PowerShell's reserved words, which are statements, not commands, at the start of a line.</summary>
    private static readonly HashSet<string> PowerShellKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "begin", "break", "catch", "class", "clean", "configuration", "continue", "data", "define", "do", "dynamicparam",
        "else", "elseif", "end", "enum", "exit", "filter", "finally", "for", "foreach", "from", "function", "hidden", "if",
        "in", "inlinescript", "parallel", "param", "process", "return", "sequence", "static", "switch", "throw", "trap",
        "try", "until", "using", "var", "while", "workflow",
    };

    private static string Quote(string token, bool powerShell) => powerShell ? QuotePowerShell(token) : QuotePosix(token);

    private static bool IsPosixBare(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
            or '_' or '@' or '%' or '+' or '=' or ':' or ',' or '.' or '/' or '-';

    private static bool IsPowerShellBare(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
            or '_' or '.' or '/' or '\\' or ':' or '=' or '+' or '-';
}
