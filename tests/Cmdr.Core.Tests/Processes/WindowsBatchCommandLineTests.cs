using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Tests.Processes;

/// <summary>When a copied Windows command line must go through cmd.exe. Pure: the environment and file system are given.</summary>
public class WindowsBatchCommandLineTests
{
    private static readonly Dictionary<string, string?> Environment = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PATH"] = @"C:\bin",
        ["PATHEXT"] = ".COM;.EXE;.BAT;.CMD",
    };

    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase) { @"C:\bin\pnpm.cmd", @"C:\bin\node.exe" };

    private static string? ForCopy(string program, params string[] args) =>
        WindowsBatchCommandLine.ForCopy(
            new LaunchSpec(program, args, @"C:\work", Array.Empty<EnvLevel>(), @"C:\work", StopMethod.CtrlC, null),
            Environment,
            Files.Contains);

    [Theory]
    [InlineData("a&ver")]
    [InlineData("x|y")]
    [InlineData("%TEMP%")]
    [InlineData("say \"hi\"")]
    [InlineData("")]
    [InlineData(@"C:\dir with space\")]
    public void BatchProgramWithAnArgumentPowerShellWouldMangle_UsesTheCommandCmdrRunsThroughCmd(string arg)
    {
        Assert.Equal(
            WindowsCmdEscaping.BuildCommand(@"C:\bin\pnpm.CMD", new[] { "run", arg }),
            ForCopy("pnpm", "run", arg));
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("--port=3000")]
    [InlineData("a,b;c")]
    [InlineData("a b")]
    [InlineData(@"C:\dir\")]
    public void BatchProgramWithPlainArguments_KeepsThePowerShellForm(string arg)
    {
        Assert.Null(ForCopy("pnpm", "run", arg));
    }

    [Fact]
    public void ExecutableWithSpecialArguments_KeepsThePowerShellForm()
    {
        Assert.Null(ForCopy("node", "-e", "a&b|c"));
    }

    [Fact]
    public void ProgramThatDoesNotResolve_KeepsThePowerShellForm()
    {
        Assert.Null(ForCopy("missing", "a&b"));
    }
}
