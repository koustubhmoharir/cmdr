using Cfgedit.Core;

namespace Cfgedit.Core.Tests;

public sealed class CfgeditExceptionTests
{
    [Fact]
    public void Message_and_location_properties_are_kept()
    {
        var ex = new CfgeditException("bad value")
        {
            Path = "app.json",
            Line = 3,
            Column = 7,
        };

        Assert.Equal("bad value", ex.Message);
        Assert.Equal("app.json", ex.Path);
        Assert.Equal(3, ex.Line);
        Assert.Equal(7, ex.Column);
    }

    [Fact]
    public void Location_properties_are_null_when_not_given()
    {
        var ex = new CfgeditException("no file");

        Assert.Null(ex.Path);
        Assert.Null(ex.Line);
        Assert.Null(ex.Column);
    }
}
