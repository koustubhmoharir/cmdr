using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

/// <summary>The <c>ready</c> sub-object of a command: JSON shape, validation and cloning.</summary>
public class ReadySpecTests
{
    private static CmdrConfig WithReady(ReadySpec? ready) => new()
    {
        Groups =
        {
            new GroupSpec
            {
                Name = "servers",
                Items = { new CommandSpec { Name = "web", Program = "dotnet", Args = { "watch" }, Ready = ready } },
            },
        },
    };

    private static CommandSpec Web(CmdrConfig config) =>
        Assert.IsType<CommandSpec>(Assert.Single(config.Groups[0].Items));

    [Fact]
    public void RoundTrip_PatternAndBusy_Preserved()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");

        ConfigStore.Save(WithReady(new ReadySpec { Pattern = "Now listening", Busy = "Rebuilding" }), path);
        ReadySpec? loaded = Web(ConfigStore.Load(path)).Ready;

        Assert.NotNull(loaded);
        Assert.Equal("Now listening", loaded.Pattern);
        Assert.Equal("Rebuilding", loaded.Busy);
    }

    [Fact]
    public void RoundTrip_PatternOnly_BusyStaysNull()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");

        ConfigStore.Save(WithReady(new ReadySpec { Pattern = "ready" }), path);
        ReadySpec? loaded = Web(ConfigStore.Load(path)).Ready;

        Assert.NotNull(loaded);
        Assert.Equal("ready", loaded.Pattern);
        Assert.Null(loaded.Busy);
        Assert.DoesNotContain("busy", File.ReadAllText(path));
    }

    [Fact]
    public void NullReady_IsOmittedFromFile()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");

        ConfigStore.Save(WithReady(null), path);

        Assert.DoesNotContain("ready", File.ReadAllText(path));
        Assert.Null(Web(ConfigStore.Load(path)).Ready);
    }

    [Fact]
    public void JsonNullReady_LoadsAsNoReadiness()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """
            {
              "groups": [
                { "name": "g", "items": [ { "name": "web", "program": "dotnet", "ready": null } ] }
              ]
            }
            """);

        CmdrConfig config = ConfigStore.Load(path);

        Assert.Null(Web(config).Ready);
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void JsonNullPattern_LoadsAsBlankAndFailsValidation()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """
            { "groups": [ { "name": "g", "items": [ { "name": "web", "program": "dotnet", "ready": { "pattern": null } } ] } ] }
            """);

        string error = Assert.Single(ConfigStore.Load(path).Validate());

        Assert.Equal("Group 'g' › command 'web': ready.pattern must not be blank.", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPattern_IsAnError(string pattern)
    {
        string error = Assert.Single(WithReady(new ReadySpec { Pattern = pattern }).Validate());

        Assert.Equal("Group 'servers' › command 'web': ready.pattern must not be blank.", error);
    }

    [Fact]
    public void InvalidPattern_IsAnErrorNamingThePattern()
    {
        string error = Assert.Single(WithReady(new ReadySpec { Pattern = "(unclosed" }).Validate());

        Assert.StartsWith("Group 'servers' › command 'web': ready.pattern is not a valid regular expression: '(unclosed'", error);
    }

    [Fact]
    public void InvalidBusy_IsAnErrorNamingBusy()
    {
        string error = Assert.Single(WithReady(new ReadySpec { Pattern = "ready", Busy = "[a-" }).Validate());

        Assert.StartsWith("Group 'servers' › command 'web': ready.busy is not a valid regular expression: '[a-'", error);
    }

    [Fact]
    public void BlankBusy_IsAnError()
    {
        string error = Assert.Single(WithReady(new ReadySpec { Pattern = "ready", Busy = " " }).Validate());

        Assert.Equal("Group 'servers' › command 'web': ready.busy must not be blank (omit it instead).", error);
    }

    [Fact]
    public void ValidPatterns_ReturnNoErrors()
    {
        Assert.Empty(WithReady(new ReadySpec { Pattern = @"^Now listening on: https?://\S+$", Busy = "rebuild" }).Validate());
    }

    [Fact]
    public void Clone_CopiesReadyAndIsDeep()
    {
        var original = new CommandSpec
        {
            Name = "web",
            Program = "dotnet",
            Ready = new ReadySpec { Pattern = "ready", Busy = "busy" },
        };

        CommandSpec clone = original.Clone();

        Assert.NotNull(clone.Ready);
        Assert.NotSame(original.Ready, clone.Ready);
        Assert.Equal("ready", clone.Ready.Pattern);
        Assert.Equal("busy", clone.Ready.Busy);

        original.Ready.Pattern = "changed";
        original.Ready.Busy = null;
        Assert.Equal("ready", clone.Ready.Pattern);
        Assert.Equal("busy", clone.Ready.Busy);
    }

    [Fact]
    public void Clone_NullReadyStaysNull()
    {
        Assert.Null(new CommandSpec { Name = "web", Program = "dotnet" }.Clone().Ready);
    }
}
