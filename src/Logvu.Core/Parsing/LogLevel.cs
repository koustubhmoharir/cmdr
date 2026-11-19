namespace Logvu.Core.Parsing;

/// <summary>The normalized severity of an event.</summary>
public enum LogLevel
{
    /// <summary>The level is absent or not one of the known spellings.</summary>
    Unknown,

    /// <summary>Trace, verbose, all or finest.</summary>
    Trace,

    /// <summary>Debug or fine.</summary>
    Debug,

    /// <summary>Info, information or notice.</summary>
    Info,

    /// <summary>Warn or warning.</summary>
    Warn,

    /// <summary>Error.</summary>
    Error,

    /// <summary>Fatal, critical, emergency, alert or severe.</summary>
    Fatal,
}

/// <summary>Normalizes level spellings from the supported producers.</summary>
public static class LogLevels
{
    /// <summary>
    /// Maps a level spelling to a <see cref="LogLevel"/>. The match is case-insensitive and ignores surrounding white
    /// space. A slog offset such as <c>INFO+2</c> or <c>DEBUG-4</c> maps to its base level. Anything else is
    /// <see cref="LogLevel.Unknown"/>.
    /// </summary>
    /// <param name="text">The level as written in the log, or <c>null</c> when absent.</param>
    public static LogLevel Normalize(string? text)
    {
        if (text is null)
        {
            return LogLevel.Unknown;
        }

        string name = StripOffset(text.Trim()).ToUpperInvariant();
        return name switch
        {
            "TRACE" or "VERBOSE" or "ALL" or "FINEST" => LogLevel.Trace,
            "DEBUG" or "FINE" => LogLevel.Debug,
            "INFO" or "INFORMATION" or "NOTICE" => LogLevel.Info,
            "WARN" or "WARNING" => LogLevel.Warn,
            "ERROR" => LogLevel.Error,
            "FATAL" or "CRITICAL" or "EMERGENCY" or "ALERT" or "SEVERE" => LogLevel.Fatal,
            _ => LogLevel.Unknown,
        };
    }

    /// <summary>Removes a trailing <c>+digits</c> or <c>-digits</c> offset, if present.</summary>
    private static string StripOffset(string text)
    {
        int sign = Math.Max(text.LastIndexOf('+'), text.LastIndexOf('-'));
        if (sign <= 0 || sign == text.Length - 1)
        {
            return text;
        }

        for (int i = sign + 1; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                return text;
            }
        }

        return text[..sign];
    }
}
