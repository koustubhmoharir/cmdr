using Cmdr.Core.Config;
using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class CommandFormTests
{
    private static CommandFormValues Valid(
        string name = "hello",
        string program = "sh",
        StopMethod method = StopMethod.CtrlC,
        string stopInput = "",
        string env = "",
        string args = "-c\necho hi",
        StdinMode stdin = StdinMode.Pipe) =>
        new(name, string.Empty, program, args, string.Empty, env, method, stopInput, false, string.Empty, false, stdin);

    [Fact]
    public void Valid_values_have_no_errors()
    {
        Assert.Empty(CommandForm.Validate(Valid(), new[] { "other" }));
    }

    [Fact]
    public void Blank_name_is_an_error()
    {
        IReadOnlyList<string> errors = CommandForm.Validate(Valid(name: "   "), Array.Empty<string>());

        Assert.Single(errors);
        Assert.Contains("Name", errors[0]);
    }

    [Theory]
    [InlineData("Build")]
    [InlineData("build")]
    [InlineData(" BUILD ")]
    public void Name_clashing_with_another_command_in_the_target_group_is_an_error_ignoring_case(string name)
    {
        IReadOnlyList<string> errors = CommandForm.Validate(Valid(name: name), new[] { "build" });

        Assert.Single(errors);
        Assert.Contains("already named", errors[0]);
    }

    [Fact]
    public void Blank_program_is_an_error()
    {
        IReadOnlyList<string> errors = CommandForm.Validate(Valid(program: " "), Array.Empty<string>());

        Assert.Single(errors);
        Assert.Contains("Program", errors[0]);
    }

    [Fact]
    public void Env_errors_are_reported_with_their_line()
    {
        IReadOnlyList<string> errors = CommandForm.Validate(Valid(env: "A=1\nbogus"), Array.Empty<string>());

        Assert.Single(errors);
        Assert.StartsWith("Env: Line 2", errors[0]);
    }

    [Fact]
    public void Input_method_requires_a_non_empty_stop_input()
    {
        IReadOnlyList<string> errors = CommandForm.Validate(Valid(method: StopMethod.Input, stopInput: string.Empty), Array.Empty<string>());

        Assert.Single(errors);
        Assert.Contains("must not be empty", errors[0]);
    }

    [Fact]
    public void Stop_input_escape_errors_matter_only_for_the_input_method()
    {
        IReadOnlyList<string> ctrlC = CommandForm.Validate(Valid(method: StopMethod.CtrlC, stopInput: "\\q"), Array.Empty<string>());
        IReadOnlyList<string> input = CommandForm.Validate(Valid(method: StopMethod.Input, stopInput: "\\q"), Array.Empty<string>());

        Assert.Empty(ctrlC);
        Assert.Single(input);
        Assert.StartsWith("Stop input:", input[0]);
    }

    [Fact]
    public void Build_creates_the_spec_from_the_text()
    {
        var values = new CommandFormValues(
            Name: " tool ",
            Description: "  ",
            Program: " /opt/tool/run ",
            Args: "a\nb c",
            Cwd: " ../web ",
            Env: "FORCE_COLOR=1\n!SECRET",
            Method: StopMethod.Input,
            StopInput: "q\\n",
            LogEnabled: true,
            LogPath: "logs/tool.log",
            DetachLeftovers: true,
            Stdin: StdinMode.Console);

        CommandSpec spec = CommandForm.Build(values);

        Assert.Equal("tool", spec.Name);
        Assert.Null(spec.Description);
        Assert.Equal("/opt/tool/run", spec.Program);
        Assert.Equal(new[] { "a", "b c" }, spec.Args);
        Assert.Equal("../web", spec.Cwd);
        Assert.Equal("1", spec.Env["FORCE_COLOR"]);
        Assert.True(spec.Env.ContainsKey("SECRET"));
        Assert.Null(spec.Env["SECRET"]);
        Assert.Equal(StopMethod.Input, spec.Stop.Method);
        Assert.Equal("q\n", spec.Stop.Input);
        Assert.True(spec.Log.Enabled);
        Assert.Equal("logs/tool.log", spec.Log.Path);
        Assert.Equal(LeftoverMode.Detach, spec.Leftovers);
        Assert.Equal(StdinMode.Console, spec.Stdin);
    }

    [Fact]
    public void Console_stdin_with_the_input_stop_method_is_an_error()
    {
        IReadOnlyList<string> errors = CommandForm.Validate(
            Valid(method: StopMethod.Input, stopInput: "q", stdin: StdinMode.Console),
            Array.Empty<string>());

        Assert.Single(errors);
        Assert.Contains("Pipe", errors[0]);
    }

    [Fact]
    public void Console_stdin_with_ctrl_c_is_valid_and_pipe_is_the_default()
    {
        Assert.Empty(CommandForm.Validate(Valid(stdin: StdinMode.Console), Array.Empty<string>()));
        Assert.Equal(StdinMode.Pipe, CommandForm.Build(Valid()).Stdin);
    }

    [Fact]
    public void Detach_checkbox_maps_to_track_when_unchecked()
    {
        CommandSpec spec = CommandForm.Build(Valid());

        Assert.Equal(LeftoverMode.Track, spec.Leftovers);
    }

    [Fact]
    public void Build_drops_stop_input_for_ctrl_c_and_blank_optional_fields_become_null()
    {
        var values = new CommandFormValues("x", "d", "p", string.Empty, "", "", StopMethod.CtrlC, "leftover", false, " ", false, StdinMode.Pipe);

        CommandSpec spec = CommandForm.Build(values);

        Assert.Empty(spec.Args);
        Assert.Null(spec.Cwd);
        Assert.Null(spec.Stop.Input);
        Assert.Null(spec.Log.Path);
        Assert.Equal("d", spec.Description);
    }

    [Fact]
    public void Group_name_must_not_be_blank_or_clash_ignoring_case()
    {
        Assert.Single(GroupForm.Validate("  ", Array.Empty<string>()));
        Assert.Single(GroupForm.Validate("Build", new[] { "build" }));
        Assert.Empty(GroupForm.Validate("Build", new[] { "servers" }));
    }
}
