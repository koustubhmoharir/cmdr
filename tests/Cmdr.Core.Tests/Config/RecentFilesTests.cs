using System.Text.Json;
using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class RecentFilesTests
{
    [Fact]
    public void Load_MissingStore_ReturnsEmpty()
    {
        using var dir = new TempDir();

        RecentFiles recent = RecentFiles.Load(dir.File("missing.json"));

        Assert.Empty(recent.Files);
        Assert.Equal(dir.File("missing.json"), recent.StorePath);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsEmpty()
    {
        using var dir = new TempDir();
        string path = dir.File("recent.json");
        File.WriteAllText(path, "{ \"files\": [ \"/a\"");

        Assert.Empty(RecentFiles.Load(path).Files);
    }

    [Fact]
    public void Load_JsonNull_ReturnsEmpty()
    {
        using var dir = new TempDir();
        string path = dir.File("recent.json");
        File.WriteAllText(path, "null");

        Assert.Empty(RecentFiles.Load(path).Files);
    }

    [Fact]
    public void Load_FilesNull_ReturnsEmpty()
    {
        using var dir = new TempDir();
        string path = dir.File("recent.json");
        File.WriteAllText(path, "{ \"files\": null }");

        Assert.Empty(RecentFiles.Load(path).Files);
    }

    [Fact]
    public void Load_DropsNullBlankAndRelativeEntries()
    {
        using var dir = new TempDir();
        string path = dir.File("recent.json");
        string keep = dir.File("keep.json");
        File.WriteAllText(path, "{ \"files\": [null, \"\", \"   \", \"relative/a.json\", " + Quote(keep) + "] }");

        RecentFiles recent = RecentFiles.Load(path);

        Assert.Equal(new[] { keep }, recent.Files);
    }

    [Fact]
    public void Load_DropsDuplicatesKeepingFirst()
    {
        using var dir = new TempDir();
        string path = dir.File("recent.json");
        string a = dir.File("a.json");
        string b = dir.File("b.json");
        File.WriteAllText(path, "{ \"files\": [" + Quote(a) + ", " + Quote(b) + ", " + Quote(a) + "] }");

        RecentFiles recent = RecentFiles.Load(path);

        Assert.Equal(new[] { a, b }, recent.Files);
    }

    [Fact]
    public void Load_BomNormalizesAndDeduplicatesPathsAndDropsInvalidPath()
    {
        using var dir = new TempDir();
        string path = dir.File("recent.json");
        string valid = dir.File("a.json");
        string alias = Path.Combine(dir.Path, "sub", "..", "a.json");
        string json = JsonSerializer.Serialize(new { files = new[] { valid, alias, dir.Path + "\0bad" } });
        File.WriteAllBytes(path, [.. new System.Text.UTF8Encoding(true).GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(json)]);

        RecentFiles recent = RecentFiles.Load(path);

        Assert.Equal(new[] { valid }, recent.Files);
        Assert.True(recent.Remove(alias));
        Assert.Empty(recent.Files);
    }

    [Fact]
    public void Load_TruncatesToMaxEntries()
    {
        using var dir = new TempDir();
        string path = dir.File("recent.json");
        var entries = Enumerable.Range(0, RecentFiles.MaxEntries + 5).Select(i => dir.File($"f{i}.json")).ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(new { files = entries }));

        RecentFiles recent = RecentFiles.Load(path);

        Assert.Equal(RecentFiles.MaxEntries, recent.Files.Count);
        Assert.Equal(entries.Take(RecentFiles.MaxEntries), recent.Files);
    }

    [Fact]
    public void Add_PutsNewestFirst()
    {
        using var dir = new TempDir();
        var recent = RecentFiles.Load(dir.File("recent.json"));

        recent.Add(dir.File("a.json"));
        recent.Add(dir.File("b.json"));

        Assert.Equal(new[] { dir.File("b.json"), dir.File("a.json") }, recent.Files);
    }

    [Fact]
    public void Add_ExistingPath_MovesToFrontWithoutDuplicating()
    {
        using var dir = new TempDir();
        var recent = RecentFiles.Load(dir.File("recent.json"));
        recent.Add(dir.File("a.json"));
        recent.Add(dir.File("b.json"));
        recent.Add(dir.File("c.json"));

        recent.Add(dir.File("a.json"));

        Assert.Equal(new[] { dir.File("a.json"), dir.File("c.json"), dir.File("b.json") }, recent.Files);
    }

    [Fact]
    public void Add_RelativePath_StoresFullPath()
    {
        using var dir = new TempDir();
        var recent = RecentFiles.Load(dir.File("recent.json"));

        recent.Add(Path.Combine("relative", "x.json"));

        Assert.Equal(Path.GetFullPath(Path.Combine("relative", "x.json")), Assert.Single(recent.Files));
    }

    [Fact]
    public void Add_BeyondMaxEntries_DropsOldest()
    {
        using var dir = new TempDir();
        var recent = RecentFiles.Load(dir.File("recent.json"));
        for (int i = 0; i <= RecentFiles.MaxEntries; i++)
        {
            recent.Add(dir.File($"f{i}.json"));
        }

        Assert.Equal(RecentFiles.MaxEntries, recent.Files.Count);
        Assert.Equal(dir.File($"f{RecentFiles.MaxEntries}.json"), recent.Files[0]);
        Assert.DoesNotContain(dir.File("f0.json"), recent.Files);
    }

    [Fact]
    public void Add_DifferentCaseOfSamePath_DedupesOnlyOnCaseInsensitiveOs()
    {
        using var dir = new TempDir();
        var recent = RecentFiles.Load(dir.File("recent.json"));

        recent.Add(dir.File("A.json"));
        recent.Add(dir.File("a.json"));

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Equal(new[] { dir.File("a.json") }, recent.Files);
        }
        else
        {
            Assert.Equal(new[] { dir.File("a.json"), dir.File("A.json") }, recent.Files);
        }
    }

    [Fact]
    public void Remove_ReturnsTrueWhenPresentAndFalseWhenNot()
    {
        using var dir = new TempDir();
        var recent = RecentFiles.Load(dir.File("recent.json"));
        recent.Add(dir.File("a.json"));

        Assert.True(recent.Remove(dir.File("a.json")));
        Assert.Empty(recent.Files);
        Assert.False(recent.Remove(dir.File("a.json")));
    }

    [Fact]
    public void TrySave_ThenLoad_RoundTripsOrder()
    {
        using var dir = new TempDir();
        string path = dir.File("recent.json");
        var recent = RecentFiles.Load(path);
        recent.Add(dir.File("a.json"));
        recent.Add(dir.File("b.json"));
        recent.Add(dir.File("c.json"));

        Assert.True(recent.TrySave());

        byte[] bytes = File.ReadAllBytes(path);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "File must not have a BOM.");
        Assert.Equal((byte)'\n', bytes[^1]);

        RecentFiles reloaded = RecentFiles.Load(path);
        Assert.Equal(new[] { dir.File("c.json"), dir.File("b.json"), dir.File("a.json") }, reloaded.Files);
    }

    [Fact]
    public void TrySave_CreatesMissingDirectory()
    {
        using var dir = new TempDir();
        string path = dir.File(Path.Combine("nested", "cmdr", "recent.json"));
        var recent = RecentFiles.Load(path);
        recent.Add(dir.File("a.json"));

        Assert.True(recent.TrySave());

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void TrySave_LeavesNoTempFiles()
    {
        using var dir = new TempDir();
        var recent = RecentFiles.Load(dir.File("recent.json"));
        recent.Add(dir.File("a.json"));

        Assert.True(recent.TrySave());
        Assert.True(recent.TrySave());

        Assert.DoesNotContain(Directory.EnumerateFiles(dir.Path, "*", SearchOption.AllDirectories), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void TrySave_KeepsExistingOwnerOnlyMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        string path = dir.File("recent.json");
        var recent = RecentFiles.Load(path);
        recent.Add(dir.File("a.json"));
        Assert.True(recent.TrySave());
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        recent.Add(dir.File("b.json"));

        Assert.True(recent.TrySave());

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal(new[] { dir.File("b.json"), dir.File("a.json") }, RecentFiles.Load(path).Files);
    }

    [Fact]
    public void TrySave_ThroughSymlink_UpdatesTargetAndKeepsLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        string target = dir.File("recent-target.json");
        string link = dir.File("recent.json");
        Assert.True(RecentFiles.Load(target).TrySave());
        File.CreateSymbolicLink(link, "recent-target.json");
        var recent = RecentFiles.Load(link);
        recent.Add(dir.File("a.json"));

        Assert.True(recent.TrySave());

        Assert.Equal("recent-target.json", new FileInfo(link).LinkTarget);
        Assert.Equal(new[] { dir.File("a.json") }, RecentFiles.Load(target).Files);
        Assert.Equal(new[] { dir.File("a.json") }, RecentFiles.Load(link).Files);
    }

    [Fact]
    public void TrySave_ReturnsFalse_WhenDirectoryCannotBeCreated()
    {
        using var dir = new TempDir();
        // A regular file where the store's directory should be.
        File.WriteAllText(dir.File("blocker"), "not a directory");
        var recent = RecentFiles.Load(dir.File(Path.Combine("blocker", "recent.json")));
        recent.Add(dir.File("a.json"));

        Assert.False(recent.TrySave());
    }

    [Fact]
    public void DefaultStorePath_EndsWithCmdrRecentJson_WhenKnown()
    {
        string? path = RecentFiles.DefaultStorePath;

        if (path is not null)
        {
            Assert.EndsWith(Path.Combine("cmdr", "recent.json"), path);
        }
    }

    // A JSON string literal for a path, so that paths with backslashes (Windows) are escaped.
    private static string Quote(string value) => JsonSerializer.Serialize(value);
}
