namespace Cfgedit.Core.Model;

/// <summary>
/// An insert that adds a node shaped like a sibling (see <see cref="ConfigDocument.ShapeLike"/>).
/// </summary>
/// <param name="Edit">The edit that inserts the node and any children it has, as one undo step.</param>
/// <param name="Node">The path of the inserted node. The edit's focus is its last step's, so the node is named here.</param>
public sealed record ShapedInsert(CompositeEdit Edit, NodePath Node);
