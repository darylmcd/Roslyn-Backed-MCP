using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests.Support;

/// <summary>
/// Shared git helpers for tests. Two distinct usages, both supported:
/// <list type="number">
///   <item>
///     Building a small on-disk git repo around the SampleSolution fixture —
///     <see cref="StageFixtureBaseline"/> / <see cref="StageAndCommitAll"/> plus
///     <see cref="RunGit"/>. Extracted from <c>ValidateRecentGitChangesTests</c>
///     and <c>ValidateWorkspaceChangeTrackerReconcileTests</c> where the same
///     implementation was duplicated verbatim.
///   </item>
///   <item>
///     Read-only git queries against the REAL repository root — e.g.
///     <c>McpServerSurfaceTestSkillTests.CanonicalPromotionScorecard_IsGitTracked</c>
///     calls <see cref="RunGitCapture"/> with <c>git ls-files</c> to assert a
///     path's tracked state. Only non-mutating porcelain/plumbing queries are
///     acceptable against the real root; the staging and commit helpers above
///     are strictly for throwaway fixture directories.
///   </item>
/// </list>
/// <see cref="IsAvailable"/> gates both usages so a machine without git on PATH
/// yields an <c>Assert.Inconclusive</c> instead of a hard failure.
/// </summary>
internal static class GitFixtureRunner
{
    private static readonly string[] FixturePathSpecs =
    {
        ".gitignore",
        "BannedSymbols.txt",
        "Directory.Build.props",
        "Directory.Packages.props",
        "global.json",
        "SampleSolution.sln",
        "SampleSolution.slnx",
        "SampleApp",
        "SampleLib",
        "SampleLib.Generators",
        "SampleLib.Tests",
    };

    public static bool IsAvailable(out string? failureReason)
    {
        try
        {
            _ = RunGitCapture(Environment.CurrentDirectory, "--version");
            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public static void InitializeRepository(string directory, string? originUrl = null)
    {
        // Force a named initial branch so fixtures never inherit machine-specific
        // init.defaultBranch settings or remain on an ambiguous unborn HEAD.
        RunGit(directory, "init", "-q", "-b", "main");
        if (!Directory.Exists(Path.Combine(directory, ".git")))
        {
            throw new InvalidOperationException(
                $"git init appeared to succeed but '.git' is missing in '{directory}'.");
        }

        File.WriteAllText(Path.Combine(directory, ".gitignore"), "bin/\nobj/\n");
        RunGit(directory, "config", "--local", "user.email", "ci@example.invalid");
        RunGit(directory, "config", "--local", "user.name", "CI");
        RunGit(directory, "config", "--local", "commit.gpgsign", "false");
        RunGit(directory, "config", "--local", "core.autocrlf", "false");

        if (!string.IsNullOrWhiteSpace(originUrl))
        {
            RunGit(directory, "remote", "add", "origin", originUrl);
        }
    }

    public static void StageAndCommitAll(string directory)
    {
        StageFixtureBaseline(directory);
        RunGit(directory, "commit", "-q", "-m", "seed");
    }

    public static void StageFixtureBaseline(string directory)
    {
        var pathspecs = FixturePathSpecs
            .Where(path => File.Exists(Path.Combine(directory, path)) || Directory.Exists(Path.Combine(directory, path)))
            .ToArray();

        if (pathspecs.Length == 0)
        {
            throw new InvalidOperationException($"No fixture paths were found to stage in '{directory}'.");
        }

        var arguments = new string[2 + pathspecs.Length];
        arguments[0] = "add";
        arguments[1] = "--";
        Array.Copy(pathspecs, 0, arguments, 2, pathspecs.Length);
        RunGit(directory, arguments);
    }

    public static void RunGit(string workingDirectory, params string[] arguments)
        => _ = RunGitCapture(workingDirectory, arguments);

    public static string RunGitCapture(string workingDirectory, params string[] arguments)
    {
        // Keep the synchronous fixture API; the shared runner owns the combined exit/drain
        // deadline and cancels and observes both readers before disposing their streams.
        var result = PwshScriptRunner.RunExecutableAsync(
            "git",
            arguments,
            workingDirectory,
            timeout: TimeSpan.FromSeconds(30),
            description: $"git {string.Join(' ', arguments)}").GetAwaiter().GetResult();
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited {result.ExitCode}. stdout=[{result.StdOut}] stderr=[{result.StdErr}]");
        }

        return result.StdOut;
    }
}
