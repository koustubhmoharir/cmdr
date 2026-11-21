namespace Shared.Processes;

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
    /// extension in order. Last, a base whose final segment has an extension that is not in PATHEXT is tried as is,
    /// so that an explicitly named file such as <c>tool.bin</c> still runs. A base with no extension is never tried bare.
    /// A null result is a start failure for the caller: it must not fall back to a native CreateProcess search.
    /// A drive-relative path (<c>C:tools\run</c>: a drive without a root) would be resolved against that drive's current
    /// directory, which the child's environment does not define: as the program it is rejected, and as a PATH entry it is
    /// skipped.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="program"/> is a drive-relative path.</exception>
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

        if (IsDriveRelative(program))
        {
            throw new ArgumentException(
                $"'{program}' is relative to the current directory of drive {program[..2]}, which cmdr does not know. " +
                $"Use a full path such as '{program[..2]}\\{program[2..].TrimStart('\\', '/')}', or a path relative to the working directory.",
                nameof(program));
        }

        if (IsPathLike(program))
        {
            string basePath = IsRooted(program) ? RootRelativePath(program, workingDirectory) : JoinPath(workingDirectory, program);
            return FirstExisting(Candidates(basePath, extensions), fileExists);
        }

        foreach (string directory in SearchDirectories(workingDirectory, pathVariable))
        {
            if (IsDriveRelative(directory))
            {
                continue;
            }

            string absoluteDirectory = IsRooted(directory)
                ? RootRelativePath(directory, workingDirectory)
                : JoinPath(workingDirectory, directory);
            string? found = FirstExisting(Candidates(JoinPath(absoluteDirectory, program), extensions), fileExists);
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
        string? extension = FinalExtension(basePath);
        bool hasPathExtExtension = extension is not null && extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

        if (hasPathExtExtension)
        {
            yield return basePath;
        }

        foreach (string pathExt in extensions)
        {
            yield return basePath + pathExt;
        }

        if (extension is not null && !hasPathExtExtension)
        {
            yield return basePath; // explicitly named file with a non-PATHEXT extension: tried last
        }
    }

    /// <summary>The extension of the final segment, including its dot (<c>.bin</c>), or null when the segment has no dot.</summary>
    private static string? FinalExtension(string path)
    {
        int lastSeparator = path.LastIndexOfAny(['\\', '/']);
        int dot = path.LastIndexOf('.');
        return dot < 0 || dot < lastSeparator ? null : path[dot..];
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

    /// <summary>A drive-absolute path (<c>C:\</c> or <c>C:/</c>) or a UNC path: one that names its file without a current directory.</summary>
    private static bool IsFullyQualified(string path) =>
        IsUnc(path) || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/');

    /// <summary>A drive letter and colon not followed by a separator: <c>C:</c>, <c>C:tools\run</c>.</summary>
    private static bool IsDriveRelative(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path.Length == 2 || path[2] is not ('\\' or '/'));

    /// <summary>Contains '\\' or '/'.</summary>
    private static bool IsPathLike(string program) => program.Contains('\\') || program.Contains('/');

    /// <summary>A drive-absolute path (<c>C:\</c> or <c>C:/</c>), a UNC path, or a path rooted at the current drive (<c>\x</c>).</summary>
    private static bool IsRooted(string program) =>
        IsUnc(program)
        || program.StartsWith('\\')
        || program.StartsWith('/')
        || (program.Length >= 3
            && char.IsAsciiLetter(program[0])
            && program[1] == ':'
            && (program[2] == '\\' || program[2] == '/'));

    /// <summary>Expands a drive-rooted path such as <c>\tools\run.cmd</c> using the working directory's drive.</summary>
    private static string RootRelativePath(string path, string workingDirectory)
    {
        if (IsUnc(path))
        {
            return path.Replace('/', '\\'); // Windows accepts either separator; the candidates use '\\'
        }

        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        {
            return path;
        }

        if (workingDirectory.Length >= 2 && char.IsAsciiLetter(workingDirectory[0]) && workingDirectory[1] == ':')
        {
            return workingDirectory[..2] + path.Replace('/', '\\');
        }

        if (IsUnc(workingDirectory))
        {
            string[] parts = workingDirectory[2..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $@"\\{parts[0]}\{parts[1]}\" + path.TrimStart('\\', '/').Replace('/', '\\');
            }
        }

        throw new ArgumentException("A root-relative Windows path requires a drive-qualified working directory.", nameof(workingDirectory));
    }

    /// <summary>
    /// A UNC path: two leading separators, either of which may be '/' (Windows treats <c>//server/share</c> like
    /// <c>\\server\share</c>).
    /// </summary>
    private static bool IsUnc(string path) => path.Length >= 2 && (path[0] is '\\' or '/') && (path[1] is '\\' or '/');

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
    /// Message for a program that <see cref="Resolve"/> could not find. Names the working directory for a relative
    /// path-like program, and lists the PATHEXT extensions that were tried (the resolver default when unset).
    /// </summary>
    internal static string NotFoundMessage(string program, string workingDirectory, string? pathExtVariable)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        string detail = !IsPathLike(program)
            ? $"'{program}' was not found in the working directory or on PATH"
            : IsRooted(program)
                ? $"'{program}' was not found"
                : $"'{program}' was not found relative to the working directory '{workingDirectory}'";

        return $"{detail} (PATHEXT extensions tried: {string.Join(';', ParsePathExt(pathExtVariable))})";
    }

    /// <summary>
    /// Decides how a program is started once resolved. A resolved <c>.cmd</c> or <c>.bat</c> file is run through the
    /// command interpreter with a raw, escaped command line; anything else is started directly with the argument list.
    /// The interpreter is <paramref name="comSpec"/> when it is a full path, otherwise <c>cmd.exe</c> in
    /// <paramref name="systemDirectory"/>: a bare or relative name would be found by CreateProcess's own search, which
    /// looks in cmdr's directory and current directory before the system directory.
    /// </summary>
    /// <param name="program">The program as configured.</param>
    /// <param name="resolvedPath">The path returned by <see cref="Resolve"/>. A program that did not resolve is not planned.</param>
    /// <param name="comSpec">The ComSpec environment variable, or null.</param>
    /// <param name="systemDirectory">The Windows system directory (<c>Environment.SystemDirectory</c>).</param>
    /// <param name="args">The configured arguments.</param>
    public static WindowsLaunchPlan PlanLaunch(
        string program,
        string resolvedPath,
        string? comSpec,
        string systemDirectory,
        IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(systemDirectory);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(resolvedPath);
        ArgumentNullException.ThrowIfNull(args);

        if (IsBatchFile(resolvedPath))
        {
            for (int i = 0; i < args.Count; i++)
            {
                if (ContainsLineBreak(args[i]))
                {
                    throw new ArgumentException(
                        $"argument {i + 1} contains a line break, which cmd.exe cannot pass to a batch file");
                }
            }

            return new WindowsLaunchPlan(
                FileName: comSpec is not null && IsFullyQualified(comSpec) ? comSpec : JoinPath(systemDirectory, "cmd.exe"),
                Arguments: WindowsCmdEscaping.BuildCmdArguments(resolvedPath, args),
                ArgumentList: Array.Empty<string>(),
                InputAfterInterrupt: BatchTerminateAnswer);
        }

        return new WindowsLaunchPlan(
            FileName: resolvedPath,
            Arguments: null,
            ArgumentList: args,
            InputAfterInterrupt: null);
    }

    /// <summary>
    /// Refuses a working directory that cmd.exe cannot use for a batch file: cmd.exe refuses a UNC current directory and
    /// silently runs in C:\Windows instead. A UNC path is one that starts with two separators, except the local device forms
    /// <c>\\?\C:\</c> and <c>\\.\C:\</c>; <c>\\?\UNC\server\share</c> is UNC.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="workingDirectory"/> is a UNC path.</exception>
    internal static void CheckBatchWorkingDirectory(string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);

        if (IsUncForBatchFile(workingDirectory))
        {
            throw new ArgumentException(
                $"{workingDirectory} is a UNC path, which cmd.exe cannot use as the working directory of a batch file; " +
                "map a drive letter to the share and use that path.");
        }
    }

    private static bool IsUncForBatchFile(string path)
    {
        string normalized = path.Replace('/', '\\');
        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        // \\?\C:\ and \\.\C:\ name a local drive; any other path that starts with two separators is a network path.
        bool localDrive = normalized.Length >= 6
            && normalized[2] is ('?' or '.')
            && normalized[3] == '\\'
            && char.IsAsciiLetter(normalized[4])
            && normalized[5] == ':';
        return !localDrive;
    }

    /// <summary>True when the argument contains a CR or LF, which cmd.exe cannot receive intact.</summary>
    internal static bool ContainsLineBreak(string arg) => arg.AsSpan().IndexOfAny('\r', '\n') >= 0;

    internal static bool IsBatchFile(string path) =>
        path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
}
