namespace Logvu.Core.Config;

/// <summary>
/// A logvu configuration file could not be read, parsed or validated. The message names the file and, for a setting, the
/// JSON path of the offending value.
/// </summary>
public sealed class LogvuConfigException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message, naming the configuration file.</param>
    public LogvuConfigException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message, naming the configuration file.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public LogvuConfigException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
