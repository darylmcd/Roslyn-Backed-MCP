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
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml");
        return File.ReadAllText(workflowPath).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

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
            "$selfHostedMatrix = '[{\"runs_on\":[\"self-hosted\",\"roslynmcp-dev\"],\"os\":\"windows\",\"primary\":true},{\"runs_on\":\"ubuntu-latest\",\"os\":\"linux\",\"primary\":false}]'");

        // Hosted routes stay single-leg (primary) so the Linux leg is never
        // duplicated when routing already targets ubuntu-latest.
        StringAssert.Contains(
            workflow,
            "$hostedMatrix = '[{\"runs_on\":\"ubuntu-latest\",\"os\":\"linux\",\"primary\":true}]'");

        // Every runs_on emission must be paired with a runner_matrix emission,
        // so no routing branch can leave validate's matrix unset.
        var runsOnEmissions = CountOccurrences(workflow, "\"runs_on=$");
        var matrixEmissions = CountOccurrences(workflow, "\"runner_matrix=$");
        Assert.AreEqual(runsOnEmissions, matrixEmissions,
            "every route branch that emits runs_on must also emit runner_matrix");
        Assert.IsTrue(matrixEmissions >= 5, $"expected at least 5 routing branches, found {matrixEmissions}");
    }

    [TestMethod]
    public void PullRequestVerifyRelease_IsNotNarrowedBeyondDocumentedFlags()
    {
        var workflow = LoadCiWorkflow();

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
}
