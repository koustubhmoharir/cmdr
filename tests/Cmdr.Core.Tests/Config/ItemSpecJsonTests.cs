using System.Text.Json;
using Cmdr.Core.Config;
using Shared.Processes;

namespace Cmdr.Core.Tests.Config;

/// <summary>JSON shape of the item tree: round trips, discrimination of commands and groups, and legacy input.</summary>
public class ItemSpecJsonTests
{
    private static CmdrConfig ThreeLevelTree() => new()
    {
        Groups =
        {
            new GroupSpec
            {
                Name = "go-apps",
                Cwd = "../golang",
                Env = new Dictionary<string, string?> { ["TMP"] = "${cwd}/tmp", ["GONE"] = null },
                Items =
                {
                    new CommandSpec { Name = "meta", Program = "go", Args = { "run", "./gen" } },
                    new GroupSpec
                    {
                        Name = "nav",
                        Mode = GroupMode.Sequential,
                        Cwd = "nav",
                        Env = new Dictionary<string, string?> { ["NAV_ONLY"] = "1", ["PATH"] = "${env:PATH}${pathsep}/x" },
                        Items =
                        {
                            new CommandSpec { Name = "build", Program = "go", Args = { "build" }, Description = "Builds.\nTwo lines." },
                            new GroupSpec
                            {
                                Name = "deep",
                                Description = "innermost",
                                Items = { new CommandSpec { Name = "run", Program = "nav/__debug_bin", Cwd = "bin" } },
                            },
                            new CommandSpec { Name = "after", Program = "echo" },
                        },
                    },
                    new CommandSpec { Name = "run", Program = "nav/__debug_bin", Stop = new StopSpec { Method = StopMethod.Input, Input = "q\n" } },
                },
            },
        },
    };

    [Fact]
    public void RoundTrip_ThreeLevelTree_PreservesOrderTypesAndSettings()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");

        ConfigStore.Save(ThreeLevelTree(), path);
        CmdrConfig loaded = ConfigStore.Load(path);

        GroupSpec goApps = Assert.Single(loaded.Groups);
        Assert.Equal("../golang", goApps.Cwd);
        Assert.Equal(new Dictionary<string, string?> { ["TMP"] = "${cwd}/tmp", ["GONE"] = null }, goApps.Env);
        Assert.Equal(3, goApps.Items.Count);

        var meta = Assert.IsType<CommandSpec>(goApps.Items[0]);
        Assert.Equal("meta", meta.Name);
        Assert.Equal(new[] { "run", "./gen" }, meta.Args);

        var nav = Assert.IsType<GroupSpec>(goApps.Items[1]);
        Assert.Equal("nav", nav.Name);
        Assert.Equal(GroupMode.Sequential, nav.Mode);
        Assert.Equal("nav", nav.Cwd);
        Assert.Equal(new Dictionary<string, string?> { ["NAV_ONLY"] = "1", ["PATH"] = "${env:PATH}${pathsep}/x" }, nav.Env);
        Assert.Equal(3, nav.Items.Count);
        Assert.Equal("build", nav.Items[0].Name);
        Assert.Equal("Builds.\nTwo lines.", nav.Items[0].Description);
        var deep = Assert.IsType<GroupSpec>(nav.Items[1]);
        Assert.Equal("innermost", deep.Description);
        Assert.Null(deep.Cwd);
        Assert.Null(deep.Env);
        Assert.Equal("bin", ((CommandSpec)Assert.Single(deep.Items)).Cwd);
        Assert.Equal("after", nav.Items[2].Name);

