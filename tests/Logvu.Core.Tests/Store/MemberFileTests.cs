using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using Shared.Core;

namespace Logvu.Core.Tests.Store;

public sealed class MemberFileTests
{
    [Fact]
    public void Opens_a_file_and_takes_its_identity_from_the_handle()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("SheetKraft.log", "INFO  line\n");

        bool opened = MemberFile.TryOpen(path, null, out FileStream? stream, out string? identity, out string? error);

        Assert.True(opened);
        Assert.Null(error);
        Assert.NotNull(stream);
        using FileStream handle = stream;
        Assert.NotNull(identity);
        Assert.Equal(FileIdentity.TryGet(path), identity);
    }

    [Fact]
    public void Matching_expected_identity_opens()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("SheetKraft.log", "INFO  line\n");
        string identity = FileIdentity.TryGet(path)!;

        Assert.True(MemberFile.TryOpen(path, identity, out FileStream? stream, out string? actual, out string? error));
        using FileStream handle = stream!;
        Assert.Null(error);
        Assert.Equal(identity, actual);
    }

    [Fact]
    public void Wrong_expected_identity_fails_with_identity_changed_and_closes_the_file()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("SheetKraft.log", "INFO  line\n");

        bool opened = MemberFile.TryOpen(path, "0:0:0", out FileStream? stream, out string? identity, out string? error);

        Assert.False(opened);
        Assert.Null(stream);
        Assert.Null(identity);
        Assert.Equal(MemberFile.IdentityChanged, error);
    }

    [Fact]
    public void Missing_file_fails_with_not_found()
    {
        using var dir = new ConfigTestDirectory();

        bool opened = MemberFile.TryOpen(dir.Full("SheetKraft.log"), null, out FileStream? stream, out string? identity, out string? error);

        Assert.False(opened);
        Assert.Null(stream);
        Assert.Null(identity);
        Assert.Equal("not found", error);
    }

    [Fact]
    public void Missing_directory_fails_with_not_found()
    {
        using var dir = new ConfigTestDirectory();

        bool opened = MemberFile.TryOpen(dir.Full("no-such-dir/SheetKraft.log"), null, out _, out _, out string? error);

        Assert.False(opened);
        Assert.Equal("not found", error);
    }

    [Fact]
    public void A_directory_fails_with_a_message_and_does_not_throw()
    {
        using var dir = new ConfigTestDirectory();

        bool opened = MemberFile.TryOpen(dir.Path, null, out FileStream? stream, out string? identity, out string? error);

        Assert.False(opened);
        Assert.Null(stream);
        Assert.Null(identity);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Renamed_file_is_opened_by_its_new_path_with_the_old_identity()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("SheetKraft.log", "INFO  line\n");
        Assert.True(MemberFile.TryOpen(path, null, out FileStream? first, out string? identity, out _));
        first!.Dispose();

        string renamed = dir.Full("SheetKraft1.log");
        File.Move(path, renamed);

        Assert.False(MemberFile.TryOpen(path, identity, out _, out _, out string? missing));
        Assert.Equal("not found", missing);

        Assert.True(MemberFile.TryOpen(renamed, identity, out FileStream? stream, out string? actual, out string? error));
        using FileStream handle = stream!;
        Assert.Null(error);
        Assert.Equal(identity, actual);
    }

    [Fact]
    public void Path_that_names_another_file_fails_with_identity_changed()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("SheetKraft.log", "INFO  line\n");
        Assert.True(MemberFile.TryOpen(path, null, out FileStream? first, out string? identity, out _));
        first!.Dispose();

        // The old file still exists under another name, so the new file cannot reuse its identity.
        File.Move(path, dir.Full("SheetKraft1.log"));
        dir.Write("SheetKraft.log", "INFO  another\n");

        Assert.False(MemberFile.TryOpen(path, identity, out _, out _, out string? error));
        Assert.Equal(MemberFile.IdentityChanged, error);
    }

    [Fact]
    public void Two_reads_of_one_file_can_be_open_together()
    {
        using var dir = new ConfigTestDirectory();
        string path = dir.Write("SheetKraft.log", "INFO  line\n");

        Assert.True(MemberFile.TryOpen(path, null, out FileStream? a, out _, out _));
        Assert.True(MemberFile.TryOpen(path, null, out FileStream? b, out _, out _));
        a!.Dispose();
        b!.Dispose();
    }

    [Fact]
    public void On_Linux_an_open_file_can_be_renamed_and_deleted_and_still_read()
    {
        // Verified on Linux only. The Windows behaviour (FileShare.Delete allows delete-pending while open) is unverified here.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }


        using var dir = new ConfigTestDirectory();
        const string content = "INFO  still readable\n";
        string path = dir.Write("SheetKraft.log", content);
        Assert.True(MemberFile.TryOpen(path, null, out FileStream? stream, out string? identity, out _));
        using FileStream handle = stream!;

        string moved = dir.Full("SheetKraft1.log");
        File.Move(path, moved);
        File.Delete(moved);

        Assert.False(File.Exists(moved));
        byte[] buffer = new byte[content.Length];
        handle.Position = 0;
        handle.ReadExactly(buffer);
        Assert.Equal(content, System.Text.Encoding.UTF8.GetString(buffer));
        Assert.Equal(identity, FileIdentity.TryGet(handle.SafeFileHandle));
    }
}
