using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class OwnedCleanupOutcomeTests
{
    [TestMethod]
    public void NestedCleanup_PreservesEarlierAndLaterOutcomesAndCancellationIdentity()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var primary = new OperationCanceledException(cancellation.Token);
        OwnedResourceCleanup.AttachOutcomes(primary, ["CompilerCapture:IOException"]);
        OwnedResourceCleanup.AttachOutcomes(primary, ["CompilerParserDirectory:UnauthorizedAccessException"]);
        var observed = new MsBuildCommandProperties.Redactor([]).Failure(primary);
        Assert.AreSame(primary, observed);
        Assert.AreEqual(cancellation.Token, ((OperationCanceledException)observed).CancellationToken);
        var outcomes = observed.Data[OwnedResourceCleanup.OutcomeDataKey] as string[];
        Assert.IsNotNull(outcomes);
        CollectionAssert.AreEqual(new[] { "CompilerCapture:IOException", "CompilerParserDirectory:UnauthorizedAccessException" }, outcomes);
    }

    [TestMethod]
    public void RepeatedCleanup_StillBoundsRetainedOutcomeCountAndLength()
    {
        var primary = new IOException("Synthetic primary.");
        OwnedResourceCleanup.AttachOutcomes(primary, [new string('x', OwnedResourceCleanup.MaximumOutcomeLength * 2)]);
        OwnedResourceCleanup.AttachOutcomes(primary, Enumerable.Range(0, OwnedResourceCleanup.MaximumOutcomeCount * 2).Select(index => "Cleanup:" + index).ToArray());
        var outcomes = primary.Data[OwnedResourceCleanup.OutcomeDataKey] as string[];
        Assert.IsNotNull(outcomes);
        Assert.HasCount(OwnedResourceCleanup.MaximumOutcomeCount, outcomes);
        Assert.IsTrue(outcomes.All(outcome => outcome.Length <= OwnedResourceCleanup.MaximumOutcomeLength));
        Assert.AreEqual(new string('x', OwnedResourceCleanup.MaximumOutcomeLength), outcomes[0]);
    }
}
