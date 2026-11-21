namespace Cfgedit.Core.Model;

/// <summary>The outcome of an applied edit: the new document, and the path of the node to focus.</summary>
/// <param name="Document">The new document.</param>
/// <param name="Focus">The path of the inserted, renamed, moved or edited node. Null when there is none.</param>
public sealed record EditResult(ConfigDocument Document, NodePath? Focus);
