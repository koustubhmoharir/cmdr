namespace Cmdr.Core.Processes;

/// <summary>How a Windows launch is performed: the file to start, and its arguments either raw or as a list.</summary>
/// <param name="FileName">File to start (the resolved program, or the command interpreter for batch files).</param>
/// <param name="Arguments">Raw argument string, or null when <paramref name="ArgumentList"/> is used.</param>
/// <param name="ArgumentList">Arguments passed as a list (empty when <paramref name="Arguments"/> is used).</param>
/// <param name="InputAfterInterrupt">Stdin text written after a Ctrl+C, or null.</param>
internal sealed record WindowsLaunchPlan(
    string FileName,
    string? Arguments,
    IReadOnlyList<string> ArgumentList,
    string? InputAfterInterrupt);

/// <summary>
/// Finds the file Windows would run for a program name, applying PATHEXT so that shims such as <c>npm.cmd</c> are
/// found (CreateProcess only finds <c>.exe</c> files when <c>UseShellExecute</c> is false).
/// </summary>
/// <remarks>
/// Pure: the file system is injected, and path handling uses Windows rules on every host OS, so the logic is
/// testable on Linux. Paths are joined with '\\' and never passed to <see cref="System.IO.Path"/>.
/// </remarks>
internal static class WindowsCommandResolver
{
    /// <summary>Answer to cmd.exe's "Terminate batch job (Y/N)?" prompt after a Ctrl+C.</summary>
    internal const string BatchTerminateAnswer = "Y\r\n";

    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD";
    private const char Separator = '\\';

    /// <summary>
    /// Returns the full path of the first existing candidate for <paramref name="program"/>, or null.
    /// </summary>
    /// <remarks>
    /// A program containing '\\' or '/' is path-like and is resolved only against <paramref name="workingDirectory"/>
    /// when relative. A bare name is searched in <paramref name="workingDirectory"/> first, then in the PATH entries.
    /// For each base path, a base that already has a PATHEXT extension is tried as is, then base + each PATHEXT
    /// extension in order. A base without such an extension is never tried bare.
    /// </remarks>
    public static string? Resolve(
        string program,
        string workingDirectory,
        string? pathVariable,
        string? pathExtVariable,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(fileExists);

        IReadOnlyList<string> extensions = ParsePathExt(pathExtVariable);

        if (IsPathLike(program))
        {
            string basePath = IsRooted(program) ? program : JoinPath(workingDirectory, program);
            return FirstExisting(Candidates(basePath, extensions), fileExists);
        }

        foreach (string directory in SearchDirectories(workingDirectory, pathVariable))
        {
            string? found = FirstExisting(Candidates(JoinPath(directory, program), extensions), fileExists);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ParsePathExt(string? pathExtVariable)
    {
        string source = string.IsNullOrEmpty(pathExtVariable) ? DefaultPathExt : pathExtVariable;
        return source.Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The working directory first, then each non-empty PATH entry with surrounding double quotes removed.</summary>
    private static IEnumerable<string> SearchDirectories(string workingDirectory, string? pathVariable)
    {
        yield return workingDirectory;

        if (string.IsNullOrEmpty(pathVariable))
        {
            yield break;
        }

        foreach (string entry in pathVariable.Split(';'))
        {
            string directory = entry.Trim('"');
            if (directory.Length > 0)
            {
                yield return directory;
            }
        }
    }

    private static IEnumerable<string> Candidates(string basePath, IReadOnlyList<string> extensions)
    {
        if (HasPathExtExtension(basePath, extensions))
        {
            yield return basePath;
        }

        foreach (string extension in extensions)
        {
            yield return basePath + extension;
        }
    }

    private static bool HasPathExtExtension(string path, IReadOnlyList<string> extensions)
    {
        int lastSeparator = path.LastIndexOfAny(['\\', '/']);
        int dot = path.LastIndexOf('.');
        if (dot < 0 || dot < lastSeparator)
        {
            return false; // no dot in the final segment
        }

        return extensions.Contains(path[dot..], StringComparer.OrdinalIgnoreCase);
    }

    private static string? FirstExisting(IEnumerable<string> candidates, Func<string, bool> fileExists)
    {
        foreach (string candidate in candidates)
        {
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Contains '\\' or '/'.</summary>
    private static bool IsPathLike(string program) => program.Contains('\\') || program.Contains('/');

    /// <summary>A drive-absolute path (<c>C:\</c> or <c>C:/</c>) or a UNC path (<c>\\</c>).</summary>
    private static bool IsRooted(string program) =>
        program.StartsWith(@"\\", StringComparison.Ordinal)
        || (program.Length >= 3
            && char.IsAsciiLetter(program[0])
            && program[1] == ':'
            && (program[2] == '\\' || program[2] == '/'));

    /// <summary>Joins with '\\'. An empty directory yields the name unchanged; a directory already ending in a separator is not given another.</summary>
    private static string JoinPath(string directory, string name)
    {
        if (directory.Length == 0)
        {
            return name;
        }

        char last = directory[^1];
        return last == Separator || last == '/' ? directory + name : directory + Separator + name;
    }

    /// <summary>
    /// Decides how a program is started once resolved. A resolved <c>.cmd</c> or <c>.bat</c> file is run through the
    /// command interpreter (<paramref name="comSpec"/>, or <c>cmd.exe</c> when missing) with a raw, escaped command
    /// line; anything else is started directly with the argument list. An unresolved program keeps its name so that
    /// Process.Start reports the failure.
    /// </summary>
    /// <param name="program">The program as configured.</param>
    /// <param name="resolvedPath">The path returned by <see cref="Resolve"/>, or null.</param>
    /// <param name="comSpec">The ComSpec environment variable, or null.</param>
    /// <param name="args">The configured arguments.</param>
    public static WindowsLaunchPlan PlanLaunch(string program, string? resolvedPath, string? comSpec, IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(args);

        if (resolvedPath is not null && IsBatchFile(resolvedPath))
        {
            return new WindowsLaunchPlan(
                FileName: string.IsNullOrEmpty(comSpec) ? "cmd.exe" : comSpec,
                Arguments: WindowsCmdEscaping.BuildCmdArguments(resolvedPath, args),
                ArgumentList: Array.Empty<string>(),
                InputAfterInterrupt: BatchTerminateAnswer);
        }

        return new WindowsLaunchPlan(
            FileName: resolvedPath ?? program,
            Arguments: null,
            ArgumentList: args,
            InputAfterInterrupt: null);
    }

    private static bool IsBatchFile(string path) =>
        path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
}
