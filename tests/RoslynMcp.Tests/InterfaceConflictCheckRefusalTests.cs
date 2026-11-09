using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class InterfaceConflictCheckRefusalTests : InterfaceRouteTestBase
{
    private const string Source = "namespace SampleLib; public sealed class RefService { public int Get() => 1; }";
    private const string Consumer = "namespace Consumer; public sealed class User { public User(SampleLib.RefService service) { } }";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("standalone", "null")]
    [DataRow("standalone", "io")]
    [DataRow("standalone", "access")]
    [DataRow("cross-project", "null")]
    [DataRow("extract-and-wire", "null")]
    public async Task Unavailable_Conflict_Compilation_Refuses_Before_Publishing(string route, string failure)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", Source, "User.cs", Consumer);
        var ordinary = new TrackingPreviewStore(new PreviewStore());
        var composite = new CompositePreviewStore();
        var cache = new FailingCompilationCache(failure, "Contracts");
        var crossProject = new CrossProjectRefactoringService(WorkspaceManager, ordinary, cache);
        var standalone = new InterfaceExtractionService(WorkspaceManager, ordinary, cache,
            NullLogger<InterfaceExtractionService>.Instance);
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var files = Snapshot(workspace.RootPath);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            Preview(route, workspace.WorkspaceId, sourcePath, standalone, crossProject, composite));
        StringAssert.Contains(refusal.Message, "conflict check");
        StringAssert.Contains(refusal.Message, "Retry");
        Assert.IsTrue(refusal.Message.Length < 250, refusal.Message);
        Assert.AreEqual(0, ordinary.GetRedeemableTokens(workspace.WorkspaceId).Count);
        Assert.AreEqual(0, StoreCount(composite));
        Assert.AreSame(solution, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        AssertSnapshot(files);
        CollectionAssert.AreEquivalent(files.Keys.ToArray(), Snapshot(workspace.RootPath).Keys.ToArray());
        Assert.IsFalse(File.Exists(workspace.GetPath("Contracts", "IRefService.cs")));
    }

    [TestMethod]
    [DataRow("standalone", false)]
    [DataRow("cross-project", false)]
    [DataRow("extract-and-wire", false)]
    [DataRow("standalone", true)]
    [DataRow("cross-project", true)]
    [DataRow("extract-and-wire", true)]
    public async Task Available_Compilation_Preserves_No_Conflict_And_Real_Conflict(string route, bool conflict)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", Source, "User.cs", Consumer);
        if (conflict)
        {
            if (route == "standalone")
                await File.WriteAllTextAsync(sourcePath, Source + " public sealed class IRefService { }");
            else
                await File.WriteAllTextAsync(workspace.GetPath("Contracts", "IRefService.cs"),
                    "namespace Contracts; public sealed class IRefService { }");
            await workspace.ReloadAsync();
        }

        var ordinary = new TrackingPreviewStore(new PreviewStore());
        var composite = new CompositePreviewStore();
        var cache = new FailingCompilationCache("none", "Contracts");
        var crossProject = new CrossProjectRefactoringService(WorkspaceManager, ordinary, cache);
        var standalone = new InterfaceExtractionService(WorkspaceManager, ordinary, cache,
            NullLogger<InterfaceExtractionService>.Instance);
        if (conflict)
        {
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Preview(route, workspace.WorkspaceId, sourcePath, standalone, crossProject, composite));
            StringAssert.Contains(refusal.Message, "already exists in project");
            Assert.AreEqual(0, ordinary.GetRedeemableTokens(workspace.WorkspaceId).Count);
            Assert.AreEqual(0, StoreCount(composite));
        }
        else
        {
            var preview = await Preview(route, workspace.WorkspaceId, sourcePath, standalone, crossProject, composite);
            Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));
            Assert.AreEqual(route == "extract-and-wire" ? 0 : 1,
                ordinary.GetRedeemableTokens(workspace.WorkspaceId).Count);
            Assert.AreEqual(route == "extract-and-wire" ? 1 : 0, StoreCount(composite));
        }
    }

    private static Task<RefactoringPreviewDto> Preview(
        string route, string workspaceId, string sourcePath, InterfaceExtractionService standalone,
        CrossProjectRefactoringService crossProject, CompositePreviewStore composite) => route switch
    {
        "standalone" => standalone.PreviewExtractInterfaceAsync(workspaceId, sourcePath,
            "RefService", "IRefService", null, false, CancellationToken.None),
        "cross-project" => crossProject.PreviewExtractInterfaceAsync(workspaceId, sourcePath,
            "RefService", "IRefService", "Contracts", CancellationToken.None),
        "extract-and-wire" => new ExtractAndWireOrchestrator(WorkspaceManager, composite,
            crossProject, DiRegistrationService).PreviewExtractAndWireInterfaceAsync(workspaceId,
                sourcePath, "RefService", "IRefService", "Contracts", false, CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private sealed class FailingCompilationCache(string failure, string projectName) : ICompilationCache
    {
        public Task<Compilation?> GetCompilationAsync(string workspaceId, Project project, CancellationToken ct) =>
            project.Name != projectName || failure == "none"
                ? project.GetCompilationAsync(ct)
                : failure switch
                {
                    "null" => Task.FromResult<Compilation?>(null),
                    "io" => Task.FromException<Compilation?>(new IOException("synthetic private disk detail")),
                    "access" => Task.FromException<Compilation?>(new UnauthorizedAccessException("synthetic private path")),
                    _ => throw new ArgumentOutOfRangeException(nameof(failure)),
                };

        public Task<CompilationWithAnalyzers?> GetCompilationWithAnalyzersAsync(
            string workspaceId, Project project, CancellationToken ct) =>
            throw new NotSupportedException();

        public bool IsLiveSolution(string workspaceId, Solution solution) => true;

        public void Invalidate(string workspaceId) { }
    }
}
