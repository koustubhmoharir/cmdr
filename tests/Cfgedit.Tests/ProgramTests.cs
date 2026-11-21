using Cfgedit;

namespace Cfgedit.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void Version_prints_the_informational_version()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run(["--version"], stdout, stderr);

        Assert.Equal(0, code);
        Assert.StartsWith("cfgedit ", stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void No_arguments_print_usage_to_stderr_and_return_2()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run([], stdout, stderr);

        Assert.Equal(2, code);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.StartsWith("usage: cfgedit <file>", stderr.ToString());
    }

    [Fact]
    public void Unknown_option_prints_usage_to_stderr_and_returns_2()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run(["--bogus"], stdout, stderr);

        Assert.Equal(2, code);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.StartsWith("usage: cfgedit <file>", stderr.ToString());
    }

    [Fact]
    public void Empty_string_argument_prints_usage_to_stderr_and_returns_2()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run([""], stdout, stderr);

        Assert.Equal(2, code);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.StartsWith("usage: cfgedit <file>", stderr.ToString());
    }
}
