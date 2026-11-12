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
                Name = "build",
                Items = { new CommandSpec { Name = "restore", Program = "dotnet" } },
            },
            new GroupSpec
            {
                Name = "servers",
                Items =
                {
                    new CommandSpec { Name = "web", Program = "pnpm", Stop = new StopSpec { Method = StopMethod.Input, Input = "q\n" } },
                    new CommandSpec { Name = "tool", Program = "/opt/tool/run" },
                },
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
        var config = new CmdrConfig { Groups = { new GroupSpec { Name = "a" }, new GroupSpec { Name = "b" }, new GroupSpec { Name = name } } };

        Assert.Equal("Group #3: name must not be blank.", Assert.Single(config.Validate()));
    }

    [Fact]
    public void GroupName_BlankGroup_CommandErrorsIdentifyGroupByIndex()
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "", Items = { new CommandSpec { Name = "a", Program = "" } } } },
        };

        List<string> errors = config.Validate().ToList();

        Assert.Equal(2, errors.Count);
        Assert.Equal("Group #1: name must not be blank.", errors[0]);
        Assert.Equal("Group #1 › command 'a': program must not be blank.", errors[1]);
    }

    [Fact]
    public void GroupNames_MustBeUniqueCaseInsensitive()
    {
        var config = new CmdrConfig { Groups = { new GroupSpec { Name = "servers" }, new GroupSpec { Name = "build" }, new GroupSpec { Name = "SERVERS" } } };

        Assert.Equal("Duplicate group name 'servers' (groups #1, #3).", Assert.Single(config.Validate()));
    }

    [Fact]
    public void GroupNames_ReportEachDuplicateOnce()
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "build" }, new GroupSpec { Name = "Build" }, new GroupSpec { Name = "build" } },
        };

        Assert.Equal("Duplicate group name 'build' (groups #1, #2, #3).", Assert.Single(config.Validate()));
    }

    [Fact]
    public void GroupNames_BlankNamesAreNotReportedAsDuplicates()
    {
        var config = new CmdrConfig { Groups = { new GroupSpec { Name = "" }, new GroupSpec { Name = " " } } };

        List<string> errors = config.Validate().ToList();

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Contains("name must not be blank", e));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CommandName_MustNotBeBlank_IdentifiesGroupAndCommandIndex(string name)
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "servers", Items = { new CommandSpec { Name = "a", Program = "p" }, new CommandSpec { Name = "b", Program = "p" }, new CommandSpec { Name = name, Program = "p" } } } },
        };

        Assert.Equal("Group 'servers' › command #3: name must not be blank.", Assert.Single(config.Validate()));
    }

    [Fact]
    public void CommandNames_MustBeUniqueCaseInsensitive_WithinGroup()
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "g", Items = { new CommandSpec { Name = "web", Program = "p" }, new CommandSpec { Name = "api", Program = "p" }, new CommandSpec { Name = "WEB", Program = "p" } } } },
        };

        Assert.Equal("Group 'g': duplicate name 'web' (items #1, #3).", Assert.Single(config.Validate()));
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
                    Items =
                    {
                        new CommandSpec { Name = "x", Program = "p" },
                        new CommandSpec { Name = "X", Program = "p" },
                        new CommandSpec { Name = "x", Program = "p" },
                    },
                },
            },
        };

        Assert.Equal("Group 'g': duplicate name 'x' (items #1, #2, #3).", Assert.Single(config.Validate()));
    }

    [Fact]
    public void CommandName_SameNameInDifferentGroups_IsValid()
    {
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec { Name = "build", Items = { new CommandSpec { Name = "web", Program = "p" } } },
                new GroupSpec { Name = "servers", Items = { new CommandSpec { Name = "web", Program = "p" } } },
            },
        };

        Assert.Empty(config.Validate());
    }

    [Fact]
    public void CommandName_DuplicateInBlankGroup_IdentifiesGroupByIndex()
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "", Items = { new CommandSpec { Name = "x", Program = "p" }, new CommandSpec { Name = "x", Program = "p" } } } },
        };

        List<string> errors = config.Validate().ToList();

        Assert.Equal(2, errors.Count);
        Assert.Equal("Group #1: name must not be blank.", errors[0]);
        Assert.Equal("Group #1: duplicate name 'x' (items #1, #2).", errors[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Program_MustNotBeBlank_IdentifiesGroupAndCommand(string program)
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "g", Items = { new CommandSpec { Name = "web", Program = program } } } },
        };

        Assert.Equal("Group 'g' › command 'web': program must not be blank.", Assert.Single(config.Validate()));
    }

    [Fact]
    public void Program_BlankCommandName_IdentifiesCommandByIndex()
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "g", Items = { new CommandSpec { Name = "", Program = "" } } } },
        };

        string error = Assert.Single(config.Validate(), e => e.Contains("program"));
        Assert.Equal("Group 'g' › command #1: program must not be blank.", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void StopInput_RequiredWhenMethodIsInput(string? input)
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "g", Items = { new CommandSpec { Name = "a", Program = "p", Stop = new StopSpec { Method = StopMethod.Input, Input = input } } } } },
        };

        Assert.Equal(
            "Group 'g' › command 'a': stop.input must not be empty when stop.method is 'input'.",
            Assert.Single(config.Validate()));
    }

    [Fact]
    public void StopInput_NotRequiredForCtrlC()
    {
        var config = new CmdrConfig
        {
            Groups = { new GroupSpec { Name = "g", Items = { new CommandSpec { Name = "a", Program = "p", Stop = new StopSpec { Method = StopMethod.CtrlC, Input = null } } } } },
        };

        Assert.Empty(config.Validate());
    }

    [Fact]
    public void NestedGroup_ErrorsIdentifyFullPath()
    {
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "go-apps",
                    Items =
                    {
                        new GroupSpec
                        {
                            Name = "nav",
                            Items = { new CommandSpec { Name = "run", Program = " " } },
                        },
                    },
                },
            },
        };

        Assert.Equal("Group 'go-apps' › 'nav' › command 'run': program must not be blank.", Assert.Single(config.Validate()));
    }

    [Fact]
    public void NestedGroup_BlankNameAndBlankCommandName_IdentifiedByPosition()
    {
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "go-apps",
                    Items =
                    {
                        new CommandSpec { Name = "ok", Program = "p" },
                        new GroupSpec { Name = "", Items = { new CommandSpec { Name = "", Program = "p" } } },
                    },
                },
            },
        };

        List<string> errors = config.Validate().ToList();

        Assert.Equal(2, errors.Count);
        Assert.Equal("Group 'go-apps' › #2: name must not be blank.", errors[0]);
        Assert.Equal("Group 'go-apps' › #2 › command #1: name must not be blank.", errors[1]);
    }

    [Fact]
    public void Sibling_CommandAndGroupWithSameName_IsDuplicate()
    {
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "go-apps",
                    Items =
                    {
                        new CommandSpec { Name = "nav", Program = "p" },
                        new GroupSpec { Name = "Nav", Items = { new CommandSpec { Name = "run", Program = "p" } } },
                        new CommandSpec { Name = "other", Program = "p" },
                        new CommandSpec { Name = "NAV", Program = "p" },
                    },
                },
            },
        };

        Assert.Equal("Group 'go-apps': duplicate name 'nav' (items #1, #2, #4).", Assert.Single(config.Validate()));
    }

    [Fact]
    public void Sibling_GroupAndCommandWithSameName_InDifferentBranches_IsValid()
    {
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "a",
                    Items =
                    {
                        new GroupSpec { Name = "shared", Items = { new CommandSpec { Name = "run", Program = "p" } } },
                    },
                },
                new GroupSpec
                {
                    Name = "b",
                    Items =
                    {
                        new CommandSpec { Name = "shared", Program = "p" },
                        new GroupSpec { Name = "run", Items = { new CommandSpec { Name = "run", Program = "p" } } },
                    },
                },
            },
        };

        Assert.Empty(config.Validate());
    }

    [Fact]
    public void NestedGroup_InvalidStopInput_IsReportedWithPath()
    {
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "outer",
                    Items =
                    {
                        new GroupSpec
                        {
                            Name = "inner",
                            Items = { new CommandSpec { Name = "tool", Program = "p", Stop = new StopSpec { Method = StopMethod.Input, Input = "" } } },
                        },
                    },
                },
            },
        };

        Assert.Equal(
            "Group 'outer' › 'inner' › command 'tool': stop.input must not be empty when stop.method is 'input'.",
            Assert.Single(config.Validate()));
    }

    [Fact]
    public void MalformedInputPlaceholder_IsReportedWithCommandLabel()
    {
        var config = new CmdrConfig
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "g",
                    Items = { new CommandSpec { Name = "x", Program = "p", Args = new List<string> { "ok", "${input:a b}" } } },
                },
            },
        };

        string error = Assert.Single(config.Validate());

        Assert.Equal("Group 'g' › command 'x': args[1]: malformed input placeholder '${input:a b}'", error);
    }

    [Fact]
    public void WellFormedInputPlaceholders_AndEscapes_AreValid()
    {
        var config = ValidConfig();
        ((CommandSpec)config.Groups[1].Items[0]).Args = new List<string> { "${input:x}", "--n=${input:n?}", "$${input:literal}" };

        Assert.Empty(config.Validate());
    }
}
