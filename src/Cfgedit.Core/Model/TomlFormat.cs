using Cfgedit.Core.Text;
using Cfgedit.Core.Toml;

namespace Cfgedit.Core.Model;

/// <summary>
/// The TOML format (TOML 1.0). Parsing checks the syntax, the TOML rules about tables and keys, and then the text again with Tomlyn.
/// Editing is not supported yet: every operation is refused.
/// </summary>
public sealed class TomlFormat : IConfigFormat
{
    /// <inheritdoc/>
    public string Name => "toml";

    /// <inheritdoc/>
    /// <exception cref="Cfgedit.Core.CfgeditException">The text is not valid TOML 1.0, or Tomlyn disagrees with cfgedit about it.</exception>
    public ConfigDocument Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        TomlDocument syntax = TomlDocument.Parse(source);
        TomlModel model = TomlModelBuilder.Build(syntax);
        TomlCrossCheck.Verify(source.Text, model.DataLogical());
        return new ConfigDocument(this, source, model.Tree, model);
    }

    /// <inheritdoc/>
    /// <exception cref="Cfgedit.Core.CfgeditException">Always: editing TOML files is not supported yet.</exception>
    public IReadOnlyList<TextEdit> Plan(ConfigDocument document, EditOp op)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(op);
        throw new CfgeditException("editing TOML files is not supported yet");
    }

    /// <inheritdoc/>
    public LogicalNode ToLogical(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax is not TomlModel model)
        {
            throw new ArgumentException("The document was not parsed as TOML.", nameof(document));
        }

        return model.ToLogical();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every reparse error is the user's except an internal parse mismatch, which is cfgedit's own bug. That is the rule of XML too.
    /// A syntax or semantic error of the edited text means the edit made the document invalid, and the message says why.
    /// </remarks>
    public bool IsUserError(CfgeditException reparseError)
    {
        ArgumentNullException.ThrowIfNull(reparseError);
        return !reparseError.Message.StartsWith("internal parse mismatch", StringComparison.Ordinal);
    }
}
