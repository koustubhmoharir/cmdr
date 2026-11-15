using System.Diagnostics;
using System.Text.Json;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;

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
    public void Load_Utf8BomAndComments_IsAccepted()
    {
        using var dir = new TempDir();
        string path = dir.File("bom.json");
        File.WriteAllBytes(path, [.. new System.Text.UTF8Encoding(true).GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes("// comment\n{ \"groups\": [{ \"name\": \"g\", \"items\": [] }] }")]);

        CmdrConfig config = ConfigStore.Load(path);

        Assert.Equal("g", Assert.Single(config.Groups).Name);
    }

    [Fact]
    public void Load_InvalidOpenedPath_IsConfigException()
    {
        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load("bad\0config.json"));

        Assert.Contains("Invalid configuration path", ex.Message);
    }

    [Fact]
    public void Load_InaccessibleParent_IsConfigExceptionInsteadOfDefaults()
    {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root")
        {
            return;
        }

        using var dir = new TempDir();
        string parent = dir.File("private");
        Directory.CreateDirectory(parent);
        string path = Path.Combine(parent, "cmdr.json");
        ConfigStore.Save(new CmdrConfig { BufferLines = 7 }, path);
        UnixFileMode originalMode = File.GetUnixFileMode(parent);
        try
        {
            File.SetUnixFileMode(parent, UnixFileMode.None);

            ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));

            Assert.Contains("Could not read configuration", ex.Message);
        }
        finally
        {
            File.SetUnixFileMode(parent, originalMode);
        }
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
    [InlineData("""{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "stdin": "pipe, console" } ] } ] }""")]
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
    [InlineData("""{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "leftovers": "track, detach" } ] } ] }""")]
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
    [InlineData("""{ "groups": [ { "name": "g", "items": [ { "name": "a", "program": "p", "stop": { "method": "ctrlC, input", "input": "x" } } ] } ] }""")]
    public void Load_InvalidStopMethod_ThrowsConfigException(string json)
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, json);

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));
        Assert.Contains(path, ex.Message);
    }

    [Theory]
    [InlineData("parallel, sequential")]
    [InlineData("parallel, parallel")]
    public void Load_CompositeGroupMode_ThrowsConfigException(string mode)
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { groups = new[] { new { name = "g", mode, items = Array.Empty<object>() } } }));

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
    public void Load_NestedTypedError_UsesWholeFileLine()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, "{\n  \"groups\": [\n    { \"name\": \"g\",\n      \"description\": \"é\",\n      \"items\": [\n        { \"name\": \"c\", \"program\": \"p\",\n          \"args\": 12 }\n      ] }\n  ]\n}");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));

        Assert.True(ex.Message.Contains("line 7", StringComparison.Ordinal), ex.Message);
        Assert.Contains("$.args", ex.Message);
    }

    [Fact]
    public void Load_DeepNestedTypedError_UsesWholeFileLine()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, "{\n  \"groups\": [\n    { \"name\": \"outer\", \"items\": [\n      { \"name\": \"inner\", \"items\": [\n        { \"name\": \"c\", \"program\": \"p\",\n          \"args\": 12 }\n      ] }\n    ] }\n  ]\n}");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));

        Assert.True(ex.Message.Contains("line 6", StringComparison.Ordinal), ex.Message);
        Assert.Contains("$.args", ex.Message);
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

    [Fact]
    public void Save_KeepsExistingOwnerOnlyMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { BufferLines = 1 }, path);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        ConfigStore.Save(new CmdrConfig { BufferLines = 2 }, path);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal(2, ConfigStore.Load(path).BufferLines);
        Assert.Equal(new[] { "cmdr.json" }, Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_KeepsExistingWorldWritableMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // The default umask would narrow a mode created from this one, so this proves the mode is set explicitly.
        const UnixFileMode everyone = UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite;
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig(), path);
        File.SetUnixFileMode(path, everyone);

        ConfigStore.Save(new CmdrConfig { BufferLines = 3 }, path);

        Assert.Equal(everyone, File.GetUnixFileMode(path));
    }

    [Fact]
    public void Save_ThroughRelativeSymlink_UpdatesTargetAndKeepsLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        string target = dir.File("target.json");
        string link = dir.File("linked.json");
        ConfigStore.Save(new CmdrConfig { BufferLines = 1 }, target);
        File.CreateSymbolicLink(link, "target.json");

        ConfigStore.Save(new CmdrConfig { BufferLines = 5 }, link);

        Assert.Equal("target.json", new FileInfo(link).LinkTarget);
        Assert.Equal(5, ConfigStore.Load(target).BufferLines);
        Assert.Equal(5, ConfigStore.Load(link).BufferLines);
        Assert.Equal(new[] { "linked.json", "target.json" }, Directory.GetFiles(dir.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Save_ThroughAbsoluteSymlink_UpdatesTargetAndKeepsLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        string target = dir.File("target.json");
        string link = dir.File("linked.json");
        ConfigStore.Save(new CmdrConfig { BufferLines = 1 }, target);
        File.CreateSymbolicLink(link, target);

        ConfigStore.Save(new CmdrConfig { BufferLines = 6 }, link);

        Assert.Equal(target, new FileInfo(link).LinkTarget);
        Assert.Equal(6, ConfigStore.Load(target).BufferLines);
        Assert.Equal(6, ConfigStore.Load(link).BufferLines);
    }

    [Fact]
    public void Save_ThroughDanglingSymlink_ThrowsAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("a"));
        Directory.CreateDirectory(dir.File("b"));
        string link = dir.File(Path.Combine("a", "linked.json"));
        string missing = Path.Combine("..", "b", "missing.json");
        File.CreateSymbolicLink(link, missing);

        IOException ex = Assert.Throws<IOException>(() => ConfigStore.Save(new CmdrConfig(), link));

        Assert.Contains("which does not exist", ex.Message);
        Assert.Equal(missing, new FileInfo(link).LinkTarget);
        Assert.Equal(new[] { "linked.json" }, Directory.GetFiles(dir.File("a")).Select(Path.GetFileName));
        Assert.Empty(Directory.GetFiles(dir.File("b")));
        Assert.False(File.Exists(dir.File(Path.Combine("b", "missing.json"))));
    }

    [Fact]
    public void Save_ThroughSymlinkWithDotDotAfterADirectoryLink_UpdatesTheFileTheLinkReads()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // linked.json -> alias/../target.json, alias -> other/child: the file system reads other/target.json, not the
        // target.json beside the link (which lexical normalization of the link target would name).
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File(Path.Combine("other", "child")));
        Directory.CreateSymbolicLink(dir.File("alias"), Path.Combine("other", "child"));
        ConfigStore.Save(new CmdrConfig { BufferLines = 10 }, dir.File(Path.Combine("other", "target.json")));
        ConfigStore.Save(new CmdrConfig { BufferLines = 20 }, dir.File("target.json"));
        string link = dir.File("linked.json");
        File.CreateSymbolicLink(link, Path.Combine("alias", "..", "target.json"));
        Assert.Equal(10, ConfigStore.Load(link).BufferLines);

        ConfigStore.Save(new CmdrConfig { BufferLines = 30 }, link);

        Assert.Equal(30, ConfigStore.Load(link).BufferLines);
        Assert.Equal(30, ConfigStore.Load(dir.File(Path.Combine("other", "target.json"))).BufferLines);
        Assert.Equal(20, ConfigStore.Load(dir.File("target.json")).BufferLines);
    }

    [Fact]
    public void Save_ThroughLinkWithDotDotAfterAMissingDirectory_ThrowsAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // linked.json -> missing/../target.json. The file system cannot open it (ENOENT), but a walk that drops the missing
        // directory would reach the target.json beside the link, which must not be overwritten.
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { BufferLines = 1 }, dir.File("target.json"));
        string link = dir.File("linked.json");
        File.CreateSymbolicLink(link, Path.Combine("missing", "..", "target.json"));
        byte[] before = File.ReadAllBytes(dir.File("target.json"));

        Assert.Throws<IOException>(() => ConfigStore.Save(new CmdrConfig { BufferLines = 2 }, link));

        Assert.Equal(before, File.ReadAllBytes(dir.File("target.json")));
        Assert.Equal(Path.Combine("missing", "..", "target.json"), new FileInfo(link).LinkTarget);
        Assert.Equal(new[] { "linked.json", "target.json" }, Directory.GetFileSystemEntries(dir.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Save_ThroughLinkWithDotDotAfterARegularFile_ThrowsAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // linked.json -> blocker/../target.json, with blocker a regular file: opening it fails with ENOTDIR.
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { BufferLines = 1 }, dir.File("target.json"));
        File.WriteAllText(dir.File("blocker"), "not a directory");
        string link = dir.File("linked.json");
        File.CreateSymbolicLink(link, Path.Combine("blocker", "..", "target.json"));
        byte[] before = File.ReadAllBytes(dir.File("target.json"));

        Assert.Throws<IOException>(() => ConfigStore.Save(new CmdrConfig { BufferLines = 2 }, link));

        Assert.Equal(before, File.ReadAllBytes(dir.File("target.json")));
        Assert.Equal(Path.Combine("blocker", "..", "target.json"), new FileInfo(link).LinkTarget);
        Assert.Equal(new[] { "blocker", "linked.json", "target.json" }, Directory.GetFileSystemEntries(dir.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Save_InADirectoryThatIsNotWritable_OverwritesTheWritableFileInPlace()
    {
        if (!OperatingSystem.IsLinux() || IsRoot())
        {
            return; // root can rename into a directory it cannot write, so the in-place branch is not reached
        }

        using var dir = new TempDir();
        string sub = dir.File("locked");
        Directory.CreateDirectory(sub);
        string path = Path.Combine(sub, "cmdr.json");
        ConfigStore.Save(new CmdrConfig { BufferLines = 1 }, path);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        string inode = Inode(path);
        UnixFileMode dirMode = File.GetUnixFileMode(sub);
        File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute); // no file can be created or removed
        try
        {
            ConfigStore.Save(new CmdrConfig { BufferLines = 7 }, path);

            Assert.Equal(inode, Inode(path)); // the same file, written in place: a replacement would have a new inode
            Assert.Equal(7, ConfigStore.Load(path).BufferLines);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, File.GetUnixFileMode(path));
            Assert.Equal(new[] { "cmdr.json" }, Directory.GetFiles(sub).Select(Path.GetFileName));
        }
        finally
        {
            File.SetUnixFileMode(sub, dirMode);
        }
    }

    [Fact]
    public void Save_InPlaceOverAShorterConfig_LeavesNoTrailingBytes()
    {
        if (!OperatingSystem.IsLinux() || IsRoot())
        {
            return; // root replaces the file instead of writing it in place
        }

        using var dir = new TempDir();
        string sub = dir.File("locked");
        Directory.CreateDirectory(sub);
        string path = Path.Combine(sub, "cmdr.json");
        ConfigStore.Save(new CmdrConfig { LogsDir = new string('x', 500) }, path);
        string inode = Inode(path);
        UnixFileMode dirMode = File.GetUnixFileMode(sub);
        File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            ConfigStore.Save(new CmdrConfig { BufferLines = 3 }, path);

            Assert.Equal(inode, Inode(path));
            Assert.Equal(3, ConfigStore.Load(path).BufferLines);
            Assert.DoesNotContain("xxx", File.ReadAllText(path));
        }
        finally
        {
            File.SetUnixFileMode(sub, dirMode);
        }
    }

    [Fact]
    public void Save_FileThatIsNotWritableInADirectoryThatIsNotWritable_ThrowsAndChangesNothing()
    {
        if (OperatingSystem.IsWindows() || IsRoot())
        {
            return; // root writes a read-only file, so there is no denial to expect
        }

        using var dir = new TempDir();
        string sub = dir.File("locked");
        Directory.CreateDirectory(sub);
        string path = Path.Combine(sub, "cmdr.json");
        ConfigStore.Save(new CmdrConfig { BufferLines = 1 }, path);
        File.SetUnixFileMode(path, UnixFileMode.UserRead);
        UnixFileMode dirMode = File.GetUnixFileMode(sub);
        File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => ConfigStore.Save(new CmdrConfig { BufferLines = 7 }, path));

            Assert.Equal(1, ConfigStore.Load(path).BufferLines);
            Assert.Equal(new[] { "cmdr.json" }, Directory.GetFiles(sub).Select(Path.GetFileName));
        }
        finally
        {
            File.SetUnixFileMode(sub, dirMode);
        }
    }

    /// <summary>
    /// A chain of nested groups g1 › g2 › … › g{levels}. The deepest group holds a command with args, env, ready and stop set,
    /// so the JSON is as deep as a real file gets.
    /// </summary>
    private static CmdrConfig NestedChain(int levels)
    {
        ItemSpec inner = new CommandSpec
        {
            Name = "run",
            Program = "pnpm",
            Args = { "start", "--port", "3000" },
            Env = new Dictionary<string, string?> { ["FORCE_COLOR"] = "1" },
            Ready = new ReadySpec { Pattern = "ready on", Busy = "compiling" },
            Stop = new StopSpec { Method = StopMethod.Input, Input = "q\n" },
        };
        for (int level = levels; level >= 1; level--)
        {
            inner = new GroupSpec { Name = $"g{level}", Items = { inner } };
        }

        return new CmdrConfig { Groups = { (GroupSpec)inner } };
    }

    [Fact]
    public void Nesting_SixteenLevels_SavesAndLoadsBackWithSameStructure()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        CmdrConfig original = NestedChain(CmdrConfig.MaxGroupDepth);

        ConfigStore.Save(original, path);
        CmdrConfig loaded = ConfigStore.Load(path);

        GroupSpec group = Assert.Single(loaded.Groups);
        for (int level = 2; level <= CmdrConfig.MaxGroupDepth; level++)
        {
            group = Assert.IsType<GroupSpec>(Assert.Single(group.Items));
            Assert.Equal($"g{level}", group.Name);
        }

        CommandSpec command = Assert.IsType<CommandSpec>(Assert.Single(group.Items));
        Assert.Equal("run", command.Name);
        Assert.Equal(new[] { "start", "--port", "3000" }, command.Args);
        Assert.Equal("1", command.Env["FORCE_COLOR"]);
        Assert.Equal("ready on", command.Ready?.Pattern);
        Assert.Equal("compiling", command.Ready?.Busy);
        Assert.Equal(StopMethod.Input, command.Stop.Method);
        Assert.Equal("q\n", command.Stop.Input);
        Assert.Empty(loaded.Validate());
    }

    [Fact]
    public void Nesting_SeventeenLevels_FailsToLoad()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(NestedChain(17), path);

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.Load(path));

        Assert.Contains("nested deeper than 16 group levels", ex.Message);
        // The level-17 group is reached through sixteen 'items[0]' steps from groups[0].
        string itemPath = "groups[0]" + string.Concat(Enumerable.Repeat(".items[0]", 16));
        Assert.Contains($"{itemPath} is nested deeper than 16 group levels.", ex.Message);
    }

    // File.Replace failed with ERROR_UNABLE_TO_MOVE_REPLACEMENT (1176): the destination is gone and the new content is only in the temp file.
    // RecoverFromFailedReplace is called directly, so these run on any OS.
    [Fact]
    public void RecoverFromFailedReplace_UnableToMove_MovesTheTempFileIntoPlace()
    {
        using var dir = new TempDir();
        string temp = dir.File(".target.json.tmp");
        File.WriteAllText(temp, "new\n");

        AtomicFile.RecoverFromFailedReplace(UnableToMoveReplacement(), temp, dir.File("target.json"));

        Assert.Equal("new\n", File.ReadAllText(dir.File("target.json")));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void RecoverFromFailedReplace_UnableToMoveAndTheMoveFails_ThrowsNamingTheKeptTempFile()
    {
        using var dir = new TempDir();
        string temp = dir.File(".target.json.tmp");
        File.WriteAllText(temp, "new\n");
        string destination = dir.File("target.json");
        Directory.CreateDirectory(destination); // occupies the destination, so the move cannot happen

        IOException ex = Assert.Throws<IOException>(() => AtomicFile.RecoverFromFailedReplace(UnableToMoveReplacement(), temp, destination));

        Assert.Contains(temp, ex.Message);
        Assert.Equal("new\n", File.ReadAllText(temp));
    }

    [Fact]
    public void RecoverFromFailedReplace_OtherError_RethrowsTheOriginalAndLeavesTheTempFile()
    {
        using var dir = new TempDir();
        string temp = dir.File(".target.json.tmp");
        File.WriteAllText(temp, "new\n");
        var original = new IOException("access", unchecked((int)0x80070005)); // ERROR_ACCESS_DENIED

        IOException thrown = Assert.Throws<IOException>(() => AtomicFile.RecoverFromFailedReplace(original, temp, dir.File("target.json")));

        Assert.Same(original, thrown);
        Assert.False(File.Exists(dir.File("target.json")));
        Assert.True(File.Exists(temp));
    }

    private static IOException UnableToMoveReplacement() => new("unable to move the replacement", unchecked((int)0x80070498));

    private static bool IsRoot() => LinuxNative.GetEffectiveUserId() == 0;

    /// <summary>The inode number of a path, read with stat(1) so that the test does not depend on the code under test.</summary>
    private static string Inode(string path) => RunBounded("stat", "-c", "%i", path);

    /// <summary>
    /// Runs a program and returns its trimmed stdout. Both streams are drained asynchronously; the wait is bounded, and a
    /// process that does not exit in time is killed and reported.
    /// </summary>
    private static string RunBounded(string fileName, params string[] args)
    {
        var info = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(info)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        TimeSpan timeout = TimeSpan.FromSeconds(10);
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(timeout);
            throw new TimeoutException($"'{fileName} {string.Join(' ', args)}' did not exit within {timeout.TotalSeconds}s and was killed.");
        }

        if (!Task.WaitAll(new Task[] { stdout, stderr }, timeout))
        {
            throw new TimeoutException($"Output of '{fileName}' did not end within {timeout.TotalSeconds}s after it exited.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{fileName}' exited with {process.ExitCode}: {stderr.Result.Trim()}");
        }

        return stdout.Result.Trim();
    }
}
