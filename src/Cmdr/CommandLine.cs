using Cmdr.Core.Session;

namespace Cmdr;

/// <summary>What the command line asks for.</summary>
internal enum CommandLineAction
{
    /// <summary>Show the recent-configs picker (no arguments).</summary>
    Picker,

    /// <summary>Open the config at <see cref="CommandLineRequest.Path"/> in <see cref="CommandLineRequest.Mode"/>.</summary>
    Open,

    /// <summary>Print the version.</summary>
    Version,

    /// <summary>The arguments are not a valid combination: print the usage and exit 2.</summary>
    Usage,
}

/// <summary>The parsed command line. <see cref="Path"/> is set only for <see cref="CommandLineAction.Open"/>.</summary>
internal sealed record CommandLineRequest(CommandLineAction Action, string? Path, SessionMode Mode);

/// <summary>
/// Parses the arguments of <c>cmdr</c> (after the <c>--send-ctrl-c</c> helper, which is handled before). Pure: it touches
/// neither the file system nor the terminal.
/// </summary>
internal static class CommandLine
{
    /// <summary>
    /// Parses <paramref name="args"/>: no arguments show the picker; <c>--version</c> prints the version; one path opens it in run
    /// mode; <c>--edit</c> before or after one path opens it in edit mode. Any other combination, an empty path, or an argument
    /// starting with a dash (other than <c>--version</c> and <c>--edit</c>) is a usage error.
    /// </summary>
    public static CommandLineRequest Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            return new CommandLineRequest(CommandLineAction.Picker, null, SessionMode.Run);
        }

        if (args.Count == 1 && args[0] == "--version")
        {
            return new CommandLineRequest(CommandLineAction.Version, null, SessionMode.Run);
        }

        if (args.Count == 1 && IsPath(args[0]))
        {
            return Open(args[0], SessionMode.Run);
        }

        if (args.Count == 2 && args[0] == "--edit" && IsPath(args[1]))
        {
            return Open(args[1], SessionMode.Edit);
        }

        if (args.Count == 2 && args[1] == "--edit" && IsPath(args[0]))
        {
            return Open(args[0], SessionMode.Edit);
        }

        return new CommandLineRequest(CommandLineAction.Usage, null, SessionMode.Run);
    }

    private static CommandLineRequest Open(string path, SessionMode mode) => new(CommandLineAction.Open, path, mode);

    // A path is non-empty and does not look like an option.
    private static bool IsPath(string arg) => arg.Length > 0 && !arg.StartsWith('-');
}
