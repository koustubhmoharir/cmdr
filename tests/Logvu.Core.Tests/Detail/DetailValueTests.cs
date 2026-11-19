using Logvu.Core.Detail;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Logvu.Core.Store;

namespace Logvu.Core.Tests.Detail;

/// <summary>
/// <see cref="DetailLine.Value"/>: the exact value of field and JSON scalar lines, in the form a field predicate compares, so that
/// "filter to this value" built from any such line matches the event it came from.
/// </summary>
public sealed class DetailValueTests
{
    private const string Serilog =
        "{\"Timestamp\":\"2026-11-18T10:20:30.1234567+05:30\",\"Level\":\"Error\",\"MessageTemplate\":\"Order {OrderId} failed\","
        + "\"Exception\":\"System.InvalidOperationException: boom\\r\\n   at A.B.C() in C:\\\\src\\\\x.cs:line 10\","
        + "\"Properties\":{\"OrderId\":42,\"Paid\":false,\"Note\":null,\"SourceContext\":\"SampleDotNetApp.Orders\",\"Scope\":[\"Request 7\"]}}";

    private const string Slog =
        "{\"time\":\"2026-11-18T10:20:30.5+05:30\",\"level\":\"ERROR\",\"msg\":\"request failed\",\"logger\":\"api\","
        + "\"err\":{\"message\":\"db down\",\"attrs\":{\"table\":\"orders\"},\"cause\":[\"dial tcp: refused\",{\"message\":\"inner\",\"attrs\":{\"attempt\":2}}],"
        + "\"stack\":[\"main.handler (/src/app/main.go:42)\"]}}";

    private const string Log4Net =
        "ERROR 2026-11-18 10:20:31,007 [Worker 3] SheetKraft.Engine.Runner - Run failed\nSystem.InvalidOperationException: x\n   at A.B()";

    public static TheoryData<string, string> Events => new()
    {
        { "serilog", Serilog },
        { "slog", Slog },
        { "log4net", Log4Net },
    };

    private static ILogScheme SchemeOf(string name) => name switch
    {
        "serilog" => BuiltInSchemes.SerilogJson,
        "slog" => BuiltInSchemes.SlogJson,
        _ => BuiltInSchemes.Log4NetCore,
    };

    [Theory]
    [MemberData(nameof(Events))]
    public void A_filter_on_any_value_line_matches_the_event(string scheme, string text)
    {
        EventDetail.Loaded loaded = DetailRig.Loaded(text, SchemeOf(scheme));
        DetailDocument doc = DetailRig.Build(loaded);
        List<DetailLine> valued = doc.FullLines.Where(line => line.Value is not null).ToList();
        Assert.NotEmpty(valued);

        foreach (DetailLine line in valued)
        {
            Assert.NotNull(line.Scope);
            var filter = new EventFilter(new FilterSpec(Fields: [new FieldPredicate(line.Scope!, FieldOperator.Equals, line.Value!)]));
            Assert.True(filter.Matches(loaded.Row, loaded.Event, loaded.Text), $"{line.Scope} = {line.Value}");
        }
    }

    [Fact]
    public void Json_scalars_carry_strings_unquoted_and_other_scalars_as_raw_json()
    {
        DetailDocument doc = DetailRig.Build(DetailRig.Loaded(Serilog, BuiltInSchemes.SerilogJson));
        Dictionary<string, string?> values = doc.FullLines.Where(l => l.Kind == LineKind.Json && l.Scope is not null && l.Value is not null)
            .GroupBy(l => l.Scope!).ToDictionary(g => g.Key, g => g.First().Value);

        Assert.Equal("SampleDotNetApp.Orders", values["Properties.SourceContext"]);
        Assert.Equal("42", values["Properties.OrderId"]);
        Assert.Equal("false", values["Properties.Paid"]);
        Assert.Equal("null", values["Properties.Note"]);
        Assert.Equal("Request 7", values["Properties.Scope[0]"]);
    }

    [Fact]
    public void Multi_line_strings_headers_and_messages_carry_no_value()
    {
        DetailDocument doc = DetailRig.Build(DetailRig.Loaded(Serilog, BuiltInSchemes.SerilogJson));

        Assert.All(doc.FullLines.Where(l => l.Scope == "Exception"), l => Assert.Null(l.Value));
        Assert.All(doc.FullLines.Where(l => l.Kind is LineKind.Message or LineKind.Fold or LineKind.Notice),
            l => Assert.Null(l.Value));
    }

    [Fact]
    public void Text_event_fields_carry_their_value()
    {
        DetailDocument doc = DetailRig.Build(DetailRig.Loaded(Log4Net, BuiltInSchemes.Log4NetCore));

        DetailLine thread = Assert.Single(doc.FullLines, l => l.Kind == LineKind.Field && l.Scope == "thread");
        Assert.Equal("thread", thread.Scope);
        Assert.Equal("Worker 3", thread.Value);
    }
}
