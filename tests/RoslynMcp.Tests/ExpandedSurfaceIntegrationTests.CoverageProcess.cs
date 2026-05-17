using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[DoNotParallelize]
[TestClass]
[TestCategory("Process")]
public sealed class ExpandedSurfaceIntegrationTests_CoverageProcess : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task TestCoverageTool_Returns_Structured_Response()
    {
        var json = await TestCoverageTools.RunTestCoverage(
            WorkspaceExecutionGate,
            WorkspaceManager,
            DotnetCommandRunner,
            WorkspaceId,
            projectName: "SampleLib.Tests",
            progress: null,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("success", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("error", out _));
    }

    // test-coverage-vague-error-when-coverlet-missing: if the sample solution's test project
    // doesn't reference coverlet.collector, the tool must return success=false with
    // errorKind=CoverletMissing and a populated missingPackages list. If it DOES reference
    // coverlet, the tool should attempt to run tests (the test doesn't assert success of
    // that run, only that the short-circuit didn't fire when coverlet is present).
    [TestMethod]
    public async Task TestCoverageTool_WhenCoverletMissing_ReturnsStructuredErrorBeforeRunning()
    {
        var json = await TestCoverageTools.RunTestCoverage(
            WorkspaceExecutionGate,
            WorkspaceManager,
            DotnetCommandRunner,
            WorkspaceId,
            projectName: "SampleLib.Tests",
            progress: null,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var hasEnvelope = doc.RootElement.TryGetProperty("failureEnvelope", out var envelope)
            && envelope.ValueKind == JsonValueKind.Object;

        // Find whether the sample test csproj references coverlet. The regression test tracks
        // the branch that matches this fixture's state.
        var testProjectPath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib.Tests", "SampleLib.Tests.csproj");
        var hasCoverlet = File.Exists(testProjectPath) &&
            File.ReadAllText(testProjectPath).Contains("coverlet.collector", StringComparison.OrdinalIgnoreCase);

        if (!hasCoverlet)
        {
            Assert.IsTrue(hasEnvelope,
                $"Sample test project lacks coverlet.collector — tool must emit a structured envelope. JSON: {json}");
            Assert.AreEqual("CoverletMissing", envelope.GetProperty("errorKind").GetString());
            Assert.IsTrue(envelope.TryGetProperty("missingPackages", out var missing)
                && missing.ValueKind == JsonValueKind.Array
                && missing.GetArrayLength() >= 1,
                "missingPackages must list the test projects that need coverlet.");
            var names = missing.EnumerateArray().Select(e => e.GetString()).ToList();
            CollectionAssert.Contains(names, "SampleLib.Tests",
                "The sample test project name must appear in missingPackages.");
        }
        else
        {
            // Sample has coverlet — the short-circuit must NOT have fired, so either success
            // is true (coverage ran) or the envelope is something other than CoverletMissing.
            if (hasEnvelope && envelope.TryGetProperty("errorKind", out var kind))
            {
                Assert.AreNotEqual("CoverletMissing", kind.GetString(),
                    "Sample has coverlet but tool short-circuited as CoverletMissing.");
            }
        }
    }
}
