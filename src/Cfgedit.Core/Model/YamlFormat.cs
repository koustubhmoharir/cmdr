using Cfgedit.Core.Text;
using Cfgedit.Core.Yaml;

namespace Cfgedit.Core.Model;

/// <summary>
/// The YAML format: the config subset of YAML 1.2 (see docs/cfgedit-design.md, YAML). Parsing checks the syntax, builds the document tree,
/// and then checks the data again with YamlDotNet. Editing is not supported yet: the planner is the next step.
/// </summary>
public sealed class YamlFormat : IConfigFormat
{
    /// <inheritdoc/>
    public string Name => "yaml";

    /// <inheritdoc/>
    /// <exception cref="Cfgedit.Core.CfgeditException">The text is not valid YAML within the subset, or YamlDotNet disagrees with cfgedit about it.</exception>
    public ConfigDocument Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        YamlDocument syntax = YamlDocument.Parse(source);
        YamlLimits.Check(syntax);
        YamlModel model = YamlModelBuilder.Build(syntax);
        YamlCrossCheck.Verify(source.Text, model.DataLogical());
        return new ConfigDocument(this, source, model.Tree, model);
    }

    /// <inheritdoc/>
    /// <exception cref="Cfgedit.Core.CfgeditException">Always: YAML editing is not supported yet.</exception>
    public IReadOnlyList<TextEdit> Plan(ConfigDocument document, EditOp op)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(op);
        throw new CfgeditException("editing YAML files is not supported yet");
    }

    /// <inheritdoc/>
    public LogicalNode ToLogical(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax is not YamlModel model)
        {
            throw new ArgumentException("The document was not parsed as YAML.", nameof(document));
        }

        return model.ToLogical();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every reparse error is the user's except an internal parse mismatch, which is cfgedit's own bug. That is the rule of TOML and XML too.
    /// </remarks>
    public bool IsUserError(CfgeditException reparseError)
    {
        ArgumentNullException.ThrowIfNull(reparseError);
        return !reparseError.Message.StartsWith("internal parse mismatch", StringComparison.Ordinal);
    }
}
