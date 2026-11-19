using Logvu.Core.Config;
using Logvu.Tui;

namespace Logvu.Tests;

/// <summary>
/// The host's set loop without a terminal: each run returns the config to switch to, or null to end, and the loop runs the sets in
/// the order they were chosen. The picker's hints in the Ctrl+O flow are pinned here too.
/// </summary>
public sealed class LogvuHostTests : IDisposable
{
    private readonly LogSet _first = new(1);
    private readonly LogSet _second = new(1);
    private readonly LogSet _third = new(1);

    [Fact]
    public void Without_a_switch_the_loop_runs_the_first_set_once()
    {
        var ran = new List<LogvuConfig>();

        LogvuHost.RunSets(_first.Config, config =>
        {
            ran.Add(config);
            return null;
        });

        Assert.Equal([_first.Config], ran);
    }

    [Fact]
    public void A_switch_runs_the_chosen_set_next_and_the_loop_ends_when_a_run_has_no_switch()
    {
        var ran = new List<LogvuConfig>();

        LogvuHost.RunSets(_first.Config, config =>
        {
            ran.Add(config);
            return config == _first.Config ? _second.Config : config == _second.Config ? _third.Config : null;
        });

        Assert.Equal([_first.Config, _second.Config, _third.Config], ran);
    }

    [Fact]
    public void Choosing_the_set_that_is_open_runs_it_again()
    {
        var ran = new List<LogvuConfig>();

        LogvuHost.RunSets(_first.Config, config =>
        {
            ran.Add(config);
            return ran.Count == 1 ? _first.Config : null;
        });

        Assert.Equal([_first.Config, _first.Config], ran);
    }

    [Fact]
    public void The_picker_hints_of_the_ctrl_O_flow_offer_back_and_quit()
    {
        Assert.Equal(
            "Enter Open  Del Remove  o/^O Open file  0-9 Select by number  Esc back  ^Q quit",
            LogvuHost.SetPickerHints);
    }

    public void Dispose()
    {
        _first.Dispose();
        _second.Dispose();
        _third.Dispose();
    }
}
