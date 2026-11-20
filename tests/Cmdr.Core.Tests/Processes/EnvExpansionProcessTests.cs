using Cmdr.Core.Config;
using Cmdr.Core.Output;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;

namespace Cmdr.Core.Tests.Processes;

/// <summary>End-to-end: group and command env with expansion, applied to a real Linux process.</summary>
public class EnvExpansionProcessTests
{
    [Fact]
    public void GroupAndCommandEnv_ExpandAgainstEarlierLevels_AndReachTheProcess()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var group = new GroupSpec
        {
            Name = "g",
            Cwd = "sub",
            Env = new Dictionary<string, string?> { ["FOO"] = "a" },
        };
        var cmd = new CommandSpec
        {
            Name = "c",
            Program = "sh",
            Args = { "-c", "echo \"$BAR\"; echo \"$WHERE\"; case $PATH in /cmdr-test-prefix:*) echo prefixed;; *) echo plain;; esac" },
            Env = new Dictionary<string, string?>
            {
                ["BAR"] = "${env:FOO}-b",
                ["WHERE"] = "${cwd}",
                ["PATH"] = "/cmdr-test-prefix${pathsep}${env:PATH}",
            },
        };

        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path, Array.Empty<EnvLevel>());
        var p = new ManagedProcess(1000);
        try
        {
            p.Start(spec);
            ProcessTestHelpers.WaitForExited(p);

            string[] Stdout() => ProcessTestHelpers.Lines(p)
                .Where(l => l.Source == OutputSource.StdOut)
                .Select(l => l.PlainText)
                .ToArray();

            string[] expected = { "a-b", Path.GetFullPath(Path.Combine(dir.Path, "sub")), "prefixed" };
            // The exit marker can be written before the last stdout chunks are read (the drain is bounded), so wait for them.
            ProcessTestHelpers.WaitUntil(() => Stdout().Length >= expected.Length, "all stdout lines", p);

            Assert.Equal(expected, Stdout());
        }
        finally
        {
            ProcessTestHelpers.Cleanup(p);
        }
    }

    /// <summary>A command that prints each of its arguments on its own line (the script is <c>printf '%s\n' "$@"</c>).</summary>
    private static CommandSpec Printing(params string[] args) =>
        new()
        {
            Name = "c",
            Program = "sh",
            Args = new List<string> { "-c", "printf '%s\\n' \"$@\"", "_" }.Concat(args).ToList(),
        };

    /// <summary>Starts the launch, waits for it to exit, and returns its stdout lines.</summary>
    private static string[] Run(LaunchSpec spec, int expectedLines)
    {
        var p = new ManagedProcess(1000);
        try
        {
            p.Start(spec);
            ProcessTestHelpers.WaitForExited(p);
            string[] Stdout() => ProcessTestHelpers.Lines(p)
                .Where(l => l.Source == OutputSource.StdOut)
                .Select(l => l.PlainText)
                .ToArray();

            ProcessTestHelpers.WaitUntil(() => Stdout().Length >= expectedLines, "all stdout lines", p);
            return Stdout();
        }
        finally
        {
            ProcessTestHelpers.Cleanup(p);
        }
    }

    [Fact]
    public void ArgEnvPlaceholder_ReadsAGroupEnvValue()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var group = new GroupSpec { Name = "g", Env = new Dictionary<string, string?> { ["FOO"] = "from-group" } };
        CommandSpec cmd = Printing("${env:FOO}");

        Assert.Equal(new[] { "from-group" }, Run(LaunchSpec.From(new[] { group }, cmd, dir.Path, Array.Empty<EnvLevel>()), 1));
    }

    [Fact]
    public void ArgEnvPlaceholder_SeesTheCommandsOwnOverride()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var group = new GroupSpec { Name = "g", Env = new Dictionary<string, string?> { ["FOO"] = "group" } };
        CommandSpec cmd = Printing("${env:FOO}", "${env:FOO}-x");
        cmd.Env = new Dictionary<string, string?> { ["FOO"] = "own" };

        Assert.Equal(new[] { "own", "own-x" }, Run(LaunchSpec.From(new[] { group }, cmd, dir.Path, Array.Empty<EnvLevel>()), 2));
    }

    [Fact]
    public void ArgPlaceholders_CwdConfigDirAndPathsep_ExpandToTheLaunchValues()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var group = new GroupSpec { Name = "g", Cwd = "sub" };
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        CommandSpec cmd = Printing("${cwd}", "${configDir}", "a${pathsep}b");

        string[] lines = Run(LaunchSpec.From(new[] { group }, cmd, dir.Path, Array.Empty<EnvLevel>()), 3);

        Assert.Equal(
            new[] { Path.GetFullPath(Path.Combine(dir.Path, "sub")), Path.GetFullPath(dir.Path), "a:b" },
            lines);
    }

    [Fact]
    public void ArgEscape_ArrivesAsLiteralPlaceholderText()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CommandSpec cmd = Printing("$${env:FOO}");

        Assert.Equal(new[] { "${env:FOO}" }, Run(LaunchSpec.From(Array.Empty<GroupSpec>(), cmd, dir.Path, Array.Empty<EnvLevel>()), 1));
    }

    [Fact]
    public void InputInGroupEnvValue_ReachesTheProcessEnvironment()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var group = new GroupSpec { Name = "g", Env = new Dictionary<string, string?> { ["V"] = "[${input:x}]" } };
        var cmd = new CommandSpec { Name = "c", Program = "sh", Args = { "-c", "printf '%s\\n' \"$V\"" } };
        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path, Array.Empty<EnvLevel>()) with
        {
            Inputs = new Dictionary<string, string> { ["x"] = "hi" },
        };

        Assert.Equal(new[] { "[hi]" }, Run(spec, 1));
    }

    [Fact]
    public void InputValueThatLooksLikeAPlaceholder_ArrivesLiterally()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var group = new GroupSpec { Name = "g", Env = new Dictionary<string, string?> { ["FOO"] = "from-group" } };
        CommandSpec cmd = Printing("${input:x}", "${env:FOO}");
        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path, Array.Empty<EnvLevel>()) with
        {
            Inputs = new Dictionary<string, string> { ["x"] = "${env:FOO}" },
        };

        Assert.Equal(new[] { "${env:FOO}", "from-group" }, Run(spec, 2));
    }
}
