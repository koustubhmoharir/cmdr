using System.Diagnostics;
using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class LogPathConflictTests
{
    private static CommandSpec Cmd(string name, string? logPath = null, bool logEnabled = false) =>
        new() { Name = name, Program = "dotnet", Log = new LogSpec { Enabled = logEnabled, Path = logPath } };

    private static GroupSpec Group(string name, params ItemSpec[] items) =>
        new() { Name = name, Items = items.ToList() };

    private static void Save(TempDir dir, string relativePath, CmdrConfig config) =>
        ConfigStore.Save(config, dir.File(relativePath));

    private static IReadOnlyList<ConfigFile> LoadOpened(TempDir dir) =>
        ConfigStore.LoadChain(dir.File("cmdr.json"));

    private static string Logs(params string[] parts) => Path.Combine(new[] { "logs" }.Concat(parts).ToArray());

    [Fact]
    public void SiblingsDifferingOnlyInUnsafeCharacters_ReportOneExactDuplicate()
    {
        using var dir = new TempDir();
        Save(dir, "cmdr.json", new CmdrConfig { Groups = { Group("g", Cmd("a b"), Cmd("a_b")) } });

        string error = Assert.Single(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));

        Assert.Equal(
            $"Log path conflict: Group 'g' › command 'a b' and Group 'g' › command 'a_b' both log to '{Logs("g", "a_b.log")}'.",
            error);
    }

    [Fact]
    public void SiblingsWithDistinctNames_NoError()
    {
        using var dir = new TempDir();
        Save(dir, "cmdr.json", new CmdrConfig { Groups = { Group("g", Cmd("a b"), Cmd("a c")) } });

        Assert.Empty(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));
    }

    [Fact]
    public void ThreeWayCollision_ReportsOneErrorNamingAllThree()
    {
        using var dir = new TempDir();
        Save(dir, "cmdr.json", new CmdrConfig { Groups = { Group("g", Cmd("a b"), Cmd("a_b"), Cmd("a/b")) } });

        string error = Assert.Single(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));

        Assert.Equal(
            $"Log path conflict: Group 'g' › command 'a b', Group 'g' › command 'a_b' and Group 'g' › command 'a/b' all log to '{Logs("g", "a_b.log")}'.",
            error);
    }

    [Fact]
    public void ExplicitLogPathEqualToAnotherDefaultPath_IsAConflict()
    {
        using var dir = new TempDir();
        Save(dir, "cmdr.json", new CmdrConfig
        {
            Groups = { Group("g", Cmd("a b"), Cmd("x", logPath: Path.Combine("g", "a_b.log"))) },
        });

        string error = Assert.Single(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));

        Assert.Equal(
            $"Log path conflict: Group 'g' › command 'a b' and Group 'g' › command 'x' both log to '{Logs("g", "a_b.log")}'.",
            error);
    }

    [Fact]
    public void CommandLogFileInsideAnotherCommandsLogFolder_IsAnAncestorConflict()
    {
        using var dir = new TempDir();
        Save(dir, "cmdr.json", new CmdrConfig
        {
            Groups =
            {
                Group(
                    "g",
                    Cmd("build"),
                    Group("build.log", Cmd("child"))),
            },
        });

        string error = Assert.Single(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));

        Assert.Equal(
            $"Log path conflict: the log of Group 'g' › command 'build' ('{Logs("g", "build.log")}') is the folder of the log of "
            + $"Group 'g' › 'build.log' › command 'child' ('{Logs("g", "build.log", "child.log")}').",
            error);
    }

    [Fact]
    public void PathsDifferingOnlyInCase_AreConflictsOnlyWhereFileSystemIgnoresCase()
    {
        using var dir = new TempDir();
        Save(dir, "cmdr.json", new CmdrConfig
        {
            Groups = { Group("g", Cmd("one", logPath: "same.log"), Cmd("two", logPath: "SAME.log")) },
        });

        IReadOnlyList<string> errors = ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false);

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Single(errors);
            Assert.Contains("both log to", errors[0], StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void CommandWithLogDisabled_StillConflicts()
    {
        using var dir = new TempDir();
        Save(dir, "cmdr.json", new CmdrConfig
        {
            Groups = { Group("g", Cmd("a b", logEnabled: false), Cmd("a_b", logEnabled: false)) },
        });

        Assert.Single(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));
    }

    [Fact]
    public void BaseAndOpenedFileSharingLogsDir_ConflictIsReportedAndLabeledAsBase()
    {
        using var dir = new TempDir();
        Save(dir, "base.json", new CmdrConfig { Groups = { Group("g", Cmd("a b")) } });
        Save(dir, "cmdr.json", new CmdrConfig { Base = "base.json", Groups = { Group("g", Cmd("a_b")) } });
        IReadOnlyList<ConfigFile> chain = LoadOpened(dir);

        string error = Assert.Single(ConfigStore.ValidateChain(chain));

        Assert.Equal(
            $"Log path conflict: Group 'g' › command 'a b' (in base.json) and Group 'g' › command 'a_b' both log to '{Logs("g", "a_b.log")}'.",
            error);
        Assert.Single(ConfigStore.LogPathErrors(chain, onlyInvolvingOpened: true));
    }

    [Fact]
    public void ConflictEntirelyWithinBase_IsInValidateChainButNotInOpenedOnlyErrors()
    {
        using var dir = new TempDir();
        Save(dir, "base.json", new CmdrConfig { Groups = { Group("g", Cmd("a b"), Cmd("a_b")) } });
        Save(dir, "cmdr.json", new CmdrConfig { Base = "base.json", Groups = { Group("h", Cmd("c")) } });
        IReadOnlyList<ConfigFile> chain = LoadOpened(dir);

        string error = Assert.Single(ConfigStore.ValidateChain(chain));

        Assert.Contains("(in base.json)", error, StringComparison.Ordinal);
        Assert.Empty(ConfigStore.LogPathErrors(chain, onlyInvolvingOpened: true));
    }

    [Fact]
    public void BaseFileWithItsOwnLogsDir_DoesNotConflictWithOpenedFile()
    {
        using var dir = new TempDir();
        Save(dir, Path.Combine("shared", "base.json"), new CmdrConfig { Groups = { Group("g", Cmd("a b")) } });
        Save(dir, "cmdr.json", new CmdrConfig
        {
            Base = Path.Combine("shared", "base.json"),
            Groups = { Group("g", Cmd("a_b")) },
        });

        Assert.Empty(ConfigStore.ValidateChain(LoadOpened(dir)));
    }

    [Fact]
    public void ExplicitPathsReachingOneFileThroughADirectoryLink_AreAConflict()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // creating symbolic links needs a privilege on Windows
        }

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("real"));
        Directory.CreateSymbolicLink(dir.File("alias"), "real");
        Save(dir, "cmdr.json", new CmdrConfig
        {
            Groups = { Group("g", Cmd("a", logPath: "../real/same.log"), Cmd("b", logPath: "../alias/same.log")) },
        });

        string error = Assert.Single(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));

        Assert.Equal(
            $"Log path conflict: Group 'g' › command 'a' and Group 'g' › command 'b' both log to '{Path.Combine("real", "same.log")}'.",
            error);
    }

    [Fact]
    public void LogFolderReachedThroughADirectoryLink_IsAnAncestorConflict()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("real"));
        Directory.CreateSymbolicLink(dir.File("alias"), "real");
        Save(dir, "cmdr.json", new CmdrConfig
        {
            Groups = { Group("g", Cmd("a", logPath: "../real"), Cmd("b", logPath: "../alias/b.log")) },
        });

        string error = Assert.Single(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));

        Assert.Contains("is the folder of the log of Group 'g' › command 'b'", error);
    }

    // Creates a hard link with ln(1). The output is drained and the wait is bounded, so a stuck ln cannot hang the test run.
    private static void HardLink(string existing, string link)
    {
        var info = new ProcessStartInfo("ln") { RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(existing);
        info.ArgumentList.Add(link);

        using Process process = Process.Start(info)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"'ln {existing} {link}' did not finish.");
        }

        stdout.GetAwaiter().GetResult();
        string error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'ln {existing} {link}' exited with {process.ExitCode}: {error}");
        }
    }

    [Fact]
    public void HardLinkedLogFiles_AreOneConflictNamingBoth()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the hard links are made with ln(1)
        }

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("logs"));
        File.WriteAllText(dir.File(Logs("a.log")), "");
        HardLink(dir.File(Logs("a.log")), dir.File(Logs("b.log")));
        Save(dir, "cmdr.json", new CmdrConfig
        {
            Groups = { Group("g", Cmd("a", logPath: "a.log"), Cmd("b", logPath: "b.log")) },
        });

        string error = Assert.Single(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));

        Assert.Equal(
            $"Log path conflict: Group 'g' › command 'a' and Group 'g' › command 'b' both log to '{Logs("a.log")}'.",
            error);
    }

    [Fact]
    public void DistinctExistingLogFiles_NoError()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("logs"));
        File.WriteAllText(dir.File(Logs("a.log")), "one");
        File.WriteAllText(dir.File(Logs("b.log")), "two");
        Save(dir, "cmdr.json", new CmdrConfig
        {
            Groups = { Group("g", Cmd("a", logPath: "a.log"), Cmd("b", logPath: "b.log")) },
        });

        Assert.Empty(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));
    }

    [Fact]
    public void DistinctMissingLogFiles_NoError()
    {
        using var dir = new TempDir();
        Save(dir, "cmdr.json", new CmdrConfig
        {
            Groups = { Group("g", Cmd("a", logPath: "a.log"), Cmd("b", logPath: "b.log")) },
        });

        Assert.Empty(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));
    }

    [Fact]
    public void ExistingLogFileAndMissingLogFileWithDifferentNames_NoError()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("logs"));
        File.WriteAllText(dir.File(Logs("a.log")), "");
        Save(dir, "cmdr.json", new CmdrConfig
        {
            Groups = { Group("g", Cmd("a", logPath: "a.log"), Cmd("b", logPath: "b.log")) },
        });

        Assert.Empty(ConfigStore.LogPathErrors(LoadOpened(dir), onlyInvolvingOpened: false));
    }
}
