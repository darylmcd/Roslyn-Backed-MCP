using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Order-independent observation point shared by the two parallel classes below. The first
/// fixture any class observes is latched; every later observation must be the same reference.
/// Latching (rather than comparing "the other class's" value) keeps the proof deterministic
/// whether MSTest overlaps the two classes or runs them back to back.
/// </summary>
internal static class TestAssemblyFixtureObservations
{
    private static TestAssemblyFixture? _first;
    private static int _observationCount;

    internal static int ObservationCount => Volatile.Read(ref _observationCount);

    internal static TestAssemblyFixture RecordAndGetFirst(TestAssemblyFixture observed)
    {
        Interlocked.Increment(ref _observationCount);
        return Interlocked.CompareExchange(ref _first, observed, null) ?? observed;
    }
}

/// <summary>
/// First of the two parallel classes asserting that <see cref="TestBase"/>'s get-only forwarders
/// resolve to one assembly-owned <see cref="TestAssemblyFixture"/>. Deliberately NOT
/// <c>[DoNotParallelize]</c>: the shared context must survive concurrent class execution.
/// </summary>
[TestClass]
public sealed class TestAssemblyFixtureSharingAlphaTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public void Fixture_IsTheSingleAssemblyOwnedInstance()
    {
        var observed = Fixture;

        Assert.AreSame(
            TestAssemblyFixtureObservations.RecordAndGetFirst(observed),
            observed,
            "Every test class must observe the same assembly-owned TestAssemblyFixture instance.");
        Assert.IsTrue(
            TestAssemblyFixtureObservations.ObservationCount >= 1,
            "This class must have recorded its own observation.");
    }

    [TestMethod]
    public void InheritedForwarders_ResolveToTheFixtureTheyForwardTo()
    {
        var observed = Fixture;

        Assert.AreSame(observed.Services.WorkspaceManager, WorkspaceManager);
        Assert.AreSame(observed.Services.PreviewStore, PreviewStore);
        Assert.AreSame(observed.Services.ChangeTracker, ChangeTracker);
        Assert.AreEqual(observed.RepositoryFixtures.RepositoryRootPath, RepositoryRootPath);
        Assert.AreEqual(observed.RepositoryFixtures.SampleSolutionPath, SampleSolutionPath);
        Assert.AreEqual(observed.RepositoryFixtures.BuildFailureSolutionPath, BuildFailureSolutionPath);
        Assert.AreEqual(observed.RepositoryFixtures.GeneratedDocumentSolutionPath, GeneratedDocumentSolutionPath);
    }
}

/// <summary>
/// Second of the two parallel classes. Also owns the disposal regression: the fixture releases
/// its owned resources exactly once no matter how many callers dispose it, which is the
/// invariant that lets <c>[AssemblyCleanup]</c> be the single disposal owner.
/// </summary>
[TestClass]
public sealed class TestAssemblyFixtureSharingBetaTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public void Fixture_IsTheSingleAssemblyOwnedInstance()
    {
        var observed = Fixture;
        var first = TestAssemblyFixtureObservations.RecordAndGetFirst(observed);

        Assert.AreSame(
            first,
            observed,
            "Every test class must observe the same assembly-owned TestAssemblyFixture instance.");
        Assert.AreSame(
            first.WorkspaceIdCache,
            observed.WorkspaceIdCache,
            "The single WorkspaceIdCache that collapses duplicate SampleSolution loads must be shared.");
    }

    [TestMethod]
    public async Task DisposeAsync_ReleasesOwnedResourcesExactlyOnce()
    {
        // A standalone fixture, so the assembly-owned one stays live for the rest of the run. It
        // never requests a path-authorized server, so no MCP server is stood up.
        var fixture = new TestAssemblyFixture(
            TestServiceContainer.Create(new ValidationServiceOptions()),
            Fixture.RepositoryFixtures);

        Assert.AreEqual(0, fixture.DisposalEntryCount, "A fresh fixture has released nothing.");

        await fixture.DisposeAsync();
        await fixture.DisposeAsync();
        await fixture.DisposeAsync();

        Assert.AreEqual(
            1,
            fixture.DisposalEntryCount,
            "Owned resources must be released exactly once however many callers dispose the fixture.");

        // NOTE: this assertion proves the release block was ENTERED exactly once, not that it ran
        // to completion — deleting the block's body would leave it green. An observable
        // post-condition was attempted (asserting the disposed WorkspaceManager refuses a
        // LoadAsync) and REVERTED: it throws only when the call reaches the disposed semaphore,
        // and with the sample solutions pre-prepared by a full suite run it short-circuits before
        // that, so it passed in isolation and failed under the full gate. A deterministic check
        // needs a read accessor on WorkspaceIdCache to assert Clear() ran, which is a fourth test
        // file and over this initiative's Rule 4 budget. Tracked by row
        // test-assembly-fixture-disposal-observable-postcondition.

        // Disposing the fixture must not reach through to the assembly-owned one.
        Assert.AreEqual(
            0,
            Fixture.DisposalEntryCount,
            "The assembly-owned fixture must still be live; only AssemblyLifecycle.Cleanup disposes it.");
    }
}
