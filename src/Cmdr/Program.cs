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

    if (args is not [var configPath])
    {
        Console.Error.WriteLine("usage: cmdr <config.json>");
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
