using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class PlaceholdersTests
{
    private const string Cwd = "/work/item";
    private const string ConfigDir = "/work";

    private static IReadOnlyDictionary<string, string> Values(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string?> Vars(params (string Name, string? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);

    private static string Expand(
        string text,
        IReadOnlyDictionary<string, string?>? vars = null,
        IReadOnlyDictionary<string, string>? inputs = null) =>
        Placeholders.Expand(
            text,
            name => vars is not null && vars.TryGetValue(name, out string? v) ? v : null,
            inputs,
            Cwd,
            ConfigDir,
            ':');

    private static IReadOnlyList<InputRequest> RequestsOf(params string[] texts) => Placeholders.Requests(texts);

    // Input placeholders

    [Fact]
    public void WholeArgument_IsReplaced()
    {
        Assert.Equal("hello", Expand("${input:x}", inputs: Values(("x", "hello"))));
    }

    [Fact]
    public void Embedded_IsReplacedInPlace()
    {
        Assert.Equal("--name=bob-1", Expand("--name=${input:who}-1", inputs: Values(("who", "bob"))));
    }

    [Fact]
    public void SeveralInOneText_AreAllReplaced()
    {
        Assert.Equal("a/b/a", Expand("${input:x}/${input:y}/${input:x}", inputs: Values(("x", "a"), ("y", "b"))));
    }

    [Fact]
    public void InputNames_AllowLettersDigitsUnderscoreDotAndDash()
    {
        Assert.Equal("ok", Expand("${input:Az09_.-}", inputs: Values(("Az09_.-", "ok"))));
        Assert.Empty(Placeholders.Errors("${input:Az09_.-}"));
    }

    [Fact]
    public void InputNames_AreCaseSensitive()
    {
        var requests = RequestsOf("${input:Name}", "${input:name}");

        Assert.Equal(new[] { "Name", "name" }, requests.Select(r => r.Name));
        Assert.Throws<InvalidOperationException>(() => Expand("${input:Name}", inputs: Values(("name", "x"))));
    }

    [Fact]
    public void OptionalInput_AcceptsEmptyValue()
    {
        Assert.Equal("[]", Expand("[${input:x?}]", inputs: Values(("x", ""))));
    }

    [Fact]
    public void RequiredInput_RejectsEmptyValue()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Expand("${input:x}", inputs: Values(("x", ""))));

        Assert.Equal("input 'x' must not be empty", ex.Message);
    }

    [Fact]
    public void MissingValue_Throws_AndMessageNamesInputOnly()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Expand("a ${input:secret} b", inputs: Values(("other", "v"))));

        Assert.Equal("no value for input 'secret'", ex.Message);
    }

    [Fact]
    public void MissingOptionalValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Expand("${input:x?}", inputs: Values()));
    }

    [Fact]
    public void InputsNull_KeepsInputPlaceholdersAsWritten()
    {
        Assert.Equal("a ${input:x} ${input:y?}", Expand("a ${input:x} ${input:y?}"));
    }

    [Fact]
    public void InputEscape_ProducesLiteralText_AndRestIsNotAPlaceholder()
    {
        Assert.Equal("${input:x}", Expand("$${input:x}", inputs: Values()));
        Assert.Equal("${input:x}", Expand("$${input:x}"));
        Assert.Empty(RequestsOf("$${input:x}"));
        Assert.Empty(Placeholders.Errors("$${input:x"));
    }

    [Fact]
    public void InputEscape_BeforeRealPlaceholder_BothWork()
    {
        Assert.Equal("${input:x} v", Expand("$${input:x} ${input:y}", inputs: Values(("y", "v"))));
    }

    [Theory]
    [InlineData("${input:}")]
    [InlineData("${input:?}")]
    [InlineData("${input:a b}")]
    [InlineData("${input:a?b}")]
    [InlineData("${input:a??}")]
    [InlineData("${input:a$}")]
    [InlineData("${input:a/b}")]
    public void MalformedInput_IsReported(string text)
    {
        string error = Assert.Single(Placeholders.Errors(text));

        Assert.Equal($"malformed placeholder '{text}'", error);
    }

    [Fact]
    public void MalformedInput_IsCopiedLiterallyAndIgnoredByRequests()
    {
        Assert.Equal("${input:a b}", Expand("${input:a b}", inputs: Values()));
        Assert.Empty(RequestsOf("${input:a b}", "${input:"));
    }

    [Fact]
    public void Errors_ReportEachMalformedPlaceholder_InOrder()
    {
        var errors = Placeholders.Errors("${input:ok}${input:}${input:bad x}");

        Assert.Equal(new[] { "malformed placeholder '${input:}'", "malformed placeholder '${input:bad x}'" }, errors);
    }

    [Fact]
    public void Requests_AreDistinct_InFirstSeenOrder()
    {
        var requests = RequestsOf("--b=${input:b}", "${input:a}", "${input:b}/${input:c}");

        Assert.Equal(
            new[] { new InputRequest("b", false), new InputRequest("a", false), new InputRequest("c", false) },
            requests);
    }

    [Fact]
    public void Requests_RequiredWins_WhenAnyOccurrenceLacksQuestionMark()
    {
        Assert.Equal(new InputRequest("x", false), Assert.Single(RequestsOf("${input:x?}", "${input:x}")));
        Assert.Equal(new InputRequest("x", false), Assert.Single(RequestsOf("${input:x}", "${input:x?}")));
    }

    [Fact]
    public void Requests_OptionalOnlyWhenEveryOccurrenceHasQuestionMark()
    {
        var requests = RequestsOf("${input:x?}", "a ${input:x?}");

        Assert.Equal(new InputRequest("x", true), Assert.Single(requests));
    }

    [Fact]
    public void Requests_AcrossTexts_MergeByName()
    {
        var requests = RequestsOf("${input:a?}", "${input:b}", "${input:a}");

        Assert.Equal(new[] { new InputRequest("a", false), new InputRequest("b", false) }, requests);
    }

    [Fact]
    public void Requests_NoPlaceholders_IsEmpty()
    {
        Assert.Empty(RequestsOf("plain", "$HOME", "${env:HOME}", "${cwd}"));
    }

    [Fact]
    public void CheckInputs_PassesWhenEveryValueIsAcceptable()
    {
        Placeholders.CheckInputs(new[] { "${input:a}", "${input:b?}" }, Values(("a", "x"), ("b", "")));
    }

    [Fact]
    public void CheckInputs_IgnoresEscapesEnvAndBuiltIns()
    {
        Placeholders.CheckInputs(new[] { "$${input:q}", "${env:X}", "${cwd}" }, Values());
    }

    [Fact]
    public void CheckInputs_ThrowsForMissingValue_NamingTheInput()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Placeholders.CheckInputs(new[] { "${input:c}" }, Values()));

        Assert.Equal("no value for input 'c'", ex.Message);
    }

    [Fact]
    public void CheckInputs_ThrowsForFirstEmptyRequiredValue_InTextOrder()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Placeholders.CheckInputs(new[] { "${input:a}", "${input:b}" }, Values(("a", ""), ("b", ""))));

        Assert.Equal("input 'a' must not be empty", ex.Message);
    }

    // Environment variables

    [Fact]
    public void PlainValue_IsUnchanged()
    {
        Assert.Equal("just text", Expand("just text"));
        Assert.Equal("", Expand(""));
    }

    [Fact]
    public void EnvReference_UsesLookup()
    {
        Assert.Equal("abc-def", Expand("${env:FOO}-${env:BAR}", Vars(("FOO", "abc"), ("BAR", "def"))));
    }

    [Fact]
    public void EnvReference_Undefined_IsEmpty()
    {
        Assert.Equal("[]", Expand("[${env:NOPE}]"));
    }

    [Fact]
    public void EnvReference_LookupReturningNull_IsEmpty()
    {
        Assert.Equal("[]", Expand("[${env:NULLISH}]", Vars(("NULLISH", null))));
    }

    [Fact]
    public void EnvReference_NameMayContainParenthesesAndSpaces()
    {
        const string text = "${env:ProgramFiles(x86)}|${env:A B}";

        Assert.Equal("C:\\pf|1", Expand(text, Vars(("ProgramFiles(x86)", "C:\\pf"), ("A B", "1"))));
        Assert.Empty(Placeholders.Errors(text));
    }

    [Theory]
    [InlineData("${env:}")]
    [InlineData("${env:A=B}")]
    public void EnvReference_WithoutAValidName_IsMalformedAndCopied(string text)
    {
        string error = Assert.Single(Placeholders.Errors(text));

        Assert.Equal($"malformed placeholder '{text}'", error);
        Assert.Equal(text, Expand(text, Vars(("A=B", "x"))));
    }

    [Fact]
    public void BareName_IsReportedWithTheEnvForm_AndCopiedLiterally()
    {
        string error = Assert.Single(Placeholders.Errors("${PATH}"));

        Assert.Equal("unknown placeholder '${PATH}' (write ${env:PATH} for a variable, or $${ for a literal ${)", error);
        Assert.Equal("${PATH}", Expand("${PATH}", Vars(("PATH", "/bin"))));
    }

    [Fact]
    public void BareNameWithAColon_IsMalformed()
    {
        Assert.Equal("malformed placeholder '${foo:bar}'", Assert.Single(Placeholders.Errors("${foo:bar}")));
        Assert.Equal("${foo:bar}", Expand("${foo:bar}", Vars(("foo:bar", "x"))));
    }

    [Fact]
    public void EmptyBraces_AreMalformed()
    {
        Assert.Equal("malformed placeholder '${}'", Assert.Single(Placeholders.Errors("${}")));
        Assert.Equal("${}", Expand("${}"));
    }

    [Fact]
    public void Unterminated_IsKeptLiterally_AndReported()
    {
        Assert.Equal("unterminated placeholder '${'", Assert.Single(Placeholders.Errors("${")));
        Assert.Equal("unterminated placeholder '${env:FOO'", Assert.Single(Placeholders.Errors("x${env:FOO")));
        Assert.Equal("unterminated placeholder '${input:abc'", Assert.Single(Placeholders.Errors("${input:abc")));

        Assert.Equal("x${env:FOO", Expand("x${env:FOO", Vars(("FOO", "set"))));
        Assert.Equal("${", Expand("${"));
    }

    [Fact]
    public void BuiltIns_ExpandToCwdConfigDirAndPathSeparator()
    {
        Assert.Equal(Cwd, Expand("${cwd}"));
        Assert.Equal(ConfigDir, Expand("${configDir}"));
        Assert.Equal(":", Expand("${pathsep}"));
        Assert.Equal($"{Cwd}/tmp:{ConfigDir}", Expand("${cwd}/tmp${pathsep}${configDir}"));
        Assert.Empty(Placeholders.Errors("${cwd}${configDir}${pathsep}"));
    }

    [Fact]
    public void BuiltIns_AreNotEnvironmentVariables()
    {
        var vars = Vars(("cwd", "from-env"), ("configDir", "from-env"), ("pathsep", "from-env"));

        Assert.Equal(Cwd, Expand("${cwd}", vars));
        Assert.Equal(ConfigDir, Expand("${configDir}", vars));
        Assert.Equal(":", Expand("${pathsep}", vars));
        Assert.Equal("from-env", Expand("${env:cwd}", vars));
    }

    [Fact]
    public void Escape_BecomesLiteralOpeningText()
    {
        Assert.Equal("${", Expand("$${"));
        Assert.Equal("${env:FOO}", Expand("$${env:FOO}", Vars(("FOO", "set"))));
        Assert.Equal("${cwd}", Expand("$${cwd}"));
        Assert.Equal("${PATH}", Expand("$${PATH}"));
        Assert.Empty(Placeholders.Errors("$${env:FOO} $${PATH} $${input:x}"));
    }

    [Fact]
    public void DoubleDollar_NotFollowedByBrace_IsLiteral()
    {
        Assert.Equal("$$", Expand("$$"));
        Assert.Equal("cost: $$5", Expand("cost: $$5"));
        Assert.Equal("$$$", Expand("$$$"));
    }

    [Fact]
    public void LoneDollar_IsLiteral()
    {
        Assert.Equal("$", Expand("$"));
        Assert.Equal("$5", Expand("$5"));
        Assert.Equal("a$b", Expand("a$b"));
        Assert.Equal("price $", Expand("price $"));
    }

    [Fact]
    public void OtherDollarText_IsCopiedVerbatim()
    {
        Assert.Equal("$HOME $(pwd) $ {x} $", Expand("$HOME $(pwd) $ {x} $"));
        Assert.Empty(Placeholders.Errors("$HOME $(pwd) $ {x} $"));
    }

    [Fact]
    public void Mixed_ReferencesAndLiterals()
    {
        Assert.Equal("1$$1${cwd}$", Expand("${env:A}$$1$${cwd}$", Vars(("A", "1"))));
    }

    [Fact]
    public void SubstitutedValues_AreNeverScannedAgain()
    {
        Assert.Equal("${env:Y}", Expand("${env:X}", Vars(("X", "${env:Y}"), ("Y", "secret"))));
        Assert.Equal("${env:X}", Expand("${input:a}", Vars(("X", "bad")), Values(("a", "${env:X}"))));
        Assert.Equal("${input:b}", Expand("${input:a}", inputs: Values(("a", "${input:b}"))));
    }

    [Fact]
    public void Errors_OfWellFormedText_IsEmpty()
    {
        Assert.Empty(Placeholders.Errors("${env:PATH}${pathsep}${cwd}${input:x?}$${PATH}"));
    }
}
