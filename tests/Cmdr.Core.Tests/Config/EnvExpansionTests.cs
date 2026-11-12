using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class EnvExpansionTests
{
    private const string Cwd = "/work/item";
    private const string ConfigDir = "/work";

    private static string Expand(string value, IReadOnlyDictionary<string, string?>? vars = null) =>
        EnvExpansion.Expand(
            value,
            name => vars is not null && vars.TryGetValue(name, out string? v) ? v : null,
            Cwd,
            ConfigDir,
            ':');

    [Fact]
    public void PlainValue_IsUnchanged()
    {
        Assert.Equal("just text", Expand("just text"));
        Assert.Equal("", Expand(""));
    }

    [Fact]
    public void NameReference_UsesLookup()
    {
        Assert.Equal("abc-def", Expand("${FOO}-${BAR}", new Dictionary<string, string?> { ["FOO"] = "abc", ["BAR"] = "def" }));
    }

    [Fact]
    public void NameReference_Undefined_IsEmpty()
    {
        Assert.Equal("[]", Expand("[${NOPE}]"));
    }

    [Fact]
    public void NameReference_LookupReturningNull_IsEmpty()
    {
        Assert.Equal("[]", Expand("[${NULLISH}]", new Dictionary<string, string?> { ["NULLISH"] = null }));
    }

    [Fact]
    public void BuiltIns_ExpandToCwdConfigDirAndPathSeparator()
    {
        Assert.Equal(Cwd, Expand("${cwd}"));
        Assert.Equal(ConfigDir, Expand("${configDir}"));
        Assert.Equal(":", Expand("${pathsep}"));
        Assert.Equal($"{Cwd}/tmp:{ConfigDir}", Expand("${cwd}/tmp${pathsep}${configDir}"));
    }

    [Fact]
    public void BuiltIns_TakePrecedenceOverEnvironmentVariablesOfTheSameName()
    {
        var vars = new Dictionary<string, string?> { ["cwd"] = "from-env", ["configDir"] = "from-env", ["pathsep"] = "from-env" };

        Assert.Equal(Cwd, Expand("${cwd}", vars));
        Assert.Equal(ConfigDir, Expand("${configDir}", vars));
        Assert.Equal(":", Expand("${pathsep}", vars));
    }

    [Fact]
    public void DoubleDollar_BecomesSingleDollar()
    {
        Assert.Equal("$", Expand("$$"));
        Assert.Equal("cost: $5", Expand("cost: $$5"));
        Assert.Equal("${FOO}", Expand("$${FOO}"));
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
    public void Unterminated_IsKeptLiterally()
    {
        Assert.Equal("${", Expand("${"));
        Assert.Equal("x${FOO", Expand("x${FOO", new Dictionary<string, string?> { ["FOO"] = "set" }));
        Assert.Equal("${FOO", Expand("${FOO"));
    }

    [Fact]
    public void Mixed_ReferencesAndLiterals()
    {
        var vars = new Dictionary<string, string?> { ["A"] = "1" };

        Assert.Equal("1$1${cwd}$", Expand("${A}$$1$${cwd}$", vars));
    }
}
