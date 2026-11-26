using System.Reflection;

namespace Mdedit;

internal static class Program
{
    private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs the command line. Returns the process exit code.</summary>
    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args is ["--version"])
        {
            stdout.WriteLine("mdedit " + Version());
            return 0;
        }

        return Usage(stderr);
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
