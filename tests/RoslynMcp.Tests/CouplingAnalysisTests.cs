using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[DoNotParallelize]
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
    public async Task GetCouplingMetricsResult_PerTypeFailure_IsSurfacedAsPartial()
    {
        using var compilationCache = new CompilationCache(WorkspaceManager);
        var service = new CouplingAnalysisService(
            WorkspaceManager,
            compilationCache,
            NullLogger<CouplingAnalysisService>.Instance,
            type => string.Equals(type.Name, "Dog", StringComparison.Ordinal)
                ? new InvalidOperationException("forced coupling failure")
                : null);

        var result = await service.GetCouplingMetricsResultAsync(
            WorkspaceId,
            projectFilter: "SampleLib",
            limit: 500,
            excludeTestProjects: false,
            includeInterfaces: false,
            CancellationToken.None);

        Assert.IsTrue(result.IsPartial);
        Assert.AreEqual(1, result.FailedTypeCount);
        Assert.IsTrue(
            result.Warnings.Any(warning =>
                warning.Contains("SampleLib.Dog", StringComparison.Ordinal)
                && warning.Contains(nameof(InvalidOperationException), StringComparison.Ordinal)));
        Assert.IsFalse(result.Metrics.Any(metric => metric.FullyQualifiedName == "SampleLib.Dog"));
    }

    [TestMethod]
    public async Task GetCouplingMetricsResult_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            CouplingAnalysisService.GetCouplingMetricsResultAsync(
                WorkspaceId,
                projectFilter: "SampleLib",
                limit: 500,
                excludeTestProjects: false,
                includeInterfaces: false,
                cts.Token));
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

    [TestMethod]
    public async Task GetCouplingMetrics_SummaryMode_ReturnsPerProjectRollup()
    {
        // summary=true must emit a per-project rollup envelope (summary:true, projectCount,
        // totalTypes, projects[]) with classification buckets and NO `metrics` array. This is
        // the gh #763 mitigation: on multi-project solutions, the full per-type payload blew
        // through the MCP token cap; summary mode trades detail rows for project-level counts
        // so callers can drill in via projectName + summary=false on the project they care about.
        var fullJson = await CouplingAnalysisTools.GetCouplingMetrics(
            WorkspaceExecutionGate,
            CouplingAnalysisService,
            WorkspaceId,
            projectName: null,
            limit: 500,
            excludeTestProjects: false,
            includeInterfaces: false,
            summary: false,
            ct: CancellationToken.None);

        var summaryJson = await CouplingAnalysisTools.GetCouplingMetrics(
            WorkspaceExecutionGate,
            CouplingAnalysisService,
            WorkspaceId,
            projectName: null,
            limit: 500,
            excludeTestProjects: false,
            includeInterfaces: false,
            summary: true,
            ct: CancellationToken.None);

        using var summaryDoc = JsonDocument.Parse(summaryJson);
        var summaryRoot = summaryDoc.RootElement;

        Assert.IsTrue(summaryRoot.TryGetProperty("summary", out var summaryFlag) && summaryFlag.GetBoolean(),
            "summary=true must set summary:true so callers can detect the shape.");
        Assert.IsFalse(summaryRoot.TryGetProperty("metrics", out _),
            "summary=true must omit the per-type `metrics` array.");
        Assert.IsTrue(summaryRoot.TryGetProperty("projects", out var projectsElement)
                       && projectsElement.ValueKind == JsonValueKind.Array,
            "summary=true must include a `projects` array.");
        Assert.IsTrue(summaryRoot.TryGetProperty("projectCount", out var projectCountElement)
                       && projectCountElement.GetInt32() == projectsElement.GetArrayLength(),
            "projectCount must equal the projects array length.");
        Assert.IsTrue(summaryRoot.TryGetProperty("totalTypes", out var totalTypesElement)
                       && totalTypesElement.GetInt32() > 0,
            "totalTypes must be > 0 on a non-empty sample solution.");

        // Every project entry must carry the documented rollup shape.
        foreach (var project in projectsElement.EnumerateArray())
        {
            Assert.IsTrue(project.TryGetProperty("projectName", out var projectName)
                           && !string.IsNullOrWhiteSpace(projectName.GetString()),
                "Each project rollup must have a non-empty projectName.");
            Assert.IsTrue(project.TryGetProperty("typeCount", out var typeCount) && typeCount.GetInt32() > 0,
                $"Each project rollup must have typeCount > 0 (project: {projectName.GetString()}).");
            Assert.IsTrue(project.TryGetProperty("avgInstability", out var avgInstability)
                           && avgInstability.GetDouble() is >= 0.0 and <= 1.0,
                $"avgInstability must be in [0,1] (project: {projectName.GetString()}).");
            Assert.IsTrue(project.TryGetProperty("stableCount", out var stableCount) && stableCount.GetInt32() >= 0,
                "stableCount must be non-negative.");
            Assert.IsTrue(project.TryGetProperty("balancedCount", out var balancedCount) && balancedCount.GetInt32() >= 0,
                "balancedCount must be non-negative.");
            Assert.IsTrue(project.TryGetProperty("unstableCount", out var unstableCount) && unstableCount.GetInt32() >= 0,
                "unstableCount must be non-negative.");
            Assert.IsTrue(project.TryGetProperty("isolatedCount", out var isolatedCount) && isolatedCount.GetInt32() >= 0,
                "isolatedCount must be non-negative.");
            Assert.AreEqual(
                typeCount.GetInt32(),
                stableCount.GetInt32() + balancedCount.GetInt32() + unstableCount.GetInt32() + isolatedCount.GetInt32(),
                $"Classification buckets must sum to typeCount (project: {projectName.GetString()}).");
        }

        // Summary payload must be strictly smaller than the full payload on a non-trivial sample.
        Assert.IsTrue(summaryJson.Length < fullJson.Length,
            $"summary JSON must be smaller than full JSON; summary={summaryJson.Length}, full={fullJson.Length}");

        // summary=false response shape must be preserved bit-for-bit — assert the existing
        // envelope contract (`count` + `metrics` array) is still emitted unchanged.
        using var fullDoc = JsonDocument.Parse(fullJson);
        var fullRoot = fullDoc.RootElement;
        Assert.IsTrue(fullRoot.TryGetProperty("count", out _),
            "summary=false must keep the existing `count` field.");
        Assert.IsTrue(fullRoot.TryGetProperty("metrics", out var metricsElement)
                       && metricsElement.ValueKind == JsonValueKind.Array,
            "summary=false must keep the existing `metrics` array.");
        Assert.IsFalse(fullRoot.TryGetProperty("summary", out _),
            "summary=false must NOT emit a `summary` field.");
    }
}
