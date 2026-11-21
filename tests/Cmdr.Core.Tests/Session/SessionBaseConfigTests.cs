using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using Shared.Processes;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>Sessions over an import chain: order and sources, per-file settings, read-only items, and saving the opened file only.</summary>
public class SessionBaseConfigTests
{
    private static readonly string BaseRel = Path.Combine("shared", "base.json");

    /// <summary>
    /// cmdr.json (bufferLines 50) imports shared/base.json (bufferLines 7, logsDir base-logs). Base group "b" holds "bcmd", which
    /// runs in shared/work and echoes ${configDir}. Main groups are "m1" (command "m1cmd") and "m2" (command "m2cmd").
    /// </summary>
    private static CmdrSession OpenStandard(TempDir dir, SessionMode mode)
    {
        Directory.CreateDirectory(dir.File(Path.Combine("shared", "work")));
        CommandSpec bcmd = Sh("bcmd", "pwd; echo \"where=$WHERE\"");
        bcmd.Cwd = "work";
        bcmd.Env = new Dictionary<string, string?> { ["WHERE"] = "${configDir}" };
        ConfigStore.Save(new CmdrConfig
        {
            BufferLines = 7,
            LogsDir = "base-logs",
            Groups = new List<GroupSpec> { Group("b", GroupMode.Parallel, bcmd) },
        }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig
        {
            Base = BaseRel,
            BufferLines = 50,
            Groups = new List<GroupSpec>
            {
                Group("m1", GroupMode.Parallel, Sh("m1cmd", "true")),
                Group("m2", GroupMode.Parallel, Sh("m2cmd", "true")),
            },
        }, dir.File("cmdr.json"));

        return CmdrSession.Load(dir.File("cmdr.json"), mode);
    }

