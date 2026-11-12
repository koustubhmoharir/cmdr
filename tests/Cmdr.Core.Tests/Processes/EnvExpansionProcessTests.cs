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
                ["BAR"] = "${FOO}-b",
                ["WHERE"] = "${cwd}",
                ["PATH"] = "/cmdr-test-prefix${pathsep}${PATH}",
            },
        };

        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path);
        var p = new ManagedProcess(1000);
        try
        {
            p.Start(spec);
            ProcessTestHelpers.WaitForExited(p);

            string[] stdout = ProcessTestHelpers.Lines(p)
                .Where(l => l.Source == OutputSource.StdOut)
                .Select(l => l.PlainText)
                .ToArray();

            Assert.Equal(new[] { "a-b", Path.GetFullPath(Path.Combine(dir.Path, "sub")), "prefixed" }, stdout);
        }
        finally
        {
            ProcessTestHelpers.Cleanup(p);
        }
    }
}
