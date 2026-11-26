using System.Text;
using Shared.Core.Tests.Config;

namespace Shared.Core.Tests.Text;

/// <summary>
/// <see cref="TextFile"/>: opening and its failures, creating new files, saving, the disk checks, and reloading. File state is
/// read with the file system APIs, not through the code under test.
/// </summary>
public sealed class TextFileTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly Func<byte[], string, SourceText> Decode = (bytes, path) => SourceText.Decode(bytes, path);

    // ----- Open -----

    [Fact]
    public void Open_RecordsThePathTextAndBytes()
    {
        using var dir = new TempDir();
        string path = dir.File("notes.txt");
        byte[] bytes = "one\r\ntwo\r\n"u8.ToArray();
        File.WriteAllBytes(path, bytes);

        TextFile file = TextFile.Open(path, Decode);

        Assert.True(file.Exists);
        Assert.Equal(path, file.Path);
        Assert.Equal("one\r\ntwo\r\n", file.Text.Text);
        Assert.Equal("\r\n", file.Text.Newline);
        Assert.Equal(bytes, file.Bytes.ToArray());
    }

    [Fact]
    public void Open_MakesARelativePathFull()
    {
        using var dir = new TempDir();
        string path = dir.File("notes.txt");
        File.WriteAllText(path, "x");
        string relative = System.IO.Path.GetRelativePath(Directory.GetCurrentDirectory(), path);

        TextFile file = TextFile.Open(relative, Decode);

        Assert.Equal(path, file.Path);
    }

    [Fact]
    public void Open_TheDecoderGetsTheBytesAndTheFullPath()
    {
        using var dir = new TempDir();
        string path = dir.File("notes.txt");
        File.WriteAllText(path, "abc");
        byte[]? seenBytes = null;
        string? seenPath = null;

        TextFile.Open(path, (bytes, full) =>
        {
            seenBytes = bytes;
            seenPath = full;
            return SourceText.Decode(bytes, full);
        });

        Assert.Equal("abc"u8.ToArray(), seenBytes);
        Assert.Equal(path, seenPath);
    }

    [Fact]
    public void Open_ADecoderThatThrowsPropagatesWithThePath()
    {
        using var dir = new TempDir();
        string path = dir.File("notes.txt");
        File.WriteAllText(path, "abc");

        TextFileException ex = Assert.Throws<TextFileException>(() =>
            TextFile.Open(path, (_, _) => throw new TextFileException("the decoder refused the file")));

        Assert.Equal("the decoder refused the file", ex.Message);
        Assert.Equal(path, ex.Path);
    }

    [Fact]
    public void Open_InvalidUtf8_ThrowsTheDecoderErrorWithPathLineAndColumn()
    {
        using var dir = new TempDir();
        string path = dir.File("bad.txt");
        File.WriteAllBytes(path, [(byte)'a', (byte)'\n', (byte)'b', 0xFF]);

        TextFileException ex = Assert.Throws<TextFileException>(() => TextFile.Open(path, Decode));

        Assert.Equal("not valid UTF-8 at byte 3", ex.Message);
        Assert.Equal(path, ex.Path);
        Assert.Equal(2, ex.Line);
        Assert.Equal(1, ex.Column);
    }

    [Fact]
    public void Open_AMissingFile_ThrowsWithThePath()
    {
        using var dir = new TempDir();
        string path = dir.File("missing.txt");

        TextFileException ex = Assert.Throws<TextFileException>(() => TextFile.Open(path, Decode));

        Assert.Equal(path, ex.Path);
        Assert.Equal($"the file '{path}' does not exist", ex.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Open_AFolder_ThrowsWithThePath()
    {
        using var dir = new TempDir();
        string folder = dir.File("folder.txt");
        Directory.CreateDirectory(folder);

        TextFileException ex = Assert.Throws<TextFileException>(() => TextFile.Open(folder, Decode));

        Assert.Equal(folder, ex.Path);
        Assert.Equal($"'{folder}' is a folder, not a file", ex.Message);
    }

    [Fact]
    public void Open_AnInvalidPath_ThrowsNotAValidFilePath()
    {
        TextFileException ex = Assert.Throws<TextFileException>(() => TextFile.Open("bad\0name.txt", Decode));

        Assert.Equal("bad\0name.txt", ex.Path);
        Assert.Equal("'bad\0name.txt' is not a valid file path", ex.Message);
    }

    // ----- CreateNew and Save -----

    [Fact]
    public void CreateNew_IsUnsavedWithItsTextAndItsEncodedBytes()
    {
        using var dir = new TempDir();
        string path = dir.File("new.txt");
        SourceText text = SourceText.Create("a\r\nb", Utf8, hasBom: false);

        TextFile file = TextFile.CreateNew(path, text);

        Assert.False(file.Exists);
        Assert.Equal(path, file.Path);
        Assert.Same(text, file.Text);
        Assert.Equal("a\r\nb"u8.ToArray(), file.Bytes.ToArray());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CreateNew_ThenSave_CreatesTheFileWithExactBytes()
    {
        using var dir = new TempDir();
        string path = dir.File("new.txt");
        TextFile file = TextFile.CreateNew(path, SourceText.Create("a\r\nb", Utf8, hasBom: false));

        file.Save(file.Text);

        Assert.True(file.Exists);
        Assert.Equal("a\r\nb"u8.ToArray(), File.ReadAllBytes(path));
        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void Save_WritesExactBytesCrlfNoFinalNewlineAndBom_AndOpenReadsThemBack()
    {
        using var dir = new TempDir();
        string path = dir.File("config.txt");
        TextFile file = TextFile.CreateNew(path, SourceText.Create("x", Utf8, hasBom: false));
        SourceText text = SourceText.Create("x\r\ny", Encoding.UTF8, hasBom: true);

        file.Save(text);

        byte[] expected = [0xEF, 0xBB, 0xBF, (byte)'x', (byte)'\r', (byte)'\n', (byte)'y'];
        Assert.Equal(expected, File.ReadAllBytes(path));
        Assert.Same(text, file.Text);
        Assert.Equal(expected, file.Bytes.ToArray());

        TextFile reopened = TextFile.Open(path, Decode);
        Assert.Equal("x\r\ny", reopened.Text.Text);
        Assert.True(reopened.Text.HasBom);
        Assert.Equal(expected, reopened.Bytes.ToArray());
        Assert.Equal(expected, reopened.Text.Encode());
    }

    [Fact]
    public void Save_IntoAMissingFolder_ThrowsAndChangesNothing()
    {
        using var dir = new TempDir();
        string folder = dir.File("missing");
        string path = System.IO.Path.Combine(folder, "config.txt");
        SourceText initial = SourceText.Create("x", Utf8, hasBom: false);
        TextFile file = TextFile.CreateNew(path, initial);

        TextFileException ex = Assert.Throws<TextFileException>(() => file.Save(SourceText.Create("y", Utf8, hasBom: false)));

        Assert.Equal($"the folder '{folder}' does not exist", ex.Message);
        Assert.Equal(path, ex.Path);
        Assert.False(file.Exists);
        Assert.Same(initial, file.Text);
        Assert.Equal("x"u8.ToArray(), file.Bytes.ToArray());
        Assert.False(Directory.Exists(folder));
    }

    // ----- Disk checks -----

    [Fact]
    public void ChangedOnDisk_IsFalseUntilTheContentChanges()
    {
        using var dir = new TempDir();
        string path = dir.File("config.txt");
        File.WriteAllText(path, "one");
        DateTime written = File.GetLastWriteTimeUtc(path);
        TextFile file = TextFile.Open(path, Decode);
        Assert.False(file.ChangedOnDisk());

        File.SetLastWriteTimeUtc(path, written.AddMinutes(1));
        Assert.False(file.ChangedOnDisk());

        File.WriteAllText(path, "two");
        Assert.True(file.ChangedOnDisk());
    }

    [Fact]
    public void PollDisk_IsUnchangedAfterATouchWithTheSameBytes()
    {
        using var dir = new TempDir();
        string path = dir.File("config.txt");
        File.WriteAllText(path, "same");
        DateTime written = File.GetLastWriteTimeUtc(path);
        TextFile file = TextFile.Open(path, Decode);

        File.SetLastWriteTimeUtc(path, written.AddHours(1));

        Assert.Equal(DiskChange.Unchanged, file.PollDisk());
        Assert.Equal(DiskChange.Unchanged, file.PollDisk());
    }

    [Fact]
    public void PollDisk_ReportsNewContentOnce()
    {
        using var dir = new TempDir();
        string path = dir.File("config.txt");
        File.WriteAllText(path, "one");
        TextFile file = TextFile.Open(path, Decode);

        File.WriteAllText(path, "one and more");

        Assert.Equal(DiskChange.Changed, file.PollDisk());
        Assert.Equal(DiskChange.Unchanged, file.PollDisk());
    }

    [Fact]
    public void PollDisk_ReportsADeletedFileOnce()
    {
        using var dir = new TempDir();
        string path = dir.File("config.txt");
        File.WriteAllText(path, "one");
        TextFile file = TextFile.Open(path, Decode);

        File.Delete(path);

        Assert.Equal(DiskChange.Deleted, file.PollDisk());
        Assert.Equal(DiskChange.Unchanged, file.PollDisk());
    }

    [Fact]
    public void PollDisk_ReportsAFileThatAppearsForANewFile()
    {
        using var dir = new TempDir();
        string path = dir.File("new.txt");
        TextFile file = TextFile.CreateNew(path, SourceText.Create("x", Utf8, hasBom: false));

        File.WriteAllText(path, "made by someone else");

        Assert.True(file.ChangedOnDisk());
        Assert.Equal(DiskChange.Changed, file.PollDisk());
    }

    // ----- Accepting, missing and reloading -----

    [Fact]
    public void AcceptDiskState_TakesTheDiskTextAndBytes()
    {
        using var dir = new TempDir();
        string path = dir.File("config.txt");
        File.WriteAllText(path, "old");
        TextFile file = TextFile.Open(path, Decode);
        File.WriteAllText(path, "new text");
        Assert.True(file.ChangedOnDisk());

        file.AcceptDiskState();

        Assert.Equal("new text", file.Text.Text);
        Assert.Equal("new text"u8.ToArray(), file.Bytes.ToArray());
        Assert.True(file.Exists);
        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void MarkMissing_MakesTheFileUnsavedAndTheNextSaveRecreatesIt()
    {
        using var dir = new TempDir();
        string path = dir.File("config.txt");
        File.WriteAllText(path, "kept");
        TextFile file = TextFile.Open(path, Decode);
        File.Delete(path);

        file.MarkMissing();

        Assert.False(file.Exists);
        Assert.False(file.ChangedOnDisk());

        file.Save(SourceText.Create("again", Utf8, hasBom: false));

        Assert.True(file.Exists);
        Assert.Equal("again"u8.ToArray(), File.ReadAllBytes(path));
    }

    [Fact]
    public void Reload_ReReadsWithTheDecoderItWasOpenedWith()
    {
        using var dir = new TempDir();
        string path = dir.File("config.txt");
        File.WriteAllText(path, "first");
        int calls = 0;
        TextFile file = TextFile.Open(path, (bytes, full) =>
        {
            calls++;
            return SourceText.Decode(bytes, full);
        });
        Assert.Equal(1, calls);

        File.WriteAllText(path, "second");
        file.Reload();

        Assert.Equal(2, calls);
        Assert.Equal("second", file.Text.Text);
        Assert.Equal("second"u8.ToArray(), file.Bytes.ToArray());
        Assert.False(file.ChangedOnDisk());
    }
}
