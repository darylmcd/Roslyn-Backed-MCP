namespace RoslynMcp.Tests;

[TestClass]
public sealed class PublishWorkflowContractTests
{
    [TestMethod]
    public void PublishWorkflow_UsesTypedDryRunTruthTable()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "publish-nuget.yml");
        var workflow = File.ReadAllText(workflowPath).Replace("\r\n", "\n", StringComparison.Ordinal);

        StringAssert.Contains(workflow, "default: false\n        type: boolean");
        Assert.AreEqual(
            "${{ github.event_name == 'release' || github.event_name == 'push' || !inputs.dry_run }}",
            GetStepCondition(workflow, "Push to NuGet"));
        Assert.AreEqual(
            "${{ github.event_name == 'workflow_dispatch' && inputs.dry_run }}",
            GetStepCondition(workflow, "Dry run notice"));

        var cases = new[]
        {
            new PublishCase("tag push", "push", DryRun: false, ShouldPush: true, ShouldNotice: false),
            new PublishCase("GitHub release", "release", DryRun: false, ShouldPush: true, ShouldNotice: false),
            new PublishCase("manual publish", "workflow_dispatch", DryRun: false, ShouldPush: true, ShouldNotice: false),
            new PublishCase("manual dry run", "workflow_dispatch", DryRun: true, ShouldPush: false, ShouldNotice: true),
        };

        foreach (var testCase in cases)
        {
            var shouldPush = testCase.EventName is "release" or "push" || !testCase.DryRun;
            var shouldNotice = testCase.EventName == "workflow_dispatch" && testCase.DryRun;

            Assert.AreEqual(testCase.ShouldPush, shouldPush, $"{testCase.Name}: push condition");
            Assert.AreEqual(testCase.ShouldNotice, shouldNotice, $"{testCase.Name}: notice condition");
        }
    }

    [TestMethod]
    public void PublishWorkflow_RegistryJobRunsOnTagReleaseOrOptedInDispatch()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "publish-nuget.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        StringAssert.Contains(workflow, "      publish_registry:\n");
        StringAssert.Contains(workflow, "default: false\n        type: boolean\n\njobs:");

        var jobStart = workflow.IndexOf("  publish-registry:\n", StringComparison.Ordinal);
        Assert.IsTrue(jobStart >= 0, "publish-registry job was not found.");
        var condition = workflow[jobStart..]
            .Split('\n')
            .Select(line => line.Trim())
            .First(line => line.StartsWith("if: ", StringComparison.Ordinal))[4..];
        Assert.AreEqual(
            "${{ github.event_name == 'push' || github.event_name == 'release' || (github.event_name == 'workflow_dispatch' && inputs.publish_registry && !inputs.dry_run) }}",
            condition);

        foreach (var (eventName, dryRun, publishRegistry, expected) in new[]
        {
            ("push", false, false, true),
            ("release", false, false, true),
            ("workflow_dispatch", false, false, false),
            ("workflow_dispatch", false, true, true),
            ("workflow_dispatch", true, true, false),
        })
        {
            var runs = eventName is "push" or "release"
                || (eventName == "workflow_dispatch" && publishRegistry && !dryRun);
            Assert.AreEqual(expected, runs, $"{eventName} dry_run={dryRun} publish_registry={publishRegistry}");
        }

        StringAssert.Contains(workflow, "elif [ \"$PUBLISH_EVENT_NAME\" = \"workflow_dispatch\" ]; then");
    }

    private static string GetStepCondition(string workflow, string stepName)
    {
        var stepMarker = $"      - name: {stepName}\n";
        var stepStart = workflow.IndexOf(stepMarker, StringComparison.Ordinal);
        Assert.IsTrue(stepStart >= 0, $"Workflow step '{stepName}' was not found.");

        var nextStep = workflow.IndexOf("\n      - name:", stepStart + stepMarker.Length, StringComparison.Ordinal);
        var stepEnd = nextStep >= 0 ? nextStep : workflow.Length;
        var stepBlock = workflow[stepStart..stepEnd];
        var conditionLine = stepBlock
            .Split('\n')
            .Select(line => line.Trim())
            .SingleOrDefault(line => line.StartsWith("if: ", StringComparison.Ordinal));

        Assert.IsNotNull(conditionLine, $"Workflow step '{stepName}' has no if condition.");
        return conditionLine[4..];
    }

    private sealed record PublishCase(
        string Name,
        string EventName,
        bool DryRun,
        bool ShouldPush,
        bool ShouldNotice);
}
