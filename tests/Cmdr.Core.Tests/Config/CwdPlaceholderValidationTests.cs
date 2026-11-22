using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

/// <summary>The placeholders a cwd may use (<c>${env:NAME}</c>, <c>${configDir}</c>, <c>${pathsep}</c>, <c>$${</c>) and the ones it may not.</summary>
public class CwdPlaceholderValidationTests
{
    private static CommandSpec Cmd(string cwd) => new() { Name = "cmd", Program = "echo", Cwd = cwd };

    private static GroupSpec Group(string cwd, params ItemSpec[] items) => new() { Name = "g", Cwd = cwd, Items = items.ToList() };

    private static EnvScriptSpec Script(string cwd) => new() { Program = "sh", Cwd = cwd };

    private static List<string> CwdErrors(CmdrConfig config) =>
        config.Validate().Where(error => error.Contains(": cwd:", StringComparison.Ordinal) || error.Contains(": cwd ", StringComparison.Ordinal)).ToList();

    [Theory]
    [InlineData("${env:HOME}/bin")]
    [InlineData("${configDir}/sub")]
    [InlineData("a${pathsep}b")]
    [InlineData("$${literal}")]
    [InlineData("plain/relative")]
    public void AllowedPlaceholders_AreNotReported(string cwd)
    {
        var config = new CmdrConfig
        {
            EnvScript = Script(cwd),
            Groups = new List<GroupSpec> { Group(cwd, Cmd(cwd)) },
        };

        Assert.Empty(CwdErrors(config));
    }

    [Fact]
    public void CwdPlaceholder_IsReportedInGroupCommandAndEnvScript()
    {
        var config = new CmdrConfig
        {
            EnvScript = Script("${cwd}"),
            Groups = new List<GroupSpec> { Group("${cwd}/x", Cmd("${cwd}")) },
        };

        List<string> errors = CwdErrors(config);

        Assert.Equal(3, errors.Count(error => error.Contains("cwd: ${cwd} is not allowed in cwd", StringComparison.Ordinal)));
        Assert.Contains(errors, error => error.StartsWith("envScript: cwd: ${cwd} is not allowed", StringComparison.Ordinal));
    }

    [Fact]
    public void InputPlaceholder_IsReportedInGroupCommandAndEnvScript()
    {
        var config = new CmdrConfig
        {
            EnvScript = Script("${input:x}"),
            Groups = new List<GroupSpec> { Group("${input:y?}", Cmd("a/${input:x}")) },
        };

        List<string> errors = CwdErrors(config);

        Assert.Contains(errors, error => error.Contains("cwd: ${input:x} is not allowed in cwd", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("cwd: ${input:y} is not allowed in cwd", StringComparison.Ordinal));
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void MalformedPlaceholder_IsReportedInCwd()
    {
        var config = new CmdrConfig { Groups = new List<GroupSpec> { Group("${env:}", Cmd("x")) } };

        Assert.Contains(CwdErrors(config), error => error.Contains("cwd: malformed placeholder '${env:}'", StringComparison.Ordinal));
    }
}
