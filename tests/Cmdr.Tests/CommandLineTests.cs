using Cmdr;
using Cmdr.Core.Session;

namespace Cmdr.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void No_arguments_show_the_picker()
    {
        Assert.Equal(new CommandLineRequest(CommandLineAction.Picker, null, SessionMode.Run), Parse());
    }

    [Fact]
    public void A_path_opens_it_in_run_mode()
    {
        Assert.Equal(new CommandLineRequest(CommandLineAction.Open, "cmdr.json", SessionMode.Run), Parse("cmdr.json"));
    }

    [Fact]
    public void Edit_before_the_path_opens_it_in_edit_mode()
    {
        Assert.Equal(new CommandLineRequest(CommandLineAction.Open, "cmdr.json", SessionMode.Edit), Parse("--edit", "cmdr.json"));
    }

    [Fact]
    public void Edit_after_the_path_opens_it_in_edit_mode()
    {
        Assert.Equal(new CommandLineRequest(CommandLineAction.Open, "cmdr.json", SessionMode.Edit), Parse("cmdr.json", "--edit"));
    }

    [Fact]
    public void Version_is_its_own_action()
    {
        Assert.Equal(new CommandLineRequest(CommandLineAction.Version, null, SessionMode.Run), Parse("--version"));
    }

    [Theory]
    [InlineData("--edit")]
    [InlineData("--version", "cmdr.json")]
    [InlineData("cmdr.json", "--version")]
    [InlineData("--edit", "--version")]
    [InlineData("a.json", "b.json")]
    [InlineData("--edit", "a.json", "b.json")]
    [InlineData("a.json", "b.json", "--edit")]
    [InlineData("--edit", "--edit", "a.json")]
    [InlineData("--edit", "--edit")]
    [InlineData("--bogus")]
    [InlineData("--bogus", "cmdr.json")]
    [InlineData("cmdr.json", "--bogus")]
    [InlineData("--edit", "--bogus")]
    [InlineData("")]
    [InlineData("--edit", "")]
    [InlineData("", "--edit")]
    [InlineData("-x.json")]
    public void Other_combinations_are_usage_errors(params string[] args)
    {
        Assert.Equal(new CommandLineRequest(CommandLineAction.Usage, null, SessionMode.Run), Parse(args));
    }

    private static CommandLineRequest Parse(params string[] args) => CommandLine.Parse(args);
}
