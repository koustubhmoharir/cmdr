using Cfgedit.Core.Env;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Model;

/// <summary>
/// The .env format: dotenv files, where <c>__</c> in a key separates the levels of a hierarchy. Editing is not supported yet, so
/// every operation is refused; the document can be shown and verified.
/// </summary>
public sealed class EnvFormat : IConfigFormat
{
    /// <inheritdoc/>
    public string Name => "env";

    /// <inheritdoc/>
    public ConfigDocument Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnvDocument syntax = EnvDocument.Parse(source);
        return new ConfigDocument(this, source, EnvModelBuilder.BuildTree(syntax), syntax);
    }

    /// <inheritdoc/>
    /// <exception cref="CfgeditException">Always: editing .env files is not supported yet.</exception>
    public IReadOnlyList<TextEdit> Plan(ConfigDocument document, EditOp op)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(op);
        throw new CfgeditException("editing .env files is not supported yet");
    }

    /// <inheritdoc/>
    public LogicalNode ToLogical(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax is not EnvDocument syntax)
        {
            throw new ArgumentException("The document was not parsed as .env.", nameof(document));
        }

        return EnvModelBuilder.BuildLogical(syntax);
    }

    /// <inheritdoc/>
    /// <remarks>No edit is planned for .env yet, so a reparse error is never the user's.</remarks>
    public bool IsUserError(CfgeditException reparseError)
    {
        ArgumentNullException.ThrowIfNull(reparseError);
        return false;
    }
}
