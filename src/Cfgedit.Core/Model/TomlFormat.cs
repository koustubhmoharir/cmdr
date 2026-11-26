using Cfgedit.Core.Toml;
using Shared.Core;

namespace Cfgedit.Core.Model;

/// <summary>
/// The TOML format (TOML 1.0). Parsing checks the syntax, the TOML rules about tables and keys, and then the text again with Tomlyn.
/// Editing covers values, keys, comments, types, adds, deletes, duplicates and moves.
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

    /// <summary>
    /// True when the node is an array of tables: an array defined by <c>[[x]]</c> headers, which holds tables only. False for any other
    /// node, and for a document that is not TOML.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="node">A node of the document.</param>
    public static bool IsArrayOfTables(ConfigDocument document, DocNode node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);
        return node.Kind == NodeKind.TomlArray && document.Syntax is TomlModel model && model.ShapeOf(node) == TomlContainer.ArrayOfTables;
    }

    /// <inheritdoc/>
    /// <exception cref="Cfgedit.Core.CfgeditException">The operation is invalid.</exception>
    public IReadOnlyList<TextEdit> Plan(ConfigDocument document, EditOp op)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(op);
        return new TomlEditPlanner(document).Plan(op);
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
