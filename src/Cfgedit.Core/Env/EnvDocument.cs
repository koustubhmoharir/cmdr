using Cfgedit.Core.Text;

namespace Cfgedit.Core.Env;

/// <summary>
/// A parsed .env file: the lossless concrete syntax tree of a source text. The items are blank lines, comment lines and
/// entries, in source order. Their spans tile <see cref="SourceText.Text"/>, so printing them in order reproduces the text
/// byte for byte. A byte order mark is not part of the text: <see cref="SourceText"/> keeps it.
/// </summary>
public sealed class EnvDocument
{
    internal EnvDocument(SourceText source, IReadOnlyList<EnvItem> items)
    {
        Source = source;
        Items = items;
    }

    /// <summary>The source text the tree was parsed from.</summary>
    public SourceText Source { get; }

    /// <summary>The items in source order. An empty file has none.</summary>
    public IReadOnlyList<EnvItem> Items { get; }

    /// <summary>
    /// Parses a source text as a .env file. Lines may end in LF, CRLF or CR. Duplicate keys are accepted.
    /// </summary>
    /// <exception cref="CfgeditException">
    /// The text is not valid .env syntax. Line and column say where, and the message says what is wrong.
    /// </exception>
    public static EnvDocument Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return EnvParser.Parse(source);
    }
}
