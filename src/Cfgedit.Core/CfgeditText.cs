using Shared.Core;

namespace Cfgedit.Core;

/// <summary>
/// The <see cref="SourceText"/> operations that cfgedit calls, with cfgedit's error type. A <see cref="TextFileException"/> becomes a
/// <see cref="CfgeditException"/> with the same message, <see cref="CfgeditException.Path"/>, <see cref="CfgeditException.Line"/> and
/// <see cref="CfgeditException.Column"/>, and the original as its inner exception. Cfgedit code calls these instead of the
/// <see cref="SourceText"/> members, so that its callers keep catching <see cref="CfgeditException"/> only.
/// </summary>
public static class CfgeditText
{
    /// <summary><see cref="SourceText.Decode"/>, with cfgedit's error type.</summary>
    /// <exception cref="CfgeditException">As for <see cref="SourceText.Decode"/>.</exception>
    public static SourceText Decode(ReadOnlySpan<byte> bytes, string? path = null)
    {
        try
        {
            return SourceText.Decode(bytes, path);
        }
        catch (TextFileException ex)
        {
            throw ToCfgedit(ex);
        }
    }

    /// <summary><see cref="SourceText.Encode"/>, with cfgedit's error type.</summary>
    /// <exception cref="CfgeditException">As for <see cref="SourceText.Encode"/>.</exception>
    public static byte[] Encode(SourceText text)
    {
        try
        {
            return text.Encode();
        }
        catch (TextFileException ex)
        {
            throw ToCfgedit(ex);
        }
    }

    /// <summary>Runs an operation on a <see cref="TextFile"/>, converting its <see cref="TextFileException"/> to a <see cref="CfgeditException"/>.</summary>
    /// <exception cref="CfgeditException">The operation failed with a <see cref="TextFileException"/>.</exception>
    public static T Run<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (TextFileException ex)
        {
            throw ToCfgedit(ex);
        }
    }

    /// <inheritdoc cref="Run{T}(Func{T})"/>
    public static void Run(Action operation)
    {
        try
        {
            operation();
        }
        catch (TextFileException ex)
        {
            throw ToCfgedit(ex);
        }
    }

    /// <summary>The cfgedit form of a text file error: the same message, position and path, with the original as inner exception.</summary>
    public static CfgeditException ToCfgedit(TextFileException ex) =>
        new(ex.Message, ex) { Path = ex.Path, Line = ex.Line, Column = ex.Column };
}
