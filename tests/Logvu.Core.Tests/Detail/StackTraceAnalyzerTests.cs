using Logvu.Core.Config;
using Logvu.Core.Detail;

namespace Logvu.Core.Tests.Detail;

/// <summary>The .NET side of the stack trace analyzer: exception headers, frame variants, markers and framework frames.</summary>
public sealed class StackTraceAnalyzerDotNetTests
{
    private static readonly string[] Prefixes = ["System.", "Microsoft."];

    [Fact]
    public void Frame_with_file_and_line_styles_method_directory_and_location()
    {
        StackLine line = Analyze(@"   at SheetKraft.Engine.Runner.Execute(Int32 id) in C:\src\Engine\Runner.cs:line 88").Single();

        Assert.Equal(StackLineKind.Frame, line.Kind);
        Assert.False(line.IsFramework);
        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Muted, "at"),
            (SpanStyle.Method, "Execute"),
            (SpanStyle.Punctuation, "("),
            (SpanStyle.Punctuation, ")"),
            (SpanStyle.Muted, " in "),
            (SpanStyle.Directory, @"C:\src\Engine\"),
            (SpanStyle.FileLocation, "Runner.cs:line 88"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Frame_without_file_has_only_method_and_parentheses()
    {
        StackLine line = Analyze("   at Ns.Type.Method(args)").Single();

        Assert.Equal(StackLineKind.Frame, line.Kind);
        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Muted, "at"),
            (SpanStyle.Method, "Method"),
            (SpanStyle.Punctuation, "("),
            (SpanStyle.Punctuation, ")"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Async_state_machine_frame_keeps_its_angle_bracket_names()
    {
        StackLine line = Analyze("   at Ns.Type.<Method>d__5.MoveNext()").Single();

        Assert.Equal(StackLineKind.Frame, line.Kind);
        Assert.Contains((SpanStyle.Method, "MoveNext"), DetailRig.SpansOf(line));
    }

    [Fact]
    public void Generic_frame_keeps_its_type_parameters_in_the_method_name()
    {
        StackLine line = Analyze("   at MyApp.Repo`1.Get[T](Int32 id)").Single();

        Assert.Equal(StackLineKind.Frame, line.Kind);
        Assert.Contains((SpanStyle.Method, "Get[T]"), DetailRig.SpansOf(line));
    }

    [Fact]
    public void Frame_method_is_the_last_segment_of_a_dotted_name_with_file_and_line()
    {
        StackLine line = Analyze(@"   at MyApp.Services.OrderService.Process(Int32 id) in C:\src\OrderService.cs:line 42").Single();

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Muted, "at"),
            (SpanStyle.Method, "Process"),
            (SpanStyle.Punctuation, "("),
            (SpanStyle.Punctuation, ")"),
            (SpanStyle.Muted, " in "),
            (SpanStyle.Directory, @"C:\src\"),
            (SpanStyle.FileLocation, "OrderService.cs:line 42"),
        ], DetailRig.SpansOf(line));
    }

    [Theory]
    [InlineData("   at MyApp.Widget..ctor()", ".ctor")]
    [InlineData("   at MyApp.Widget..cctor()", ".cctor")]
    public void Constructor_names_keep_the_dot_before_the_name(string text, string method)
    {
        StackLine line = Analyze(text).Single();

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Muted, "at"),
            (SpanStyle.Method, method),
            (SpanStyle.Punctuation, "("),
            (SpanStyle.Punctuation, ")"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Compiler_generated_name_in_angle_brackets_is_one_method_segment()
    {
        StackLine line = Analyze("   at MyApp.Program.<>c.<Main>b__0_0(String s)").Single();

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Muted, "at"),
            (SpanStyle.Method, "<Main>b__0_0"),
            (SpanStyle.Punctuation, "("),
            (SpanStyle.Punctuation, ")"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Dots_inside_brackets_do_not_split_the_name()
    {
        StackLine line = Analyze("   at MyApp.Repo.Load[System.Int32](Int32 id)").Single();

        Assert.Contains((SpanStyle.Method, "Load[System.Int32]"), DetailRig.SpansOf(line));
    }

    [Fact]
    public void Forward_slash_path_is_split_into_directory_and_location()
    {
        StackLine line = Analyze("   at A.B.C() in /home/dev/src/x.cs:line 10").Single();

        Assert.Contains((SpanStyle.Directory, "/home/dev/src/"), DetailRig.SpansOf(line));
        Assert.Contains((SpanStyle.FileLocation, "x.cs:line 10"), DetailRig.SpansOf(line));
    }

    [Fact]
    public void System_and_Microsoft_frames_are_framework_frames_styled_as_one_span()
    {
        StackLine system = Analyze("   at System.Threading.Tasks.Task.Execute()").Single();
        StackLine microsoft = Analyze("   at Microsoft.AspNetCore.Routing.RouteBase.Run(Int32 x) in /src/Host/x.cs:line 3").Single();

        foreach (StackLine line in new[] { system, microsoft })
        {
            Assert.Equal(StackLineKind.Frame, line.Kind);
            Assert.True(line.IsFramework);
            Assert.Equal<(SpanStyle, string)>([(SpanStyle.Framework, line.Text)], DetailRig.SpansOf(line));
        }
    }

    [Fact]
    public void Application_frame_is_not_a_framework_frame_under_the_same_prefixes()
    {
        StackLine line = Analyze("   at SheetKraft.Engine.Runner.Execute(Int32 id)").Single();

        Assert.False(line.IsFramework);
    }

    [Fact]
    public void Exception_header_styles_type_separator_and_message()
    {
        StackLine line = Analyze(["System.InvalidOperationException: Sheet not found", "   at A.B.C()"])[0];

        Assert.Equal(StackLineKind.ExceptionHeader, line.Kind);
        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.ExceptionType, "System.InvalidOperationException"),
            (SpanStyle.Punctuation, ": "),
            (SpanStyle.ExceptionMessage, "Sheet not found"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Inner_exception_header_after_arrow_and_its_end_marker_are_recognized()
    {
        IReadOnlyList<StackLine> lines = Analyze(
        [
            "System.InvalidOperationException: Sheet not found",
            "   at SheetKraft.Engine.Runner.Execute(Int32 id) in C:\\src\\Engine\\Runner.cs:line 88",
            " ---> System.IO.IOException: locked",
            "   --- End of inner exception stack trace ---",
        ]);

        Assert.Equal(StackLineKind.ExceptionHeader, lines[2].Kind);
        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Muted, " ---> "),
            (SpanStyle.ExceptionType, "System.IO.IOException"),
            (SpanStyle.Punctuation, ": "),
            (SpanStyle.ExceptionMessage, "locked"),
        ], DetailRig.SpansOf(lines[2]));
        Assert.Equal(StackLineKind.EndOfInner, lines[3].Kind);
        Assert.Equal<(SpanStyle, string)>([(SpanStyle.Muted, lines[3].Text)], DetailRig.SpansOf(lines[3]));
    }

    [Fact]
    public void End_of_stack_trace_from_previous_location_is_its_own_marker()
    {
        StackLine line = Analyze(["   --- End of stack trace from previous location ---"]).Single();

        Assert.Equal(StackLineKind.EndOfStack, line.Kind);
    }

    [Fact]
    public void Dotted_header_without_exception_suffix_counts_only_when_a_frame_follows()
    {
        IReadOnlyList<StackLine> followed = Analyze(["SheetKraft.Engine.Retry: attempts exhausted", "   at A.B.C()"]);
        IReadOnlyList<StackLine> alone = Analyze(["SheetKraft.Engine.Retry: attempts exhausted", "continued text"]);

        Assert.Equal(StackLineKind.ExceptionHeader, followed[0].Kind);
        Assert.Equal(
            [(SpanStyle.ExceptionType, "SheetKraft.Engine.Retry"), (SpanStyle.Punctuation, ": "), (SpanStyle.ExceptionMessage, "attempts exhausted")],
            DetailRig.SpansOf(followed[0]));
        Assert.Equal(StackLineKind.Other, alone[0].Kind);
    }

    [Fact]
    public void Ordinary_message_with_a_colon_is_not_a_type_even_before_a_frame()
    {
        IReadOnlyList<StackLine> lines = Analyze(["Note: retry later", "   at A.B.C()"]);

        Assert.Equal(StackLineKind.Other, lines[0].Kind);
        Assert.Empty(lines[0].Spans);
    }

    [Fact]
    public void Type_only_exception_line_before_frames_is_a_header_without_message()
    {
        StackLine line = Analyze(["System.NullReferenceException", "   at A.B.C()"])[0];

        Assert.Equal(StackLineKind.ExceptionHeader, line.Kind);
        Assert.Equal<(SpanStyle, string)>([(SpanStyle.ExceptionType, "System.NullReferenceException")], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Every_input_line_yields_one_result_in_order()
    {
        string[] input = ["Run failed", "System.Exception: x", "   at A.B.C()", "", "--- End of inner exception stack trace ---"];

        IReadOnlyList<StackLine> lines = Analyze(input);

        Assert.Equal(input.Length, lines.Count);
        Assert.Equal(input, lines.Select(l => l.Text));
    }

    private static IReadOnlyList<StackLine> Analyze(params string[] lines) =>
        StackTraceAnalyzer.Analyze(lines, Prefixes);
}

/// <summary>The Go side of the stack trace analyzer: <c>function (file:line)</c> frames from a structured error's stack array.</summary>
public sealed class StackTraceAnalyzerGoTests
{
    private static readonly IReadOnlyList<string> Prefixes = LogvuConfig.DefaultFrameworkPrefixes;

    [Fact]
    public void Application_frame_styles_function_directory_and_file_line()
    {
        StackLine line = Analyze("main.handler (/src/app/main.go:42)").Single();

        Assert.Equal(StackLineKind.Frame, line.Kind);
        Assert.False(line.IsFramework);
        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Method, "handler"),
            (SpanStyle.Punctuation, "("),
            (SpanStyle.Directory, "/src/app/"),
            (SpanStyle.FileLocation, "main.go:42"),
            (SpanStyle.Punctuation, ")"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Standard_library_frame_is_a_framework_frame_by_package_prefix()
    {
        StackLine line = Analyze("net/http.HandlerFunc.ServeHTTP (net/http/server.go:2220)").Single();

        Assert.Equal(StackLineKind.Frame, line.Kind);
        Assert.True(line.IsFramework);
        Assert.Equal<(SpanStyle, string)>([(SpanStyle.Framework, line.Text)], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Pointer_receiver_frame_keeps_its_parenthesized_receiver_in_the_function()
    {
        StackLine line = Analyze("pkg.(*T).Method (file.go:10)").Single();

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Method, "Method"),
            (SpanStyle.Punctuation, "("),
            (SpanStyle.FileLocation, "file.go:10"),
            (SpanStyle.Punctuation, ")"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Module_path_frame_is_a_framework_frame_by_its_module_prefix()
    {
        StackLine line = Analyze("github.com/labstack/echo/v4.(*Echo).ServeHTTP (github.com/labstack/echo/v4@v4.11.4/echo.go:663)").Single();

        Assert.True(line.IsFramework);
        Assert.Equal(StackLineKind.Frame, line.Kind);
    }

    [Fact]
    public void Package_path_is_dimmed_and_the_method_is_the_last_segment_of_a_pointer_receiver()
    {
        StackLine line = Analyze("CodeKraft/golang/ckiecho/internal.(*CodeKraftClient).requestToken (ckclient.go:607)").Single();

        Assert.Equal(StackLineKind.Frame, line.Kind);
        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Directory, "CodeKraft/golang/ckiecho/"),
            (SpanStyle.Method, "requestToken"),
            (SpanStyle.Punctuation, "("),
            (SpanStyle.FileLocation, "ckclient.go:607"),
            (SpanStyle.Punctuation, ")"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Generic_method_keeps_its_brackets_and_the_package_path_stops_before_them()
    {
        StackLine line = Analyze("CodeKraft/golang/ckiecho/utils.getJsonResponse[...] (http.go:155)").Single();

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Directory, "CodeKraft/golang/ckiecho/"),
            (SpanStyle.Method, "getJsonResponse[...]"),
            (SpanStyle.Punctuation, "("),
            (SpanStyle.FileLocation, "http.go:155"),
            (SpanStyle.Punctuation, ")"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Closure_name_has_its_package_directory_and_its_last_segment_as_the_method()
    {
        StackLine line = Analyze("CodeKraft/golang/ckiecho.(*CodeKraftIntegration).BeforeServe.func1 (cki.go:558)").Single();

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Directory, "CodeKraft/golang/"),
            (SpanStyle.Method, "func1"),
            (SpanStyle.Punctuation, "("),
            (SpanStyle.FileLocation, "cki.go:558"),
            (SpanStyle.Punctuation, ")"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Main_package_function_has_no_directory_and_its_name_is_the_method()
    {
        StackLine line = Analyze("main.main (main.go:10)").Single();

        Assert.Equal<(SpanStyle, string)>(
        [
            (SpanStyle.Method, "main"),
            (SpanStyle.Punctuation, "("),
            (SpanStyle.FileLocation, "main.go:10"),
            (SpanStyle.Punctuation, ")"),
        ], DetailRig.SpansOf(line));
    }

    [Fact]
    public void Line_that_is_not_a_frame_is_other()
    {
        StackLine line = Analyze("...additional frames elided...").Single();

        Assert.Equal(StackLineKind.Other, line.Kind);
        Assert.Empty(line.Spans);
    }

    private static IReadOnlyList<StackLine> Analyze(params string[] frames) =>
        StackTraceAnalyzer.AnalyzeGoStack(frames, Prefixes);
}
