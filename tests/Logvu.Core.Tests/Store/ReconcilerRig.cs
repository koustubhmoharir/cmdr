using System.Text;
using Logvu.Core.Config;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;

namespace Logvu.Core.Tests.Store;

/// <summary>
/// A catalog, ring and reconciler over a real directory, with the batches of every run kept in order. Retries are instant, as the
/// tests set them.
/// </summary>
internal sealed class ReconcilerRig
{
    private readonly ConfigTestDirectory _dir;

    private ReconcilerRig(ConfigTestDirectory dir, LogvuConfig config)
    {
        _dir = dir;
        Config = config;
        Catalog = new Catalog(config);
        Ring = new RowRing(config.RecentCapacity, Catalog.Compare);
        Reconciler = new Reconciler(config, Catalog, Ring) { RetryDelay = TimeSpan.Zero };
    }

    /// <summary>The numbered-rotation set of the SheetKraft examples, with <c>SheetKraft.log</c> as the active file.</summary>
    public static ReconcilerRig Numbered(ConfigTestDirectory dir) =>
        new(dir, StoreTestConfig.Create(dir, activeFile: "SheetKraft.log"));

    /// <summary>A dated set of JSON files with no active file: the newest file is the live one.</summary>
    public static ReconcilerRig Dated(ConfigTestDirectory dir) =>
        new(dir, StoreTestConfig.Create(dir, prefix: "log", pattern: @"^log[0-9]{8}[.]json$"));

    /// <summary>A numbered set whose logs are in a subdirectory, which may not exist yet.</summary>
    public static ReconcilerRig NumberedIn(ConfigTestDirectory dir, string logsDirectory) =>
        new(dir, StoreTestConfig.Create(dir, activeFile: "SheetKraft.log", logsDirectory: logsDirectory));

    public LogvuConfig Config { get; }

    public Catalog Catalog { get; }

    public RowRing Ring { get; }

    public Reconciler Reconciler { get; }

    /// <summary>The batches of every run so far, in order.</summary>
    public List<ChangeBatch> Batches { get; } = [];

    /// <summary>The keys of every row added by any run so far, in order.</summary>
    public List<RowKey> AddedKeys => Batches.SelectMany(b => b.Added).Select(row => row.Key).ToList();

    /// <summary>Runs the reconciler and keeps the batch.</summary>
    public ChangeBatch Run(bool rescan = false)
    {
        ChangeBatch batch = Reconciler.Run(rescan);
        Batches.Add(batch);
        return batch;
    }

    /// <summary>The live member of a name: the one that is not expired.</summary>
    public Member Find(string name) =>
        Catalog.All.Single(member => member.Name == name && member.State != MemberState.Expired);

    /// <summary>The full path of a file of the directory.</summary>
    public string Path(string name) => _dir.Full(name);

    /// <summary>Appends text to a file of the directory, creating it when needed, as a writer does: no truncation, no rename.</summary>
    public void Append(string name, string text)
    {
        string path = _dir.Full(name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.AppendAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Writes a file of the directory, replacing it.</summary>
    public void Write(string name, string text) => _dir.Write(name, text);

    /// <summary>The keys of the rows of a batch, in order.</summary>
    public static RowKey[] Keys(IEnumerable<EventRow> rows) => rows.Select(row => row.Key).ToArray();
}
