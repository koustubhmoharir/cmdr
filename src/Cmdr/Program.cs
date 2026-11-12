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

    if (args is not [var configPath])
    {
        Console.Error.WriteLine("usage: cmdr <config.json>");
        Console.Error.WriteLine("       cmdr --version");
        return 2;
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

    using (session)
    {
        TuiHost.Run(session);
    }

    return 0;
}

// The version set in Directory.Build.props.
static string Version() =>
    typeof(TuiHost).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion ?? "unknown";
