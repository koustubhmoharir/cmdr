using Shared.Core;

namespace Cmdr.Core.Tests.Config;

public class RecentFilesTests
{
    [Fact]
    public void DefaultStorePath_ForCmdr_EndsWithCmdrRecentJson_WhenKnown()
    {
        string? path = RecentFiles.DefaultStorePathFor("cmdr");

        if (path is not null)
        {
            Assert.EndsWith(Path.Combine("cmdr", "recent.json"), path);
        }
    }
}
