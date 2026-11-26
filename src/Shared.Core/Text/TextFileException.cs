namespace Shared.Core;

/// <summary>
/// A text file operation failed in an expected way: a file could not be read, written or decoded, or its text could not be
/// encoded. The message is meant for the user. <see cref="Path"/>, <see cref="Line"/> and <see cref="Column"/> say where the
/// failure is when that is known.
/// </summary>
public sealed class TextFileException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message for the user.</param>
    public TextFileException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message for the user.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TextFileException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>The file the failure concerns, or null when it does not concern a file.</summary>
    public string? Path { get; init; }

    /// <summary>The 1-based line of the failure, or null when it is not known.</summary>
    public int? Line { get; init; }

    /// <summary>The 1-based column of the failure, or null when it is not known.</summary>
    public int? Column { get; init; }
}
