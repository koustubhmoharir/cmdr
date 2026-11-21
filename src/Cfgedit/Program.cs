using System.Reflection;

namespace Cfgedit;

internal static class Program
{
    private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs the command line. Returns the process exit code.</summary>
    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args is ["--version"])
        {
            stdout.WriteLine("cfgedit " + Version());
            return 0;
        }

        return Usage(stderr);
    }

    private static int Usage(TextWriter stderr)
    {
        stderr.WriteLine("usage: cfgedit <file> [--format xml|json|jsonc|json5]");
        stderr.WriteLine("       cfgedit --check <file> [--format xml|json|jsonc|json5]");
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
