using System.Diagnostics;

namespace RoslynMcp.Tests.Support;

/// <summary>
/// Shared helpers for tests that build a small on-disk git repo around the
/// SampleSolution fixture (stage baseline files, commit, run plain git
/// commands). Extracted from <c>ValidateRecentGitChangesTests</c> and
/// <c>ValidateWorkspaceChangeTrackerReconcileTests</c> where the same
/// implementation was duplicated verbatim.
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
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort. The timeout failure below is the actionable signal.
            }

            throw new TimeoutException($"git {string.Join(' ', arguments)} did not exit within 30 seconds.");
        }
        var stderr = stderrTask.GetAwaiter().GetResult();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited {process.ExitCode}. stdout=[{stdout}] stderr=[{stderr}]");
        }
    }
}
