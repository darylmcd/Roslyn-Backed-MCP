using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression cover for <c>compilation-cache-generator-rerun-blinds-unused-analysis</c> and
/// <c>find-type-consumers-mutations-generator-blind</c>: in a
/// project with a source generator, the symbol analyses must take their symbols from the
/// Solution-owned compilation. A symbol taken from the generator-rerun diagnostic snapshot is
/// foreign to the Solution, so <c>SymbolFinder</c> resolves zero references and live code is
/// reported as dead.
/// </summary>
// Retained serial for the same reason as DiagnosticSourceGeneratorParityTests: the test spawns a
// real `dotnet restore` over its isolated copy, which contends with other classes' restore/build
// processes for MSBuild/NuGet resources.
[DoNotParallelize]
[TestClass]
public sealed class GeneratorProjectSymbolIdentityTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task SymbolAnalyses_InGeneratorProject_SeeReferencesToLiveCode()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(
            workspace.GetPath("SampleLib", "GeneratorSymbolIdentityProbe.cs"),
            """
            using System.Text.RegularExpressions;

            namespace SampleLib;

            public static partial class GeneratorSymbolIdentityProbe
            {
                private static int s_probeThreshold = 3;

                [GeneratedRegex("^[a-z]+$")]
                private static partial Regex ProbePattern();

                public static bool Matches(string value) =>
                    value.Length > s_probeThreshold
                    && ProbePattern().IsMatch(value)
                    && GeneratorSymbolIdentityTarget.IsReady();
            }

            internal static class GeneratorSymbolIdentityTarget
            {
                internal static bool IsReady() => true;
            }

            public sealed class GeneratorSymbolIdentityIoStore
            {
                public void Write(string p) => System.IO.File.WriteAllText(p, "probe");
            }
            """,
            CancellationToken.None);

        await RestoreWorkspaceAsync(workspace, CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var deadFields = await UnusedCodeAnalyzer.FindDeadFieldsAsync(
            workspace.WorkspaceId,
            new DeadFieldsAnalysisOptions { ProjectFilter = "SampleLib", Limit = 500 },
            CancellationToken.None);
        Assert.IsFalse(
            deadFields.Any(field => field.SymbolName == "s_probeThreshold"),
            "find_dead_fields must see the read of s_probeThreshold; a hit means the field symbol came " +
            "from the generator-rerun snapshot, which the Solution does not own. Hits: " +
            string.Join(", ", deadFields.Select(field => $"{field.SymbolName}({field.UsageKind})")));

        var unused = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            workspace.WorkspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = "SampleLib", Limit = 500 },
            CancellationToken.None);
        Assert.IsFalse(
            unused.Any(symbol => symbol.SymbolName is "GeneratorSymbolIdentityTarget" or "IsReady" or "ProbePattern"),
            "find_unused_symbols must see the references from Matches. Hits: " +
            string.Join(", ", unused.Select(symbol => symbol.SymbolName)));

        var coupling = await CouplingAnalysisService.GetCouplingMetricsAsync(
            workspace.WorkspaceId,
            projectFilter: "SampleLib",
            limit: 500,
            excludeTestProjects: false,
            includeInterfaces: false,
            CancellationToken.None);
        var target = coupling.SingleOrDefault(metric => metric.TypeName == "GeneratorSymbolIdentityTarget");
        Assert.IsNotNull(target, "get_coupling_metrics must report the probe target type.");
        Assert.IsTrue(
            target.AfferentCoupling > 0,
            "get_coupling_metrics must count GeneratorSymbolIdentityProbe as a consumer of " +
            "GeneratorSymbolIdentityTarget; Ca = 0 means the target symbol is foreign to the Solution.");

        var consumers = await TypeConsumersService.FindTypeConsumersAsync(
            workspace.WorkspaceId,
            "SampleLib.GeneratorSymbolIdentityTarget",
            50,
            CancellationToken.None);
        var probeRollup = consumers.SingleOrDefault(rollup =>
            Path.GetFileName(rollup.FilePath) == "GeneratorSymbolIdentityProbe.cs");
        Assert.IsNotNull(
            probeRollup,
            "find_type_consumers must report GeneratorSymbolIdentityProbe.cs as a consumer of " +
            "GeneratorSymbolIdentityTarget in a generator project. Rollups: " +
            string.Join(", ", consumers.Select(rollup => $"{rollup.FilePath}({rollup.Count})")));
        Assert.IsTrue(probeRollup.Count >= 1, $"Expected at least one consumer site, got {probeRollup.Count}.");

        var mutations = await MutationAnalysisService.FindTypeMutationsAsync(
            workspace.WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.GeneratorSymbolIdentityIoStore"),
            CancellationToken.None);
        Assert.IsNotNull(mutations, "GeneratorSymbolIdentityIoStore should resolve to a named type.");
        var write = mutations.MutatingMembers.FirstOrDefault(member => member.Name == "Write");
        Assert.IsNotNull(
            write,
            "find_type_mutations must flag Write as mutating; a miss means the side-effect classifier " +
            "compared a symbol against a foreign compilation's assembly. Members: " +
            string.Join(", ", mutations.MutatingMembers.Select(member => member.Name)));
        CollectionAssert.Contains(
            write.MutationScopes.ToList(),
            SideEffectClassifier.Scopes.IO,
            "File.WriteAllText should classify as IO.");
    }
}
