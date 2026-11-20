using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>The file rows of the command list: one per file of the chain, in chain order, each before the items of its file.</summary>
public class SessionFileRowTests
{
    private static readonly string BaseRel = Path.Combine("shared", "base.json");
    private static readonly string Base2Rel = Path.Combine("shared", "base2.json");

    /// <summary>The label of a row: "F:" and the display path for a file row, "G:" and the name for a group, "C:" and the name for a command.</summary>
    private static string Label(ListRow row) => row switch
    {
        FileRow file => "F:" + file.Source.DisplayPath,
        GroupRow group => "G:" + group.Group.Name,
        CommandRow command => "C:" + command.Entry.Name,
        _ => "?",
    };

    [Fact]
    public void A_single_file_session_has_one_file_row_before_its_groups()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Groups = { Group("g", GroupMode.Parallel, Sh("c", "true")) } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        IReadOnlyList<ListRow> rows = session.BuildRows();

        Assert.Equal(new[] { "F:cmdr.json", "G:g", "C:c" }, rows.Select(Label));
        Assert.Same(session.MainSource, Assert.IsType<FileRow>(rows[0]).Source);
    }

    [Fact]
    public void A_file_without_groups_still_gets_its_file_row_and_the_rows_follow_the_chain_order()
    {
        using var dir = new TempDir();
        // shared/base2.json has no groups; shared/base.json imports it and has group b; cmdr.json imports base.json and has group m.
        ConfigStore.Save(new CmdrConfig(), dir.File(Base2Rel));
        ConfigStore.Save(new CmdrConfig { Base = "base2.json", Groups = { Group("b", GroupMode.Parallel, Sh("bc", "true")) } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, Groups = { Group("m", GroupMode.Parallel, Sh("mc", "true")) } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        IReadOnlyList<ListRow> rows = session.BuildRows();

        Assert.Equal(
            new[] { "F:" + Base2Rel, "F:" + BaseRel, "G:b", "C:bc", "F:cmdr.json", "G:m", "C:mc" },
            rows.Select(Label));
        Assert.Equal(session.Sources, rows.OfType<FileRow>().Select(row => row.Source));
    }

    [Fact]
    public void Base_setting_of_each_file_is_its_base_as_written_and_null_for_a_file_that_imports_none()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig(), dir.File(Base2Rel));
        ConfigStore.Save(new CmdrConfig { Base = "base2.json", Groups = { Group("b", GroupMode.Parallel, Sh("bc", "true")) } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        Assert.Null(session.Sources[0].BaseSetting);
        Assert.Equal("base2.json", session.Sources[1].BaseSetting);
        Assert.Equal(BaseRel, session.MainSource.BaseSetting);
    }

    [Fact]
    public void A_file_row_has_no_action_target()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Groups = { Group("g", GroupMode.Parallel, Sh("c", "true")) } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
        var selection = new Selection();

        Assert.Null(selection.Targets(new FileRow(session.MainSource)));
    }
}
