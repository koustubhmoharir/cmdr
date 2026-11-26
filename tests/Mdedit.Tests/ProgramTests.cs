using Mdedit;

namespace Mdedit.Tests;

public sealed class ProgramTests
{
    private const string Usage =
        "usage: mdedit <file>\n" +
        "       mdedit               pick a recently used file\n" +
        "       mdedit --version\n";

    [Fact]
    public void Version_prints_the_informational_version()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run(["--version"], stdout, stderr);

        Assert.Equal(0, code);
        Assert.StartsWith("mdedit ", stdout.ToString());
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
        Assert.Equal(Usage, stderr.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_file_argument_prints_usage_to_stderr_and_returns_2()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run(["README.md"], stdout, stderr);

        Assert.Equal(2, code);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal(Usage, stderr.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void Unknown_option_prints_usage_to_stderr_and_returns_2()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run(["--bogus"], stdout, stderr);

        Assert.Equal(2, code);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal(Usage, stderr.ToString().Replace("\r\n", "\n"));
    }
}
