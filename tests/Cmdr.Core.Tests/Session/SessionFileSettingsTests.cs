using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using Shared.Processes;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>
/// The opened file's own top-level settings changed in edit mode (<see cref="CmdrSession.UpdateFileSettings"/>): applied to the opened
/// file, its environment levels, the dirty flag, the saved file, and refused in run mode.
/// </summary>
public class SessionFileSettingsTests
{
    private static readonly string BaseRel = Path.Combine("shared", "base.json");

    private static FileSettings Settings() => new(
        new Dictionary<string, string?> { ["TOP"] = "v" },
        new EnvScriptSpec { Program = "sh", Args = new List<string> { "-c", "echo TOP=v" }, TimeoutSeconds = 5 },
        null,
        new List<string> { "TOKEN" });

    [Fact]
    public void UpdateFileSettings_InRunMode_IsRefused_AndChangesNothing()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "true")));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => session.UpdateFileSettings(Settings()));

        Assert.Contains("edit mode", ex.Message);
        Assert.Null(session.MainSource.Env);
        Assert.Null(session.MainSource.EnvScript);
        Assert.Null(session.MainSource.Secret);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void UpdateFileSettings_AppliesToTheOpenedFile_MarksDirty_AndRaisesStructureChanged()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));
        int changes = 0;
        session.StructureChanged += () => changes++;

        session.UpdateFileSettings(Settings());

        Assert.Equal("v", session.MainSource.Env!["TOP"]);
        Assert.Equal("sh", session.MainSource.EnvScript!.Program);
        Assert.Equal(5, session.MainSource.EnvScript.TimeoutSeconds);
        Assert.Equal(new[] { "TOKEN" }, session.MainSource.Secret);
        Assert.Null(session.MainSource.BaseEnv);
        Assert.True(session.IsDirty);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void UpdateFileSettings_RecomputesTheEnvironmentLevels_SoACommandOfTheFileSeesTheNewEnvironment()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));
        CommandEntry command = session.AllEntries.Single();
        Assert.DoesNotContain(command.Source.EnvLevels, level => level.Env.ContainsKey("TOP"));

        session.UpdateFileSettings(Settings());

        Assert.Contains(command.Source.EnvLevels, level => level.Env.TryGetValue("TOP", out string? value) && value == "v");
    }

    [Fact]
    public void UpdateFileSettings_CopiesTheValues()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));
        var env = new Dictionary<string, string?> { ["TOP"] = "v" };
        var secret = new List<string> { "TOKEN" };

        session.UpdateFileSettings(new FileSettings(env, null, null, secret));
        env["TOP"] = "changed";
        env["NEW"] = "x";
        secret.Add("OTHER");

        Assert.Equal("v", session.MainSource.Env!["TOP"]);
        Assert.DoesNotContain("NEW", session.MainSource.Env.Keys);
        Assert.Equal(new[] { "TOKEN" }, session.MainSource.Secret);
    }

    [Fact]
    public void UpdateFileSettings_WithABase_ImportsFromTheBase_AndLeavesTheBaseFileAlone()
    {
        using var dir = new TempDir();
        ConfigStore.Save(
            new CmdrConfig
            {
                Env = new Dictionary<string, string?> { ["IMPORTED"] = "from-base" },
                Groups = { Group("b", GroupMode.Parallel, Sh("bcmd", "true")) },
            },
            dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, Groups = { Group("m", GroupMode.Parallel, Sh("mcmd", "true")) } }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Edit);
        ConfigSource baseSource = session.Sources[0];
        IReadOnlyList<EnvLevel> baseLevels = baseSource.EnvLevels;

        session.UpdateFileSettings(new FileSettings(null, null, new List<string> { "IMPORTED" }, null));

        Assert.Equal(new[] { "IMPORTED" }, session.MainSource.BaseEnv);
        Assert.NotNull(Assert.Single(session.MainSource.EnvLevels).ImportFrom);
        Assert.Same(baseLevels, baseSource.EnvLevels);
        Assert.Equal("from-base", baseSource.Env!["IMPORTED"]);
        Assert.True(baseSource.IsBase);
    }

    [Fact]
    public void UpdateFileSettings_IsWrittenBySave()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));
        session.UpdateFileSettings(Settings());

        session.Save();

        using CmdrSession reloaded = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
        Assert.Equal("v", reloaded.MainSource.Env!["TOP"]);
        Assert.Equal("sh", reloaded.MainSource.EnvScript!.Program);
        Assert.Equal(new[] { "TOKEN" }, reloaded.MainSource.Secret);
        Assert.False(reloaded.IsDirty);
    }

    [Fact]
    public void UpdateFileSettings_NullArgument_Throws()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit);

        Assert.Throws<ArgumentNullException>(() => session.UpdateFileSettings(null!));
    }

    // A file with env, an environment script, a secret, and baseEnv importing from a base file.
    private static CmdrSession OpenWithFileSettings(TempDir dir)
    {
        ConfigStore.Save(
            new CmdrConfig
            {
                Env = new Dictionary<string, string?> { ["IMPORTED"] = "from-base" },
                Groups = { Group("b", GroupMode.Parallel, Sh("bcmd", "true")) },
            },
            dir.File(BaseRel));
        ConfigStore.Save(
            new CmdrConfig
            {
                Base = BaseRel,
                Env = new Dictionary<string, string?> { ["TOP"] = "v" },
                EnvScript = new EnvScriptSpec { Program = "sh", Args = new List<string> { "-c", "echo TOP=v" }, TimeoutSeconds = 5 },
                BaseEnv = new List<string> { "IMPORTED" },
                Secret = new List<string> { "TOKEN" },
                Groups = { Group("m", GroupMode.Parallel, Sh("mcmd", "true")) },
            },
            dir.File("cmdr.json"));
        return CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Edit);
    }

    [Fact]
    public void UpdateFileSettings_WithTheCurrentValues_LeavesACleanSessionClean_AndRaisesNothing()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWithFileSettings(dir);
        int changes = 0;
        session.StructureChanged += () => changes++;

        session.UpdateFileSettings(new FileSettings(
            session.MainSource.Env,
            session.MainSource.EnvScript,
            session.MainSource.BaseEnv,
            session.MainSource.Secret));

        Assert.False(session.IsDirty);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void UpdateFileSettings_WithTheCurrentValues_KeepsADirtySessionDirty()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWithFileSettings(dir);
        session.UpdateFileSettings(Settings());
        int changes = 0;
        session.StructureChanged += () => changes++;

        session.UpdateFileSettings(new FileSettings(
            session.MainSource.Env,
            session.MainSource.EnvScript,
            session.MainSource.BaseEnv,
            session.MainSource.Secret));

        Assert.True(session.IsDirty);
        Assert.Equal(0, changes);
    }
}
