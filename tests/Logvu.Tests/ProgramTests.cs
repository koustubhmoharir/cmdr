using Logvu;

namespace Logvu.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void Unknown_arguments_print_usage_to_stderr_and_return_2()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run(["--bogus"], stdout, stderr);

        Assert.Equal(2, code);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.StartsWith("usage: logvu <config.json>", stderr.ToString());
    }

    [Fact]
    public void Version_prints_the_informational_version()
    {
        var stdout = new StringWriter();

        int code = Program.Run(["--version"], stdout, new StringWriter());

        Assert.Equal(0, code);
        Assert.StartsWith("logvu ", stdout.ToString());
    }
}
