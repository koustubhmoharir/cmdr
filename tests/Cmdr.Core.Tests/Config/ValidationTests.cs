using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class ValidationTests
{
    private static CmdrConfig ValidConfig() => new()
    {
        Commands =
        {
            new CommandSpec { Name = "web", Program = "pnpm" },
            new CommandSpec
            {
                Name = "tool",
                Program = "/opt/tool/run",
                Stop = new StopSpec { Method = StopMethod.Input, Input = "q\n" },
            },
        },
    };

    [Fact]
    public void ValidConfig_ReturnsNoErrors()
    {
        Assert.Empty(ValidConfig().Validate());
    }

    [Fact]
    public void EmptyConfig_ReturnsNoErrors()
    {
        Assert.Empty(new CmdrConfig().Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void BufferLines_MustBePositive(int bufferLines)
    {
        var config = ValidConfig();
        config.BufferLines = bufferLines;

        string error = Assert.Single(config.Validate());
        Assert.Contains("bufferLines", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_MustNotBeBlank_ReportsIndex(string name)
    {
        var config = ValidConfig();
        config.Commands.Add(new CommandSpec { Name = name, Program = "p" });

        string error = Assert.Single(config.Validate());
        Assert.Contains("#3", error);
    }

    [Fact]
    public void Names_MustBeUniqueCaseInsensitive()
    {
        var config = ValidConfig();
        config.Commands.Add(new CommandSpec { Name = "WEB", Program = "p" });

        string error = Assert.Single(config.Validate());
        Assert.Contains("Duplicate", error);
        Assert.Contains("web", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1", error);
        Assert.Contains("3", error);
    }

    [Fact]
    public void Names_ReportEachDuplicateOnce()
    {
        var config = new CmdrConfig
        {
            Commands =
            {
                new CommandSpec { Name = "a", Program = "p" },
                new CommandSpec { Name = "A", Program = "p" },
                new CommandSpec { Name = "a", Program = "p" },
            },
        };

        string error = Assert.Single(config.Validate());
        Assert.Contains("Duplicate", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Program_MustNotBeBlank_IdentifiesCommandByName(string program)
    {
        var config = ValidConfig();
        config.Commands[0].Program = program;

        string error = Assert.Single(config.Validate());
        Assert.Contains("'web'", error);
        Assert.Contains("program", error);
    }

    [Fact]
    public void Program_BlankName_IdentifiesCommandByIndex()
    {
        var config = new CmdrConfig
        {
            Commands = { new CommandSpec { Name = "", Program = "p" } },
        };

        string error = Assert.Single(config.Validate());
        Assert.Contains("#1", error);
    }

    [Fact]
    public void Program_BlankName_BlankProgram_BothErrorsIdentifyByIndex()
    {
        var config = new CmdrConfig
        {
            Commands = { new CommandSpec { Name = "", Program = "" } },
        };

        IReadOnlyList<string> errors = config.Validate();

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Contains("#1", e));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void StopInput_RequiredWhenMethodIsInput(string? input)
    {
        var config = ValidConfig();
        config.Commands[0].Stop = new StopSpec { Method = StopMethod.Input, Input = input };

        string error = Assert.Single(config.Validate());
        Assert.Contains("stop.input", error);
        Assert.Contains("'web'", error);
    }

    [Fact]
    public void StopInput_NotRequiredForCtrlC()
    {
        var config = ValidConfig();
        config.Commands[0].Stop = new StopSpec { Method = StopMethod.CtrlC, Input = null };

        Assert.Empty(config.Validate());
    }
}
