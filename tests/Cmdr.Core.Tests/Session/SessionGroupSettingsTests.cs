using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>The environment script and the secret names of a group, applied by AddGroup and UpdateGroup as copies.</summary>
public class SessionGroupSettingsTests
{
    private static GroupSettings WithScriptAndSecret(EnvScriptSpec? script, IReadOnlyList<string>? secret) =>
        new("g", null, GroupMode.Parallel, null, null, true, script, secret);

    [Fact]
    public void AddGroup_AppliesTheEnvScriptAndSecret_AsCopies()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit);
        var script = new EnvScriptSpec { Program = "sh", Args = new List<string> { "-c", "true" } };
        var secret = new List<string> { "TOKEN" };

        GroupEntry group = session.AddGroup(null, WithScriptAndSecret(script, secret));
        script.Program = "changed";
        secret.Add("OTHER");

        Assert.Equal("sh", group.EnvScript!.Program);
        Assert.Equal(new[] { "TOKEN" }, group.Secret);
    }

    [Fact]
    public void UpdateGroup_AppliesTheEnvScriptAndSecret_AndClearsThemWhenNotGiven()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit);
        GroupEntry group = session.AddGroup(null, WithScriptAndSecret(null, null));
        var script = new EnvScriptSpec { Program = "sh", Args = new List<string> { "-c", "true" }, TimeoutSeconds = 9 };

        session.UpdateGroup(group, WithScriptAndSecret(script, new List<string> { "TOKEN" }), null);
        script.TimeoutSeconds = 1;

        Assert.Equal(9, group.EnvScript!.TimeoutSeconds);
        Assert.Equal(new[] { "TOKEN" }, group.Secret);
        Assert.Equal(new[] { "TOKEN" }, Assert.Single(session.BuildConfig().Groups).Secret);
        Assert.Equal(9, Assert.Single(session.BuildConfig().Groups).EnvScript!.TimeoutSeconds);

        session.UpdateGroup(group, WithScriptAndSecret(null, null), null);

        Assert.Null(group.EnvScript);
        Assert.Null(group.Secret);
    }

    private static GroupSpec WithEnvScriptSecretAndEnv()
    {
        GroupSpec spec = Group("g", GroupMode.Parallel, Sh("a", "true"));
        spec.Env = new Dictionary<string, string?> { ["G"] = "1" };
        spec.EnvScript = new EnvScriptSpec { Program = "sh", Args = new List<string> { "-c", "echo G=1" }, TimeoutSeconds = 5 };
        spec.Secret = new List<string> { "TOKEN" };
        return spec;
    }

    // The settings that keep every current value of the group, as the group dialog passes them back.
    private static GroupSettings CurrentSettings(GroupEntry group) =>
        new(group.Name, group.Description, group.Mode, group.Cwd, group.Env, group.Expanded, group.EnvScript, group.Secret);

    [Fact]
    public void UpdateGroup_WithTheCurrentValues_LeavesACleanSessionClean_AndRaisesNothing()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, WithEnvScriptSecretAndEnv());
        GroupEntry group = session.Groups[0];
        int changes = 0;
        session.StructureChanged += () => changes++;

        session.UpdateGroup(group, CurrentSettings(group), group.Parent);

        Assert.False(session.IsDirty);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void UpdateGroup_WithTheCurrentValues_KeepsADirtySessionDirty()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, WithEnvScriptSecretAndEnv());
        GroupEntry group = session.Groups[0];
        session.UpdateGroup(group, Settings("renamed"), null);
        int changes = 0;
        session.StructureChanged += () => changes++;

        session.UpdateGroup(group, CurrentSettings(group), group.Parent);

        Assert.True(session.IsDirty);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void UpdateGroup_ChangingOnlyAnEnvValue_MarksDirtyAndRaisesStructureChanged()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, WithEnvScriptSecretAndEnv());
        GroupEntry group = session.Groups[0];
        int changes = 0;
        session.StructureChanged += () => changes++;

        session.UpdateGroup(group, CurrentSettings(group) with { Env = new Dictionary<string, string?> { ["G"] = "2" } }, group.Parent);

        Assert.True(session.IsDirty);
        Assert.Equal(1, changes);
    }
}
