using System.Text.Json;
using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class GroupSpecTests
{
    [Fact]
    public void Defaults()
    {
        var group = new GroupSpec();

        Assert.Equal(GroupMode.Parallel, group.Mode);
        Assert.Equal("", group.Name);
        Assert.Null(group.Description);
        Assert.Null(group.Cwd);
        Assert.Null(group.Env);
        Assert.Empty(group.Items);
    }

    [Fact]
    public void Save_WritesModeAsString_AndOmitsNullFields()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { Groups = { new GroupSpec { Name = "g", Mode = GroupMode.Sequential } } }, path);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement group = doc.RootElement.GetProperty("groups")[0];
        Assert.Equal("sequential", group.GetProperty("mode").GetString());
        Assert.False(group.TryGetProperty("description", out _));
        Assert.False(group.TryGetProperty("cwd", out _));
        Assert.False(group.TryGetProperty("env", out _));
        Assert.Equal(JsonValueKind.Array, group.GetProperty("items").ValueKind);
    }

    [Fact]
    public void Load_ModeMissing_DefaultsToParallel()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "g" } ] }""");

        Assert.Equal(GroupMode.Parallel, Assert.Single(ConfigStore.Load(path).Groups).Mode);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("\"parallelish\"")]
    [InlineData("\"\"")]
    public void Load_NonCanonicalMode_IsRejected(string value)
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, $$"""{ "groups": [ { "name": "g", "mode": {{value}} } ] }""");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Clone_CopiesAllGroupFields()
    {
        var original = new GroupSpec
        {
            Name = "nav",
            Description = "desc",
            Mode = GroupMode.Sequential,
            Cwd = "../golang",
            Env = new Dictionary<string, string?> { ["A"] = "1", ["B"] = null },
            Items =
            {
                new CommandSpec { Name = "restore", Program = "dotnet", Args = { "restore" } },
                new GroupSpec { Name = "inner", Items = { new CommandSpec { Name = "deep", Program = "p" } } },
            },
        };

        GroupSpec clone = original.Clone();

        Assert.Equal("nav", clone.Name);
        Assert.Equal("desc", clone.Description);
        Assert.Equal(GroupMode.Sequential, clone.Mode);
        Assert.Equal("../golang", clone.Cwd);
        Assert.Equal(new Dictionary<string, string?> { ["A"] = "1", ["B"] = null }, clone.Env);
        Assert.Equal(2, clone.Items.Count);
        Assert.Equal("restore", clone.Items[0].Name);
        Assert.IsType<GroupSpec>(clone.Items[1]);
        Assert.Equal("deep", Assert.Single(((GroupSpec)clone.Items[1]).Items).Name);
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var original = new GroupSpec
        {
            Name = "g",
            Env = new Dictionary<string, string?> { ["A"] = "1" },
            Items =
            {
                new CommandSpec { Name = "restore", Program = "dotnet", Args = { "restore" } },
                new GroupSpec { Name = "inner", Items = { new CommandSpec { Name = "deep", Program = "p" } } },
            },
        };

        GroupSpec clone = original.Clone();
        clone.Env!["A"] = "changed";
        clone.Items.Add(new CommandSpec { Name = "extra" });
        ((CommandSpec)clone.Items[0]).Args.Add("--x");
        ((GroupSpec)clone.Items[1]).Items.Clear();

        Assert.Equal("1", original.Env!["A"]);
        Assert.Equal(2, original.Items.Count);
        Assert.Equal(new[] { "restore" }, ((CommandSpec)original.Items[0]).Args);
        Assert.Single(((GroupSpec)original.Items[1]).Items);
    }
}
