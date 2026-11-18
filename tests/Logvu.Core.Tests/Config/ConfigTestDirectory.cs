using System.Text;

namespace Logvu.Core.Tests.Config;

/// <summary>A fresh temporary directory, deleted on disposal.</summary>
internal sealed class ConfigTestDirectory : IDisposable
{
    public ConfigTestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "logvu-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>The full path of the directory.</summary>
    public string Path { get; }

    /// <summary>The full path of a file or subdirectory relative to the directory.</summary>
    public string Full(string relative) => System.IO.Path.Combine(Path, relative);

    /// <summary>Writes a UTF-8 file without a byte order mark and returns its full path.</summary>
    public string Write(string relative, string content)
    {
        string full = Full(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return full;
    }

    /// <summary>Writes a file with the given bytes and returns its full path.</summary>
    public string WriteBytes(string relative, byte[] bytes)
    {
        string full = Full(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temporary directory does not fail the test.
        }
    }
}
