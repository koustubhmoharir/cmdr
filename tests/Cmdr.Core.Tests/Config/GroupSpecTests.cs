using System.Text.Json;
using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class GroupSpecTests
{
    [Fact]
    public void Mode_DefaultsToParallel()
    {
        Assert.Equal(GroupMode.Parallel, new GroupSpec().Mode);
        Assert.Equal("", new GroupSpec().Name);
        Assert.Null(new GroupSpec().Description);
        Assert.Empty(new GroupSpec().Commands);
    }

    [Fact]
    public void Save_WritesModeAsString_AndOmitsNullDescription()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec { Name = "a", Mode = GroupMode.Parallel },
                new GroupSpec { Name = "b", Description = "second", Mode = GroupMode.Sequential },
            },
        };

        ConfigStore.Save(config, path);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement first = doc.RootElement.GetProperty("groups")[0];
        JsonElement second = doc.RootElement.GetProperty("groups")[1];
        Assert.Equal("parallel", first.GetProperty("mode").GetString());
        Assert.False(first.TryGetProperty("description", out _));
        Assert.Equal("sequential", second.GetProperty("mode").GetString());
        Assert.Equal("second", second.GetProperty("description").GetString());
    }

    [Fact]
    public void RoundTrip_PreservesModeAndDescription()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec { Name = "a", Description = "desc", Mode = GroupMode.Sequential },
                new GroupSpec { Name = "b", Mode = GroupMode.Parallel },
            },
        };

        ConfigStore.Save(config, path);
        CmdrConfig loaded = ConfigStore.Load(path);

        Assert.Equal("desc", loaded.Groups[0].Description);
        Assert.Equal(GroupMode.Sequential, loaded.Groups[0].Mode);
        Assert.Null(loaded.Groups[1].Description);
        Assert.Equal(GroupMode.Parallel, loaded.Groups[1].Mode);
    }

    [Fact]
    public void Load_ModeMissing_DefaultsToParallel()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "a" } ] }""");

        Assert.Equal(GroupMode.Parallel, Assert.Single(ConfigStore.Load(path).Groups).Mode);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    public void Load_IntegerMode_IsRejected(string value)
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, $$"""{ "groups": [ { "name": "a", "mode": {{value}} } ] }""");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains(path, ex.Message);
    }

    [Theory]
    [InlineData("parallelish")]
    [InlineData("")]
    public void Load_UnknownModeString_IsRejected(string value)
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, $$"""{ "groups": [ { "name": "a", "mode": "{{value}}" } ] }""");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Clone_CopiesAllValues()
    {
        var original = new GroupSpec
        {
            Name = "build",
            Description = "desc",
            Mode = GroupMode.Sequential,
            Commands =
            {
                new CommandSpec { Name = "restore", Program = "dotnet", Args = { "restore" } },
                new CommandSpec { Name = "build", Program = "dotnet", Args = { "build" } },
            },
        };

        GroupSpec clone = original.Clone();

        Assert.Equal("build", clone.Name);
        Assert.Equal("desc", clone.Description);
        Assert.Equal(GroupMode.Sequential, clone.Mode);
        Assert.Equal(2, clone.Commands.Count);
        Assert.Equal("restore", clone.Commands[0].Name);
        Assert.Equal(new[] { "build" }, clone.Commands[1].Args);
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var original = new GroupSpec
        {
            Name = "build",
            Commands = { new CommandSpec { Name = "restore", Program = "dotnet", Args = { "restore" } } },
        };

        GroupSpec clone = original.Clone();
        clone.Name = "changed";
        clone.Mode = GroupMode.Sequential;
        clone.Commands.Add(new CommandSpec { Name = "extra" });
        clone.Commands[0].Name = "renamed";
        clone.Commands[0].Args.Add("--x");
        clone.Commands[0].Stop.Method = StopMethod.Input;
        clone.Commands[0].Log.Enabled = true;

        Assert.Equal("build", original.Name);
        Assert.Equal(GroupMode.Parallel, original.Mode);
        Assert.Single(original.Commands);
        Assert.Equal("restore", original.Commands[0].Name);
        Assert.Equal(new[] { "restore" }, original.Commands[0].Args);
        Assert.Equal(StopMethod.CtrlC, original.Commands[0].Stop.Method);
        Assert.False(original.Commands[0].Log.Enabled);
    }
}
