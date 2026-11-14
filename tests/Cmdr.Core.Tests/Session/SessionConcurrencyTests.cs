using System.Collections.Concurrent;
using System.Diagnostics;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>Tree edits on one thread while restarts walk the same tree on another.</summary>
public class SessionConcurrencyTests
{
    [Fact]
    public async Task RestartAsync_ConcurrentWithTopLevelEdits_DoesNotThrow()
    {
        using var dir = new TempDir();

        // The command needs an input that no restart supplies, so every restart refuses to start it and launches nothing.
        var needy = new CommandSpec { Name = "needy", Program = "sh", Args = new List<string> { "-c", "echo ${input:x}" } };
        var groups = new List<GroupSpec> { Group("target", GroupMode.Parallel, needy) };
        for (int i = 0; i < 100; i++)
        {
            groups.Add(Group($"idle{i}", GroupMode.Parallel));
        }

        CmdrSession session = OpenWith(dir, groups.ToArray());
        try
        {
            CommandEntry target = session.Groups[0].Commands[0];
            var restartErrors = new ConcurrentQueue<Exception>();
            var editErrors = new ConcurrentQueue<Exception>();

            Task restarts = Task.Run(async () =>
            {
                for (int i = 0; i < 300; i++)
                {
                    try
                    {
                        await session.RestartAsync(new[] { target });
                    }
                    catch (Exception ex)
                    {
                        restartErrors.Enqueue(ex);
                        return;
                    }
                }
            });

            // Edits run on this thread, as they do in the UI. Keep going until the restarts finish, with a minimum count.
            var clock = Stopwatch.StartNew();
            int edits = 0;
            while (edits < 200 || (!restarts.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(3)))
            {
                try
                {
                    GroupEntry added = session.AddGroup(null, Settings($"extra{edits}"));
                    session.RemoveGroup(added);
                }
                catch (Exception ex)
                {
                    editErrors.Enqueue(ex);
                    break;
                }

                edits++;
            }

            await restarts.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Empty(restartErrors);
            Assert.Empty(editErrors);
        }
        finally
        {
            Cleanup(session);
        }
    }
}