        var run = Assert.IsType<CommandSpec>(goApps.Items[2]);
        Assert.Equal(StopMethod.Input, run.Stop.Method);
        Assert.Equal("q\n", run.Stop.Input);
    }

    [Fact]
    public void Save_WritesItemsAndOmitsNullDescriptionsAndEnv()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");

        ConfigStore.Save(ThreeLevelTree(), path);
        string text = File.ReadAllText(path);

        Assert.DoesNotContain("\"commands\"", text);
        using JsonDocument doc = JsonDocument.Parse(text);
        JsonElement top = doc.RootElement.GetProperty("groups")[0];
        Assert.False(top.TryGetProperty("description", out _));
        Assert.Equal("../golang", top.GetProperty("cwd").GetString());
        Assert.Equal(JsonValueKind.Null, top.GetProperty("env").GetProperty("GONE").ValueKind);

        JsonElement nav = top.GetProperty("items")[1];
        Assert.Equal("sequential", nav.GetProperty("mode").GetString());
        Assert.False(nav.GetProperty("items")[2].TryGetProperty("description", out _));

        JsonElement deep = nav.GetProperty("items")[1];
        Assert.False(deep.TryGetProperty("env", out _));
        Assert.False(deep.TryGetProperty("cwd", out _));
        Assert.True(deep.TryGetProperty("items", out _));
        Assert.False(deep.TryGetProperty("program", out _));

        JsonElement command = nav.GetProperty("items")[0];
        Assert.True(command.TryGetProperty("program", out _));
        Assert.False(command.TryGetProperty("items", out _));
    }

    [Fact]
    public void Load_NullGroupEnvValues_ArePreserved()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "g", "env": { "KEEP": "1", "REMOVE": null } } ] }""");

        GroupSpec group = Assert.Single(ConfigStore.Load(path).Groups);

        Assert.True(group.Env!.ContainsKey("REMOVE"));
        Assert.Null(group.Env["REMOVE"]);
        Assert.Equal("1", group.Env["KEEP"]);
    }

    [Fact]
    public void Load_NestedGroupWithoutEnv_HasNullEnv()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "g", "items": [ { "name": "n", "items": [] } ] } ] }""");

        var nested = (GroupSpec)Assert.Single(Assert.Single(ConfigStore.Load(path).Groups).Items);
        Assert.Null(nested.Env);
        Assert.Empty(nested.Items);
    }

    [Fact]
    public void Load_ItemWithItemsAndProgram_IsRejectedAsBoth()
    {
        ConfigException ex = LoadFails("""{ "groups": [ { "name": "g", "items": [ { "name": "x", "program": "p", "items": [] } ] } ] }""");

        Assert.Contains("both", ex.Message);
        Assert.Contains("'items'", ex.Message);
        Assert.Contains("'program'", ex.Message);
    }

    [Fact]
    public void Load_ItemWithNeitherItemsNorProgram_IsRejected()
    {
        ConfigException ex = LoadFails("""{ "groups": [ { "name": "g", "items": [ { "name": "x", "args": [] } ] } ] }""");

        Assert.Contains("must have 'program'", ex.Message);
    }

    [Fact]
    public void Load_LegacyTopLevelGroupCommands_IsRejectedWithPath()
    {
        ConfigException ex = LoadFails("""{ "groups": [ { "name": "g", "commands": [ { "name": "a", "program": "p" } ] } ] }""");

        Assert.Contains("groups[0]", ex.Message);
        Assert.Contains("use 'items' instead of 'commands'", ex.Message);
    }

    [Theory]
    [InlineData("{ \"groups\": [ { \"name\": \"build\", \"program\": \"dotnet\" } ] }")]
    [InlineData("{ \"groups\": [ { \"name\": \"build\", \"program\": \"dotnet\", \"items\": [] } ] }")]
    public void Load_TopLevelCommandShapeInGroups_IsRejected(string json)
    {
        ConfigException ex = LoadFails(json);

        Assert.Contains("groups[0]", ex.Message);
        Assert.Contains("program", ex.Message);
    }

    [Fact]
    public void Load_LegacyNestedGroupCommands_IsRejectedWithItemPath()
    {
        ConfigException ex = LoadFails("""
            { "groups": [ { "name": "g", "items": [
                { "name": "ok", "program": "p" },
                { "name": "inner", "commands": [ { "name": "a", "program": "p" } ] }
            ] } ] }
            """);

        Assert.Contains("groups[0].items[1]", ex.Message);
        Assert.Contains("use 'items' instead of 'commands'", ex.Message);
    }

    [Fact]
    public void Load_GroupWithItemsAndLegacyCommands_IsRejected()
    {
        ConfigException ex = LoadFails("""{ "groups": [ { "name": "g", "items": [ { "name": "n", "items": [], "commands": [] } ] } ] }""");

        Assert.Contains("groups[0].items[0]", ex.Message);
        Assert.Contains("use 'items' instead of 'commands'", ex.Message);
    }

    [Fact]
    public void Load_ItemThatIsNotAnObject_IsRejected()
    {
        ConfigException ex = LoadFails("""{ "groups": [ { "name": "g", "items": [ "nope" ] } ] }""");

        Assert.Contains("must be a JSON object", ex.Message);
    }

    [Fact]
    public void Load_NullItem_ThrowsConfigExceptionWithPath()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p" }, { "name": "n", "items": [ null ] } ] } ] }""");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));

        Assert.Contains(path, ex.Message);
        Assert.Contains("groups[0].items[1].items[0]", ex.Message);
    }

    [Fact]
    public void Load_NullNestedItems_BecomeEmpty()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "g", "items": [ { "name": "n", "items": null } ] } ] }""");

        var nested = (GroupSpec)Assert.Single(Assert.Single(ConfigStore.Load(path).Groups).Items);
        Assert.NotNull(nested.Items);
        Assert.Empty(nested.Items);
    }

    [Fact]
    public void Load_NullNestedName_BecomesEmptyString()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "g", "items": [ { "name": null, "items": [] } ] } ] }""");

        Assert.Equal("", Assert.Single(Assert.Single(ConfigStore.Load(path).Groups).Items).Name);
    }

    private static ConfigException LoadFails(string json)
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, json);
        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains(path, ex.Message);
        return ex;
    }
}
