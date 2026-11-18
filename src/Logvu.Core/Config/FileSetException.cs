namespace Logvu.Core.Config;

/// <summary>The set of files of a configuration could not be enumerated, for example because its directory is unreadable.</summary>
public sealed class FileSetException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message, naming the directory.</param>
    public FileSetException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message, naming the directory.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public FileSetException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
