using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;

namespace Cmdr.Core.Tests.Session;

public class SelectionTests
{
    /// <summary>Session with g1 = [a, b, c] and g2 = [d]. Process-free.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _dir = new();

        public Fixture()
        {
            Session = OpenEmpty(_dir, SessionMode.Edit);
            G1 = Session.AddGroup(null, Settings("g1", GroupMode.Parallel));
            A = Session.AddCommand(G1, Sh("a", "true"));
            B = Session.AddCommand(G1, Sh("b", "true"));
            C = Session.AddCommand(G1, Sh("c", "true"));
            G2 = Session.AddGroup(null, Settings("g2", GroupMode.Parallel));
            D = Session.AddCommand(G2, Sh("d", "true"));
        }

        public Cmdr.Core.Session.Session Session { get; }

        public GroupEntry G1 { get; }

        public GroupEntry G2 { get; }

        public CommandEntry A { get; }

        public CommandEntry B { get; }

        public CommandEntry C { get; }

        public CommandEntry D { get; }

        public void Dispose()
        {
            Session.Dispose();
            _dir.Dispose();
        }
    }

    [Fact]
    public void Toggle_MarksAndUnmarks_AndClearsGroupWhenNothingIsLeft()
    {
        using var f = new Fixture();
        var selection = new Selection();

        selection.Toggle(f.A);
        Assert.True(selection.IsMarked(f.A));
        Assert.Same(f.G1, selection.Group);

        selection.Toggle(f.A);
        Assert.False(selection.IsMarked(f.A));
        Assert.Null(selection.Group);
        Assert.Empty(selection.Marked);
    }

    [Fact]
    public void Toggle_InAnotherGroup_ClearsPreviousMarks()
    {
        using var f = new Fixture();
        var selection = new Selection();
        selection.Toggle(f.A);
        selection.Toggle(f.B);

        selection.Toggle(f.D);

        Assert.Same(f.G2, selection.Group);
        Assert.False(selection.IsMarked(f.A));
        Assert.False(selection.IsMarked(f.B));
        Assert.Equal(new[] { f.D }, selection.Marked);
    }

    [Fact]
    public void ToggleAll_MarksWholeGroup_ThenClearsWhenAllMarked()
    {
        using var f = new Fixture();
        var selection = new Selection();

        selection.ToggleAll(f.G1);
        Assert.Equal(new[] { f.A, f.B, f.C }, selection.Marked);
        Assert.Same(f.G1, selection.Group);

        selection.ToggleAll(f.G1);
        Assert.Empty(selection.Marked);
        Assert.Null(selection.Group);
    }

    [Fact]
    public void ToggleAll_OnOtherGroup_ReplacesMarks()
    {
        using var f = new Fixture();
        var selection = new Selection();
        selection.Toggle(f.A);

        selection.ToggleAll(f.G2);

        Assert.Same(f.G2, selection.Group);
        Assert.Equal(new[] { f.D }, selection.Marked);
    }

    [Fact]
    public void Targets_GroupHeader_IsGroupTarget()
    {
        using var f = new Fixture();
        var selection = new Selection();

        GroupTarget target = Assert.IsType<GroupTarget>(selection.Targets(new GroupRow(f.G1)));
        Assert.Same(f.G1, target.Group);
    }

    [Fact]
    public void Targets_FocusedCommandWithoutMarks_IsFocusedOnly()
    {
        using var f = new Fixture();
        var selection = new Selection();

        CommandsTarget target = Assert.IsType<CommandsTarget>(selection.Targets(new CommandRow(f.B)));
        Assert.Equal(new[] { f.B }, target.Commands);
    }

    [Fact]
    public void Targets_FocusedCommandInMarkedGroup_IsMarkedInFileOrder()
    {
        using var f = new Fixture();
        var selection = new Selection();
        selection.Toggle(f.C);
        selection.Toggle(f.A);

        CommandsTarget target = Assert.IsType<CommandsTarget>(selection.Targets(new CommandRow(f.B)));

        Assert.Equal(new[] { f.A, f.C }, target.Commands);
    }

    [Fact]
    public void Targets_FocusedCommandInOtherGroup_IsFocusedOnly()
    {
        using var f = new Fixture();
        var selection = new Selection();
        selection.Toggle(f.A);

        CommandsTarget target = Assert.IsType<CommandsTarget>(selection.Targets(new CommandRow(f.D)));
        Assert.Equal(new[] { f.D }, target.Commands);
    }

    [Fact]
    public void Prune_DropsMarksOfMovedAndRemovedCommands()
    {
        using var f = new Fixture();
        var selection = new Selection();
        selection.Toggle(f.A);
        selection.Toggle(f.B);

        f.Session.UpdateCommand(f.A, f.A.Spec, f.G2);
        selection.Prune();

        Assert.Equal(new[] { f.B }, selection.Marked);
        Assert.Same(f.G1, selection.Group);

        f.Session.RemoveCommand(f.B);
        selection.Prune();

        Assert.Empty(selection.Marked);
        Assert.Null(selection.Group);
    }

    [Fact]
    public void ToggleAll_OnGroupWithNestedGroup_MarksDirectCommandsOnly()
    {
        using var f = new Fixture();
        var selection = new Selection();
        GroupEntry inner = f.Session.AddGroup(f.G1, Settings("inner"));
        CommandEntry nested = f.Session.AddCommand(inner, Sh("n", "true"));

        selection.ToggleAll(f.G1);

        Assert.Equal(new[] { f.A, f.B, f.C }, selection.Marked);
        Assert.False(selection.IsMarked(nested));
        Assert.Same(f.G1, selection.Group);
    }
}
