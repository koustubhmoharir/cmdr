using Mdedit.Core.Render;
using Mdedit.Core.Tables;

namespace Mdedit.Core.Tests.Tables;

/// <summary>Helpers for table tests: the table block of a document, and the document after a result is applied.</summary>
internal static class TableCase
{
    /// <summary>The first table block of a document.</summary>
    public static RenderBlock Table(string text) => RenderDocument.Parse(text).Blocks.First(block => block.Kind == BlockKind.Table);

    /// <summary>The document after a result's replacement; a refused result leaves the text as it was.</summary>
    public static string Apply(string text, TableEditResult result) => result.IsRefused
        ? text
        : string.Concat(text.AsSpan(0, result.Start), result.Text, text.AsSpan(result.Start + result.Length));
}
