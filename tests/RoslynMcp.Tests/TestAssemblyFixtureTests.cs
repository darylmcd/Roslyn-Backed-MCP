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
        // A standalone fixture, so the assembly-owned one stays live for the rest of the run.
        // The session factory throws: this regression must never stand up a real MCP server.
        var fixture = new TestAssemblyFixture(
            TestServiceContainer.Create(new ValidationServiceOptions()),
            Fixture.RepositoryFixtures,
            sessionFactory: static (_, _) => throw new InvalidOperationException(
                "The disposal regression must not create a path-authorized server session."));

        Assert.AreEqual(0, fixture.OwnedResourceDisposalCount, "A fresh fixture has released nothing.");

        await fixture.DisposeAsync();
        await fixture.DisposeAsync();
        await fixture.DisposeAsync();

        Assert.AreEqual(
            1,
            fixture.OwnedResourceDisposalCount,
            "Owned resources must be released exactly once however many callers dispose the fixture.");
        Assert.AreEqual(
            0,
            Fixture.OwnedResourceDisposalCount,
            "The assembly-owned fixture must still be live; only AssemblyLifecycle.Cleanup disposes it.");
    }
}
