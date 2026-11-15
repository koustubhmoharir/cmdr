using Cmdr.Core.Config;

namespace Cmdr.Core.Processes;

/// <summary>
/// Decides when a copied Windows command line has to go through cmd.exe. PowerShell passes the arguments of a batch file
/// (<c>.cmd</c>/<c>.bat</c>, such as the <c>npm.cmd</c> shims) in its legacy mode: it adds quotes only around whitespace, so
/// cmd.exe then parses <c>&amp; | &lt; &gt; ^ %</c>, mangles embedded quotes and drops empty arguments. (A batch file that
/// reads <c>%1</c>..<c>%9</c> also splits unquoted arguments on <c>, ; =</c>; those alone do not switch to the cmd.exe form.) For such a command the copy uses the command cmdr itself runs through cmd.exe (see
/// <see cref="WindowsCmdEscaping"/>); every other command keeps the plain PowerShell form.
/// </summary>
public static class WindowsBatchCommandLine
{
    // Not , ; = : they split arguments only for a batch file that reads %1..%9; shims such as npm's pass %*, which keeps
    // them, and options like --port=3000 are common enough that flagging them would send most npm commands through cmd.exe.
    private const string Specials = "\"&|<>^%!()`";

    /// <summary>
    /// The command cmdr runs after <c>cmd /d /v:off /s /c</c> for <paramref name="spec"/>, when its program resolves to a batch
    /// file and one of its arguments would not reach it intact through PowerShell; otherwise null (also when the program does
    /// not resolve, and for an argument with a line break, which cmd.exe cannot receive: such a command does not start either).
    /// The program is resolved as a launch would resolve it: its working directory, then the PATH of its environment,
    /// with PATHEXT. Arguments are checked first, so the file system is only searched for the few commands that need it.
    /// </summary>
    public static string? ForCopy(LaunchSpec spec) => ForCopy(spec, CurrentEnvironment(), File.Exists);

    /// <summary>As <see cref="ForCopy(LaunchSpec)"/>, with the base environment and file check given (for tests).</summary>
    internal static string? ForCopy(LaunchSpec spec, IDictionary<string, string?> baseEnvironment, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Args.Any(WindowsCommandResolver.ContainsLineBreak) || !spec.Args.Any(NeedsCmd))
        {
            return null;
        }

        var environment = new Dictionary<string, string?>(baseEnvironment, StringComparer.OrdinalIgnoreCase);
        EnvExpansion.ApplyLevels(environment, spec.EnvLevels, spec.ConfigDir, ';');
        environment.TryGetValue("PATH", out string? path);
        environment.TryGetValue("PATHEXT", out string? pathExt);

        string? resolved;
        try
        {
            resolved = WindowsCommandResolver.Resolve(spec.Program, spec.WorkingDirectory, path, pathExt, fileExists);
        }
        catch (ArgumentException)
        {
            return null; // a root-relative program with a working directory that has no drive: it would not start either
        }

        return resolved is not null && WindowsCommandResolver.IsBatchFile(resolved)
            ? WindowsCmdEscaping.BuildCommand(resolved, spec.Args)
            : null;
    }

    /// <summary>
    /// True when PowerShell's legacy passing would not deliver <paramref name="arg"/> to a batch file intact: it is empty, it
    /// has a character cmd.exe treats specially (or an embedded quote), or it has whitespace and ends with a backslash (the
    /// quote PowerShell adds would be escaped).
    /// </summary>
    internal static bool NeedsCmd(string arg) =>
        arg.Length == 0
        || arg.AsSpan().IndexOfAny(Specials) >= 0
        || (arg.EndsWith('\\') && arg.Any(char.IsWhiteSpace));

    private static Dictionary<string, string?> CurrentEnvironment()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = (string?)entry.Value;
        }

        return environment;
    }
}
