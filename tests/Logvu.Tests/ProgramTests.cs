using Logvu;

namespace Logvu.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void No_arguments_print_usage_and_return_2()
    {
        var stderr = new StringWriter();

        int code = Program.Run([], new StringWriter(), stderr);

        Assert.Equal(2, code);
        Assert.StartsWith("usage: logvu <config.json>", stderr.ToString());
    }

    [Fact]
    public void A_config_that_does_not_exist_is_reported_on_stderr_and_returns_1()
    {
        string path = Path.Combine(Path.GetTempPath(), $"logvu-missing-{Guid.NewGuid():N}.json");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.Run([path], stdout, stderr);

        Assert.Equal(1, code);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.StartsWith("logvu: ", stderr.ToString());
        Assert.Contains(Path.GetFileName(path), stderr.ToString());
    }

    [Fact]
    public void A_config_that_is_not_valid_json_is_reported_on_stderr_and_returns_1()
    {
        string path = Path.Combine(Path.GetTempPath(), $"logvu-invalid-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var stderr = new StringWriter();

            int code = Program.Run([path], new StringWriter(), stderr);

            Assert.Equal(1, code);
            Assert.StartsWith("logvu: ", stderr.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

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
