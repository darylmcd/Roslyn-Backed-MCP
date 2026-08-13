using System.Diagnostics;

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
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--version");
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                failureReason = "Process.Start returned null.";
                return false;
            }

            if (!process.WaitForExit(5_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1_000);
                    failureReason = "git --version timed out after 5 seconds.";
                }
                catch (Exception ex)
                {
                    failureReason =
                        $"git --version timed out and termination failed ({ex.GetType().Name}: {ex.Message}).";
                }

                return false;
            }

            if (process.ExitCode == 0)
            {
                failureReason = null;
                return true;
            }

            var stderr = process.StandardError.ReadToEnd().Trim();
            failureReason = string.IsNullOrWhiteSpace(stderr)
                ? $"git --version exited {process.ExitCode}."
                : $"git --version exited {process.ExitCode}: {stderr}";
            return false;
        }
        catch (Exception ex)
        {
            failureReason = $"{ex.GetType().Name}: {ex.Message}";
            return false;
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
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start git {string.Join(' ', arguments)}.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            Exception? terminationFailure = null;
            try
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(1_000))
                {
                    terminationFailure = new TimeoutException(
                        "The git process did not exit within one second after termination.");
                }
            }
            catch (Exception ex)
            {
                terminationFailure = ex;
            }

            throw new TimeoutException(
                $"git {string.Join(' ', arguments)} did not exit within 30 seconds.",
                terminationFailure);
        }
        var stderr = stderrTask.GetAwaiter().GetResult();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited {process.ExitCode}. stdout=[{stdout}] stderr=[{stderr}]");
        }

        return stdout;
    }
}
