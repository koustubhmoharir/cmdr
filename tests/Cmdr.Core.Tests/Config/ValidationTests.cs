using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class ValidationTests
{
    private static CmdrConfig ValidConfig() => new()
    {
        Groups =
        {
            new GroupSpec
            {
                Name = "servers",
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
            },
            new GroupSpec
            {
                Name = "build",
                Mode = GroupMode.Sequential,
                Commands = { new CommandSpec { Name = "restore", Program = "dotnet" } },
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

    [Fact]
    public void EmptyGroup_ReturnsNoErrors()
    {
        var config = new CmdrConfig { Groups = { new GroupSpec { Name = "empty" } } };

        Assert.Empty(config.Validate());
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
    public void GroupName_MustNotBeBlank_ReportsIndex(string name)
    {
        var config = ValidConfig();
        config.Groups.Add(new GroupSpec { Name = name });

        Assert.Equal("Group #3: name must not be blank.", Assert.Single(config.Validate()));
    }

    [Fact]
    public void GroupName_BlankGroup_CommandErrorsIdentifyGroupByIndex()
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "", Commands = { new CommandSpec { Name = "a", Program = "" } } } },
        };

        IReadOnlyList<string> errors = config.Validate();

        Assert.Equal(2, errors.Count);
        Assert.Equal("Group #1: name must not be blank.", errors[0]);
        Assert.Equal("Group #1, command 'a': program must not be blank.", errors[1]);
    }

    [Fact]
    public void GroupNames_MustBeUniqueCaseInsensitive()
    {
        var config = ValidConfig();
        config.Groups.Add(new GroupSpec { Name = "SERVERS" });

        Assert.Equal("Duplicate group name 'servers' (groups #1, #3).", Assert.Single(config.Validate()));
    }

    [Fact]
    public void GroupNames_ReportEachDuplicateOnce()
    {
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec { Name = "build" },
                new GroupSpec { Name = "Build" },
                new GroupSpec { Name = "build" },
            },
        };

        string error = Assert.Single(config.Validate());
        Assert.Equal("Duplicate group name 'build' (groups #1, #2, #3).", error);
    }

    [Fact]
    public void GroupNames_BlankNamesAreNotReportedAsDuplicates()
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "" }, new GroupSpec { Name = " " } },
        };

        IReadOnlyList<string> errors = config.Validate();

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Contains("name must not be blank", e));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CommandName_MustNotBeBlank_IdentifiesGroupAndCommandIndex(string name)
    {
        var config = ValidConfig();
        config.Groups[0].Commands.Add(new CommandSpec { Name = name, Program = "p" });

        Assert.Equal("Group 'servers', command #3: name must not be blank.", Assert.Single(config.Validate()));
    }

    [Fact]
    public void CommandNames_MustBeUniqueCaseInsensitive_WithinGroup()
    {
        var config = ValidConfig();
        config.Groups[0].Commands.Add(new CommandSpec { Name = "WEB", Program = "p" });

        Assert.Equal(
            "Group 'servers': duplicate command name 'web' (commands #1, #3).",
            Assert.Single(config.Validate()));
    }

    [Fact]
    public void CommandNames_ReportEachDuplicateOnce()
    {
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "g",
                    Commands =
                    {
                        new CommandSpec { Name = "a", Program = "p" },
                        new CommandSpec { Name = "A", Program = "p" },
                        new CommandSpec { Name = "a", Program = "p" },
                    },
                },
            },
        };

        Assert.Equal(
            "Group 'g': duplicate command name 'a' (commands #1, #2, #3).",
            Assert.Single(config.Validate()));
    }

    [Fact]
    public void CommandName_SameNameInDifferentGroups_IsValid()
    {
        var config = ValidConfig();
        config.Groups[1].Commands.Add(new CommandSpec { Name = "WEB", Program = "p" });
        config.Groups[0].Commands.Add(new CommandSpec { Name = "restore", Program = "p" });

        Assert.Empty(config.Validate());
    }

    [Fact]
    public void CommandName_DuplicateInBlankGroup_IdentifiesGroupByIndex()
    {
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "",
                    Commands = { new CommandSpec { Name = "x", Program = "p" }, new CommandSpec { Name = "x", Program = "p" } },
                },
            },
        };

        IReadOnlyList<string> errors = config.Validate();

        Assert.Equal(2, errors.Count);
        Assert.Equal("Group #1: name must not be blank.", errors[0]);
        Assert.Equal("Group #1: duplicate command name 'x' (commands #1, #2).", errors[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Program_MustNotBeBlank_IdentifiesGroupAndCommand(string program)
    {
        var config = ValidConfig();
        config.Groups[0].Commands[0].Program = program;

        Assert.Equal(
            "Group 'servers', command 'web': program must not be blank.",
            Assert.Single(config.Validate()));
    }

    [Fact]
    public void Program_BlankCommandName_IdentifiesCommandByIndex()
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "g", Commands = { new CommandSpec { Name = "", Program = "" } } } },
        };

        string error = Assert.Single(config.Validate(), e => e.Contains("program"));
        Assert.Equal("Group 'g', command #1: program must not be blank.", error);
    }

    [Fact]
    public void Program_BlankNameAndBlankProgram_BothErrorsIdentifyByIndex()
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "", Commands = { new CommandSpec { Name = "", Program = "" } } } },
        };

        IReadOnlyList<string> errors = config.Validate();

        Assert.Equal(
            new[]
            {
                "Group #1: name must not be blank.",
                "Group #1, command #1: name must not be blank.",
                "Group #1, command #1: program must not be blank.",
            },
            errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void StopInput_RequiredWhenMethodIsInput(string? input)
    {
        var config = ValidConfig();
        config.Groups[0].Commands[0].Stop = new StopSpec { Method = StopMethod.Input, Input = input };

        Assert.Equal(
            "Group 'servers', command 'web': stop.input must not be empty when stop.method is 'input'.",
            Assert.Single(config.Validate()));
    }

    [Fact]
    public void StopInput_NotRequiredForCtrlC()
    {
        var config = ValidConfig();
        config.Groups[0].Commands[0].Stop = new StopSpec { Method = StopMethod.CtrlC, Input = null };

        Assert.Empty(config.Validate());
    }
}
