using System.Globalization;
using System.Text;
using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using Shared.Core;

namespace Logvu.Core.Tests.Store;

/// <summary>Builds realistic log files and catalogs for the store tests.</summary>
internal static class StoreFixtures
{
    private static readonly string[] Levels = ["INFO", "DEBUG", "WARN", "ERROR"];

    /// <summary>One log4net-core header line, in the format the built-in scheme reads.</summary>
    public static string Line(DateTimeOffset time, string level, string message) =>
        $"{level,-5} {time.ToString("yyyy-MM-dd HH:mm:ss,fff", CultureInfo.InvariantCulture)} [1] SheetKraft.Engine - {message}\n";

    /// <summary>
    /// <paramref name="count"/> log4net-core events from <paramref name="start"/>, seven seconds apart, levels cycling through INFO, DEBUG,
    /// WARN and ERROR. Each ERROR has a stack trace with a blank line in it, so events span several lines.
    /// </summary>
    public static string Events(DateTimeOffset start, string tag, int count)
    {
        var text = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            string level = Levels[i % Levels.Length];
            text.Append(Line(start.AddSeconds(i * 7), level, $"{tag} event {i}"));
            if (level == "ERROR")
            {
                text.Append($"System.InvalidOperationException: {tag} failed {i}\n");
                text.Append("   at SheetKraft.Engine.Runner.Execute(Int32 id) in C:\\src\\Runner.cs:line 88\n");
                text.Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>Adds a file of the directory to the catalog and probes it as a closed file, with its read cursor at the end.</summary>
    public static Member AddProbed(Catalog catalog, ConfigTestDirectory dir, LogvuConfig config, string name)
    {
        string path = dir.Full(name);
        Member member = catalog.Add(path, FileIdentity.TryGet(path)!);
        using FileStream stream = Open(path);
        member.Apply(MemberProbe.Probe(stream, config, live: false));
        member.ReadCursor = stream.Length;
        return member;
    }

    /// <summary>A catalog holding the named files of the directory, each probed.</summary>
    public static Catalog CatalogOf(ConfigTestDirectory dir, LogvuConfig config, params string[] names)
    {
        var catalog = new Catalog(config);
        foreach (string name in names)
        {
            AddProbed(catalog, dir, config, name);
        }

        return catalog;
    }

    /// <summary>
    /// The rows of every readable member, in timeline order, each read by framing its file forward from the start and keeping the
    /// events that end within its read cursor. This is the reference the reader is compared with.
    /// </summary>
    public static List<EventRow> Expected(Catalog catalog, LogvuConfig config)
    {
        var context = new ParseContext(config.TimeZone);
        var rows = new List<EventRow>();
        foreach (Member member in catalog.Ordered.Where(m => m.IsReadable))
        {
            using FileStream stream = Open(member.Path);
            var framer = new EventFramer(stream, member.Scheme!, live: false);
            foreach (EventExtent extent in framer.ReadForward(0))
            {
                if (extent.End > member.ReadCursor)
                {
                    break;
                }

                rows.Add(RowBuilder.Build(stream, member.Scheme, member.Generation, extent, context).Row);
            }
        }

        return rows;
    }

    /// <summary>The events of one file, framed forward as a closed file.</summary>
    public static EventExtent[] Extents(Member member)
    {
        using FileStream stream = Open(member.Path);
        return new EventFramer(stream, member.Scheme!, live: false).ReadForward(0).ToArray();
    }

    /// <summary>Opens a file the way the store does: shared for reading, writing and deleting.</summary>
    public static FileStream Open(string path) =>
        File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
