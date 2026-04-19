using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CouplingAnalysisTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task GetCouplingMetrics_ReturnsMetrics_ForSampleWorkspace()
    {
        var result = await CouplingAnalysisService.GetCouplingMetricsAsync(
            WorkspaceId, projectFilter: "SampleLib", limit: 100,
            excludeTestProjects: false, includeInterfaces: false, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0, "Expected at least one type with coupling metrics.");

        foreach (var m in result)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(m.TypeName));
            Assert.IsFalse(string.IsNullOrWhiteSpace(m.FullyQualifiedName));
            Assert.IsFalse(string.IsNullOrWhiteSpace(m.Classification));
            Assert.IsTrue(m.AfferentCoupling >= 0, $"Ca must be non-negative for {m.TypeName}.");
            Assert.IsTrue(m.EfferentCoupling >= 0, $"Ce must be non-negative for {m.TypeName}.");
            Assert.IsTrue(m.Instability is >= 0.0 and <= 1.0,
                $"Instability must be in [0,1] for {m.TypeName}, got {m.Instability}.");
        }
    }

    [TestMethod]
    public async Task GetCouplingMetrics_WhenExcludeTestProjects_OmitsTestProjectTypes()
    {
        var result = await CouplingAnalysisService.GetCouplingMetricsAsync(
            WorkspaceId, projectFilter: null, limit: 500,
            excludeTestProjects: true, includeInterfaces: false, CancellationToken.None);

        Assert.IsTrue(
            result.All(m => m.FilePath is null ||
                !m.FilePath.Contains("SampleLib.Tests", StringComparison.OrdinalIgnoreCase)),
            "Types from MSBuild test projects should be omitted when excludeTestProjects is true.");
    }

    [TestMethod]
    public async Task GetCouplingMetrics_AbcDependencyChain_MatchesMartinStabilityFormula()
    {
        // Plan validation fixture: A -> B -> C dependency chain.
        //   A.Ce = 1 (references B), A.Ca = 0 -> I = 1 / (0+1) = 1.0 (unstable)
        //   B.Ce = 1 (references C), B.Ca = 1 (A references B) -> I = 1 / (1+1) = 0.5 (balanced)
        //   C.Ce = 0, C.Ca = 1 (B references C) -> I = 0 / (1+0) = 0.0 (stable)
        // This is the canonical Martin-stability example and proves the service's Ca/Ce numbers
        // are accurate (not just non-negative).
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        string? workspaceId = null;

        try
        {
            var filePath = Path.Combine(copiedRoot, "SampleLib", "AbcChain.cs");
            await File.WriteAllTextAsync(filePath, """
namespace SampleLib.CouplingFixture;

public class TypeA
{
    private readonly TypeB _b = new TypeB();

    public int DoWork() => _b.DoWork();
}

public class TypeB
{
    private readonly TypeC _c = new TypeC();

    public int DoWork() => _c.Value;
}

public class TypeC
{
    public int Value => 42;
}
""", CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var metrics = await CouplingAnalysisService.GetCouplingMetricsAsync(
                workspaceId, projectFilter: "SampleLib", limit: 500,
                excludeTestProjects: false, includeInterfaces: false, CancellationToken.None);

            var a = metrics.FirstOrDefault(m => m.FullyQualifiedName == "SampleLib.CouplingFixture.TypeA");
            var b = metrics.FirstOrDefault(m => m.FullyQualifiedName == "SampleLib.CouplingFixture.TypeB");
            var c = metrics.FirstOrDefault(m => m.FullyQualifiedName == "SampleLib.CouplingFixture.TypeC");

            Assert.IsNotNull(a, "TypeA should be in the metrics output.");
            Assert.IsNotNull(b, "TypeB should be in the metrics output.");
            Assert.IsNotNull(c, "TypeC should be in the metrics output.");

            // A: Ce=1 (depends on B), Ca=0, I=1.0
            Assert.AreEqual(0, a.AfferentCoupling, "TypeA has no incoming consumers — Ca must be 0.");
            Assert.AreEqual(1, a.EfferentCoupling, "TypeA depends on TypeB — Ce must be 1.");
            Assert.AreEqual(1.0, a.Instability, 1e-9, "TypeA I = Ce/(Ca+Ce) = 1/1 = 1.0 (maximally unstable).");
            Assert.AreEqual("unstable", a.Classification);

            // B: Ce=1 (depends on C), Ca=1 (A depends on B), I=0.5
            Assert.AreEqual(1, b.AfferentCoupling, "TypeB is consumed by TypeA — Ca must be 1.");
            Assert.AreEqual(1, b.EfferentCoupling, "TypeB depends on TypeC — Ce must be 1.");
            Assert.AreEqual(0.5, b.Instability, 1e-9, "TypeB I = 1/(1+1) = 0.5 (balanced).");
            Assert.AreEqual("balanced", b.Classification);

            // C: Ce=0, Ca=1 (B depends on C), I=0.0
            Assert.AreEqual(1, c.AfferentCoupling, "TypeC is consumed by TypeB — Ca must be 1.");
            Assert.AreEqual(0, c.EfferentCoupling, "TypeC has no outbound deps — Ce must be 0.");
            Assert.AreEqual(0.0, c.Instability, 1e-9, "TypeC I = 0/(1+0) = 0.0 (maximally stable).");
            Assert.AreEqual("stable", c.Classification);
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task GetCouplingMetrics_IsolatedType_ClassifiesAsIsolated()
    {
        // A type with no incoming and no outgoing references should be reported as "isolated"
        // rather than "stable" — this avoids the misleading I=0 verdict for a class that simply
        // nothing consumes and that consumes nothing (a dead-code candidate, not a stable one).
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        string? workspaceId = null;

        try
        {
            var filePath = Path.Combine(copiedRoot, "SampleLib", "OrphanType.cs");
            await File.WriteAllTextAsync(filePath, """
namespace SampleLib.CouplingFixture;

public class OrphanType
{
    public int Value { get; set; }
}
""", CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var metrics = await CouplingAnalysisService.GetCouplingMetricsAsync(
                workspaceId, projectFilter: "SampleLib", limit: 500,
                excludeTestProjects: false, includeInterfaces: false, CancellationToken.None);

            var orphan = metrics.FirstOrDefault(m => m.FullyQualifiedName == "SampleLib.CouplingFixture.OrphanType");
            Assert.IsNotNull(orphan, "OrphanType should be in the metrics output.");
            Assert.AreEqual(0, orphan.AfferentCoupling);
            Assert.AreEqual(0, orphan.EfferentCoupling);
            Assert.AreEqual("isolated", orphan.Classification,
                "A type with Ca=0 and Ce=0 should be reported as 'isolated', not 'stable'.");
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task GetCouplingMetrics_BaseTypeAndInterface_CountTowardEfferent()
    {
        // Base-class + interface declarations in the base list are real outbound dependencies
        // — they must show up in Ce regardless of whether the implementing class references
        // the base via code inside its body.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        string? workspaceId = null;

        try
        {
            var filePath = Path.Combine(copiedRoot, "SampleLib", "BaseAndIface.cs");
            await File.WriteAllTextAsync(filePath, """
namespace SampleLib.CouplingFixture;

public interface IThing
{
    int Get();
}

public abstract class ThingBase
{
    public abstract int Get();
}

public class ConcreteThing : ThingBase, IThing
{
    public override int Get() => 7;
}
""", CancellationToken.None);

            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var metrics = await CouplingAnalysisService.GetCouplingMetricsAsync(
                workspaceId, projectFilter: "SampleLib", limit: 500,
                excludeTestProjects: false, includeInterfaces: true, CancellationToken.None);

            var concrete = metrics.FirstOrDefault(m => m.FullyQualifiedName == "SampleLib.CouplingFixture.ConcreteThing");
            Assert.IsNotNull(concrete, "ConcreteThing should be in the metrics output.");
            // Ce should include ThingBase AND IThing (2 outbound types).
            Assert.IsTrue(concrete.EfferentCoupling >= 2,
                $"ConcreteThing Ce should include base class + interface (>= 2). Actual: {concrete.EfferentCoupling}.");
        }
        finally
        {
            if (workspaceId is not null)
            {
                WorkspaceManager.Close(workspaceId);
            }

            DeleteDirectoryIfExists(copiedRoot);
        }
    }
}
