using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

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


}
