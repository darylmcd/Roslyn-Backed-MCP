namespace RoslynMcp.Tests;

/// <summary>
/// Guards the merge/publish OS-parity contract in .github/workflows/ci.yml:
/// the validate job must fan out over the route job's runner matrix, the
/// self-hosted (maintainer PR) route must carry an ubuntu-latest leg so the
/// pre-merge gate shares OS coverage with the Linux publish gate, and the
/// pull_request verify-release invocation must not be narrowed beyond the
/// documented -NoCoverage -ExcludeNetworkTests flags. A future edit that
/// silently drops the Linux leg or filters its test suite re-opens the
/// v3.0.1 hole (Windows-only pre-merge pass, Linux publish-gate failure at
/// tag time) and must fail here instead.
/// </summary>
[TestClass]
public sealed class CiRunnerParityContractTests
{
    private static string LoadCiWorkflow()
        => LoadRepositoryFile(".github", "workflows", "ci.yml");

    [TestMethod]
    public void Validate_FansOutOverRouteRunnerMatrix()
    {
        var workflow = LoadCiWorkflow();

        StringAssert.Contains(workflow, "leg: ${{ fromJSON(needs.route.outputs.runner_matrix) }}");
        StringAssert.Contains(workflow, "runs-on: ${{ matrix.leg.runs_on }}");
        StringAssert.Contains(workflow, "fail-fast: false");
        StringAssert.Contains(workflow, "runner_matrix: ${{ steps.decide.outputs.runner_matrix }}");
    }

    [TestMethod]
    public void SelfHostedRoute_CarriesAnUbuntuLatestParityLeg()
    {
        var workflow = LoadCiWorkflow();

        // The two-leg matrix emitted whenever route targets the self-hosted
        // Windows runner: primary Windows leg + non-primary Linux parity leg.
        StringAssert.Contains(
            workflow,
            "[ordered]@{ runs_on = $selfHostedLabels; os = 'windows'; primary = $true },");
        StringAssert.Contains(
            workflow,
            "[ordered]@{ runs_on = $hostedLabel;      os = 'linux';   primary = $false }");

        // Hosted routes stay single-leg (primary) so the Linux leg is never
        // duplicated when routing already targets ubuntu-latest.
        StringAssert.Contains(
            workflow,
            "[ordered]@{ runs_on = $hostedLabel;      os = 'linux';   primary = $true }");

        // -AsArray is load-bearing: without it the single-leg hosted payload
        // serializes as a bare JSON object and fromJSON produces no matrix.
        Assert.AreEqual(2, CountOccurrences(workflow, "ConvertTo-Json -Compress -Depth 5 -AsArray"),
            "both matrix payloads must be serialized with -AsArray");

        // Every routing branch must emit runner_matrix — it is now the route
        // job's ONLY output, so a branch that skips it leaves validate unset.
        // Branches: non-maintainer, missing PAT, runner online, runner offline,
        // and the probe-failure catch.
        const int routingBranches = 5;
        var matrixEmissions = CountOccurrences(workflow, "\"runner_matrix=$");
        Assert.AreEqual(routingBranches, matrixEmissions,
            $"every one of the {routingBranches} routing branches must emit runner_matrix");
        Assert.AreEqual(0, CountOccurrences(workflow, "\"runs_on=$"),
            "the legacy single-target runs_on output has no consumers and must stay deleted");
    }

    [TestMethod]
    public void RunnerLabels_AreSingleSourcedInTheRouteJob()
    {
        var workflow = LoadCiWorkflow();

        // The label list feeds BOTH matrix payloads and the online-runner
        // filter. A second hardcoded copy can drift out of sync and silently
        // disable the hosted fallback.
        StringAssert.Contains(workflow, "$selfHostedLabels = @('self-hosted', 'roslynmcp-dev')");
        StringAssert.Contains(workflow, "$selfHostedRunnerLabel = $selfHostedLabels[-1]");
        StringAssert.Contains(workflow, "-contains $selfHostedRunnerLabel)");
        Assert.AreEqual(1, CountOccurrences(workflow, "roslynmcp-dev"),
            "the roslynmcp-dev label must appear exactly once in ci.yml (the $selfHostedLabels definition)");
    }

