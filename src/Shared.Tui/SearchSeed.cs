namespace Shared.Tui;

/// <summary>
/// A selection that starts a search: the selected text, its line (a logical line in the info pane, an absolute buffer line in the
/// output pane), and the UTF-16 offset of the text in that line, the units of the pane's matches.
/// </summary>
public readonly record struct SearchSeed(string Text, long Line, int Start);
