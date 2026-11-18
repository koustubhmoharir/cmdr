namespace Shared.Core.Tests.Config;

/// <summary>A temporary directory that is deleted on disposal.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = Directory.CreateTempSubdirectory("shared-core-tests-").FullName;
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}
