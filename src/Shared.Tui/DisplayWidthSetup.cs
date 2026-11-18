using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using Shared.Core;
using Terminal.Gui.Text;

namespace Shared.Tui;

/// <summary>
/// Plugs Terminal.Gui's grapheme measure into <see cref="DisplayWidth"/> when the Shared.Tui assembly loads, so that output
/// layout uses the widths Terminal.Gui draws with.
/// </summary>
public static class DisplayWidthSetup
{
    // The initializer is intentional: the applications that use this library must have the width measure installed before any output is parsed.
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "The applications that use this library need the width measure installed before any output is parsed, so the initializer is the intended mechanism.")]
    internal static void Initialize()
    {
        DisplayWidth.GraphemeWidth = GraphemeColumns;
    }

    /// <summary>
    /// Terminal.Gui returns 0 or -1 for control characters, but the core default gives them one column so layout never
    /// collapses. This keeps that rule for a single control character.
    /// </summary>
    private static int GraphemeColumns(string grapheme)
    {
        if (grapheme.Length == 1 && char.IsControl(grapheme[0]))
        {
            return 1;
        }

        return Math.Max(0, grapheme.GetColumns());
    }
}