    private static void AssertRefused(Action action, string name)
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(action);
        Assert.Equal($"'{name}' comes from {BaseRel}; edit that file.", ex.Message);
    }

    [Fact]
    public void Chain_TopLevelGroupsInChainOrder_AndSourcesDescribeEachFile()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Groups = { Group("c2", GroupMode.Parallel, Sh("x", "true")) } }, dir.File(Path.Combine("shared", "base2.json")));
        ConfigStore.Save(new CmdrConfig { Base = "base2.json", Groups = { Group("b", GroupMode.Parallel, Sh("y", "true")) } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, Groups = { Group("m", GroupMode.Parallel, Sh("z", "true")) } }, dir.File("cmdr.json"));

        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        Assert.Equal(new[] { "c2", "b", "m" }, session.Groups.Select(g => g.Name));
        Assert.Equal(new[] { Path.Combine("shared", "base2.json"), BaseRel, "cmdr.json" }, session.Sources.Select(s => s.DisplayPath));
        Assert.Equal(new[] { true, true, false }, session.Sources.Select(s => s.IsBase));
        Assert.Equal(dir.File(Path.Combine("shared", "base2.json")), session.Sources[0].Path);
        Assert.Same(session.Sources[^1], session.MainSource);
        Assert.Equal(dir.File("cmdr.json"), session.ConfigPath);
        Assert.Equal(dir.Path, session.ConfigDir);

        Assert.Same(session.Sources[0], session.Groups[0].Source);
        Assert.Same(session.Sources[1], session.Groups[1].Source);
        Assert.Same(session.MainSource, session.Groups[2].Source);
        Assert.Equal(new[] { true, true, false }, session.Groups.Select(g => g.IsReadOnly));
        Assert.Equal(new[] { true, true, false }, session.Groups.Select(g => g.AllCommands.Single().IsReadOnly));

        Assert.Empty(session.LoadErrors);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Chain_SameTopLevelGroupNameInBaseAndMain_IsNotAnError()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Groups = { Group("shared", GroupMode.Parallel, Sh("a", "true")) } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, Groups = { Group("shared", GroupMode.Parallel, Sh("c", "true")) } }, dir.File("cmdr.json"));

        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        Assert.Equal(2, session.Groups.Count);
        Assert.Empty(session.LoadErrors);
    }

    [Fact]
    public void Chain_DuplicateInsideBase_IsReportedWithBaseFilePrefix()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Groups = { Group("b", GroupMode.Parallel, Sh("a", "true"), Sh("A", "true")) } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, Groups = { Group("m", GroupMode.Parallel, Sh("c", "true")) } }, dir.File("cmdr.json"));

        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        Assert.Contains(session.LoadErrors, e => e.StartsWith(BaseRel + ": ", StringComparison.Ordinal) && e.Contains("duplicate name 'a'", StringComparison.Ordinal));
        Assert.All(session.LoadErrors, e => Assert.StartsWith(BaseRel + ": ", e, StringComparison.Ordinal));
    }

    [Fact]
    public void Load_MissingBaseFile_ThrowsConfigException()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { Base = "missing.json" }, dir.File("cmdr.json"));

        Assert.Throws<ConfigException>(() => CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run));
    }

    [Fact]
    public void Load_InvalidLogsDirectory_IsConfigException()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { LogsDir = "bad\0logs" }, dir.File("cmdr.json"));

        ConfigException ex = Assert.Throws<ConfigException>(() => CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run));

        Assert.Contains("logsDir", ex.Message);
        Assert.Contains(dir.File("cmdr.json"), ex.Message);
    }

    [Fact]
    public void Buffer_FollowsBufferLinesOfTheFileTheCommandCameFrom()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenStandard(dir, SessionMode.Run);

        Assert.Equal(7, session.Groups[0].Commands[0].Process.Output.Buffer.Capacity);
        Assert.Equal(50, session.Groups[1].Commands[0].Process.Output.Buffer.Capacity);
        Assert.Equal(50, session.BufferLines);
        Assert.Equal(7, session.Sources[0].BufferLines);
    }

    [Fact]
    public void Buffer_InvalidBaseBufferLines_ReportsLoadError_AndStillUsesOneLine()
    {
        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig { BufferLines = 0, Groups = { Group("b", GroupMode.Parallel, Sh("a", "true")) } }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig { Base = BaseRel, Groups = { Group("m", GroupMode.Parallel, Sh("c", "true")) } }, dir.File("cmdr.json"));

        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        Assert.Contains(session.LoadErrors, e => e.StartsWith(BaseRel + ": ", StringComparison.Ordinal) && e.Contains("bufferLines", StringComparison.Ordinal));
        Assert.Equal(1, session.Groups[0].Commands[0].Process.Output.Buffer.Capacity);
        Assert.Equal(10000, session.BufferLines);
    }

    [Fact]
    public void Log_DefaultPath_UsesLogsDirOfTheFileTheCommandCameFrom()
    {
        using var dir = new TempDir();
        CmdrSession session = OpenStandard(dir, SessionMode.Run);
        try
        {
            CommandEntry baseEntry = session.Groups[0].Commands[0];
            CommandEntry mainEntry = session.Groups[1].Commands[0];

            session.ToggleLog(baseEntry);
            session.ToggleLog(mainEntry);

            Assert.True(File.Exists(dir.File(Path.Combine("shared", "base-logs", "b", "bcmd.log"))));
            Assert.True(File.Exists(dir.File(Path.Combine("logs", "m1", "m1cmd.log"))));
            Assert.False(File.Exists(dir.File(Path.Combine("logs", "b", "bcmd.log"))));
            Assert.False(session.IsDirty);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void BaseCommand_RunsFromBaseDirectory_AndConfigDirIsBaseDirectory()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenStandard(dir, SessionMode.Run);
        try
        {
            CommandEntry entry = session.Groups[0].Commands[0];
            string where = "where=" + dir.File("shared");

            Assert.Null(session.Activate(new[] { entry }));
            WaitUntil(() => Text(entry.Process).Contains(where, StringComparison.Ordinal), "base command output", entry.Process);

            string text = Text(entry.Process);
            Assert.Contains(Path.Combine("shared", "work"), text, StringComparison.Ordinal);
            Assert.Contains(where, text, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void ReadOnly_EditsOfBaseItemsAreRefused_NamingTheItemAndItsFile()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenStandard(dir, SessionMode.Edit);
        GroupEntry b = session.Groups[0];
        CommandEntry bcmd = b.Commands[0];
        GroupEntry m1 = session.Groups[1];
        CommandEntry m1cmd = m1.Commands[0];

        AssertRefused(() => session.AddGroup(b, Settings("x")), "b");
        AssertRefused(() => session.AddCommand(b, Sh("x", "true")), "b");
        AssertRefused(() => session.UpdateGroup(b, Settings("renamed"), null), "b");
        AssertRefused(() => session.UpdateGroup(m1, Settings("m1"), b), "b");
        AssertRefused(() => session.RemoveGroup(b), "b");
        AssertRefused(() => session.UpdateCommand(bcmd, Sh("renamed", "true"), b), "bcmd");
        AssertRefused(() => session.UpdateCommand(bcmd, bcmd.Spec, m1), "bcmd");
        AssertRefused(() => session.UpdateCommand(m1cmd, m1cmd.Spec, b), "b");
        AssertRefused(() => session.RemoveCommand(bcmd), "bcmd");
        AssertRefused(() => session.MoveItem(b, 1), "b");
        AssertRefused(() => session.MoveItem(bcmd, 1), "bcmd");
        AssertRefused(() => session.MoveCommand(bcmd, 1), "bcmd");

        Assert.False(session.IsDirty);
        Assert.Equal(new[] { "b", "m1", "m2" }, session.Groups.Select(g => g.Name));
        Assert.Equal(new[] { "bcmd" }, b.Commands.Select(c => c.Name));
        Assert.Equal(new[] { "m1cmd" }, m1.Commands.Select(c => c.Name));
    }

    [Fact]
    public void MoveItem_TopLevel_MainGroupNeverMovesAboveBaseGroups()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenStandard(dir, SessionMode.Edit);
        GroupEntry m1 = session.Groups[1];
        GroupEntry m2 = session.Groups[2];

        Assert.False(session.MoveItem(m1, -1));
        Assert.Equal(new[] { "b", "m1", "m2" }, session.Groups.Select(g => g.Name));
        Assert.False(session.IsDirty);

        Assert.True(session.MoveItem(m2, -1));
        Assert.Equal(new[] { "b", "m2", "m1" }, session.Groups.Select(g => g.Name));
        Assert.False(session.MoveItem(m2, -1));
    }

    [Fact]
    public void MainItems_EditsStillWork_WithBaseGroupsPresent()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenStandard(dir, SessionMode.Edit);
        GroupEntry m1 = session.Groups[1];

        GroupEntry added = session.AddGroup(null, Settings("new"));
        CommandEntry extra = session.AddCommand(m1, Sh("m1extra", "true"));
        session.UpdateCommand(m1.Commands[0], Sh("m1renamed", "true"), m1);
        session.UpdateGroup(m1, Settings("m1renamed"), added);

        Assert.Same(session.MainSource, added.Source);
        Assert.Same(session.MainSource, extra.Source);
        Assert.Same(added, m1.Parent);
        Assert.Equal(new[] { "b", "m2", "new" }, session.Groups.Select(g => g.Name));
        Assert.Equal(new[] { "m1renamed", "m1extra" }, m1.Commands.Select(c => c.Name));
        Assert.True(session.IsDirty);

        session.RemoveCommand(extra);
        Assert.Equal(new[] { "m1renamed" }, m1.Commands.Select(c => c.Name));
    }

    [Fact]
    public void Save_WritesOnlyOpenedFileGroups_KeepsBase_AndLeavesBaseFilesUntouched()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenStandard(dir, SessionMode.Edit);
        string basePath = dir.File(BaseRel);
        byte[] baseBytes = File.ReadAllBytes(basePath);
        CmdrConfig built = session.BuildConfig();
        Assert.Equal(new[] { "m1", "m2" }, built.Groups.Select(g => g.Name));
        Assert.Equal(BaseRel, built.Base);

        GroupEntry added = session.AddGroup(null, Settings("new"));
        session.AddCommand(added, Sh("newcmd", "true"));
        session.AddCommand(session.Groups[1], Sh("m1extra", "true"));
        session.Save();

        Assert.False(session.IsDirty);
        Assert.Equal(baseBytes, File.ReadAllBytes(basePath));

        CmdrConfig reloaded = ConfigStore.Load(dir.File("cmdr.json"));
        Assert.Equal(BaseRel, reloaded.Base);
        Assert.Equal(50, reloaded.BufferLines);
        Assert.Equal(new[] { "m1", "m2", "new" }, reloaded.Groups.Select(g => g.Name));
        Assert.Equal(new[] { "m1cmd", "m1extra" }, reloaded.Groups[0].Items.Select(i => i.Name));

        using CmdrSession reopened = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Edit);
        Assert.Empty(reopened.LoadErrors);
        Assert.Equal(new[] { "b", "m1", "m2", "new" }, reopened.Groups.Select(g => g.Name));
    }

    /// <summary>A command that prints the value of each name, in brackets, so an unset variable is visible.</summary>
    private static CommandSpec Print(string name, params string[] variables) =>
        Sh(name, string.Join(" ", variables.Select(v => $"echo \"{v}=[${v}]\";")));

    private static void StartAndWait(CmdrSession session, CommandEntry entry, string expected, IReadOnlyDictionary<string, string>? inputs = null)
    {
        Assert.Null(session.Activate(new[] { entry }, inputs));
        WaitUntil(() => Text(entry.Process).Contains(expected, StringComparison.Ordinal), "output " + expected, entry.Process);
    }

    [Fact]
    public void TopLevelEnv_ReachesCommandsOfTheOpenedFile()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig
        {
            Env = new Dictionary<string, string?> { ["CMDRTEST_TOP"] = "top", ["CMDRTEST_DIR"] = "${cwd}/d" },
            Groups = { Group("m", GroupMode.Parallel, Print("c", "CMDRTEST_TOP", "CMDRTEST_DIR")) },
        }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        StartAndWait(session, session.Groups[0].Commands[0], $"CMDRTEST_DIR=[{dir.File("d")}]");

        Assert.Contains("CMDRTEST_TOP=[top]", Text(session.Groups[0].Commands[0].Process), StringComparison.Ordinal);
    }

    [Fact]
    public void BaseEnv_OnlyOptedInNamesReachTheImportingFile()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig
        {
            Env = new Dictionary<string, string?> { ["CMDRTEST_IMPORTED"] = "yes", ["CMDRTEST_NOT_IMPORTED"] = "no" },
            Groups = { Group("b", GroupMode.Parallel, Sh("bcmd", "true")) },
        }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig
        {
            Base = BaseRel,
            BaseEnv = new List<string> { "CMDRTEST_IMPORTED" },
            Groups = { Group("m", GroupMode.Parallel, Print("c", "CMDRTEST_IMPORTED", "CMDRTEST_NOT_IMPORTED")) },
        }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        CommandEntry entry = session.Groups[1].Commands[0];
        StartAndWait(session, entry, "CMDRTEST_NOT_IMPORTED=[]");

        Assert.Contains("CMDRTEST_IMPORTED=[yes]", Text(entry.Process), StringComparison.Ordinal);
    }

    [Fact]
    public void BaseEnv_ImportedValue_ExpandsConfigDirAndCwdAsTheBaseFileDirectory()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig
        {
            Env = new Dictionary<string, string?> { ["CMDRTEST_BASE_DIRS"] = "${configDir}|${cwd}" },
            Groups = { Group("b", GroupMode.Parallel, Sh("bcmd", "true")) },
        }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig
        {
            Base = BaseRel,
            BaseEnv = new List<string> { "CMDRTEST_BASE_DIRS" },
            Groups = { Group("m", GroupMode.Parallel, Print("c", "CMDRTEST_BASE_DIRS")) },
        }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        string baseDir = Path.GetDirectoryName(Path.GetFullPath(dir.File(BaseRel)))!;
        StartAndWait(session, session.Groups[1].Commands[0], $"CMDRTEST_BASE_DIRS=[{baseDir}|{baseDir}]");
    }

    [Fact]
    public void BaseFileGroups_SeeTheTopLevelEnvOfTheirOwnFile()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig
        {
            Env = new Dictionary<string, string?> { ["CMDRTEST_BASE_TOP"] = "base-value" },
            Groups = { Group("b", GroupMode.Parallel, Print("bcmd", "CMDRTEST_BASE_TOP")) },
        }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig
        {
            Base = BaseRel,
            Env = new Dictionary<string, string?> { ["CMDRTEST_BASE_TOP"] = "main-value" },
            Groups = { Group("m", GroupMode.Parallel, Print("mcmd", "CMDRTEST_BASE_TOP")) },
        }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        CommandEntry baseEntry = session.Groups[0].Commands[0];
        StartAndWait(session, baseEntry, "CMDRTEST_BASE_TOP=[base-value]");
        CommandEntry mainEntry = session.Groups[1].Commands[0];
        StartAndWait(session, mainEntry, "CMDRTEST_BASE_TOP=[main-value]");
    }

    [Fact]
    public void ThreeFileChain_ImportsThroughTheMiddleFile()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string deepRel = Path.Combine("shared", "deep.json");
        ConfigStore.Save(new CmdrConfig
        {
            Env = new Dictionary<string, string?> { ["CMDRTEST_DEEP"] = "deep", ["CMDRTEST_DEEP_ONLY"] = "x" },
            Groups = { Group("d", GroupMode.Parallel, Sh("dcmd", "true")) },
        }, dir.File(deepRel));
        ConfigStore.Save(new CmdrConfig
        {
            Base = "deep.json",
            BaseEnv = new List<string> { "CMDRTEST_DEEP" },
            Groups = { Group("b", GroupMode.Parallel, Sh("bcmd", "true")) },
        }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig
        {
            Base = BaseRel,
            BaseEnv = new List<string> { "CMDRTEST_DEEP" },
            Groups = { Group("m", GroupMode.Parallel, Print("c", "CMDRTEST_DEEP", "CMDRTEST_DEEP_ONLY")) },
        }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);

        Assert.Empty(session.LoadErrors);
        CommandEntry entry = session.Groups[2].Commands[0];
        StartAndWait(session, entry, "CMDRTEST_DEEP=[deep]");
        Assert.Contains("CMDRTEST_DEEP_ONLY=[]", Text(entry.Process), StringComparison.Ordinal);
    }

    [Fact]
    public void InputInImportedBaseValue_IsRequestedAndReachesTheProcess()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        ConfigStore.Save(new CmdrConfig
        {
            Env = new Dictionary<string, string?> { ["CMDRTEST_GREETING"] = "hi-${input:who}" },
            Groups = { Group("b", GroupMode.Parallel, Sh("bcmd", "true")) },
        }, dir.File(BaseRel));
        ConfigStore.Save(new CmdrConfig
        {
            Base = BaseRel,
            BaseEnv = new List<string> { "CMDRTEST_GREETING" },
            Groups = { Group("m", GroupMode.Parallel, Print("c", "CMDRTEST_GREETING")) },
        }, dir.File("cmdr.json"));
        using CmdrSession session = CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
        CommandEntry entry = session.Groups[1].Commands[0];

        Assert.Equal(new[] { new InputRequest("who", false) }, session.InputsForActivate(new[] { entry }));
        StartAndWait(session, entry, "CMDRTEST_GREETING=[hi-bob]", new Dictionary<string, string> { ["who"] = "bob" });
    }
}
