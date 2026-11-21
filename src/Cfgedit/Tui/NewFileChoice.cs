using Cfgedit.Core.Model;

namespace Cfgedit.Tui;

/// <summary>The answers to the new-file questions: the format of the file, and for XML the name of its root element.</summary>
/// <param name="Format">The format the new document is written in.</param>
/// <param name="RootName">The root element's name for XML, or null for the JSON family.</param>
internal sealed record NewFileChoice(IConfigFormat Format, string? RootName);
