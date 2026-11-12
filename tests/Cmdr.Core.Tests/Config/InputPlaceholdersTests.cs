using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class InputPlaceholdersTests
{
    private static IReadOnlyDictionary<string, string> Values(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);

    private static IReadOnlyList<InputPlaceholders.InputRequest> RequestsOf(params string[] args) =>
        InputPlaceholders.Requests(new[] { (IReadOnlyList<string>)args });

    [Fact]
    public void WholeArgument_IsReplaced()
    {
        Assert.Equal("hello", InputPlaceholders.Substitute("${input:x}", Values(("x", "hello"))));
    }

    [Fact]
    public void Embedded_IsReplacedInPlace()
    {
        Assert.Equal("--name=bob-1", InputPlaceholders.Substitute("--name=${input:who}-1", Values(("who", "bob"))));
    }

    [Fact]
    public void SeveralInOneArgument_AreAllReplaced()
    {
        Assert.Equal("a/b/a", InputPlaceholders.Substitute("${input:x}/${input:y}/${input:x}", Values(("x", "a"), ("y", "b"))));
    }

    [Fact]
    public void Names_AllowLettersDigitsUnderscoreDotAndDash()
    {
        Assert.Equal("ok", InputPlaceholders.Substitute("${input:Az09_.-}", Values(("Az09_.-", "ok"))));
        Assert.Empty(InputPlaceholders.Errors(new[] { "${input:Az09_.-}" }));
    }

    [Fact]
    public void Names_AreCaseSensitive()
    {
        var requests = RequestsOf("${input:Name}", "${input:name}");

        Assert.Equal(new[] { "Name", "name" }, requests.Select(r => r.Name));
        Assert.Throws<InvalidOperationException>(() => InputPlaceholders.Substitute("${input:Name}", Values(("name", "x"))));
    }

    [Fact]
    public void OptionalPlaceholder_AcceptsEmptyValue()
    {
        Assert.Equal("[]", InputPlaceholders.Substitute("[${input:x?}]", Values(("x", ""))));
    }

    [Fact]
    public void RequiredPlaceholder_RejectsEmptyValue()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => InputPlaceholders.Substitute("${input:x}", Values(("x", ""))));

        Assert.Equal("input 'x' must not be empty", ex.Message);
    }

    [Fact]
    public void MissingValue_Throws_AndMessageNamesInputOnly()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => InputPlaceholders.Substitute("a ${input:secret} b", Values(("other", "v"))));

        Assert.Equal("no value for input 'secret'", ex.Message);
    }

    [Fact]
    public void MissingOptionalValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => InputPlaceholders.Substitute("${input:x?}", Values()));
    }

    [Fact]
    public void Escape_ProducesLiteralOpeningText_AndRestIsNotAPlaceholder()
    {
        Assert.Equal("${input:x}", InputPlaceholders.Substitute("$${input:x}", Values()));
        Assert.Empty(RequestsOf("$${input:x}"));
        Assert.Empty(InputPlaceholders.Errors(new[] { "$${input:x" }));
    }

    [Fact]
    public void Escape_BeforeRealPlaceholder_BothWork()
    {
        Assert.Equal("${input:x} v", InputPlaceholders.Substitute("$${input:x} ${input:y}", Values(("y", "v"))));
    }

    [Fact]
    public void OtherDollarText_IsCopiedVerbatim()
    {
        Assert.Equal("$HOME $(pwd) $ {x} $", InputPlaceholders.Substitute("$HOME $(pwd) $ {x} $", Values()));
        Assert.Equal("$", InputPlaceholders.Substitute("$", Values()));
    }

    [Theory]
    [InlineData("${input:}")]
    [InlineData("${input:?}")]
    [InlineData("${input:a b}")]
    [InlineData("${input:a?b}")]
    [InlineData("${input:a??}")]
    [InlineData("${input:a$}")]
    [InlineData("${input:abc")]
    [InlineData("${input:a/b}")]
    public void Malformed_IsReported(string arg)
    {
        string error = Assert.Single(InputPlaceholders.Errors(new[] { "ok", arg }));

        Assert.Equal($"args[1]: malformed input placeholder '{arg}'", error);
    }

    [Fact]
    public void Malformed_IsCopiedLiterallyAndIgnoredByRequests()
    {
        Assert.Equal("${input:a b}", InputPlaceholders.Substitute("${input:a b}", Values()));
        Assert.Empty(RequestsOf("${input:a b}", "${input:"));
    }

    [Fact]
    public void Errors_ReportEachMalformedPlaceholder_WithItsIndex()
    {
        var errors = InputPlaceholders.Errors(new[] { "${input:ok}", "${input:}${input:bad x}", "fine" });

        Assert.Equal(new[] { "args[1]: malformed input placeholder '${input:}'", "args[1]: malformed input placeholder '${input:bad x}'" }, errors);
    }

    [Fact]
    public void Requests_AreDistinct_InFirstSeenOrder()
    {
        var requests = RequestsOf("--b=${input:b}", "${input:a}", "${input:b}/${input:c}");

        Assert.Equal(
            new[] { new InputPlaceholders.InputRequest("b", false), new InputPlaceholders.InputRequest("a", false), new InputPlaceholders.InputRequest("c", false) },
            requests);
    }

    [Fact]
    public void Requests_RequiredWins_WhenAnyOccurrenceLacksQuestionMark()
    {
        var requests = RequestsOf("${input:x?}", "${input:x}");
        Assert.Equal(new InputPlaceholders.InputRequest("x", false), Assert.Single(requests));

        var reversed = RequestsOf("${input:x}", "${input:x?}");
        Assert.Equal(new InputPlaceholders.InputRequest("x", false), Assert.Single(reversed));
    }

    [Fact]
    public void Requests_OptionalOnlyWhenEveryOccurrenceHasQuestionMark()
    {
        var requests = RequestsOf("${input:x?}", "a ${input:x?}");

        Assert.Equal(new InputPlaceholders.InputRequest("x", true), Assert.Single(requests));
    }

    [Fact]
    public void Requests_AcrossArgumentLists_MergeByName()
    {
        var requests = InputPlaceholders.Requests(new IReadOnlyList<string>[]
        {
            new[] { "${input:a?}" },
            new[] { "${input:b}", "${input:a}" },
        });

        Assert.Equal(
            new[] { new InputPlaceholders.InputRequest("a", false), new InputPlaceholders.InputRequest("b", false) },
            requests);
    }

    [Fact]
    public void Requests_NoPlaceholders_IsEmpty()
    {
        Assert.Empty(RequestsOf("plain", "$HOME"));
    }

    [Fact]
    public void SubstituteList_ReplacesEveryArgument()
    {
        IReadOnlyList<string> result = InputPlaceholders.Substitute(new[] { "-c", "echo ${input:x}", "${input:y?}" }, Values(("x", "1"), ("y", "")));

        Assert.Equal(new[] { "-c", "echo 1", "" }, result);
    }

    [Fact]
    public void SubstituteList_ThrowsOnMissingValue()
    {
        Assert.Throws<InvalidOperationException>(() => InputPlaceholders.Substitute(new[] { "${input:x}" }, Values()));
    }
}
