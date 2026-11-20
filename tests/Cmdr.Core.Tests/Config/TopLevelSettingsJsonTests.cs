using System.Text.Json;
using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

/// <summary>JSON shape and copies of the new settings: top-level env, baseEnv, secret, envScript and the group and command secret.</summary>
public class TopLevelSettingsJsonTests
{
    private static EnvScriptSpec Script() => new()
    {
        Program = "sh",
        Args = { "-c", "echo ${env:HOME}" },
        Cwd = "scripts",
        TimeoutSeconds = 5,
        Rerun = EnvScriptRerun.EachStart,
    };

    private static CmdrConfig FullConfig() => new()
    {
        Base = "base.json",
        Env = new Dictionary<string, string?> { ["SHARED"] = "${cwd}/shared", ["GONE"] = null },
        EnvScript = Script(),
        BaseEnv = new List<string> { "HOME", "TOKEN" },
        Secret = new List<string> { "TOKEN" },
        Groups =
        {
            new GroupSpec
            {
                Name = "g",
                EnvScript = new EnvScriptSpec { Program = "group-script" },
                Secret = new List<string> { "G_SECRET" },
                Items = { new CommandSpec { Name = "c", Program = "p", Secret = new List<string> { "C_SECRET" } } },
            },
        },
    };

    [Fact]
    public void RoundTrip_AllNewSettings_ArePreserved()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");

        ConfigStore.Save(FullConfig(), path);
        CmdrConfig loaded = ConfigStore.Load(path);

        Assert.Equal(new Dictionary<string, string?> { ["SHARED"] = "${cwd}/shared", ["GONE"] = null }, loaded.Env);
        Assert.Equal("sh", loaded.EnvScript!.Program);
        Assert.Equal(new[] { "-c", "echo ${env:HOME}" }, loaded.EnvScript.Args);
        Assert.Equal("scripts", loaded.EnvScript.Cwd);
        Assert.Equal(5, loaded.EnvScript.TimeoutSeconds);
        Assert.Equal(EnvScriptRerun.EachStart, loaded.EnvScript.Rerun);
        Assert.Equal(new[] { "HOME", "TOKEN" }, loaded.BaseEnv);
        Assert.Equal(new[] { "TOKEN" }, loaded.Secret);

        GroupSpec group = Assert.Single(loaded.Groups);
        Assert.Equal("group-script", group.EnvScript!.Program);
        Assert.Equal(EnvScriptRerun.Once, group.EnvScript.Rerun);
        Assert.Null(group.EnvScript.Cwd);
        Assert.Null(group.EnvScript.TimeoutSeconds);
        Assert.Equal(new[] { "G_SECRET" }, group.Secret);
        Assert.Equal(new[] { "C_SECRET" }, Assert.IsType<CommandSpec>(Assert.Single(group.Items)).Secret);
    }

    [Fact]
    public void Save_NewSettingsAreOmittedWhenNull()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { Groups = { new GroupSpec { Name = "g", Items = { new CommandSpec { Name = "c", Program = "p" } } } } }, path);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        Assert.False(root.TryGetProperty("env", out _));
        Assert.False(root.TryGetProperty("envScript", out _));
        Assert.False(root.TryGetProperty("baseEnv", out _));
        Assert.False(root.TryGetProperty("secret", out _));
        JsonElement group = root.GetProperty("groups")[0];
        Assert.False(group.TryGetProperty("envScript", out _));
        Assert.False(group.TryGetProperty("secret", out _));
        Assert.False(group.GetProperty("items")[0].TryGetProperty("secret", out _));
    }

    [Fact]
    public void Save_EnvScriptRerunOnce_IsOmitted_AndEachStartIsWritten()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");

        ConfigStore.Save(new CmdrConfig { EnvScript = new EnvScriptSpec { Program = "p" } }, path);
        using (JsonDocument once = JsonDocument.Parse(File.ReadAllText(path)))
        {
            Assert.False(once.RootElement.GetProperty("envScript").TryGetProperty("rerun", out _));
        }

        ConfigStore.Save(new CmdrConfig { EnvScript = new EnvScriptSpec { Program = "p", Rerun = EnvScriptRerun.EachStart } }, path);
        using JsonDocument each = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("eachStart", each.RootElement.GetProperty("envScript").GetProperty("rerun").GetString());
    }

    [Fact]
    public void Load_RerunIsAbsent_DefaultsToOnce()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "envScript": { "program": "p" } }""");

        Assert.Equal(EnvScriptRerun.Once, ConfigStore.Load(path).EnvScript!.Rerun);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("\"always\"")]
    [InlineData("\"once,eachStart\"")]
    [InlineData("\"each_start\"")]
    public void Load_RerunRejectsIntegersAndUnknownStrings(string value)
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, $$"""{ "envScript": { "program": "p", "rerun": {{value}} } }""");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));

        Assert.Contains("Malformed JSON", ex.Message);
    }

    [Fact]
    public void Clone_CopiesEnvScriptAndSecret_Deeply()
    {
        CmdrConfig config = FullConfig();
        GroupSpec original = config.Groups[0];

        var group = (GroupSpec)original.Clone();
        var command = (CommandSpec)group.Items[0];
        original.EnvScript!.Args.Add("changed");
        original.EnvScript.Program = "changed";
        original.Secret!.Add("CHANGED");
        ((CommandSpec)original.Items[0]).Secret!.Add("CHANGED");

        Assert.Equal("group-script", group.EnvScript!.Program);
        Assert.Empty(group.EnvScript.Args);
        Assert.Equal(new[] { "G_SECRET" }, group.Secret);
        Assert.Equal(new[] { "C_SECRET" }, command.Secret);
        Assert.NotSame(original.EnvScript, group.EnvScript);
        Assert.NotSame(original.Secret, group.Secret);
    }

    [Fact]
    public void EnvScriptClone_IsDeep()
    {
        EnvScriptSpec original = Script();

        EnvScriptSpec clone = original.Clone();
        original.Args.Add("more");

        Assert.Equal(new[] { "-c", "echo ${env:HOME}" }, clone.Args);
        Assert.Equal(EnvScriptRerun.EachStart, clone.Rerun);
        Assert.Equal(5, clone.TimeoutSeconds);
        Assert.Equal("scripts", clone.Cwd);
    }

    [Fact]
    public void DefaultTimeoutSeconds_IsThirty()
    {
        Assert.Equal(30, EnvScriptSpec.DefaultTimeoutSeconds);
    }
}
