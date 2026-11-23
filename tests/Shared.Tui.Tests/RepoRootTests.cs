using Shared.Tui;

namespace Shared.Tui.Tests;

public sealed class RepoRootTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "h");
    private static readonly string Repo = Path.Combine(Base, "srv", "cmdr");

    [Fact]
    public void No_git_entry_gives_null()
    {
        Assert.Null(RepoRoot.Outermost(Path.Combine(Repo, "docs"), _ => false));
    }

    [Fact]
    public void The_repository_above_the_directory_is_found()
    {
        string docs = Path.Combine(Repo, "docs");

        Assert.Equal(Repo, RepoRoot.Outermost(docs, folder => folder == Repo));
    }

    [Fact]
    public void The_outermost_of_nested_repositories_is_returned()
    {
        string inner = Path.Combine(Repo, "vendor", "lib");
        string deepest = Path.Combine(inner, "src");

        string? found = RepoRoot.Outermost(deepest, folder => folder == Repo || folder == inner);

        Assert.Equal(Repo, found);
    }

    [Fact]
    public void A_directory_with_git_is_its_own_root_when_nothing_encloses_it()
    {
        Assert.Equal(Repo, RepoRoot.Outermost(Repo, folder => folder == Repo));
    }

    [Fact]
    public void A_trailing_separator_on_the_directory_makes_no_difference()
    {
        string docs = Path.Combine(Repo, "docs") + Path.DirectorySeparatorChar;

        Assert.Equal(Repo, RepoRoot.Outermost(docs, folder => folder == Repo));
    }

    [Fact]
    public void The_walk_goes_up_to_the_file_system_root()
    {
        string root = Path.GetPathRoot(Repo)!;
        var visited = new List<string>();

        string? found = RepoRoot.Outermost(Repo, folder =>
        {
            visited.Add(folder);
            return folder == root;
        });

        Assert.Equal(root, found);
        Assert.Equal(root, visited[^1]);
        Assert.Equal(Repo, visited[0]);
    }

    [Fact]
    public void An_empty_directory_gives_null()
    {
        Assert.Null(RepoRoot.Outermost(string.Empty, _ => true));
    }

    [Fact]
    public void A_git_folder_and_a_git_file_both_count()
    {
        // The folders are under one temporary root. The walk above it is limited to that root, because the machine's temporary folder
        // may itself be inside a repository.
        string root = Path.Combine(Path.GetTempPath(), "repo-root-" + Guid.NewGuid().ToString("N"));
        string clone = Path.Combine(root, "clone");
        string worktree = Path.Combine(root, "worktree");
        string plain = Path.Combine(root, "plain");
        try
        {
            Directory.CreateDirectory(Path.Combine(clone, ".git"));
            Directory.CreateDirectory(worktree);
            File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: /elsewhere");
            Directory.CreateDirectory(plain);

            Assert.True(RepoRoot.HasGitEntry(clone));
            Assert.True(RepoRoot.HasGitEntry(worktree));
            Assert.False(RepoRoot.HasGitEntry(plain));
            Assert.Equal(worktree, RepoRoot.Outermost(Path.Combine(worktree, "docs"), folder => folder.StartsWith(root, StringComparison.Ordinal) && RepoRoot.HasGitEntry(folder)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void A_missing_folder_has_no_git_entry()
    {
        Assert.False(RepoRoot.HasGitEntry(Path.Combine(Path.GetTempPath(), "repo-root-missing-" + Guid.NewGuid().ToString("N"))));
    }
}
