using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Table-tests <c>eng/resolve-ci-topology.ps1</c>, the pure CI validation-topology decision
/// extracted from <c>.github/workflows/ci.yml</c>'s inline `route` job step. Every case here
/// invokes the real script as a process (no GitHub Actions expression interpolation involved),
/// exercising branches -- rename-path handling, the fail-closed count-mismatch/cap guard, and an
/// API-failure signal -- that were previously untestable text embedded in workflow YAML.
/// <see cref="CiRunnerParityContractTests"/> keeps the surrounding workflow-integration sentinels
/// (script invocation, output wiring, action pinning) that this file does not duplicate.
/// </summary>
[TestClass]
public sealed class CiTopologyDecisionContractTests
{
    private static readonly TimeSpan _processTimeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    [TestCategory("Process")]
    public async Task CodePullRequest_NonDocFile_RoutesFullMatrixWithExactLegSetAsync()
    {
        var result = await RunTopologyAsync(
            "pull_request",
            changedFilesJson: SinglePage(("src/Foo.cs", null)),
            reportedChangedFileCount: 1);

        AssertSucceeded(result);
        var decision = ParseDecision(result.StdOut);

        Assert.IsFalse(decision.DocsOnly);
        Assert.AreEqual("Code PR: four hosted Windows and two hosted Linux shards.", decision.Reason);
        AssertExactCodePullRequestMatrix(decision.RunnerMatrix);
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task DocsOnlyPullRequest_PureMarkdownAndAiDocsJson_RoutesTwoLinuxShardsAsync()
    {
        var result = await RunTopologyAsync(
            "pull_request",
            changedFilesJson: SinglePage(("README.md", null), ("ai_docs/plans/foo.json", null)),
            reportedChangedFileCount: 2);

        AssertSucceeded(result);
        var decision = ParseDecision(result.StdOut);

        Assert.IsTrue(decision.DocsOnly);
        Assert.AreEqual("Policy-only docs PR: two hosted Linux test shards.", decision.Reason);
        AssertExactDocsOnlyPullRequestMatrix(decision.RunnerMatrix);
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow("CHANGELOG.md")]
    [DataRow("skills/review/SKILL.md")]
    [DataRow(".claude/skills/release-cut/SKILL.md")]
    [DataRow("agents/audit-phase-runner.md")]
    [DataRow(".claude/agents/pr-reconciler.md")]
    [DataRow(".github/prompts/review.md")]
    public async Task BehaviorBearingMarkdown_ForcesFullValidationEvenAsTheSoleChangeAsync(string path)
    {
        var result = await RunTopologyAsync(
            "pull_request",
            changedFilesJson: SinglePage((path, null)),
            reportedChangedFileCount: 1);

        AssertSucceeded(result);
        var decision = ParseDecision(result.StdOut);

        Assert.IsFalse(decision.DocsOnly, $"'{path}' is behavior-bearing and must force full validation.");
        Assert.AreEqual("Code PR: four hosted Windows and two hosted Linux shards.", decision.Reason);
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task NonBehaviorBearingDocPath_StaysDocsOnlyAsync()
    {
        var result = await RunTopologyAsync(
            "pull_request",
            changedFilesJson: SinglePage(("docs/setup.md", null)),
            reportedChangedFileCount: 1);

        AssertSucceeded(result);
        var decision = ParseDecision(result.StdOut);

        Assert.IsTrue(decision.DocsOnly);
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Rename_PreviousNonDocPath_ForcesFullValidationAsync()
    {
        // A source file renamed to a Markdown path is still code-bearing: the removed source
        // path itself required full validation and must not be dropped from classification.
        var result = await RunTopologyAsync(
            "pull_request",
            changedFilesJson: SinglePage(("docs/new.md", "src/Old.cs")),
            reportedChangedFileCount: 1);

        AssertSucceeded(result);
        var decision = ParseDecision(result.StdOut);

        Assert.IsFalse(decision.DocsOnly);
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Rename_DocToDocPath_StaysDocsOnlyAsync()
    {
        var result = await RunTopologyAsync(
            "pull_request",
            changedFilesJson: SinglePage(("docs/new-name.md", "docs/old-name.md")),
            reportedChangedFileCount: 1);

        AssertSucceeded(result);
        var decision = ParseDecision(result.StdOut);

        Assert.IsTrue(decision.DocsOnly);
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task MultiplePages_AreFlattenedIntoOneCompleteEnumerationAsync()
    {
        var changedFilesJson = BuildChangedFilesJson(
        [
            [("README.md", null)],
            [("ai_docs/plans/foo.json", null)],
        ]);

        var result = await RunTopologyAsync(
            "pull_request",
            changedFilesJson: changedFilesJson,
            reportedChangedFileCount: 2);

        AssertSucceeded(result);
        var decision = ParseDecision(result.StdOut);

        Assert.IsTrue(decision.DocsOnly, "Both pages together are pure documentation; docs-only routing must still apply.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task CountMismatch_FailsClosedToFullValidationEvenWhenAllFilesAreDocShapedAsync()
    {
        var result = await RunTopologyAsync(
            "pull_request",
            changedFilesJson: SinglePage(("README.md", null)),
            reportedChangedFileCount: 2); // GitHub reports 2; the files API only enumerated 1.

        AssertSucceeded(result);
        var decision = ParseDecision(result.StdOut);

        Assert.IsFalse(
            decision.DocsOnly,
            "A capped or incomplete files-API enumeration must never be trusted to justify docs-only routing.");
        AssertExactCodePullRequestMatrix(decision.RunnerMatrix);
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task EnumerationAtPaginationCeiling_FailsClosedToFullValidationAsync()
    {
        var repeatedFile = ("docs/repeated.md", (string?)null);
        var changedFilesJson = BuildChangedFilesJson(
        [
            Enumerable.Repeat(repeatedFile, 3000).ToArray(),
        ]);

        // 3000 repeated records overflow a single Windows command-line argument (~32K chars), so
        // this large payload goes through -ChangedFilesJsonPath instead of the inline CLI
        // argument every other case in this file uses.
        var payloadPath = Path.Combine(
            TestTempRoot.Current,
            nameof(CiTopologyDecisionContractTests),
            $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
        await File.WriteAllTextAsync(payloadPath, changedFilesJson);

        var result = await RunTopologyAsync(
            "pull_request",
            changedFilesJsonPath: payloadPath,
            reportedChangedFileCount: 3000); // Count matches; the cap alone must still fail closed.

        AssertSucceeded(result);
        var decision = ParseDecision(result.StdOut);

        Assert.IsFalse(
            decision.DocsOnly,
            "Reaching GitHub's files-API pagination ceiling must fail closed even with a matching reported count.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task EnumerationFailed_FailsClosedWithADistinctReasonAsync()
    {
        var result = await RunTopologyAsync("pull_request", enumerationFailed: true);

        AssertSucceeded(result);
        var decision = ParseDecision(result.StdOut);

        Assert.IsFalse(decision.DocsOnly);
        Assert.AreEqual(
            "Pull-request file listing could not be verified (API failure); routing full validation.",
            decision.Reason);
        AssertExactCodePullRequestMatrix(decision.RunnerMatrix);
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow("workflow_dispatch")]
    [DataRow("schedule")]
    public async Task DispatchAndSchedule_RouteOneUnshardedLinuxLegAsync(string eventName)
    {
        var result = await RunTopologyAsync(eventName);

        AssertSucceeded(result);
        var decision = ParseDecision(result.StdOut);

        Assert.IsFalse(decision.DocsOnly);
        Assert.AreEqual("Dispatch/schedule: one unsharded Linux coverage leg.", decision.Reason);
        Assert.AreEqual(1, decision.RunnerMatrix.Length);
        var leg = decision.RunnerMatrix[0];
        Assert.AreEqual("linux-full", leg.Name);
        Assert.AreEqual("ubuntu-latest", leg.RunsOn);
        Assert.IsTrue(leg.ArtifactOwner);
        Assert.AreEqual(45, leg.TimeoutMinutes);
        Assert.AreEqual(0, leg.TestShardIndex);
        Assert.AreEqual(1, leg.TestShardCount);
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task PullRequestWithoutReportedCountOrEnumerationFailed_FailsClosedAsync()
    {
        var result = await RunTopologyAsync(
            "pull_request",
            changedFilesJson: SinglePage(("README.md", null)));

        Assert.AreNotEqual(0, result.ExitCode, "Missing ReportedChangedFileCount must fail closed, not default to a route.");
        StringAssert.Contains(result.AllOutput, "ReportedChangedFileCount is required");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task SameInputs_ProduceByteIdenticalJsonAsync()
    {
        var changedFilesJson = SinglePage(("src/Foo.cs", null));

        var first = await RunTopologyAsync("pull_request", changedFilesJson: changedFilesJson, reportedChangedFileCount: 1);
        var second = await RunTopologyAsync("pull_request", changedFilesJson: changedFilesJson, reportedChangedFileCount: 1);

        AssertSucceeded(first);
        AssertSucceeded(second);
        Assert.AreEqual(
            first.StdOut.Trim(),
            second.StdOut.Trim(),
            "The same inputs must produce byte-identical JSON so downstream fromJSON consumption never drifts.");
    }

    private static void AssertExactCodePullRequestMatrix(CiTopologyLeg[] matrix)
    {
        var expected = new (string Name, string RunsOn, bool ArtifactOwner, int TimeoutMinutes, int ShardIndex, int ShardCount)[]
        {
            ("windows-hosted-1-of-4", "windows-latest", false, 45, 0, 4),
            ("windows-hosted-2-of-4", "windows-latest", false, 45, 1, 4),
            ("windows-hosted-3-of-4", "windows-latest", false, 45, 2, 4),
            ("windows-hosted-4-of-4", "windows-latest", false, 45, 3, 4),
            ("linux-1-of-2", "ubuntu-latest", true, 30, 0, 2),
            ("linux-2-of-2", "ubuntu-latest", false, 30, 1, 2),
        };

        AssertExactMatrix(matrix, expected);
        AssertCompleteShardUnion(matrix, "windows-latest", expectedShardCount: 4);
        AssertCompleteShardUnion(matrix, "ubuntu-latest", expectedShardCount: 2);
        AssertSoleArtifactOwner(matrix);
    }

    private static void AssertExactDocsOnlyPullRequestMatrix(CiTopologyLeg[] matrix)
    {
        var expected = new (string Name, string RunsOn, bool ArtifactOwner, int TimeoutMinutes, int ShardIndex, int ShardCount)[]
        {
            ("docs-linux-1-of-2", "ubuntu-latest", true, 30, 0, 2),
            ("docs-linux-2-of-2", "ubuntu-latest", false, 30, 1, 2),
        };

        AssertExactMatrix(matrix, expected);
        AssertCompleteShardUnion(matrix, "ubuntu-latest", expectedShardCount: 2);
        AssertSoleArtifactOwner(matrix);
    }

    private static void AssertExactMatrix(
        CiTopologyLeg[] matrix,
        (string Name, string RunsOn, bool ArtifactOwner, int TimeoutMinutes, int ShardIndex, int ShardCount)[] expected)
    {
        Assert.AreEqual(expected.Length, matrix.Length, "Leg count must match exactly -- no extra or missing legs.");
        for (var index = 0; index < expected.Length; index++)
        {
            var leg = matrix.SingleOrDefault(candidate => candidate.Name == expected[index].Name)
                ?? throw new AssertFailedException($"Expected leg '{expected[index].Name}' was not present.");
            Assert.AreEqual(expected[index].RunsOn, leg.RunsOn, $"{leg.Name}.runs_on");
            Assert.AreEqual(expected[index].ArtifactOwner, leg.ArtifactOwner, $"{leg.Name}.artifact_owner");
            Assert.AreEqual(expected[index].TimeoutMinutes, leg.TimeoutMinutes, $"{leg.Name}.timeout_minutes");
            Assert.AreEqual(expected[index].ShardIndex, leg.TestShardIndex, $"{leg.Name}.test_shard_index");
            Assert.AreEqual(expected[index].ShardCount, leg.TestShardCount, $"{leg.Name}.test_shard_count");
        }
    }

    private static void AssertCompleteShardUnion(CiTopologyLeg[] matrix, string runsOn, int expectedShardCount)
    {
        var shardIndexes = matrix
            .Where(leg => leg.RunsOn == runsOn)
            .Select(leg => leg.TestShardIndex)
            .OrderBy(index => index)
            .ToArray();
        Assert.AreEqual(expectedShardCount, shardIndexes.Length, $"'{runsOn}' shard count");
        CollectionAssert.AreEqual(
            Enumerable.Range(0, expectedShardCount).ToArray(),
            shardIndexes,
            $"'{runsOn}' shard indices must form a complete, gap-free, non-overlapping partition.");
        Assert.IsTrue(
            matrix.Where(leg => leg.RunsOn == runsOn).All(leg => leg.TestShardCount == expectedShardCount),
            $"Every '{runsOn}' leg must report the same test_shard_count.");
    }

    private static void AssertSoleArtifactOwner(CiTopologyLeg[] matrix)
        => Assert.AreEqual(
            1,
            matrix.Count(leg => leg.ArtifactOwner),
            "Exactly one leg in the matrix must own policy/release artifacts.");

    private static void AssertSucceeded(PwshScriptResult result)
        => Assert.AreEqual(
            0,
            result.ExitCode,
            $"Resolver failed. stdout={result.StdOut} stderr={result.StdErr}");

    private static CiTopologyDecision ParseDecision(string json)
        => JsonSerializer.Deserialize<CiTopologyDecision>(json)
            ?? throw new InvalidOperationException("Resolver returned JSON null.");

    private static string SinglePage(params (string Filename, string? PreviousFilename)[] files)
        => BuildChangedFilesJson([files]);

    private static string BuildChangedFilesJson(IReadOnlyList<IReadOnlyList<(string Filename, string? PreviousFilename)>> pages)
    {
        var pagesPayload = pages
            .Select(page => page
                .Select(file => file.PreviousFilename is null
                    ? new Dictionary<string, string> { ["filename"] = file.Filename }
                    : new Dictionary<string, string>
                    {
                        ["filename"] = file.Filename,
                        ["previous_filename"] = file.PreviousFilename,
                    })
                .ToArray())
            .ToArray();

        return JsonSerializer.Serialize(pagesPayload);
    }

    private static Task<PwshScriptResult> RunTopologyAsync(
        string eventName,
        string? changedFilesJson = null,
        string? changedFilesJsonPath = null,
        int? reportedChangedFileCount = null,
        bool enumerationFailed = false)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "eng", "resolve-ci-topology.ps1");
        var arguments = new List<string>
        {
            "-NoProfile",
            "-File",
            scriptPath,
            "-EventName",
            eventName,
        };
        if (changedFilesJson is not null)
        {
            arguments.Add("-ChangedFilesJson");
            arguments.Add(changedFilesJson);
        }
        if (changedFilesJsonPath is not null)
        {
            arguments.Add("-ChangedFilesJsonPath");
            arguments.Add(changedFilesJsonPath);
        }
        if (reportedChangedFileCount is not null)
        {
            arguments.Add("-ReportedChangedFileCount");
            arguments.Add(reportedChangedFileCount.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (enumerationFailed)
        {
            arguments.Add("-EnumerationFailed");
        }

        return PwshScriptRunner.RunAsync(
            arguments,
            workingDirectory: repositoryRoot,
            timeout: _processTimeout,
            description: "CI topology resolver");
    }

    private sealed record CiTopologyDecision(
        [property: JsonPropertyName("docs_only")] bool DocsOnly,
        [property: JsonPropertyName("runner_matrix")] CiTopologyLeg[] RunnerMatrix,
        [property: JsonPropertyName("reason")] string Reason);

    private sealed record CiTopologyLeg(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("runs_on")] string RunsOn,
        [property: JsonPropertyName("artifact_owner")] bool ArtifactOwner,
        [property: JsonPropertyName("timeout_minutes")] int TimeoutMinutes,
        [property: JsonPropertyName("test_shard_index")] int TestShardIndex,
        [property: JsonPropertyName("test_shard_count")] int TestShardCount);
}
