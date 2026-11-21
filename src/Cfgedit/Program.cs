using System.Reflection;
using Cfgedit.Core;
using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Shared.Core;

namespace Cfgedit;

internal static class Program
{
    private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs the command line with the recent-files picker, the main window and the new-file flow. Returns the process exit code.</summary>
    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
        Run(args, stdout, stderr, CfgeditHost.RunPicker, (file, recent) => CfgeditHost.Run(file, recent), LoadRecent, CfgeditHost.RunNew);

    /// <summary>
    /// Runs the command line. With no arguments, <paramref name="showPicker"/> is given the recent list (null when there is no store)
    /// and shows the picker. With a file, the file is loaded, recorded in the recent list, and <paramref name="runDocument"/> runs its
    /// window. A file that does not load is reported on <paramref name="stderr"/> and the window is not run. Returns the process exit code.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="stdout">Receives the version.</param>
    /// <param name="stderr">Receives the usage and the load errors.</param>
    /// <param name="showPicker">Shows the picker over the given recent list.</param>
    /// <param name="runDocument">Runs the window of a loaded file, with the recent list that Ctrl+O picks from.</param>
    /// <param name="loadRecent">Loads the recent list, or returns null when there is no store.</param>
    /// <param name="runNew">
    /// Runs the new-file flow for a path that does not exist, with the format named on the command line (or null) and the recent list
    /// loader. Null uses <see cref="CfgeditHost.RunNew"/>.
    /// </param>
    internal static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        Action<RecentFiles?> showPicker,
        Action<OpenedFile, RecentFiles?> runDocument,
        Func<RecentFiles?> loadRecent,
        Action<string, IConfigFormat?, Func<RecentFiles?>>? runNew = null)
    {
        if (args is ["--version"])
        {
            stdout.WriteLine("cfgedit " + Version());
            return 0;
        }

        if (args is [])
        {
            showPicker(loadRecent());
            return 0;
        }

        if (!TryParseArguments(args, out string? path, out string? formatName))
        {
            return Usage(stderr);
        }

        IConfigFormat? format = null;
        if (formatName is not null)
        {
            format = Formats.FromName(formatName);
            if (format is null)
            {
                stderr.WriteLine($"cfgedit: unknown format '{formatName}'; use xml, json, jsonc or json5");
                return 2;
            }
        }

        // A path that does not exist starts the new-file flow, which asks inside the application whether to create it.
        if (!File.Exists(path!) && !Directory.Exists(path!))
        {
            try
            {
                (runNew ?? CfgeditHost.RunNew)(path!, format, loadRecent);
            }
            catch (CfgeditException ex)
            {
                stderr.WriteLine("cfgedit: " + CfgeditHost.Describe(ex));
                return 1;
            }

            return 0;
        }

        OpenedFile file;
        try
        {
            file = CfgeditHost.Load(path!, format);
        }
        catch (CfgeditException ex)
        {
            stderr.WriteLine("cfgedit: " + CfgeditHost.Describe(ex));
            return 1;
        }

        runDocument(file, CfgeditHost.RecordRecent(loadRecent, path!));
        return 0;
    }

    // The file and the --format option, in either order. An option needs its value. Anything else that starts with '-', an empty
    // argument, a second file and a missing file are usage errors.
    private static bool TryParseArguments(string[] args, out string? path, out string? format)
    {
        path = null;
        format = null;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--format")
            {
                if (i + 1 >= args.Length)
                {
                    return false;
                }

                format = args[++i];
            }
            else if (arg.Length > 0 && !arg.StartsWith('-') && path is null)
            {
                path = arg;
            }
            else
            {
                return false;
            }
        }

        return path is not null;
    }

    // The recent list, or null when there is no store location.
    private static RecentFiles? LoadRecent()
    {
        string? store = RecentFiles.DefaultStorePathFor("cfgedit");
        return store is null ? null : RecentFiles.Load(store);
    }

    private static int Usage(TextWriter stderr)
    {
        stderr.WriteLine("usage: cfgedit <file> [--format xml|json|jsonc|json5]");
        stderr.WriteLine("       cfgedit               pick a recently used file");
        stderr.WriteLine("       cfgedit --version");
        return 2;
    }

    private static string Version() =>
        typeof(Program).Assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "unknown";
}
