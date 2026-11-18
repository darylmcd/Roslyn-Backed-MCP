using System.Text.Json;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class LinuxShardBuildTimesContractTests
{
    [TestMethod]
    [TestCategory("Process")]
    public async Task Collect_OrdersSuccessfulNamedStepsFromInProgressJobsAsync()
    {
        // The last job belongs to another matrix shape (the single policy-docs leg) and must be ignored.
        var result = await RunCollectorAsync("linux-2-of-4",
            Pages([Job("linux-3-of-4", 40), Job("linux-1-of-4", 61)],
                [Job("linux-4-of-4", 53), Job("linux-2-of-4", 59), Job("docs-linux-1-of-1", 88)]));

        Assert.AreEqual(0, result.ExitCode, result.StdErr);
        CollectionAssert.AreEqual(new double[] { 61, 59, 40, 53 },
            JsonSerializer.Deserialize<double[]>(result.StdOut));
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow("docs-linux-1-of-4")]
    [DataRow("windows-hosted-1-of-4")]
    public async Task Collect_RejectsALegOutsideTheShardedLinuxCodeRouteAsync(string legName)
    {
        var result = await RunCollectorAsync(legName, Pages([Job(legName, 61)]));

        Assert.AreNotEqual(0, result.ExitCode, result.StdOut);
        // The error formatter wraps a message this long at the host's console width and styles
        // it, so the raw stream holds the sentence split across gutter lines on hosted Linux.
        StringAssert.Contains(
            PowerShellOutputNormalizer.Normalize(result.StdErr),
            "Linux build calibration requires the current Actions run, attempt and shard identity.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Collect_PartialRerunPreservesFirstSuccessfulBuildPerShardAsync()
    {
        var result = await RunCollectorAsync("linux-2-of-4",
            Pages([Job("linux-1-of-4", 61), Job("linux-2-of-4", 59, "failure"),
                Job("linux-3-of-4", 40), Job("linux-4-of-4", 53)]),
            Pages([Job("linux-2-of-4", 57), Job("linux-1-of-4", 31)]));

        Assert.AreEqual(0, result.ExitCode, result.StdErr);
        CollectionAssert.AreEqual(new double[] { 61, 57, 40, 53 },
            JsonSerializer.Deserialize<double[]>(result.StdOut));
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Collect_RejectsDuplicateShardJobInOneAttemptAsync()
    {
        var result = await RunCollectorAsync("linux-1-of-4",
            Pages([Job("linux-1-of-4", 61), Job("linux-1-of-4", 62)]));

        Assert.AreNotEqual(0, result.ExitCode, result.StdOut);
        StringAssert.Contains(result.StdErr, "Unexpected calibration job identity");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Collect_RejectsNonpositiveSuccessfulMeasurementAsync()
    {
        var result = await RunCollectorAsync("linux-1-of-4", Pages([Job("linux-1-of-4", 0)]));

        Assert.AreNotEqual(0, result.ExitCode, result.StdOut);
        StringAssert.Contains(result.StdErr, "Invalid build duration");
    }

    private static object Job(string leg, int buildSeconds, string conclusion = "success")
    {
        var started = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        return new
        {
            name = $"validate-leg ({leg})",
            run_id = 198725,
            status = "in_progress",
            steps = new[]
            {
                new
                {
                    name = "Measure Linux Release build",
                    status = "completed",
                    conclusion,
                    started_at = started,
                    completed_at = started.AddSeconds(buildSeconds),
                },
            },
        };
    }

    private static string Pages(params object[][] pages) =>
        JsonSerializer.Serialize(pages.Select(jobs => new { jobs }));

    private static Task<PwshScriptResult> RunCollectorAsync(string legName, params string[] attempts)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        return PwshScriptRunner.RunAsync(
            [
                "-NoProfile", "-Command",
                """
                $responses = ConvertFrom-Json -InputObject $env:ROSLYNMCP_TEST_BUILD_TIMES_RESPONSES -NoEnumerate
                function gh {
                    $endpoint = $args[-1]
                    if ($endpoint -notmatch '/runs/198725/attempts/([1-9][0-9]*)/jobs\?per_page=100$') {
                        throw "Unexpected request: $endpoint"
                    }
                    $global:LASTEXITCODE = 0
                    $responses[[int]$Matches[1] - 1]
                }

                & $env:ROSLYNMCP_TEST_BUILD_TIMES_SCRIPT -LegName $env:ROSLYNMCP_TEST_BUILD_TIMES_LEG -ShardCount 4
                """,
            ],
            workingDirectory: repositoryRoot,
            environment: new Dictionary<string, string?>
            {
                ["GITHUB_REPOSITORY"] = "fixture/fixture",
                ["GITHUB_RUN_ID"] = "198725",
                ["GITHUB_RUN_ATTEMPT"] = attempts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ROSLYNMCP_TEST_BUILD_TIMES_RESPONSES"] = JsonSerializer.Serialize(attempts),
                ["ROSLYNMCP_TEST_BUILD_TIMES_SCRIPT"] =
                    Path.Combine(repositoryRoot, "eng", "resolve-linux-shard-build-times.ps1"),
                ["ROSLYNMCP_TEST_BUILD_TIMES_LEG"] = legName,
            },
            timeout: TimeSpan.FromSeconds(20),
            description: "hosted Linux build measurement resolver");
    }
}
