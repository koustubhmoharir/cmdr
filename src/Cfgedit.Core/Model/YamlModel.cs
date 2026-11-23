using Cfgedit.Core.Yaml;

namespace Cfgedit.Core.Model;

/// <summary>
/// A parsed YAML stream, held as the format's syntax: the concrete syntax tree, the document tree that the editor shows, and the syntax
/// node that each row of the tree was built from. A model is immutable.
/// </summary>
internal sealed class YamlModel
{
    private readonly IReadOnlyDictionary<DocNode, YamlNode> _syntaxOf;

    internal YamlModel(YamlDocument syntax, DocNode tree, IReadOnlyDictionary<DocNode, YamlNode> syntaxOf)
    {
        Syntax = syntax;
        Tree = tree;
        _syntaxOf = syntaxOf;
    }

    /// <summary>The concrete syntax tree of the text.</summary>
    internal YamlDocument Syntax { get; }

    /// <summary>The document node of the document tree.</summary>
    internal DocNode Tree { get; }

    /// <summary>
    /// The syntax a row was built from: a member's entry, an item, a value, a document or a comment. Null for the document node and for
    /// the empty root of a stream with no document.
    /// </summary>
    internal YamlNode? SyntaxOf(DocNode node)
    {
        return _syntaxOf.TryGetValue(node, out YamlNode? syntax) ? syntax : null;
    }

    /// <summary>The document's data, without its comments. See <see cref="YamlLogical.Data"/>.</summary>
    internal LogicalNode DataLogical()
    {
        return YamlLogical.Data(Syntax);
    }

    /// <summary>The document's logical tree: its data, then an array named <c>#comments</c> with every comment in file order.</summary>
    internal LogicalNode ToLogical()
    {
        return new LogicalNode(LogicalKind.Document, null, null, null, [], [DataLogical(), YamlLogical.Comments(Syntax)]);
    }
}
