using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using CmdrSession = Cmdr.Core.Session.Session;
using Shared.Processes;

namespace Cmdr.Core.Tests.Session;

/// <summary>The file levels of the environment chain with script values (<see cref="ConfigSource.EnvLevelsWith"/>).</summary>
public class ConfigSourceScriptLevelsTests
{
    private static readonly string BaseRel = Path.Combine("shared", "base.json");

    private static EnvScriptSpec Script() => new() { Program = "p" };

    private static Dictionary<string, string?> Apply(IReadOnlyList<EnvLevel> levels)
    {
        var target = new Dictionary<string, string?>();
        EnvExpansion.ApplyLevels(target, levels, "/cfg", ':', inputs: null);
        return target;
    }

    [Fact]
    public void EnvLevelsWith_ImportsAScriptValueOfTheBaseFile_ThroughBaseEnv()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { EnvScript = Script(), Env = new() { ["BASE_TOP"] = "base-top" } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, BaseEnv = new() { "FROM_BASE" }, Env = new() { ["MAIN_TOP"] = "main-top" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        ConfigSource baseSource = session.Sources[0];
        ScriptValues values = ScriptValues.Empty.With(baseSource, new Dictionary<string, string?> { ["FROM_BASE"] = "b", ["BASE_TOP"] = "x" });

        Dictionary<string, string?> target = Apply(session.MainSource.EnvLevelsWith(values));

        Assert.Equal("b", target["FROM_BASE"]);
        Assert.Equal("main-top", target["MAIN_TOP"]);
        Assert.False(target.ContainsKey("BASE_TOP"));
    }

    [Fact]
    public void EnvLevelsWith_ABaseFileWithoutBaseEnv_DoesNotImportItsScriptValues()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { EnvScript = Script() }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, Env = new() { ["MAIN_TOP"] = "main-top" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
        ScriptValues values = ScriptValues.Empty.With(session.Sources[0], new Dictionary<string, string?> { ["FROM_BASE"] = "b" });

        Dictionary<string, string?> target = Apply(session.MainSource.EnvLevelsWith(values));

        Assert.False(target.ContainsKey("FROM_BASE"));
        Assert.Equal("main-top", target["MAIN_TOP"]);
    }

    [Fact]
    public void EnvLevelsWith_FileScriptLevelComesBeforeTheTopLevelEnv()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { EnvScript = Script(), Env = new() { ["MAIN_TOP"] = "${env:OWN}-top" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
        ConfigSource main = session.MainSource;
        ScriptValues values = ScriptValues.Empty.With(main, new Dictionary<string, string?> { ["OWN"] = "script" });

        IReadOnlyList<EnvLevel> levels = main.EnvLevelsWith(values);

        Assert.Equal(2, levels.Count);
        Assert.True(levels[0].Literal);
        Assert.Equal("script", levels[0].Env["OWN"]);
        Assert.False(levels[1].Literal);
        Assert.Equal("script-top", Apply(levels)["MAIN_TOP"]);
    }

    [Fact]
    public void EnvLevelsWith_WithoutValues_HasNoScriptLevel()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { EnvScript = Script(), Env = new() { ["MAIN_TOP"] = "main-top" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
        ConfigSource main = session.MainSource;

        IReadOnlyList<EnvLevel> levels = main.EnvLevels;

        EnvLevel level = Assert.Single(levels);
        Assert.False(level.Literal);
        Assert.Equal("main-top", level.Env["MAIN_TOP"]);
        Assert.Equal(levels.Count, main.EnvLevelsWith(ScriptValues.Empty).Count);
    }

    [Fact]
    public void EnvLevelsWith_ValuesOfAnotherFile_AreNotUsedForThisFile()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { EnvScript = Script() }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, EnvScript = Script(), Env = new() { ["MAIN_TOP"] = "main-top" } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
        ScriptValues values = ScriptValues.Empty.With(session.Sources[0], new Dictionary<string, string?> { ["FROM_BASE"] = "b" });

        IReadOnlyList<EnvLevel> levels = session.MainSource.EnvLevelsWith(values);

        Assert.DoesNotContain(levels, level => level.Literal);
        Assert.Equal("main-top", Apply(levels)["MAIN_TOP"]);
    }
}
