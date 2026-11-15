using System.Globalization;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Tui;

return Run(args);

static int Run(string[] args)
{
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

    if (args is ["--version"])
    {
        Console.WriteLine("cmdr " + Version());
        return 0;
    }

    if (args is [])
    {
        RecentFiles? recent = LoadRecent();
        if (recent is null || recent.Files.Count == 0)
        {
            return Usage();
        }

        TuiHost.RunPicker(recent);
        return 0;
    }

    if (args is not [var configPath] || configPath.Length == 0)
    {
        return Usage();
    }

    Session session;
    try
    {
        session = Session.Load(configPath);
    }
    catch (ConfigException ex)
    {
        Console.Error.WriteLine($"cmdr: {ex.Message}");
        return 1;
    }

    RecordRecent(configPath);

    using (session)
    {
        TuiHost.Run(session);
    }

    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("usage: cmdr <config.json>");
    Console.Error.WriteLine("       cmdr               pick a recently used config");
    Console.Error.WriteLine("       cmdr --version");
    return 2;
}

// The recent list, or null when there is no store location.
static RecentFiles? LoadRecent()
{
    string? store = RecentFiles.DefaultStorePath;
    return store is null ? null : RecentFiles.Load(store);
}

// Moves an opened config to the front of the recent list. Skipped without a store; a failed save is ignored.
static void RecordRecent(string configPath)
{
    RecentFiles? recent = LoadRecent();
    if (recent is null)
    {
        return;
    }

    recent.Add(configPath);
    recent.TrySave();
}

// The version set in Directory.Build.props.
static string Version() =>
    typeof(TuiHost).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion ?? "unknown";
