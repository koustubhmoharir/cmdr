namespace Shared.Core.Tests.Config;

public class AtomicFileTests
{
    [Fact]
    public void Write_CreatesMissingDirectory_AndOverwritesExistingFile()
    {
        using var dir = new TempDir();
        string path = dir.File(Path.Combine("missing", "nested", "file.txt"));

        AtomicFile.Write(path, "first content that is longer"u8);
        AtomicFile.Write(path, "second"u8);

        Assert.Equal("second", File.ReadAllText(path));
    }
}
