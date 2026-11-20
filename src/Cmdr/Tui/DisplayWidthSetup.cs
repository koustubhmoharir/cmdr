using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using Shared.Core;
using Terminal.Gui.Text;

namespace Cmdr.Tui;

/// <summary>
/// Plugs Terminal.Gui's grapheme measure into <see cref="DisplayWidth"/> when the Cmdr assembly loads, so that output
/// layout uses the widths Terminal.Gui draws with.
/// </summary>
internal static class DisplayWidthSetup
{
    // This is an application, not a library: the initializer is intentional, and it must run before any output is parsed.
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "Cmdr is an application; the initializer installs the width measure before any output is parsed.")]
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
