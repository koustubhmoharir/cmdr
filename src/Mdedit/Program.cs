using System.Reflection;
using Mdedit.Tui;
using Shared.Core;

namespace Mdedit;

internal static class Program
{
    private static int Main(string[] args) =>
        Run(
            args,
            Console.Out,
            Console.Error,
            MdeditHost.RunPicker,
            (file, recent) => MdeditHost.Run(file, recent),
            (path, loadRecent) => MdeditHost.RunNew(path, loadRecent),
            LoadRecent);

    /// <summary>
    /// Runs the command line. With no arguments, <paramref name="showPicker"/> is given the recent list (null when there is no store)
    /// and shows the picker. With a path that does not exist, <paramref name="runNew"/> runs the new-file flow for it. With a file, the file
    /// is loaded, recorded in the recent list, and <paramref name="runDocument"/> runs its window. A file that does not load is reported on
    /// <paramref name="stderr"/> and the window is not run. Returns the process exit code.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="stdout">Receives the version.</param>
    /// <param name="stderr">Receives the usage and the load errors.</param>
    /// <param name="showPicker">Shows the picker over the given recent list.</param>
    /// <param name="runDocument">Runs the window of a loaded file, with the recent list that Ctrl+O picks from.</param>
    /// <param name="runNew">Runs the new-file flow for a path that does not exist, with the loader of the recent list.</param>
    /// <param name="loadRecent">Loads the recent list, or returns null when there is no store.</param>
    internal static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        Action<RecentFiles?> showPicker,
        Action<OpenedFile, RecentFiles?> runDocument,
        Action<string, Func<RecentFiles?>> runNew,
        Func<RecentFiles?> loadRecent)
    {
        if (args is ["--version"])
        {
            stdout.WriteLine("mdedit " + Version());
            return 0;
        }

        if (args is [])
        {
            showPicker(loadRecent());
            return 0;
        }

        if (!TryParseArguments(args, out string? path))
        {
            return Usage(stderr);
        }

        if (MdeditHost.Missing(path!))
        {
            runNew(path!, loadRecent);
            return 0;
        }

        OpenedFile file;
        try
        {
            file = MdeditHost.Load(path!);
        }
        catch (TextFileException ex)
        {
            stderr.WriteLine("mdedit: " + MdeditHost.Describe(ex));
            return 1;
        }

        runDocument(file, MdeditHost.RecordRecent(loadRecent, path!));
        return 0;
    }

    // The one file argument: non-empty and not an option. Anything else is a usage error.
    private static bool TryParseArguments(string[] args, out string? path)
    {
        path = null;
        if (args is [{ Length: > 0 } arg] && !arg.StartsWith('-'))
        {
            path = arg;
            return true;
        }

        return false;
    }

    // The recent list, or null when there is no store location.
    private static RecentFiles? LoadRecent()
    {
        string? store = RecentFiles.DefaultStorePathFor("mdedit");
        return store is null ? null : RecentFiles.Load(store);
    }

    private static int Usage(TextWriter stderr)
    {
        stderr.WriteLine("usage: mdedit <file>");
        stderr.WriteLine("       mdedit               pick a recently used file");
        stderr.WriteLine("       mdedit --version");
        return 2;
    }

    private static string Version() =>
        typeof(Program).Assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "unknown";
}
