using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests.Support;

/// <summary>
/// Shared git helpers for tests. Two distinct usages, both supported:
/// <list type="number">
///   <item>
///     Building a small on-disk git repo around the SampleSolution fixture —
///     <see cref="StageFixtureBaselineAsync"/> / <see cref="StageAndCommitAllAsync"/> plus
///     <see cref="RunGitAsync"/>. Extracted from <c>ValidateRecentGitChangesTests</c>
///     and <c>ValidateWorkspaceChangeTrackerReconcileTests</c> where the same
///     implementation was duplicated verbatim.
///   </item>
///   <item>
///     Read-only git queries against the REAL repository root — e.g.
///     <c>McpServerSurfaceTestSkillTests.CanonicalPromotionScorecard_IsGitTracked</c>
///     calls <see cref="RunGitCaptureAsync"/> with <c>git ls-files</c> to assert a
///     path's tracked state. Only non-mutating porcelain/plumbing queries are
///     acceptable against the real root; the staging and commit helpers above
///     are strictly for throwaway fixture directories.
///   </item>
/// </list>
/// <see cref="RequireAvailableAsync"/> gates both usages so a machine without git on PATH
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

    public static async Task RequireAvailableAsync()
    {
        try
        {
            _ = await RunGitCaptureAsync(Environment.CurrentDirectory, "--version");
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
        {
            Assert.Inconclusive($"git is required for repository fixtures: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public static async Task InitializeRepositoryAsync(string directory, string? originUrl = null)
    {
        // Force a named initial branch so fixtures never inherit machine-specific
        // init.defaultBranch settings or remain on an ambiguous unborn HEAD.
        await RunGitAsync(directory, "init", "-q", "-b", "main");
        if (!Directory.Exists(Path.Combine(directory, ".git")))
        {
            throw new InvalidOperationException(
                $"git init appeared to succeed but '.git' is missing in '{directory}'.");
        }

        File.WriteAllText(Path.Combine(directory, ".gitignore"), "bin/\nobj/\n");
        await RunGitAsync(directory, "config", "--local", "user.email", "ci@example.invalid");
        await RunGitAsync(directory, "config", "--local", "user.name", "CI");
        await RunGitAsync(directory, "config", "--local", "commit.gpgsign", "false");
        await RunGitAsync(directory, "config", "--local", "core.autocrlf", "false");

        if (!string.IsNullOrWhiteSpace(originUrl))
        {
            await RunGitAsync(directory, "remote", "add", "origin", originUrl);
        }
    }

    public static async Task StageAndCommitAllAsync(string directory)
    {
        await StageFixtureBaselineAsync(directory);
        await RunGitAsync(directory, "commit", "-q", "-m", "seed");
    }

    public static async Task StageFixtureBaselineAsync(string directory)
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
        await RunGitAsync(directory, arguments);
    }

    public static async Task RunGitAsync(string workingDirectory, params string[] arguments)
        => _ = await RunGitCaptureAsync(workingDirectory, arguments);

    public static async Task<string> RunGitCaptureAsync(string workingDirectory, params string[] arguments)
    {
        // Await the shared exit/drain deadline without blocking a test worker.
        var result = await PwshScriptRunner.RunExecutableAsync(
            "git",
            arguments,
            environment: null,
            workingDirectory: workingDirectory,
            timeout: TimeSpan.FromSeconds(30),
            description: $"git {string.Join(' ', arguments)}");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited {result.ExitCode}. stdout=[{result.StdOut}] stderr=[{result.StdErr}]");
        }

        return result.StdOut;
    }
}
