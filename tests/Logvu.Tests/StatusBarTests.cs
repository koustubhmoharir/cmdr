using Logvu.Tui;
using Shared.Tui;
using Terminal.Gui.Drawing;

namespace Logvu.Tests;

/// <summary>The top bar: the bar colours, and a message in bold.</summary>
public sealed class StatusBarTests
{
    [Fact]
    public void The_text_is_drawn_on_the_bar_colours_and_a_message_is_bold()
    {
        var bar = new StatusBar();

        List<TerminalScreen> screens = TerminalHarness.Run(bar, 40, 1, () => bar.Show("Copied", message: true));

        Assert.Equal(TextStyle.None, screens[0].Cells[0][0].Attribute.Style);
        Assert.Equal(TextStyle.Bold, screens[1].Cells[0][0].Attribute.Style);
        Assert.Equal("Copied", screens[1].Text(0));
        Assert.Equal(BarColors.Background, screens[1].Cells[0][0].Attribute.Background);
        Assert.Equal(BarColors.Text, screens[1].Cells[0][0].Attribute.Foreground);
        Assert.Equal(BarColors.Background, screens[1].Cells[0][39].Attribute.Background);
    }
}
