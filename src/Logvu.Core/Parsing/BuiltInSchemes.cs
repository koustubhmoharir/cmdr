namespace Logvu.Core.Parsing;

/// <summary>The schemes that ship with logvu, for the producers the team uses.</summary>
public static class BuiltInSchemes
{
    private const string DateMilliseconds = @"[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2},[0-9]{3}";
    private const string DateSeconds = @"[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2}";
    private const string MillisecondFormat = "yyyy-MM-dd HH:mm:ss,fff";
    private const string SecondFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>log4net <c>%-5level %date [%thread] %logger - %message%newline</c>.</summary>
    public static readonly TextScheme Log4NetCore = new(
        "log4net-core",
        $@"(?<level>[A-Za-z]+) +(?<time>{DateMilliseconds}) \[(?<thread>[^\]]*)\] (?<logger>\S+) - (?<message>.*)",
        [MillisecondFormat]);

    /// <summary>log4net with the web properties: version, user, source and local address before the thread.</summary>
    public static readonly TextScheme Log4NetWeb = new(
        "log4net-web",
        $@"(?<level>[A-Za-z]+) +(?<time>{DateMilliseconds}) v\.(?<version>\S+) u\.(?<user>\S+) (?<sourceAddress>\S+) (?<localAddress>\S+) \[(?<thread>[^\]]*)\] (?<logger>\S+) - (?<message>.*)",
        [MillisecondFormat]);

    /// <summary>log4net <c>[%date{yyyy-MM-dd HH:mm:ss}] %-5level %message</c>, as the scheduler writes it.</summary>
    public static readonly TextScheme Log4NetScheduler = new(
        "log4net-scheduler",
        $@"\[(?<time>{DateSeconds})\] (?<level>[A-Za-z]+) +(?<message>.*)",
        [SecondFormat]);

    /// <summary>log4net with the date first: <c>%date [%thread] %level %logger - %message</c>.</summary>
    public static readonly TextScheme Log4NetDateFirst = new(
        "log4net-date-first",
        $@"(?<time>{DateMilliseconds}) \[(?<thread>[^\]]*)\] (?<level>[A-Za-z]+) (?<logger>\S+) - (?<message>.*)",
        [MillisecondFormat]);

    /// <summary>Serilog <c>JsonFormatter</c>, one object per line.</summary>
    public static readonly SerilogJsonScheme SerilogJson = new();

    /// <summary>Go <c>log/slog</c> <c>JSONHandler</c>, one object per line.</summary>
    public static readonly SlogJsonScheme SlogJson = new();

    /// <summary>Every built-in scheme, in detection order.</summary>
    public static readonly IReadOnlyList<ILogScheme> All =
    [
        Log4NetCore,
        Log4NetWeb,
        Log4NetScheduler,
        Log4NetDateFirst,
        SerilogJson,
        SlogJson,
    ];

    /// <summary>Finds a built-in scheme by name, ignoring case.</summary>
    /// <returns>The scheme, or <c>null</c> when there is none with that name.</returns>
    public static ILogScheme? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return All.FirstOrDefault(scheme => string.Equals(scheme.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
