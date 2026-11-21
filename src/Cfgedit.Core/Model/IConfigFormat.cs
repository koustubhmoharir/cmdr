using Cfgedit.Core.Text;

namespace Cfgedit.Core.Model;

/// <summary>
/// A config format: the extension point that each file format implements. A format parses source text into a
/// <see cref="ConfigDocument"/>, plans edits on it, and gives its logical content for verification.
/// </summary>
public interface IConfigFormat
{
    /// <summary>The format's name: "json", "jsonc", "json5" or "xml".</summary>
    string Name { get; }

    /// <summary>Parses decoded source text.</summary>
    /// <exception cref="Cfgedit.Core.CfgeditException">The text is not a valid document in this format.</exception>
    ConfigDocument Parse(SourceText source);

    /// <summary>
    /// Plans the text edits for an operation. Operations a format does not support are refused with a
    /// <see cref="Cfgedit.Core.CfgeditException"/> that says so.
    /// </summary>
    /// <exception cref="Cfgedit.Core.CfgeditException">The operation is invalid.</exception>
    IReadOnlyList<TextEdit> Plan(ConfigDocument document, EditOp op);

    /// <summary>The document's logical content, with its formatting removed.</summary>
    LogicalNode ToLogical(ConfigDocument document);

    /// <summary>
    /// True when a parse error of the edited text is the user's, not cfgedit's: the edit made the document invalid, and the
    /// message says why. Any other reparse error is an internal edit error.
    /// </summary>
    /// <param name="reparseError">The error that <see cref="Parse"/> raised for the edited text.</param>
    bool IsUserError(CfgeditException reparseError);
}

/// <summary>A text edit: it replaces the old text in [<see cref="Start"/>, <see cref="End"/>) with <see cref="NewText"/>.</summary>
/// <param name="Start">The offset of the first replaced character.</param>
/// <param name="End">The offset just after the last replaced character.</param>
/// <param name="NewText">The replacement.</param>
public readonly record struct TextEdit(int Start, int End, string NewText);
