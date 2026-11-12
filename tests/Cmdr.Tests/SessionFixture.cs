using Cmdr.Core.Config;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>A session loaded from a temporary configuration file. Dispose removes the file.</summary>
internal sealed class TempSession : IDisposable
{
    private readonly string _dir;

    private TempSession(string dir, CmdrSession session)
    {
        _dir = dir;
        Session = session;
    }

    public CmdrSession Session { get; }

    /// <summary>Writes the groups to a configuration file and loads it, so the session starts clean.</summary>
    public static TempSession Open(params GroupSpec[] groups)
    {
        string dir = Directory.CreateTempSubdirectory("cmdr-tui-tests-").FullName;
        string path = Path.Combine(dir, "cmdr.json");
        ConfigStore.Save(new CmdrConfig { Groups = groups.ToList() }, path);
        return new TempSession(dir, CmdrSession.Load(path));
    }

    /// <summary>The sample tree of <see cref="SampleTree"/>.</summary>
    public static TempSession Sample() => Open(SampleTree.Groups());

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

/// <summary>
/// The sample tree used by the tree tests:
/// <code>
/// top (cwd "src", env X=1, Y removed)
///   first
///   mid (cwd "sub", env Y=2, GONE removed; description of four lines)
///     m1
///     deep
///       d1
///   last
/// other
///   oc
/// </code>
/// </summary>
internal static class SampleTree
{
    public static GroupSpec[] Groups() => new[]
    {
        new GroupSpec
        {
            Name = "top",
            Cwd = "src",
            Env = new Dictionary<string, string?> { ["X"] = "1", ["Y"] = null },
            Items =
            {
                Command("first"),
                new GroupSpec
                {
                    Name = "mid",
                    Cwd = "sub",
                    Description = "Line one\nLine two\nLine three\nLine four",
                    Env = new Dictionary<string, string?> { ["Y"] = "2", ["GONE"] = null },
                    Items =
                    {
                        Command("m1"),
                        new GroupSpec { Name = "deep", Mode = GroupMode.Sequential, Items = { Command("d1") } },
                    },
                },
                Command("last"),
            },
        },
        new GroupSpec { Name = "other", Items = { Command("oc") } },
    };

    private static CommandSpec Command(string name) => new() { Name = name, Program = "sh" };
}
