using System.Collections.Concurrent;
using System.Linq;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Unit coverage for <see cref="UnresolvedAnalyzerReferenceStripper"/> — extracted from
/// <see cref="WorkspaceManager"/> by workspace-manager-decompose-restore-and-analyzer-
/// subsystems as a zero-external-caller, purely code-moved collaborator. Opens a raw
/// <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace"/> directly via
/// <see cref="WorkspaceSessionLoader"/> (bypassing <c>WorkspaceManager.LoadIntoSessionAsync</c>,
/// which would strip the reference automatically) so the stripper's own strip-count and
/// diagnostic-emission behavior can be exercised in isolation.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class UnresolvedAnalyzerReferenceStripperTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [TestMethod]
    public async Task StripAsync_RemovesUnresolvedReferences_AndEmitsDiagnostic()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var csprojPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        InjectUnresolvedAnalyzerReference(csprojPath);

        MsBuildInitializer.EnsureInitialized();
        var loader = new WorkspaceSessionLoader();
        var diagnosticsSink = new WorkspaceDiagnosticsSink(200);
        using var msbuildWorkspace = await loader.CreateAndOpenAsync(
            "test-workspace-unresolved",
            workspace.SolutionPath,
            globalProperties: null,
            diagnosticsSink,
            NullLogger.Instance,
            CancellationToken.None);

        var sampleLibBefore = msbuildWorkspace.CurrentSolution.Projects.First(p => p.Name == "SampleLib");
        Assert.IsTrue(
            sampleLibBefore.AnalyzerReferences.OfType<UnresolvedAnalyzerReference>().Any(),
            "Fixture setup must produce an UnresolvedAnalyzerReference before stripping.");

        var diagnosticsQueue = new ConcurrentQueue<DiagnosticDto>();
        var stripper = new UnresolvedAnalyzerReferenceStripper();

        await stripper.StripAsync(
            "test-workspace-unresolved",
            msbuildWorkspace,
            diagnosticsQueue,
            maxDiagnosticsPerWorkspace: 200,
            NullLogger.Instance,
            CancellationToken.None);

        var sampleLibAfter = msbuildWorkspace.CurrentSolution.Projects.First(p => p.Name == "SampleLib");
        Assert.IsFalse(
            sampleLibAfter.AnalyzerReferences.OfType<UnresolvedAnalyzerReference>().Any(),
            "UnresolvedAnalyzerReference must be stripped from project state after StripAsync.");

        Assert.AreEqual(1, diagnosticsQueue.Count);
        Assert.IsTrue(diagnosticsQueue.TryPeek(out var dto));
        Assert.AreEqual("WORKSPACE_UNRESOLVED_ANALYZER", dto!.Id);
        Assert.AreEqual("Warning", dto.Severity);
        StringAssert.Contains(dto.Message, "SampleLib");
    }

    [TestMethod]
    public async Task StripAsync_NoUnresolvedReferences_IsNoOp()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        MsBuildInitializer.EnsureInitialized();
        var loader = new WorkspaceSessionLoader();
        var diagnosticsSink = new WorkspaceDiagnosticsSink(200);
        using var msbuildWorkspace = await loader.CreateAndOpenAsync(
            "test-workspace-clean",
            workspace.SolutionPath,
            globalProperties: null,
            diagnosticsSink,
            NullLogger.Instance,
            CancellationToken.None);

        var diagnosticsQueue = new ConcurrentQueue<DiagnosticDto>();
        var stripper = new UnresolvedAnalyzerReferenceStripper();

        await stripper.StripAsync(
            "test-workspace-clean",
            msbuildWorkspace,
            diagnosticsQueue,
            maxDiagnosticsPerWorkspace: 200,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.AreEqual(0, diagnosticsQueue.Count);
    }

    private static void InjectUnresolvedAnalyzerReference(string csprojPath)
    {
        // MSBuildWorkspace turns <Analyzer Include="…"/> entries into UnresolvedAnalyzerReference
        // when the path does not resolve to an existing file. Inject one such entry into the
        // project file so the load reproduces the original crash trigger — mirrors
        // UnresolvedAnalyzerReferenceTests.InjectUnresolvedAnalyzerReference.
        var content = File.ReadAllText(csprojPath);
        var unresolvedAnalyzerSnippet = """
              <ItemGroup>
                <Analyzer Include="DefinitelyMissingAnalyzer.dll" />
              </ItemGroup>
            </Project>
            """;

        var newContent = content.Replace("</Project>", unresolvedAnalyzerSnippet);
        File.WriteAllText(csprojPath, newContent);
    }
}
