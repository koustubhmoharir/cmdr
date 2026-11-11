using System.Globalization;
using Cmdr.Core.Processes;

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

    Console.WriteLine("cmdr: TUI not implemented yet");
    return 0;
}
