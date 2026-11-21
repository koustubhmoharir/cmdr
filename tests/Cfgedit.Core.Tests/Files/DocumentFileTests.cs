using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;
using Cfgedit.Core.Tests.Model;
using Shared.Core;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Files;

/// <summary>
/// <see cref="DocumentFile"/>: opening and its failures, creating new files, the external-change check, saving, and reloading.
/// File state is read with the file system APIs, not through the code under test.
/// </summary>
public sealed class DocumentFileTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly IConfigFormat XmlConfig = Formats.FromName("xml")!;

    // ----- Open -----

    [Fact]
    public void Open_LoadsTheDocumentAndRecordsThePathAndFormat()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{\"a\": 1}\n");

        DocumentFile file = DocumentFile.Open(path, Strict);

        Assert.True(file.Exists);
        Assert.Equal(path, file.Path);
        Assert.Same(Strict, file.Format);
        Assert.Equal("{\"a\": 1}\n", file.Document!.Source.Text);
    }

    [Fact]
    public void Open_MakesARelativePathFull()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{}");
        string relative = System.IO.Path.GetRelativePath(Directory.GetCurrentDirectory(), path);

        DocumentFile file = DocumentFile.Open(relative, Strict);

        Assert.Equal(path, file.Path);
    }

    [Fact]
    public void Open_AMissingFile_ThrowsWithThePath()
    {
        using var dir = new Scratch();
        string path = dir.File("missing.json");

        CfgeditException ex = Assert.Throws<CfgeditException>(() => DocumentFile.Open(path, Strict));

        Assert.Equal(path, ex.Path);
        Assert.Contains("does not exist", ex.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Open_AFolder_ThrowsWithThePath()
    {
        using var dir = new Scratch();
        string folder = dir.File("folder.json");
        Directory.CreateDirectory(folder);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => DocumentFile.Open(folder, Strict));

        Assert.Equal(folder, ex.Path);
        Assert.Contains("is a folder", ex.Message);
    }

    [Fact]
    public void Open_AFileThatCannotBeRead_ThrowsAccessDeniedWithThePath()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // no POSIX mode to remove on Windows
        }

        using var dir = new Scratch();
        string path = dir.File("locked.json");
        File.WriteAllText(path, "{}");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            if (CanReadWithoutPermission(path))
            {
                return; // running as root, which reads any file, so there is no denial to expect
            }

            CfgeditException ex = Assert.Throws<CfgeditException>(() => DocumentFile.Open(path, Strict));

            Assert.Equal(path, ex.Path);
            Assert.Contains("access is denied", ex.Message);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void Open_BytesThatAreNotUtf8_ThrowsADecodingErrorWithThePath()
    {
        using var dir = new Scratch();
        string path = dir.File("bad.json");
        File.WriteAllBytes(path, [0x7B, 0xFF, 0x7D]);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => DocumentFile.Open(path, Strict));

        Assert.Equal(path, ex.Path);
    }

    [Fact]
    public void Open_TextThatIsNotAValidDocument_KeepsTheTextAndTheParseErrorWithThePathAndPosition()
    {
        using var dir = new Scratch();
        string path = dir.File("broken.json");
        File.WriteAllText(path, "{\n  \"a\" 1}\n");

        DocumentFile file = DocumentFile.Open(path, Strict);

        Assert.True(file.Exists);
        Assert.Null(file.Document);
        Assert.Equal("{\n  \"a\" 1}\n", file.Text.Text);
        Assert.NotNull(file.ParseError);
        Assert.Equal(path, file.ParseError.Path);
        Assert.Equal(2, file.ParseError.Line);
        Assert.Equal(7, file.ParseError.Column);
    }

    [Fact]
    public void Open_ParsableText_HasADocumentAndNoParseError()
    {
        using var dir = new Scratch();
        string path = dir.File("good.json");
        File.WriteAllText(path, "{\"a\": 1}\n");

        DocumentFile file = DocumentFile.Open(path, Strict);

        Assert.NotNull(file.Document);
        Assert.Null(file.ParseError);
        Assert.Equal(file.Text.Text, file.Document!.Source.Text);
    }

    [Fact]
    public void Open_XmlTextThatIsNotWellFormed_KeepsTheTextAndTheParseError()
    {
        using var dir = new Scratch();
        string path = dir.File("broken.xml");
        File.WriteAllText(path, "<a>\n  <b></a>\n");

        DocumentFile file = DocumentFile.Open(path, XmlConfig);

        Assert.Null(file.Document);
        Assert.Equal("<a>\n  <b></a>\n", file.Text.Text);
        Assert.Equal(path, file.ParseError!.Path);
        Assert.Equal(2, file.ParseError.Line);
    }

    [Fact]
    public void Open_BytesThatDoNotDecodeStillThrowEvenWhenTheyWouldNotParse()
    {
        using var dir = new Scratch();
        string path = dir.File("bad.xml");
        File.WriteAllBytes(path, [0x3C, 0xFF, 0x3E]);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => DocumentFile.Open(path, XmlConfig));

        Assert.Equal(path, ex.Path);
    }

    [Fact]
    public void SaveText_WritesTheExactBytesOfTheTextAndParsesThemForTheDocument()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{\n  \"a\" 1}\n");
        DocumentFile file = DocumentFile.Open(path, Strict);
        SourceText fixedText = file.Text.WithText("{\r\n  \"a\": 1}\r\n");

        file.SaveText(fixedText);

        Assert.Equal("{\r\n  \"a\": 1}\r\n", File.ReadAllText(path, Utf8));
        Assert.Same(fixedText, file.Text);
        Assert.Null(file.ParseError);
        Assert.NotNull(file.Document);
        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void SaveText_OfTextThatDoesNotParseWritesIt_AndKeepsTheParseError()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{}");
        DocumentFile file = DocumentFile.Open(path, Strict);
        SourceText broken = file.Text.WithText("{\"a\" 1}");

        file.SaveText(broken);

        Assert.Equal("{\"a\" 1}", File.ReadAllText(path, Utf8));
        Assert.Null(file.Document);
        Assert.NotNull(file.ParseError);
        Assert.Equal(1, file.ParseError.Line);
        Assert.Equal(6, file.ParseError.Column);
    }

    [Fact]
    public void ParseText_ParsesTheTextThroughItsEncodingWithoutChangingTheFile()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{}");
        DocumentFile file = DocumentFile.Open(path, Strict);

        ConfigDocument parsed = file.ParseText(file.Text.WithText("{\"b\": 2}"));
        CfgeditException ex = Assert.Throws<CfgeditException>(() => file.ParseText(file.Text.WithText("{")));

        Assert.Equal("{\"b\": 2}", parsed.Source.Text);
        Assert.Equal(path, ex.Path);
        Assert.Equal("{}", File.ReadAllText(path, Utf8));
        Assert.Equal(file.Text.Text, file.Document!.Source.Text);
    }

    [Fact]
    public void Open_AnEmptyPath_ThrowsCfgeditException()
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => DocumentFile.Open(string.Empty, Strict));

        Assert.Equal(string.Empty, ex.Path);
    }

    [Fact]
    public void Open_APathWithANulCharacter_ThrowsCfgeditException()
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => DocumentFile.Open("bad\0name.json", Strict));

        Assert.Equal("bad\0name.json", ex.Path);
    }

    // ----- CreateNew -----

    [Fact]
    public void CreateNew_JsonIsAnUnsavedEmptyObjectWithANewline()
    {
        using var dir = new Scratch();
        string path = dir.File("new.json");

        DocumentFile file = DocumentFile.CreateNew(path, Strict, xmlRootName: null);

        Assert.False(file.Exists);
        Assert.False(file.Document!.Source.HasBom);
        Assert.Equal("{}" + Environment.NewLine, file.Document!.Source.Text);
        Assert.False(File.Exists(path));
        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void CreateNew_XmlHasTheDeclarationAndAnEmptyRootOfTheGivenName()
    {
        using var dir = new Scratch();
        string path = dir.File("new.xml");

        DocumentFile file = DocumentFile.CreateNew(path, XmlConfig,"settings");

        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + "<settings />" + Environment.NewLine,
            file.Document!.Source.Text);
        Assert.NotNull(file.Document!.Resolve(At(Element("settings"))));
    }

    [Fact]
    public void CreateNew_XmlWithoutAValidRootName_ThrowsWithThePath()
    {
        using var dir = new Scratch();
        string path = dir.File("new.xml");

        CfgeditException missing = Assert.Throws<CfgeditException>(() => DocumentFile.CreateNew(path, XmlConfig,null));
        CfgeditException invalid = Assert.Throws<CfgeditException>(() => DocumentFile.CreateNew(path, XmlConfig,"1bad"));

        Assert.Equal(path, missing.Path);
        Assert.Equal(path, invalid.Path);
    }

    [Fact]
    public void CreateNew_APathThatIsNotValid_ThrowsCfgeditException()
    {
        Assert.Throws<CfgeditException>(() => DocumentFile.CreateNew(string.Empty, Strict, null));
    }

    [Fact]
    public void CreateNew_BecomesChangedOnDiskOnceSomeoneCreatesTheFile()
    {
        using var dir = new Scratch();
        string path = dir.File("new.json");
        DocumentFile file = DocumentFile.CreateNew(path, Strict, null);
        Assert.False(file.ChangedOnDisk());

        File.WriteAllText(path, "{\"other\": true}");

        Assert.True(file.ChangedOnDisk());
    }

    // ----- ChangedOnDisk for an opened file -----

    [Fact]
    public void ChangedOnDisk_IsFalseWhileTheFileIsUnchanged()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{\"a\": 1}\n");
        DocumentFile file = DocumentFile.Open(path, Strict);

        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void ChangedOnDisk_IsTrueWhenTheFileIsDeleted()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{\"a\": 1}\n");
        DocumentFile file = DocumentFile.Open(path, Strict);

        File.Delete(path);

        Assert.True(file.ChangedOnDisk());
    }

    [Fact]
    public void ChangedOnDisk_IsTrueWhenTheLengthChanges()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{\"a\": 1}\n");
        DocumentFile file = DocumentFile.Open(path, Strict);

        File.WriteAllText(path, "{\"a\": 100}\n");

        Assert.True(file.ChangedOnDisk());
    }

    [Fact]
    public void ChangedOnDisk_IsTrueForDifferentContentOfTheSameLengthAndWriteTime()
    {
        // The identity, length and write time all match, so only the content hash can see the change.
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{\"a\": 1}\n");
        DateTime written = File.GetLastWriteTimeUtc(path);
        DocumentFile file = DocumentFile.Open(path, Strict);

        File.WriteAllText(path, "{\"a\": 2}\n");
        File.SetLastWriteTimeUtc(path, written);

        Assert.True(file.ChangedOnDisk());
    }

    [Fact]
    public void ChangedOnDisk_IsFalseForAReplacementWithTheSameContent()
    {
        // An editor that saves by replacing the file gives it another identity; the content is the same, so it is no change.
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{\"a\": 1}\n");
        if (Shared.Core.FileIdentity.TryGet(path) is null)
        {
            return; // this system does not report file identities
        }

        DateTime written = File.GetLastWriteTimeUtc(path);
        DocumentFile file = DocumentFile.Open(path, Strict);
        string replacement = dir.File("replacement.tmp");
        File.WriteAllText(replacement, "{\"a\": 1}\n");
        File.SetLastWriteTimeUtc(replacement, written);

        File.Move(replacement, path, overwrite: true);

        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void ChangedOnDisk_IsFalseWhenOnlyTheWriteTimeChanges()
    {
        // Only the content decides: a touch, or a rewrite of the same bytes, is not a change worth asking about.
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{\"a\": 1}\n");
        DateTime written = File.GetLastWriteTimeUtc(path);
        DocumentFile file = DocumentFile.Open(path, Strict);

        File.SetLastWriteTimeUtc(path, written.AddMinutes(1));

        Assert.False(file.ChangedOnDisk());
    }

    // ----- Save -----

    [Fact]
    public void Save_WritesTheExactBytesOfTheDocumentWithNoNewlineAdded()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{\"a\": 1}\n");
        DocumentFile file = DocumentFile.Open(path, Strict);
        ConfigDocument edited = file.Document!.Apply(new SetPart(At(Item(0), Member("a")), PartKind.Value, -1, "2")).Document;

        file.Save(edited);

        Assert.Equal("{\"a\": 2}\n", File.ReadAllText(path));
        Assert.Equal(Utf8.GetBytes("{\"a\": 2}\n"), File.ReadAllBytes(path));
        Assert.Same(edited, file.Document);
        Assert.True(file.Exists);
        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void Save_WritesADocumentWithoutAFinalNewlineAsItIs()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{}\n");
        DocumentFile file = DocumentFile.Open(path, Strict);

        file.Save(ModelTestSupport.Parse("{ \"a\": 1 }", Strict));

        Assert.Equal(Utf8.GetBytes("{ \"a\": 1 }"), File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_OfANewFileCreatesTheFileAndItBecomesExisting()
    {
        using var dir = new Scratch();
        string path = dir.File("new.xml");
        DocumentFile file = DocumentFile.CreateNew(path, XmlConfig,"settings");

        file.Save(file.Document!);

        Assert.True(file.Exists);
        Assert.Equal(file.Document!.Source.Encode(), File.ReadAllBytes(path));
        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void Save_WithAMissingFolder_ThrowsAndCreatesNothing()
    {
        using var dir = new Scratch();
        string folder = dir.File("missing");
        string path = System.IO.Path.Combine(folder, "config.json");
        DocumentFile file = DocumentFile.CreateNew(path, Strict, null);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => file.Save(file.Document!));

        Assert.Equal(path, ex.Path);
        Assert.Contains($"the folder '{folder}' does not exist", ex.Message);
        Assert.False(Directory.Exists(folder));
        Assert.False(file.Exists);
    }

    [Fact]
    public void Save_ThroughASymbolicLink_WritesTheTargetAndKeepsTheLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new Scratch();
        string target = dir.File("target.json");
        string link = dir.File("linked.json");
        File.WriteAllText(target, "{}");
        File.CreateSymbolicLink(link, "target.json");
        DocumentFile file = DocumentFile.Open(link, Strict);

        file.Save(ModelTestSupport.Parse("[1]", Strict));

        Assert.Equal("target.json", new FileInfo(link).LinkTarget);
        Assert.Equal(Utf8.GetBytes("[1]"), File.ReadAllBytes(target));
        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void Save_IntoAFolderThatCannotBeWritten_ThrowsAndLeavesTheFileObjectUnchanged()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new Scratch();
        string folder = dir.File("locked");
        Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, "new.json");
        DocumentFile file = DocumentFile.CreateNew(path, Strict, null);
        ConfigDocument before = file.Document!;
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            if (CanWriteInside(folder))
            {
                return; // running as root, which writes anywhere, so there is no denial to expect
            }

            CfgeditException ex = Assert.Throws<CfgeditException>(() => file.Save(before));

            Assert.Equal(path, ex.Path);
            Assert.Contains("cannot be saved", ex.Message);
            Assert.False(file.Exists);
            Assert.Same(before, file.Document);
            Assert.Empty(Directory.GetFileSystemEntries(folder));
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // ----- Reload -----

    [Fact]
    public void Reload_ReturnsTheDocumentOnDiskAndNoLongerReportsAChange()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{\"a\": 1}\n");
        DocumentFile file = DocumentFile.Open(path, Strict);
        File.WriteAllText(path, "{\"b\": 2}\n");
        Assert.True(file.ChangedOnDisk());

        ConfigDocument? reloaded = file.Reload();

        Assert.Same(reloaded, file.Document);
        Assert.Equal("{\"b\": 2}\n", reloaded!.Source.Text);
        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void Reload_AMissingFile_ThrowsAndKeepsTheDocument()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{}");
        DocumentFile file = DocumentFile.Open(path, Strict);
        ConfigDocument before = file.Document!;
        File.Delete(path);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => file.Reload());

        Assert.Equal(path, ex.Path);
        Assert.Same(before, file.Document);
    }

    [Fact]
    public void Reload_AnInvalidFile_KeepsTheTextAndTheParseErrorLikeOpen()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{}");
        DocumentFile file = DocumentFile.Open(path, Strict);
        File.WriteAllText(path, "{");

        ConfigDocument? reloaded = file.Reload();

        Assert.Null(reloaded);
        Assert.Null(file.Document);
        Assert.Equal("{", file.Text.Text);
        Assert.Equal(path, file.ParseError!.Path);
        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void Reload_OfAFileThatNowParses_ReturnsTheDocumentAndClearsTheParseError()
    {
        using var dir = new Scratch();
        string path = dir.File("config.json");
        File.WriteAllText(path, "{");
        DocumentFile file = DocumentFile.Open(path, Strict);
        File.WriteAllText(path, "{\"a\": 1}");

        ConfigDocument? reloaded = file.Reload();

        Assert.Same(reloaded, file.Document);
        Assert.NotNull(reloaded);
        Assert.Null(file.ParseError);
        Assert.Equal("{\"a\": 1}", file.Text.Text);
    }

    /// <summary>True when a file with no permission to read can still be read, which means the process is root.</summary>
    private static bool CanReadWithoutPermission(string path)
    {
        try
        {
            File.ReadAllBytes(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when a file can be created in the folder, whatever its mode says.</summary>
    private static bool CanWriteInside(string folder)
    {
        string probe = System.IO.Path.Combine(folder, "probe");
        try
        {
            File.WriteAllText(probe, string.Empty);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        File.Delete(probe);
        return true;
    }

    /// <summary>A temporary folder that is removed on disposal.</summary>
    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Path = Directory.CreateTempSubdirectory("cfgedit-core-files-").FullName;
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: a folder left behind is only temporary data.
            }
        }
    }
}
