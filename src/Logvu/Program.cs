using System.Reflection;
using Logvu.Core.Config;
using Logvu.Tui;
using Shared.Core;

namespace Logvu;

internal static class Program
{
    private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs the command line with the recent-configs picker. Returns the process exit code.</summary>
    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
        Run(args, stdout, stderr, LogvuHost.RunPicker);

    /// <summary>
    /// Runs the command line. With no arguments, <paramref name="showPicker"/> is given the recent list (null when there is no
    /// store) and the picker is shown. Returns the process exit code.
    /// </summary>
    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr, Action<RecentFiles?> showPicker)
    {
        if (args is ["--version"])
        {
            stdout.WriteLine("logvu " + Version());
            return 0;
        }

        if (args is [])
        {
            showPicker(LoadRecent());
            return 0;
        }

        if (args is [var configPath] && configPath.Length > 0 && !configPath.StartsWith('-'))
        {
            return Open(configPath, stderr);
        }

        return Usage(stderr);
    }

    // Loads the config, records it in the recent list and runs the main window, which switches sets from that list. A config that
    // does not load is reported and the TUI is not started.
    private static int Open(string configPath, TextWriter stderr)
    {
        LogvuConfigFile file;
        try
        {
            file = LogvuConfigFile.Read(configPath);
        }
        catch (LogvuConfigException ex)
        {
            stderr.WriteLine($"logvu: {ex.Message}");
            return 1;
        }

        LogvuConfig? config = file.HasEnvScript
            ? ResolveWithCtrlC(file, stderr)
            : ResolveConfig(file, stderr, CancellationToken.None);
        if (config is null)
        {
            return 1;
        }

        RecentFiles? recent = RecordRecent(config.ConfigPath);
        LogvuHost.Run(config, recent);
        return 0;
    }

    /// <summary>
    /// Resolves a config whose <c>envScript</c> runs, with Ctrl+C cancelling the script. The script runs in its own process group or
    /// job, so a terminal Ctrl+C would not reach it, and logvu would exit and leave it running. The handler is removed on every path.
    /// </summary>
    private static LogvuConfig? ResolveWithCtrlC(LogvuConfigFile file, TextWriter stderr)
    {
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            // Handled here, so the process does not end before the script is killed.
            e.Cancel = true;
            cancel.Cancel();
        };

        Console.CancelKeyPress += onCancel;
        try
        {
            return ResolveConfig(file, stderr, cancel.Token);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>
    /// Runs the config's <c>envScript</c>, if it has one, and returns the resolved config. Writes the progress line first, then
    /// reports a cancel or a failure as <c>logvu: …</c> on <paramref name="stderr"/>. Returns null on a cancel or a failure. A config
    /// without a script writes nothing and completes at once.
    /// </summary>
    /// <param name="file">The config as read.</param>
    /// <param name="stderr">Receives the progress line and the messages.</param>
    /// <param name="cancellationToken">Stops the script; its process tree is killed.</param>
    internal static LogvuConfig? ResolveConfig(LogvuConfigFile file, TextWriter stderr, CancellationToken cancellationToken)
    {
        if (file.HasEnvScript)
        {
            stderr.WriteLine($"logvu: running envScript of {file.ConfigPath}…");
        }

        try
        {
            return file.ResolveAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stderr.WriteLine("logvu: cancelled");
            return null;
        }
        catch (LogvuConfigException ex)
        {
            stderr.WriteLine($"logvu: {ex.Message}");
            return null;
        }
    }

    // The recent list, or null when there is no store location.
    private static RecentFiles? LoadRecent()
    {
        string? store = RecentFiles.DefaultStorePathFor("logvu");
        return store is null ? null : RecentFiles.Load(store);
    }

    // Moves an opened config to the front of logvu's recent list and returns the list. Without a store location nothing is recorded
    // and the result is null; a failed save is ignored.
    private static RecentFiles? RecordRecent(string configPath)
    {
        RecentFiles? recent = LoadRecent();
        if (recent is null)
        {
            return null;
        }

        recent.Add(configPath);
        recent.TrySave();
        return recent;
    }

    private static int Usage(TextWriter stderr)
    {
        stderr.WriteLine("usage: logvu <config.json>");
        stderr.WriteLine("       logvu               pick a recently used config");
        stderr.WriteLine("       logvu --version");
        return 2;
    }

    private static string Version() =>
        typeof(Program).Assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "unknown";
}
