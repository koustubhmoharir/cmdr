using Cfgedit.Core.Env;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Model;

/// <summary>
/// The .env format: dotenv files, where <c>__</c> in a key separates the levels of a hierarchy. Values, keys, comments, inserts and
/// deletes are planned; moves, duplicates and type changes are refused for now.
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
    /// <exception cref="CfgeditException">The operation is invalid, or it is not supported for .env yet.</exception>
    public IReadOnlyList<TextEdit> Plan(ConfigDocument document, EditOp op)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(op);
        return new EnvEditPlanner(document).Plan(op);
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
    /// <remarks>The planner checks every edit, so a reparse error is an internal error, never the user's.</remarks>
    public bool IsUserError(CfgeditException reparseError)
    {
        ArgumentNullException.ThrowIfNull(reparseError);
        return false;
    }
}
