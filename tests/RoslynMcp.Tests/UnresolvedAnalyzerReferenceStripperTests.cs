using System.Collections.Concurrent;
using System.Linq;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>Exercises native analyzer projection, warnings, and source byte preservation.</summary>
[TestClass]
public sealed class UnresolvedAnalyzerReferenceStripperTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [TestMethod]
    public async Task Projection_RemovesUnresolvedReferences_AndEmitsDiagnostic()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var csprojPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        InjectUnresolvedAnalyzerReference(csprojPath);

        MsBuildInitializer.EnsureInitialized();
        var loader = new WorkspaceSessionLoader();
        var diagnosticsSink = new WorkspaceDiagnosticsSink(200);
        var (msbuildWorkspace, analyzerLease, _, _) = await loader.CreateAndOpenAsync(
            "test-workspace-unresolved",
            workspace.SolutionPath,
            globalProperties: OwnedNativeLoadContext.Properties(workspace.SolutionPath, new Dictionary<string, string>()),
            new OwnedTestWorkspacePathPolicy(),
            diagnosticsSink,
            NullLogger.Instance, SupplementalPackageReadAuthority.None, null,
            CancellationToken.None);
        using var _ = msbuildWorkspace;
        using var __ = analyzerLease;

        var sampleLibBefore = msbuildWorkspace.CurrentSolution.Projects.First(p => p.Name == "SampleLib");
        Assert.IsTrue(
            sampleLibBefore.AnalyzerReferences.OfType<UnresolvedAnalyzerReference>().Any(),
            "Fixture setup must produce an UnresolvedAnalyzerReference before stripping.");

        var before = await File.ReadAllBytesAsync(csprojPath);
        var projection = UnresolvedAnalyzerReferenceStripper.Project(msbuildWorkspace.CurrentSolution, CancellationToken.None);
        var diagnosticsQueue = new ConcurrentQueue<DiagnosticDto>(projection.Diagnostics);
        var sampleLibAfter = projection.Solution.Projects.First(p => p.Name == "SampleLib");
        Assert.IsFalse(sampleLibAfter.AnalyzerReferences.OfType<UnresolvedAnalyzerReference>().Any());
        Assert.IsTrue(msbuildWorkspace.CurrentSolution.Projects.First(p => p.Name == "SampleLib")
            .AnalyzerReferences.OfType<UnresolvedAnalyzerReference>().Any(), "The native workspace remains an untouched input.");
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(csprojPath));

        Assert.AreEqual(1, diagnosticsQueue.Count);
        Assert.IsTrue(diagnosticsQueue.TryPeek(out var dto));
        Assert.AreEqual("WORKSPACE_UNRESOLVED_ANALYZER", dto!.Id);
        Assert.AreEqual("Warning", dto.Severity);
        StringAssert.Contains(dto.Message, "SampleLib");
        StringAssert.Contains(dto.Message, "Run `dotnet build`");
        Assert.IsFalse(dto.Message.Contains("package path", StringComparison.Ordinal));
        Assert.IsNull(dto.Location,
            "A project-only diagnostic has no complete source span and must not fabricate a nested location.");
    }

    [TestMethod]
    public async Task Projection_NoUnresolvedReferences_IsNoOp()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        MsBuildInitializer.EnsureInitialized();
        var loader = new WorkspaceSessionLoader();
        var diagnosticsSink = new WorkspaceDiagnosticsSink(200);
        var (msbuildWorkspace, analyzerLease, _, _) = await loader.CreateAndOpenAsync(
            "test-workspace-clean",
            workspace.SolutionPath,
            globalProperties: OwnedNativeLoadContext.Properties(workspace.SolutionPath, new Dictionary<string, string>()),
            new OwnedTestWorkspacePathPolicy(),
            diagnosticsSink,
            NullLogger.Instance, SupplementalPackageReadAuthority.None, null,
            CancellationToken.None);
        using var _ = msbuildWorkspace;
        using var __ = analyzerLease;

        var projection = UnresolvedAnalyzerReferenceStripper.Project(msbuildWorkspace.CurrentSolution, CancellationToken.None);
        Assert.AreSame(msbuildWorkspace.CurrentSolution, projection.Solution);
        Assert.AreEqual(0, projection.Diagnostics.Count);
    }

    [TestMethod]
    public async Task WorkspaceLoad_ProjectsAnalyzerPolicyWithoutRewritingAuthoredInput()
    {
        await using var source = CreateIsolatedWorkspaceCopy();
        var path = source.GetPath("SampleLib", "SampleLib.csproj");
        InjectUnresolvedAnalyzerReference(path);
        var before = await File.ReadAllBytesAsync(path);
        await source.LoadAsync();
        var loaded = WorkspaceManager.GetCurrentSolution(source.WorkspaceId).Projects.Single(project => project.Name == "SampleLib");
        Assert.IsFalse(loaded.AnalyzerReferences.OfType<UnresolvedAnalyzerReference>().Any());
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path),
            "Read-side analyzer policy must preserve the authored analyzer declaration exactly.");
        Assert.IsTrue(WorkspaceManager.GetStatus(source.WorkspaceId).WorkspaceDiagnostics
            .Any(diagnostic => diagnostic.Id == "WORKSPACE_UNRESOLVED_ANALYZER"));
    }

    [TestMethod]
    public async Task WorkspaceLoad_FilteredGraphDoesNotRequireSourceWriteAccess()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Native Windows share-denial contract.");
        await using var source = CreateIsolatedWorkspaceCopy();
        var path = source.GetPath("SampleLib", "SampleLib.csproj");
        InjectUnresolvedAnalyzerReference(path);
        var before = await File.ReadAllBytesAsync(path);
        await using var readOnly = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await source.LoadAsync();
        var loaded = WorkspaceManager.GetCurrentSolution(source.WorkspaceId).Projects.Single(project => project.Name == "SampleLib");
        Assert.IsFalse(loaded.AnalyzerReferences.OfType<UnresolvedAnalyzerReference>().Any());
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path));
        Assert.IsTrue(WorkspaceManager.GetStatus(source.WorkspaceId).WorkspaceDiagnostics
            .Any(diagnostic => diagnostic.Id == "WORKSPACE_UNRESOLVED_ANALYZER"));
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