    [TestMethod]
    public void PullRequestAndLocalCi_UseTheSameDocumentedReleaseLane()
    {
        var workflow = LoadCiWorkflow();
        var justfile = LoadRepositoryFile("justfile");

        // Both matrix legs run this identical entry point — the same script the
        // publish gate runs on ubuntu-latest. The ONLY permitted narrowing on
        // pull_request is -NoCoverage -ExcludeNetworkTests (see CI_POLICY.md).
        var prRunLine = workflow
            .Split('\n')
            .Select(line => line.Trim())
            .SingleOrDefault(line =>
                line.StartsWith("run: ./eng/verify-release.ps1", StringComparison.Ordinal)
                && line.Contains("-NoCoverage", StringComparison.Ordinal));

        Assert.IsNotNull(prRunLine, "pull_request verify-release invocation not found in ci.yml");
        Assert.AreEqual(
            "run: ./eng/verify-release.ps1 -Configuration Release -NoCoverage -ExcludeNetworkTests",
            prRunLine,
            "the pull_request verify-release invocation must carry exactly the documented flags — no --filter or other test narrowing");
        Assert.IsFalse(workflow.Contains("verify-release.ps1 --filter", StringComparison.Ordinal));

        StringAssert.Contains(
            justfile,
            "ci: verify-docs verify-skills verify-release-pr vuln-audit",
            "just ci must compose the same required validation concerns as pull-request CI.");
        StringAssert.Contains(
            justfile,
            "verify-release-pr:\n" +
            "    pwsh -NoProfile -File ./eng/verify-release.ps1 -NoCoverage -ExcludeNetworkTests",
            "the local pull-request recipe must carry exactly the documented release-validation flags.");
        StringAssert.Contains(
            justfile,
            "full: verify-docs verify-skills verify-release vuln-audit",
            "just full must retain the explicit coverage and live-network validation lane.");
    }

    [TestMethod]
    public void ReleasePolicy_StatesTheCanonicalCiTriggerContract()
    {
        var policy = LoadRepositoryFile("docs", "release-policy.md");
        var normalizedPolicy = policy.Replace('\n', ' ');

        StringAssert.Contains(
            normalizedPolicy,
            "CI runs on pull requests, manual dispatch, and the weekly schedule.");
        StringAssert.Contains(
            normalizedPolicy,
            "Push-to-`main` is intentionally omitted because protected-branch changes arrive through a validated PR;");
        StringAssert.Contains(
            normalizedPolicy,
            "see `CI_POLICY.md` for the canonical trigger and runner contract.");
        Assert.IsFalse(
            policy.Contains("CI runs on every PR and `main`", StringComparison.Ordinal),
            "The retired push-to-main trigger claim must not return.");
    }

    [TestMethod]
    public void PublicReleaseDocs_UseCanonicalVulnerabilityAuditVerifier()
    {
        var releasePolicy = LoadRepositoryFile("docs", "release-policy.md");
        var upgradeMatrix = LoadRepositoryFile("docs", "upgrade-matrix.md");

        foreach (var document in new[] { releasePolicy, upgradeMatrix })
        {
            StringAssert.Contains(document, "eng/verify-nuget-audit.ps1");
            Assert.IsFalse(
                document.Contains("dotnet package list", StringComparison.OrdinalIgnoreCase),
                "Public release guidance must not bypass the fail-closed audit verifier with the crash-prone package-list walk.");
        }
    }

    [TestMethod]
    public void ArtifactUploads_AreScopedToThePrimaryLeg()
    {
        var workflow = LoadCiWorkflow();

        // upload-artifact v4+ fails on duplicate names within one run, and the
        // artifact names are cited by docs — uploads must run on exactly one leg.
        StringAssert.Contains(workflow,
            "if: steps.detect.outputs.docs_only != 'true' && matrix.leg.primary == true");
        StringAssert.Contains(workflow,
            "if: github.event_name != 'pull_request' && matrix.leg.primary == true");
    }

    [TestMethod]
    public void ValidateGate_IsTheStableAggregatorForBranchProtection()
    {
        var workflow = LoadCiWorkflow();

        StringAssert.Contains(workflow, "  validate-gate:\n");
        var gateBlock = workflow[workflow.IndexOf("  validate-gate:\n", StringComparison.Ordinal)..];
        StringAssert.Contains(gateBlock, "needs: validate");
        StringAssert.Contains(gateBlock, "if: always()");
        StringAssert.Contains(gateBlock, "${{ needs.validate.result }}");

        // The default-branch ruleset requires exactly one context: "validate".
        // The aggregator must carry that name so the matrix fan-out needs no
        // ruleset change, and the per-leg jobs must NOT collide with it —
        // otherwise the required context is never reported and every PR in the
        // repo becomes permanently unmergeable.
        StringAssert.Contains(gateBlock, "name: validate\n");
        StringAssert.Contains(workflow, "    name: validate-leg (${{ matrix.leg.os }})\n");
        Assert.AreEqual(1, CountOccurrences(workflow, "\n    name: validate\n"),
            "exactly one job may be named 'validate' — the branch-protection aggregator");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string LoadRepositoryFile(params string[] relativePathSegments)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var path = Path.Combine([repositoryRoot, .. relativePathSegments]);
        return File.ReadAllText(path).ReplaceLineEndings("\n");
    }
}
