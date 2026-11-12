using System.Text.Json;
using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class ConfigStoreTests
{
    [Fact]
    public void Load_MissingFile_ReturnsEmptyDefaults()
    {
        using var dir = new TempDir();

        CmdrConfig config = ConfigStore.Load(dir.File("missing.json"));

        Assert.Equal(10000, config.BufferLines);
        Assert.Empty(config.Groups);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        var original = new CmdrConfig
        {
            BufferLines = 500,
            Groups =
            {
                new GroupSpec
                {
                    Name = "servers",
                    Description = "Dev servers.",
                    Mode = GroupMode.Parallel,
                    Items =
                    {
                        new CommandSpec
                        {
                            Name = "web",
                            Description = "Frontend dev server.\nNeeds the api running first.\r\nLine three.",
                            Program = "pnpm",
                            Args = { "start", "--port", "3000" },
                            Cwd = "../web",
                            Env = new Dictionary<string, string?>
                            {
                                ["FORCE_COLOR"] = "1",
                                ["SOME_VAR"] = null,
                            },
                            Stop = new StopSpec { Method = StopMethod.CtrlC },
                            Log = new LogSpec { Enabled = true, Path = "logs/web.log" },
                        },
                        new CommandSpec
                        {
                            Name = "tool",
                            Program = "/opt/tool/run",
                            Cwd = ".",
                            Stop = new StopSpec { Method = StopMethod.Input, Input = "q\n" },
                        },
                    },
                },
                new GroupSpec
                {
                    Name = "build",
                    Mode = GroupMode.Sequential,
                    Items = { new CommandSpec { Name = "web", Program = "dotnet" } },
                },
            },
        };

        ConfigStore.Save(original, path);
        CmdrConfig loaded = ConfigStore.Load(path);

        Assert.Equal(500, loaded.BufferLines);
        Assert.Equal(2, loaded.Groups.Count);

        GroupSpec servers = loaded.Groups[0];
        Assert.Equal("servers", servers.Name);
        Assert.Equal("Dev servers.", servers.Description);
        Assert.Equal(GroupMode.Parallel, servers.Mode);
        Assert.Equal(2, servers.Items.Count);

        var web = (CommandSpec)servers.Items[0];
        Assert.Equal("web", web.Name);
        Assert.Equal("Frontend dev server.\nNeeds the api running first.\r\nLine three.", web.Description);
        Assert.Equal("pnpm", web.Program);
        Assert.Equal(new[] { "start", "--port", "3000" }, web.Args);
        Assert.Equal("../web", web.Cwd);
        Assert.Equal(2, web.Env.Count);
        Assert.Equal("1", web.Env["FORCE_COLOR"]);
        Assert.True(web.Env.ContainsKey("SOME_VAR"));
        Assert.Null(web.Env["SOME_VAR"]);
        Assert.Equal(StopMethod.CtrlC, web.Stop.Method);
        Assert.True(web.Log.Enabled);
        Assert.Equal("logs/web.log", web.Log.Path);

        var tool = (CommandSpec)servers.Items[1];
        Assert.Null(tool.Description);
        Assert.Null(tool.Log.Path);
        Assert.False(tool.Log.Enabled);
        Assert.Empty(tool.Args);
        Assert.Empty(tool.Env);
        Assert.Equal(StopMethod.Input, tool.Stop.Method);
        Assert.Equal("q\n", tool.Stop.Input);

        GroupSpec build = loaded.Groups[1];
        Assert.Null(build.Description);
        Assert.Equal(GroupMode.Sequential, build.Mode);
        Assert.Equal("web", Assert.Single(build.Items).Name);
    }

    [Fact]
    public void Save_WritesCamelCaseStopNamesAndOmitsNulls()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "g",
                    Items =
                    {
                        new CommandSpec
                        {
                            Name = "a",
                            Program = "p",
                            Env = new Dictionary<string, string?> { ["GONE"] = null },
                        },
                        new CommandSpec
                        {
                            Name = "b",
                            Program = "q",
                            Stop = new StopSpec { Method = StopMethod.Input, Input = "exit\n" },
                            Log = new LogSpec { Enabled = true, Path = "x.log" },
                        },
                    },
                },
            },
        };

        ConfigStore.Save(config, path);
        string text = File.ReadAllText(path);

        Assert.Contains("\"bufferLines\"", text);
        Assert.Contains("\"groups\"", text);
        Assert.Contains("\"ctrlC\"", text);
        Assert.Contains("\"input\"", text);
        Assert.Contains("\"GONE\": null", text);
        Assert.Contains("\n  ", text); // indented

        using JsonDocument doc = JsonDocument.Parse(text);
        JsonElement group = doc.RootElement.GetProperty("groups")[0];
        Assert.False(group.TryGetProperty("description", out _));
        Assert.Equal("parallel", group.GetProperty("mode").GetString());

        JsonElement first = group.GetProperty("items")[0];
        Assert.False(first.TryGetProperty("description", out _));
        Assert.False(first.TryGetProperty("cwd", out _));
        Assert.Equal("ctrlC", first.GetProperty("stop").GetProperty("method").GetString());
        Assert.False(first.GetProperty("stop").TryGetProperty("input", out _));
        Assert.False(first.GetProperty("log").TryGetProperty("path", out _));
        Assert.Equal(JsonValueKind.Null, first.GetProperty("env").GetProperty("GONE").ValueKind);

        JsonElement second = group.GetProperty("items")[1];
        Assert.Equal("input", second.GetProperty("stop").GetProperty("method").GetString());
        Assert.Equal("exit\n", second.GetProperty("stop").GetProperty("input").GetString());
        Assert.Equal("x.log", second.GetProperty("log").GetProperty("path").GetString());
    }

    [Fact]
    public void Load_MinimalGroupAndCommand_AppliesDefaults()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p" } ] } ] }""");

        CmdrConfig config = ConfigStore.Load(path);

        Assert.Equal(10000, config.BufferLines);
        GroupSpec group = Assert.Single(config.Groups);
        Assert.Equal("g", group.Name);
        Assert.Null(group.Description);
        Assert.Equal(GroupMode.Parallel, group.Mode);
        var cmd = (CommandSpec)Assert.Single(group.Items);
        Assert.Equal("a", cmd.Name);
        Assert.Equal("p", cmd.Program);
        Assert.Null(cmd.Description);
        Assert.Null(cmd.Cwd);
        Assert.Empty(cmd.Args);
        Assert.Empty(cmd.Env);
        Assert.Equal(StopMethod.CtrlC, cmd.Stop.Method);
        Assert.Null(cmd.Stop.Input);
        Assert.False(cmd.Log.Enabled);
        Assert.Null(cmd.Log.Path);
    }

    [Fact]
    public void Load_EmptyObject_AppliesDefaults()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, "{}");

        CmdrConfig config = ConfigStore.Load(path);

        Assert.Equal(10000, config.BufferLines);
        Assert.Empty(config.Groups);
    }

    [Fact]
    public void Load_IgnoresLegacyTopLevelCommandsKey()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "commands": [ { "name": "a", "program": "p" } ] }""");

        CmdrConfig config = ConfigStore.Load(path);

        Assert.Empty(config.Groups);
    }

    [Fact]
    public void Load_AcceptsCommentsTrailingCommasAndUnknownProperties()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """
            {
              // global settings
              "bufferLines": 42,
              "futureSetting": { "nested": [1, 2, 3], },
              /* block comment */
              "groups": [
                {
                  "name": "g",
                  "somethingNew": 1,
                  "items": [
                    {
                      "name": "a",
                      "program": "p",
                      "args": ["x", "y",],
                      "somethingNew": true,
                      "stop": { "method": "ctrlC", }, // trailing comment
                    },
                  ],
                },
              ],
            }
            """);

        CmdrConfig config = ConfigStore.Load(path);

        Assert.Equal(42, config.BufferLines);
        var cmd = (CommandSpec)Assert.Single(Assert.Single(config.Groups).Items);
        Assert.Equal(new[] { "x", "y" }, cmd.Args);
        Assert.Equal(StopMethod.CtrlC, cmd.Stop.Method);
    }

    [Fact]
    public void Leftovers_DetachIsReadAndWritten_DefaultIsOmitted()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "leftovers": "detach" }, { "name": "b", "program": "p" } ] } ] }""");

        GroupSpec group = Assert.Single(ConfigStore.Load(path).Groups);
        Assert.Equal(LeftoverMode.Detach, ((CommandSpec)group.Items[0]).Leftovers);
        Assert.Equal(LeftoverMode.Track, ((CommandSpec)group.Items[1]).Leftovers);

        ConfigStore.Save(ConfigStore.Load(path), path);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement items = doc.RootElement.GetProperty("groups")[0].GetProperty("items");
        Assert.Equal("detach", items[0].GetProperty("leftovers").GetString());
        Assert.False(items[1].TryGetProperty("leftovers", out _));
    }

    [Fact]
    public void Stdin_ConsoleIsReadAndWritten_DefaultIsOmitted()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "stdin": "console" }, { "name": "b", "program": "p" } ] } ] }""");

        GroupSpec group = Assert.Single(ConfigStore.Load(path).Groups);
        Assert.Equal(StdinMode.Console, ((CommandSpec)group.Items[0]).Stdin);
        Assert.Equal(StdinMode.Pipe, ((CommandSpec)group.Items[1]).Stdin);

        ConfigStore.Save(ConfigStore.Load(path), path);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement items = doc.RootElement.GetProperty("groups")[0].GetProperty("items");
        Assert.Equal("console", items[0].GetProperty("stdin").GetString());
        Assert.False(items[1].TryGetProperty("stdin", out _));
    }

    [Theory]
    [InlineData("""{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "stdin": "Console" } ] } ] }""")]
    [InlineData("""{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "stdin": "tty" } ] } ] }""")]
    [InlineData("""{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "stdin": 1 } ] } ] }""")]
    public void Load_InvalidStdin_ThrowsConfigException(string json)
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, json);

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains(path, ex.Message);
    }

    [Theory]
    [InlineData("""{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "leftovers": "Detach" } ] } ] }""")]
    [InlineData("""{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "leftovers": "keep" } ] } ] }""")]
    [InlineData("""{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "leftovers": 1 } ] } ] }""")]
    public void Load_InvalidLeftovers_ThrowsConfigException(string json)
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, json);

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains(path, ex.Message);
    }

    [Theory]
    [InlineData("""{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "stop": { "method": "ctrlc" } } ] } ] }""")]
    [InlineData("""{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "stop": { "method": 3 } } ] } ] }""")]
    public void Load_InvalidStopMethod_ThrowsConfigException(string json)
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, json);

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Load_MalformedJson_ThrowsConfigExceptionWithPathAndLine()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, "{\n  \"bufferLines\": 5,\n  \"groups\": [\n    oops\n  ]\n}");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));

        Assert.Contains(path, ex.Message);
        Assert.Contains("line 4", ex.Message);
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);
    }

    [Fact]
    public void Load_JsonNull_ThrowsConfigException()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, "null");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Load_NullNestedValues_ReplacedWithDefaults()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """
            { "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "args": null, "env": null, "stop": null, "log": null } ] } ] }
            """);

        var cmd = (CommandSpec)Assert.Single(Assert.Single(ConfigStore.Load(path).Groups).Items);

        Assert.NotNull(cmd.Args);
        Assert.Empty(cmd.Args);
        Assert.NotNull(cmd.Env);
        Assert.Empty(cmd.Env);
        Assert.Equal(StopMethod.CtrlC, cmd.Stop.Method);
        Assert.False(cmd.Log.Enabled);
    }

    [Fact]
    public void Load_NullGroups_BecomeEmpty()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": null }""");

        CmdrConfig config = ConfigStore.Load(path);

        Assert.NotNull(config.Groups);
        Assert.Empty(config.Groups);
    }

    [Fact]
    public void Load_NullGroupCommands_BecomeEmpty()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "g", "items": null } ] }""");

        GroupSpec group = Assert.Single(ConfigStore.Load(path).Groups);

        Assert.NotNull(group.Items);
        Assert.Empty(group.Items);
    }

    [Fact]
    public void Load_NullGroupName_BecomesEmptyString()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": null } ] }""");

        Assert.Equal("", Assert.Single(ConfigStore.Load(path).Groups).Name);
    }

    [Fact]
    public void Load_NullGroupEntry_ThrowsConfigExceptionWithIndex()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "a" }, null ] }""");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains(path, ex.Message);
        Assert.Contains("groups[1]", ex.Message);
    }

    [Fact]
    public void Load_NullCommandEntry_ThrowsConfigExceptionWithGroupAndCommandIndex()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "a" }, { "name": "b", "items": [ null ] } ] }""");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains(path, ex.Message);
        Assert.Contains("groups[1].items[0]", ex.Message);
    }

    [Fact]
    public void Load_NullCommandArg_ThrowsConfigExceptionWithPath()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "a", "items": [ { "name": "x", "program": "p", "args": ["ok", null] } ] } ] }""");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains("groups[0].items[0].args[1]", ex.Message);
    }

    [Fact]
    public void Save_CreatesMissingDirectoryAndLeavesNoTempFiles()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "nested", "deeper", "cmdr.json");

        ConfigStore.Save(new CmdrConfig(), path);

        Assert.True(File.Exists(path));
        Assert.Equal(new[] { "cmdr.json" }, Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { BufferLines = 1 }, path);

        ConfigStore.Save(new CmdrConfig { BufferLines = 2 }, path);

        Assert.Equal(2, ConfigStore.Load(path).BufferLines);
        Assert.Equal(new[] { "cmdr.json" }, Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_WritesUtf8WithoutBomAndTrailingNewline()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "grüppe ✓", Items = { new CommandSpec { Name = "héllo ✓", Program = "p" } } } },
        };

        ConfigStore.Save(config, path);
        byte[] bytes = File.ReadAllBytes(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal((byte)'\n', bytes[^1]);
        CmdrConfig loaded = ConfigStore.Load(path);
        Assert.Equal("grüppe ✓", loaded.Groups[0].Name);
        Assert.Equal("héllo ✓", loaded.Groups[0].Items[0].Name);
    }

    [Fact]
    public void LogsDir_RoundTripsAndIsOmittedWhenNull()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");

        ConfigStore.Save(new CmdrConfig(), path);
        Assert.DoesNotContain("logsDir", File.ReadAllText(path));
        Assert.Null(ConfigStore.Load(path).LogsDir);

        ConfigStore.Save(new CmdrConfig { LogsDir = "../logs" }, path);
        Assert.Contains("\"logsDir\": \"../logs\"", File.ReadAllText(path));
        Assert.Equal("../logs", ConfigStore.Load(path).LogsDir);
    }
}
