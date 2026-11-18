namespace Logvu.Core.Detail;

/// <summary>
/// One expandable id a line sits inside: a JSON node or a fold. A line shows only while every id on its chain is expanded.
/// </summary>
/// <param name="Id">The node's path or the fold's id.</param>
/// <param name="Parent">The enclosing expandable id, or <c>null</c> at the top level.</param>
internal sealed record Anchor(string Id, Anchor? Parent);

/// <summary>A line of the full document, with the chain of expandable ids it sits inside.</summary>
/// <param name="Line">The line, with its highlights applied.</param>
/// <param name="Parent">The innermost expandable id around the line, or <c>null</c> when the line is always visible.</param>
internal sealed record ModelLine(DetailLine Line, Anchor? Parent);

/// <summary>
/// The structured document of one event, with everything expanded, and the default expansion of every id it has.
/// </summary>
/// <param name="Lines">Every line of the document, in order.</param>
/// <param name="Defaults">The default expansion of every node and fold, keyed by id.</param>
/// <param name="NodeIds">The ids of the JSON nodes, in document order.</param>
/// <param name="FoldIds">The ids of the folds, in document order.</param>
internal sealed record DetailModel(
    IReadOnlyList<ModelLine> Lines,
    IReadOnlyDictionary<string, bool> Defaults,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> FoldIds);
