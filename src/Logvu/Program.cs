using System.Reflection;
using Logvu.Core.Config;
using Logvu.Tui;
using Shared.Core;

namespace Logvu;

internal static class Program
{
    private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs the command line. Returns the process exit code.</summary>
    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args is ["--version"])
        {
            stdout.WriteLine("logvu " + Version());
            return 0;
        }

        if (args is [var configPath] && configPath.Length > 0 && !configPath.StartsWith('-'))
        {
            return Open(configPath, stderr);
        }

        return Usage(stderr);
    }

    // Loads the config, records it in the recent list and runs the main window. A config that does not load is reported and the
    // TUI is not started.
    private static int Open(string configPath, TextWriter stderr)
    {
        LogvuConfig config;
        try
        {
            config = LogvuConfig.Load(configPath);
        }
        catch (LogvuConfigException ex)
        {
            stderr.WriteLine($"logvu: {ex.Message}");
            return 1;
        }

        RecordRecent(config.ConfigPath);
        LogvuHost.Run(config);
        return 0;
    }

    // Moves an opened config to the front of logvu's recent list. Skipped without a store location; a failed save is ignored.
    private static void RecordRecent(string configPath)
    {
        string? store = RecentFiles.DefaultStorePathFor("logvu");
        if (store is null)
        {
            return;
        }

        RecentFiles recent = RecentFiles.Load(store);
        recent.Add(configPath);
        recent.TrySave();
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
