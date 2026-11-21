using System.Globalization;
using System.Reflection;

namespace Shared.Processes;

/// <summary>
/// The command line that starts the Ctrl+C helper (<c>--send-ctrl-c &lt;pid&gt;</c>) in the cmdr executable.
/// </summary>
/// <param name="FileName">Executable to start: the cmdr apphost, or the <c>dotnet</c> host.</param>
/// <param name="PrefixArgs">Arguments placed before the helper arguments (the cmdr assembly when run through <c>dotnet</c>).</param>
internal sealed record CtrlCHelperCommand(string FileName, IReadOnlyList<string> PrefixArgs)
{
    internal const string HelperFlag = "--send-ctrl-c";

    /// <summary>Describes how to start this cmdr process again in helper mode.</summary>
    /// <exception cref="InvalidOperationException">Running under <c>dotnet</c> without an entry assembly path.</exception>
    public static CtrlCHelperCommand ForCurrentProcess() =>
        Resolve(Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unknown."), Assembly.GetEntryAssembly()?.Location);

    /// <summary>
    /// Chooses the helper command for a process started from <paramref name="processPath"/>. When the process is the
    /// <c>dotnet</c> host, the entry assembly must be passed to it; for an apphost the executable is cmdr itself.
    /// </summary>
    /// <remarks>Accepts both Windows and Unix separators so the decision can be tested on any OS.</remarks>
    /// <exception cref="InvalidOperationException">Running under <c>dotnet</c> without an entry assembly path.</exception>
    public static CtrlCHelperCommand Resolve(string processPath, string? entryAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(processPath);

        if (!string.Equals(FileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return new CtrlCHelperCommand(processPath, Array.Empty<string>());
        }

        if (string.IsNullOrEmpty(entryAssemblyPath))
        {
            throw new InvalidOperationException("Cannot start the Ctrl+C helper: the dotnet host is running without an entry assembly path.");
        }

        return new CtrlCHelperCommand(processPath, new[] { entryAssemblyPath });
    }

    /// <summary>The complete argument list for the helper that signals <paramref name="pid"/>.</summary>
    public IReadOnlyList<string> ArgumentsFor(int pid)
    {
        var args = new List<string>(PrefixArgs) { HelperFlag, pid.ToString(CultureInfo.InvariantCulture) };
        return args;
    }

    private static string FileNameWithoutExtension(string path)
    {
        int separator = path.LastIndexOfAny(new[] { '\\', '/' });
        string name = separator >= 0 ? path[(separator + 1)..] : path;
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
