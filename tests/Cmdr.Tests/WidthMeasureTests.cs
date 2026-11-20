using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Shared.Core;
using Shared.Tui;

namespace Cmdr.Tests;

/// <summary>
/// The tests measure text with the width measure the application installs at start (see Program.Run), not the library default:
/// some graphemes differ, e.g. a Devanagari conjunct. The measure is installed for the whole test assembly here, rather than by
/// whichever test happens to load Shared.Tui first.
/// </summary>
internal static class WidthMeasure
{
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "A test assembly needs the application's width measure installed before its tests run.")]
    internal static void Install() => RuntimeHelpers.RunModuleConstructor(typeof(DisplayWidthSetup).Module.ModuleHandle);
}

public sealed class WidthMeasureTests
{
    [Fact]
    public void The_tests_measure_with_the_terminal_ui_measure_not_the_library_default()
    {
        const string conjunct = "क्ष"; // ka, virama, ssa: one grapheme that the terminal UI draws two columns wide

        Assert.Equal(1, DisplayWidth.DefaultGraphemeWidth(conjunct));
        Assert.Equal(2, DisplayWidth.Of(conjunct));
    }
}
