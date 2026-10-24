using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests;

[TestClass]
public class WorkspaceForkGitIsolationTests
{
    [TestMethod]
    [DataRow(".git", false)]
    [DataRow(".GIT", false)]
    [DataRow(".git", true)]
    public void CopyDirectory_ExcludesGitMetadataAtEveryDepth(string name, bool directory)
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(WorkspaceForkGitIsolationTests), Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "source");
            var destination = Path.Combine(root, "fork");
            foreach (var relative in new[] { "", "nested" })
            {
                var folder = Path.Combine(source, relative);
                Directory.CreateDirectory(folder);
                var metadata = Path.Combine(folder, name);
                if (directory)
                {
                    Directory.CreateDirectory(metadata);
                    File.WriteAllText(Path.Combine(metadata, "config"), "source metadata");
                }
                else
                {
                    File.WriteAllText(metadata, "gitdir: /source/.git/worktrees/agent");
                }

                File.WriteAllText(Path.Combine(folder, ".gitignore"), "bin/");
                File.WriteAllText(Path.Combine(folder, "Program.cs"), "class C {}");
            }

            WorkspaceForkApplyService.CopyDirectory(source, destination, [Path.GetFullPath(destination)], CancellationToken.None);
            foreach (var relative in new[] { "", "nested" })
            {
                Assert.IsFalse(Path.Exists(Path.Combine(destination, relative, name)));
                Assert.AreEqual("bin/", File.ReadAllText(Path.Combine(destination, relative, ".gitignore")));
                Assert.AreEqual("class C {}", File.ReadAllText(Path.Combine(destination, relative, "Program.cs")));
                Assert.IsTrue(Path.Exists(Path.Combine(source, relative, name)), "Source metadata must survive.");
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task CopyDirectory_ConcurrentWorktreeForks_CannotMutateEitherAgentIndexAsync()
    {
        await GitFixtureRunner.RequireAvailableAsync();

        var root = Path.Combine(Path.GetTempPath(), nameof(WorkspaceForkGitIsolationTests), Guid.NewGuid().ToString("N"));
        try
        {
            var repository = Path.Combine(root, "repository");
            Directory.CreateDirectory(repository);
            await GitFixtureRunner.InitializeRepositoryAsync(repository);
            File.WriteAllText(Path.Combine(repository, "Program.cs"), "class C {}");
            await GitFixtureRunner.RunGitAsync(repository, "add", "Program.cs");
            await GitFixtureRunner.RunGitAsync(repository, "commit", "-qm", "baseline");
            var agents = new[] { Path.Combine(root, "agent-a"), Path.Combine(root, "agent-b") };
            for (var i = 0; i < agents.Length; i++)
            {
                await GitFixtureRunner.RunGitAsync(repository, "worktree", "add", "-b", $"agent-{i}", agents[i]);
                File.WriteAllText(Path.Combine(agents[i], "Program.cs"), $"class Agent{i} {{}}");
                await GitFixtureRunner.RunGitAsync(agents[i], "add", "Program.cs");
            }

            var before = await Task.WhenAll(agents.Select(agent => GitFixtureRunner.RunGitCaptureAsync(agent, "diff", "--cached")));
            await Task.WhenAll(agents.Select((agent, i) => Task.Run(async () =>
            {
                var fork = Path.Combine(root, $"fork-{i}");
                WorkspaceForkApplyService.CopyDirectory(agent, fork, [Path.GetFullPath(fork)], CancellationToken.None);
                Assert.IsFalse(File.Exists(Path.Combine(fork, ".git")), "A fork must not inherit an agent's gitdir pointer.");
                File.WriteAllText(Path.Combine(fork, "Program.cs"), "class Fork {}");
                // A fork may create its own repository, but must never share an agent's index.
                await GitFixtureRunner.InitializeRepositoryAsync(fork);
                await GitFixtureRunner.RunGitAsync(fork, "add", "Program.cs");
            })));

            for (var i = 0; i < agents.Length; i++)
            {
                Assert.AreEqual(before[i], await GitFixtureRunner.RunGitCaptureAsync(agents[i], "diff", "--cached"));
                Assert.AreEqual($"class Agent{i} {{}}", File.ReadAllText(Path.Combine(agents[i], "Program.cs")));
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }
}
