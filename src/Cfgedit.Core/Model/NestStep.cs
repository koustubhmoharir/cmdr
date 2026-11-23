namespace Cfgedit.Core.Model;

/// <summary>
/// The operation for one nest or unnest step (see <see cref="ConfigDocument.NestBy"/>). <see cref="NeedsKey"/> is true when the
/// operation is a <see cref="MoveNodeTo"/> into a keyed container for an array item: the caller must ask for a key and set
/// <see cref="MoveNodeTo.NewKey"/> before applying it, since the operation as given would be refused.
/// </summary>
/// <param name="Op">The move: a <see cref="MoveNode"/> within the parent, or a <see cref="MoveNodeTo"/>.</param>
/// <param name="NeedsKey">True when the operation needs a key the caller must supply.</param>
public sealed record NestStep(EditOp Op, bool NeedsKey);
