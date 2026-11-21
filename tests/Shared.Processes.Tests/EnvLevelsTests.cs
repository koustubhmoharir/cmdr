namespace Shared.Processes.Tests;

public class EnvLevelsTests
{
    private const string ConfigDir = "/cfg";

    private static EnvLevel Level(string cwd, params (string Key, string? Value)[] entries) =>
        new(entries.ToDictionary(e => e.Key, e => e.Value), cwd);

    private static Dictionary<string, string?> Target(params (string Key, string? Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value);

    private static void Apply(Dictionary<string, string?> target, params EnvLevel[] levels) =>
        EnvExpansion.ApplyLevels(target, levels, ConfigDir, ':', inputs: null);

    [Fact]
    public void OuterToInner_InnerOverrides()
    {
        var target = Target(("KEEP", "k"));

        Apply(target, Level("/a", ("A", "outer")), Level("/a/b", ("A", "inner")));

        Assert.Equal("inner", target["A"]);
        Assert.Equal("k", target["KEEP"]);
    }

    [Fact]
    public void PathReferencesThePreviousLevel()
    {
        var target = Target(("PATH", "/bin"));

        Apply(
            target,
            Level("/a", ("PATH", "/outer${pathsep}${env:PATH}")),
            Level("/a/b", ("PATH", "/inner${pathsep}${env:PATH}")));

        Assert.Equal("/inner:/outer:/bin", target["PATH"]);
    }

    [Fact]
    public void WithinOneLevel_ReferencesSeeTheEnvironmentBeforeTheLevel()
    {
        var target = Target(("A", "before"));

        Apply(target, Level("/a", ("A", "level"), ("B", "${env:A}")));

        Assert.Equal("level", target["A"]);
        Assert.Equal("before", target["B"]);
    }

    [Fact]
    public void NullRemovesTheVariable()
    {
        var target = Target(("REMOVE", "present"), ("KEEP", "k"));

        Apply(target, Level("/a", ("REMOVE", null)));

        Assert.False(target.ContainsKey("REMOVE"));
        Assert.Equal("k", target["KEEP"]);
    }

    [Fact]
    public void NullRemovalIsSeenByLaterLevels_ExpandingToEmpty()
    {
        var target = Target(("FOO", "present"));

        Apply(target, Level("/a", ("FOO", null)), Level("/a", ("BAR", "[${env:FOO}]")));

        Assert.False(target.ContainsKey("FOO"));
        Assert.Equal("[]", target["BAR"]);
    }

    [Fact]
    public void CwdIsPerLevel()
    {
        var target = Target();

        Apply(target, Level("/a", ("X", "${cwd}")), Level("/a/b", ("Y", "${cwd}")));

        Assert.Equal("/a", target["X"]);
        Assert.Equal("/a/b", target["Y"]);
    }

    [Fact]
    public void ConfigDirIsTheSameForEveryLevel()
    {
        var target = Target();

        Apply(target, Level("/a", ("X", "${configDir}")), Level("/a/b", ("Y", "${configDir}/z")));

        Assert.Equal(ConfigDir, target["X"]);
        Assert.Equal(ConfigDir + "/z", target["Y"]);
    }

    [Fact]
    public void NoLevels_LeavesTargetUnchanged()
    {
        var target = Target(("A", "1"));

        Apply(target);

        Assert.Equal("1", target["A"]);
        Assert.Single(target);
    }

    [Fact]
    public void KeyComparison_FollowsPlatform_ForSnapshotLookups()
    {
        // On Windows the snapshot is case-insensitive, so ${env:path} finds PATH. On other systems it is case-sensitive.
        var target = Target(("PATH", "/bin"));

        Apply(target, Level("/a", ("X", "${env:path}")));

        Assert.Equal(OperatingSystem.IsWindows() ? "/bin" : "", target["X"]);
    }

    private static EnvLevel Import(string cwd, params string[] names) =>
        new(names.ToDictionary(n => n, _ => (string?)null), cwd);

    [Fact]
    public void Import_PresentInBase_IsCopiedToTarget()
    {
        var target = Target(("A", "cmdr"));

        Apply(target, Import("/a", "A") with { ImportFrom = new[] { Level("/base", ("A", "base")) } });

        Assert.Equal("base", target["A"]);
    }

    [Fact]
    public void Import_NameNotSetByBase_KeepsTheValueItHadBeforeTheImport()
    {
        // The base levels run on a copy of the environment at that point, so a name they do not set still has its value there.
        // See the open question in the implementation notes: removal happens only when the name is absent from that copy.
        var target = Target(("A", "cmdr"), ("KEEP", "k"));

        Apply(target, Import("/a", "A") with { ImportFrom = new[] { Level("/base", ("OTHER", "x")) } });

        Assert.Equal("cmdr", target["A"]);
        Assert.Equal("k", target["KEEP"]);
    }

    [Fact]
    public void Import_NameAbsentFromEnvironmentAndBase_StaysAbsent()
    {
        var target = Target(("KEEP", "k"));

        Apply(target, Import("/a", "A") with { ImportFrom = new[] { Level("/base", ("OTHER", "x")) } });

        Assert.False(target.ContainsKey("A"));
        Assert.Equal("k", target["KEEP"]);
    }

    [Fact]
    public void Import_OnlyNamedVariablesAreChanged()
    {
        var target = Target(("A", "cmdr"), ("B", "b-cmdr"));

        Apply(target, Import("/a", "A") with { ImportFrom = new[] { Level("/base", ("A", "base"), ("C", "c-base")) } });

        Assert.Equal("base", target["A"]);
        Assert.Equal("b-cmdr", target["B"]);
        Assert.False(target.ContainsKey("C"));
    }

    [Fact]
    public void Import_ChainedThroughTwoBaseFiles_UsesTheMiddleFilesImport()
    {
        // deepest: X=deep. middle: imports X from deepest, and sets Y from it. The opened file imports X and Y from the middle.
        EnvLevel[] deepest = { Level("/deepest", ("X", "deep")) };
        EnvLevel[] middle =
        {
            Import("/middle", "X") with { ImportFrom = deepest },
            Level("/middle", ("Y", "${env:X}!")),
        };
        var target = Target(("X", "cmdr"));

        Apply(target, Import("/main", "X", "Y") with { ImportFrom = middle });

        Assert.Equal("deep", target["X"]);
        Assert.Equal("deep!", target["Y"]);
    }

    [Fact]
    public void Import_ValuesOfTheBaseAreExpandedByTheBaseLevel_NotAgain()
    {
        var target = Target();

        Apply(target, Import("/main", "A") with { ImportFrom = new[] { Level("/base/dir", ("A", "${cwd}/x")) } });

        Assert.Equal("/base/dir/x", target["A"]);
    }

    [Fact]
    public void Import_ImportedValueIsComputedAgainstTheEnvironmentAtThatPoint()
    {
        // The outer level changes A before the import; the base's value refers to A as it is then.
        var target = Target(("A", "cmdr"));

        Apply(
            target,
            Level("/main", ("A", "outer")),
            Import("/main", "A") with { ImportFrom = new[] { Level("/base", ("A", "${env:A}+base")) } });

        Assert.Equal("outer+base", target["A"]);
    }
}
