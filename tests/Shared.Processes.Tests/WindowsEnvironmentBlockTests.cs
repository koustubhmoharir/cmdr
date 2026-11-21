namespace Shared.Processes.Tests;

/// <summary>Pure tests of the CreateProcessW Unicode environment block builder (runs on every OS).</summary>
public class WindowsEnvironmentBlockTests
{
    [Fact]
    public void EntriesAreSortedCaseInsensitivelyAndKeepTheirSpelling()
    {
        var env = new Dictionary<string, string?>
        {
            ["beta"] = "2",
            ["Alpha"] = "1",
            ["GAMMA"] = "3",
        };

        string block = WindowsEnvironmentBlock.Build(env);

        Assert.Equal("Alpha=1\0beta=2\0GAMMA=3\0\0", block);
    }

    [Fact]
    public void NullValuesAreOmitted()
    {
        var env = new Dictionary<string, string?>
        {
            ["KEEP"] = "yes",
            ["DROP"] = null,
        };

        Assert.Equal("KEEP=yes\0\0", WindowsEnvironmentBlock.Build(env));
    }

    [Fact]
    public void EmptyEnvironmentIsTwoNuls()
    {
        Assert.Equal("\0\0", WindowsEnvironmentBlock.Build(new Dictionary<string, string?>()));
    }

    [Fact]
    public void AllNullValuesGiveEmptyBlock()
    {
        var env = new Dictionary<string, string?> { ["A"] = null };

        Assert.Equal("\0\0", WindowsEnvironmentBlock.Build(env));
    }

    [Fact]
    public void EmptyValueIsKept()
    {
        Assert.Equal("A=\0\0", WindowsEnvironmentBlock.Build(new Dictionary<string, string?> { ["A"] = "" }));
    }

    [Fact]
    public void HiddenDriveVariableWithLeadingEqualsIsKept()
    {
        var env = new Dictionary<string, string?>
        {
            ["PATH"] = "x",
            ["=C:"] = @"C:\work",
        };

        string block = WindowsEnvironmentBlock.Build(env);

        Assert.Equal("=C:=C:\\work\0PATH=x\0\0", block);
    }

    [Fact]
    public void UnicodeValueIsKeptAsIs()
    {
        var env = new Dictionary<string, string?> { ["NAME"] = "caf\u00e9 \U0001F600" };

        Assert.Equal("NAME=caf\u00e9 \U0001F600\0\0", WindowsEnvironmentBlock.Build(env));
    }

    [Fact]
    public void EmptyKeyThrowsNamingTheKey()
    {
        var env = new Dictionary<string, string?> { [""] = "v" };

        var ex = Assert.Throws<ArgumentException>(() => WindowsEnvironmentBlock.Build(env));
        Assert.Contains("empty", ex.Message);
    }

    [Theory]
    [InlineData("A=B")]
    [InlineData("X==")]
    [InlineData("==X")]
    public void KeyWithEqualsInsideThrowsNamingTheKey(string key)
    {
        var env = new Dictionary<string, string?> { [key] = "v" };

        var ex = Assert.Throws<ArgumentException>(() => WindowsEnvironmentBlock.Build(env));
        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void KeyWithNulThrowsNamingTheKey()
    {
        var env = new Dictionary<string, string?> { ["A\0B"] = "v" };

        var ex = Assert.Throws<ArgumentException>(() => WindowsEnvironmentBlock.Build(env));
        Assert.Contains("A\0B", ex.Message);
    }

    [Fact]
    public void ValueWithNulThrowsNamingTheKey()
    {
        var env = new Dictionary<string, string?> { ["MYVAR"] = "a\0b" };

        var ex = Assert.Throws<ArgumentException>(() => WindowsEnvironmentBlock.Build(env));
        Assert.Contains("MYVAR", ex.Message);
    }

    [Fact]
    public void CaseDuplicateKeysThrowNamingBoth()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Path"] = "a",
            ["PATH"] = "b",
        };

        var ex = Assert.Throws<ArgumentException>(() => WindowsEnvironmentBlock.Build(env));
        Assert.Contains("Path", ex.Message);
        Assert.Contains("PATH", ex.Message);
    }

    [Fact]
    public void CaseDuplicateKeysWithNullValueAreNotDuplicates()
    {
        // The null entry is omitted before the duplicate check, so only one variable is passed.
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Path"] = "a",
            ["PATH"] = null,
        };

        Assert.Equal("Path=a\0\0", WindowsEnvironmentBlock.Build(env));
    }
}
