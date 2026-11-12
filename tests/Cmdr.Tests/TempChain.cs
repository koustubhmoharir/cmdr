using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>
/// A session loaded from a temporary opened file that imports a base file in <c>shared/base.json</c>. Dispose removes the folder.
/// </summary>
internal sealed class TempChain : IDisposable
{
    private readonly string _dir;

    private TempChain(string dir, CmdrSession session)
    {
        _dir = dir;
        Session = session;
    }

    public CmdrSession Session { get; }

    /// <summary>The directory of the opened file.</summary>
    public string Dir => _dir;

    /// <summary>The directory of the base file: where its relative paths resolve.</summary>
    public string BaseDir => Path.Combine(_dir, "shared");

    /// <summary>
    /// The opened file has group "main" with command "mine"; the base file has group "shared" with command "tool" (cwd "bin",
    /// env OWN = ${configDir}) and a top-level command-less group "extra".
    /// </summary>
    public static TempChain Open()
    {
        string dir = Directory.CreateTempSubdirectory("cmdr-tui-chain-").FullName;
        File.WriteAllText(
            Path.Combine(dir, "cmdr.json"),
            """{ "base": "shared/base.json", "groups": [ { "name": "main", "items": [ { "name": "mine", "program": "dotnet" } ] } ] }""");
        Directory.CreateDirectory(Path.Combine(dir, "shared"));
        File.WriteAllText(
            Path.Combine(dir, "shared", "base.json"),
            """
            { "groups": [
              { "name": "shared", "cwd": "work", "items": [
                { "name": "tool", "program": "tool", "cwd": "bin", "env": { "OWN": "${configDir}" } }
              ] },
              { "name": "extra" }
            ] }
            """);
        return new TempChain(dir, CmdrSession.Load(Path.Combine(dir, "cmdr.json")));
    }

    public void Dispose()
    {
        Session.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temporary folder is left behind when it cannot be removed.
        }
    }
}
