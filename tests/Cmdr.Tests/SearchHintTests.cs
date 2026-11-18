using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>The search keys shown in the hint bar while the find field has the focus.</summary>
public sealed class SearchHintTests
{
    [Fact]
    public void With_text_the_hint_ends_with_Esc_Clear()
    {
        Assert.Equal(
            "F3 Next  ⇧F3 Prev  ↑↓ History  Alt+C Case  Alt+W Word  Alt+R Regex  Enter Pane  Esc Clear",
            SearchHint.Describe(hasText: true));
    }

    [Fact]
    public void Without_text_the_hint_ends_with_Esc_Close()
    {
        Assert.Equal(
            "F3 Next  ⇧F3 Prev  ↑↓ History  Alt+C Case  Alt+W Word  Alt+R Regex  Enter Pane  Esc Close",
            SearchHint.Describe(hasText: false));
    }
}
