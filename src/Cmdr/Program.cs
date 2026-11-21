using System.Globalization;
using System.Runtime.CompilerServices;
using Cmdr;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Shared.Core;
using Shared.Processes;
using Shared.Tui;

return Run(args);

static int Run(string[] args)
{
    // The width measure of the terminal UI is installed first, before any command output is parsed: the output is laid out by
    // the widths it gives. Shared.Tui's module initializer would otherwise run only when something from that assembly is used.
    RuntimeHelpers.RunModuleConstructor(typeof(DisplayWidthSetup).Module.ModuleHandle);

    if (args is ["--send-ctrl-c", ..])
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("cmdr: --send-ctrl-c is only supported on Windows.");
            return 1;
        }

        if (args.Length != 2 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
        {
            Console.Error.WriteLine("usage: cmdr --send-ctrl-c <pid>");
            return 1;
        }

        return CtrlCHelper.Run(pid);
    }

    CommandLineRequest request = CommandLine.Parse(args);
    switch (request.Action)
    {
        case CommandLineAction.Version:
            Console.WriteLine("cmdr " + Version());
            return 0;

        case CommandLineAction.Picker:
        {
            RecentFiles? recent = LoadRecent();
            if (recent is null || recent.Files.Count == 0)
            {
                return Usage();
            }

            TuiHost.RunPicker(recent);
            return 0;
        }

        case CommandLineAction.Open:
            return OpenConfig(request.Path!, request.Mode);

        default:
            return Usage();
    }
}

// Opens a config from the command line. A missing file in run mode is offered for creation in the TUI; any other load error is
// printed before the TUI starts.
static int OpenConfig(string configPath, SessionMode mode)
{
    RecentFiles? recent = LoadRecent();
    if (mode == SessionMode.Run && !File.Exists(configPath))
    {
        TuiHost.RunMissingConfig(configPath, recent);
        return 0;
    }

    Session session;
    try
    {
        session = TuiHost.LoadAndRecord(configPath, mode, recent);
    }
    catch (ConfigException ex)
    {
        Console.Error.WriteLine($"cmdr: {ex.Message}");
        return 1;
    }

    TuiHost.RunMain(session);
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("usage: cmdr [--edit] <config.json>");
    Console.Error.WriteLine("       cmdr               pick a recently used config");
    Console.Error.WriteLine("       cmdr --version");
    return 2;
}

// The recent list, or null when there is no store location.
static RecentFiles? LoadRecent()
{
    string? store = RecentFiles.DefaultStorePathFor("cmdr");
    return store is null ? null : RecentFiles.Load(store);
}

// The version set in Directory.Build.props.
static string Version() =>
    typeof(TuiHost).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion ?? "unknown";
