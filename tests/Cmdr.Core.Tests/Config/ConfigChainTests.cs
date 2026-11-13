using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class ConfigChainTests
{
    private const string EmptyGroups = """{ "groups": [] }""";

    private static string GroupJson(string name) =>
        $$"""{ "groups": [ { "name": "{{name}}", "items": [ { "name": "cmd", "program": "dotnet" } ] } ] }""";

    private static void Write(TempDir dir, string relativePath, string json)
    {
        string path = dir.File(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    [Fact]
    public void LoadChain_NoBase_ReturnsSingleOpenedEntry()
    {
        using var dir = new TempDir();
        Write(dir, "cmdr.json", GroupJson("main"));

        IReadOnlyList<ConfigFile> chain = ConfigStore.LoadChain(dir.File("cmdr.json"));

        ConfigFile only = Assert.Single(chain);
        Assert.False(only.IsBase);
        Assert.Equal(Path.GetFullPath(dir.File("cmdr.json")), only.Path);
        Assert.Equal(Path.GetFullPath(dir.Path), only.Dir);
        Assert.Equal("main", only.Config.Groups[0].Name);
    }

    [Fact]
    public void LoadChain_TwoLevels_OrdersDeepestBaseFirstAndResolvesRelativeToImporter()
    {
        using var dir = new TempDir();
        Write(dir, "cmdr.json", """{ "base": "sub/base1.json", "groups": [ { "name": "main" } ] }""");
        // "base2.json" is relative to sub/, the directory of base1.json, not to the opened file's directory.
        Write(dir, "sub/base1.json", """{ "base": "base2.json", "groups": [ { "name": "b1" } ] }""");
        Write(dir, "sub/base2.json", GroupJson("b2"));

        IReadOnlyList<ConfigFile> chain = ConfigStore.LoadChain(dir.File("cmdr.json"));

        Assert.Equal(3, chain.Count);
        Assert.Equal(
            [Path.GetFullPath(dir.File("sub/base2.json")), Path.GetFullPath(dir.File("sub/base1.json")), Path.GetFullPath(dir.File("cmdr.json"))],
            chain.Select(f => f.Path));
        Assert.Equal([true, true, false], chain.Select(f => f.IsBase));
        Assert.Equal(["b2", "b1", "main"], chain.Select(f => f.Config.Groups[0].Name));
        Assert.All(chain, f => Assert.True(Path.IsPathFullyQualified(f.Path)));
    }

    [Fact]
    public void LoadChain_MissingBase_ThrowsNamingBaseAndImporter()
    {
        using var dir = new TempDir();
        Write(dir, "cmdr.json", """{ "base": "nope.json" }""");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.LoadChain(dir.File("cmdr.json")));

        Assert.Contains(Path.GetFullPath(dir.File("nope.json")), ex.Message);
        Assert.Contains(Path.GetFullPath(dir.File("cmdr.json")), ex.Message);
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void LoadChain_SelfImport_ThrowsCycle()
    {
        using var dir = new TempDir();
        Write(dir, "cmdr.json", """{ "base": "cmdr.json" }""");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.LoadChain(dir.File("cmdr.json")));

        Assert.Contains("cycle", ex.Message);
    }

    [Fact]
    public void LoadChain_ThreeFileCycle_ThrowsCycleListingTheChain()
    {
        using var dir = new TempDir();
        Write(dir, "a.json", """{ "base": "b.json" }""");
        Write(dir, "b.json", """{ "base": "c.json" }""");
        Write(dir, "c.json", """{ "base": "a.json" }""");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.LoadChain(dir.File("a.json")));

        Assert.Contains("cycle", ex.Message);
        Assert.Contains(
            string.Join(" → ", new[] { "a.json", "b.json", "c.json", "a.json" }.Select(n => Path.GetFullPath(dir.File(n)))),
            ex.Message);
    }

    [Fact]
    public void LoadChain_MalformedBase_ThrowsNamingBaseFile()
    {
        using var dir = new TempDir();
        Write(dir, "cmdr.json", """{ "base": "base.json" }""");
        Write(dir, "base.json", "{ nope");

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.LoadChain(dir.File("cmdr.json")));

        Assert.Contains(Path.GetFullPath(dir.File("base.json")), ex.Message);
        Assert.Contains("Malformed JSON", ex.Message);
    }

    [Fact]
    public void LoadChain_InvalidBasePath_IsConfigException()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Base = "bad\0base.json" }, dir.File("cmdr.json"));

        ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.LoadChain(dir.File("cmdr.json")));

        Assert.Contains("base path", ex.Message);
        Assert.Contains(dir.File("cmdr.json"), ex.Message);
    }

    [Fact]
    public void LoadChain_BomPrefixedBase_IsAccepted()
    {
        using var dir = new TempDir();
        string basePath = dir.File("base.json");
        byte[] body = System.Text.Encoding.UTF8.GetBytes("{ \"groups\": [{ \"name\": \"base\", \"items\": [] }] }");
        File.WriteAllBytes(basePath, [.. new System.Text.UTF8Encoding(true).GetPreamble(), .. body]);
        ConfigStore.Save(new CmdrConfig { Base = "base.json" }, dir.File("cmdr.json"));

        IReadOnlyList<ConfigFile> chain = ConfigStore.LoadChain(dir.File("cmdr.json"));

        Assert.Equal("base", Assert.Single(chain[0].Config.Groups).Name);
    }

    [Fact]
    public void LoadChain_UnreadableBase_IsConfigException()
    {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root")
        {
            return;
        }

        using var dir = new TempDir();
        string basePath = dir.File("base.json");
        File.WriteAllText(basePath, "{}");
        UnixFileMode originalMode = File.GetUnixFileMode(basePath);
        try
        {
            File.SetUnixFileMode(basePath, UnixFileMode.None);
            ConfigStore.Save(new CmdrConfig { Base = "base.json" }, dir.File("cmdr.json"));

            ConfigException ex = Assert.Throws<ConfigException>(() => ConfigStore.LoadChain(dir.File("cmdr.json")));
            Assert.Contains("Could not read configuration", ex.Message);
            Assert.Contains(basePath, ex.Message);
        }
        finally
        {
            File.SetUnixFileMode(basePath, originalMode);
        }
    }

    [Fact]
    public void LoadChain_MissingOpenedFile_ReturnsSingleEmptyEntry()
    {
        using var dir = new TempDir();

        IReadOnlyList<ConfigFile> chain = ConfigStore.LoadChain(dir.File("missing.json"));

        ConfigFile only = Assert.Single(chain);
        Assert.False(only.IsBase);
        Assert.Null(only.Config.Base);
        Assert.Empty(only.Config.Groups);
    }

    [Fact]
    public void LoadChain_BlankBase_IsNotFollowed()
    {
        using var dir = new TempDir();
        Write(dir, "cmdr.json", """{ "base": "   " }""");

        IReadOnlyList<ConfigFile> chain = ConfigStore.LoadChain(dir.File("cmdr.json"));

        Assert.Single(chain);
    }

    [Fact]
    public void ValidateChain_SameTopLevelGroupNameInBaseAndMain_IsNotAnError()
    {
        using var dir = new TempDir();
        Write(dir, "cmdr.json", """{ "base": "base.json", "groups": [ { "name": "servers" } ] }""");
        Write(dir, "base.json", """{ "groups": [ { "name": "servers" } ] }""");

        IReadOnlyList<ConfigFile> chain = ConfigStore.LoadChain(dir.File("cmdr.json"));

        Assert.Empty(ConfigStore.ValidateChain(chain));
    }

    [Fact]
    public void ValidateChain_DuplicateInBase_IsPrefixedWithBaseRelativePath()
    {
        using var dir = new TempDir();
        Write(dir, "cmdr.json", """{ "base": "sub/base.json" }""");
        Write(dir, "sub/base.json", """{ "groups": [ { "name": "x" }, { "name": "x" } ] }""");

        IReadOnlyList<string> errors = ConfigStore.ValidateChain(ConfigStore.LoadChain(dir.File("cmdr.json")));

        string error = Assert.Single(errors);
        Assert.StartsWith(Path.Combine("sub", "base.json") + ": ", error);
        Assert.Contains("Duplicate group name 'x'", error);
    }

    [Fact]
    public void ValidateChain_DuplicateInMain_IsNotPrefixed()
    {
        using var dir = new TempDir();
        Write(dir, "cmdr.json", """{ "base": "base.json", "groups": [ { "name": "x" }, { "name": "x" } ] }""");
        Write(dir, "base.json", EmptyGroups);

        IReadOnlyList<string> errors = ConfigStore.ValidateChain(ConfigStore.LoadChain(dir.File("cmdr.json")));

        string error = Assert.Single(errors);
        Assert.StartsWith("Duplicate group name 'x'", error);
    }

    [Fact]
    public void ValidateChain_BlankBase_IsReportedUnprefixed()
    {
        using var dir = new TempDir();
        Write(dir, "cmdr.json", """{ "base": "  " }""");

        IReadOnlyList<string> errors = ConfigStore.ValidateChain(ConfigStore.LoadChain(dir.File("cmdr.json")));

        Assert.Equal(["base must not be blank."], errors);
    }

    [Fact]
    public void Base_RoundTripsThroughSaveAndIsOmittedWhenNull()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");

        ConfigStore.Save(new CmdrConfig { Base = "../shared/base.json" }, path);
        Assert.Equal("../shared/base.json", ConfigStore.Load(path).Base);

        ConfigStore.Save(new CmdrConfig(), path);
        Assert.DoesNotContain("base", File.ReadAllText(path));
        Assert.Null(ConfigStore.Load(path).Base);
    }
}
