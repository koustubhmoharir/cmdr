using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

public sealed class GroupDetailsTests : IDisposable
{
    private readonly TempSession _temp = TempSession.Sample();

    public void Dispose() => _temp.Dispose();

    private CmdrSession Session => _temp.Session;

    private GroupEntry Group(string name) => Session.AllGroups.Single(g => g.Name == name);

    private string Resolved(params string[] relative) =>
        Path.GetFullPath(Path.Combine(new[] { Session.ConfigDir }.Concat(relative).ToArray()));

    [Fact]
    public void Group_lines_are_mode_cwd_env_then_description()
    {
        IReadOnlyList<string> lines = DetailsText.GroupLines(Group("mid"), Session.ConfigDir);

        Assert.Equal(
            new[]
            {
                "mode: parallel",
                "cwd: sub → " + Resolved("src", "sub"),
                "Y=2",
                "GONE (unset)",
                "Line one",
                "Line two",
                "Line three",
            },
            lines);
    }

    [Fact]
    public void Top_group_with_a_cwd_setting_shows_setting_and_resolved_path()
    {
        IReadOnlyList<string> lines = DetailsText.GroupLines(Group("top"), Session.ConfigDir);

        Assert.Equal("cwd: src → " + Resolved("src"), lines[1]);
        Assert.Equal("X=1", lines[2]);
        Assert.Equal("Y (unset)", lines[3]);
    }

    [Fact]
    public void Without_a_cwd_setting_only_the_resolved_path_is_shown_through_the_ancestors()
    {
        IReadOnlyList<string> lines = DetailsText.GroupLines(Group("deep"), Session.ConfigDir);

        Assert.Equal("mode: sequential", lines[0]);
        Assert.Equal("cwd: " + Resolved("src", "sub"), lines[1]);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void Group_without_env_or_description_has_only_mode_and_cwd()
    {
        IReadOnlyList<string> lines = DetailsText.GroupLines(Group("other"), Session.ConfigDir);

        Assert.Equal(new[] { "mode: parallel", "cwd: " + Session.ConfigDir }, lines);
    }

    [Fact]
    public void Descriptions_are_cut_to_three_lines()
    {
        Assert.Equal(new[] { "a", "b", "c" }, DetailsText.DescriptionLines("a\r\nb\nc\nd"));
        Assert.Empty(DetailsText.DescriptionLines(null));
        Assert.Empty(DetailsText.DescriptionLines(string.Empty));
    }
}
