using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers <see cref="WorkspaceLoadFinalizer"/> (<c>workspace-manager-load-finalization-collaborator-extraction</c>):
/// the projection it builds from an opened workspace, and the manager-level invariant that a finalization
/// failure during reload disposes the new workspace and leaves the prior session observable and unchanged.
/// The link-refusal cases live in <see cref="WorkspaceLoadLinkRefusalTests"/>.
/// </summary>
// Private manager per test (same isolation rationale as WorkspaceLoadCleanupGateReleaseTests); no shared state beyond
// the checked-in samples/SampleSolution obj/ artifacts.
[TestClass]
public sealed class WorkspaceLoadFinalizerTests
{
    private static string _sampleSolutionPath = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        _sampleSolutionPath = TestFixtureFileSystem.FindFixturePath(
            repoRoot,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");
    }

    private static CancellationToken Bounded(int seconds = 120) => new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;

    private static WorkspaceManager CreateManager(
        RestoreStalenessDetector? detector = null,
        WorkspaceLoadFinalizer? loadFinalizer = null) =>
        new(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 2 },
            cacheStore: null,
            sessionLoader: null,
            restoreStalenessDetector: detector,
            ownsFileWatcher: true,
            loadFinalizer: loadFinalizer);

    private static DiagnosticDto Diagnostic(string id, string message = "diagnostic") =>
        new(id, message, "Warning", "Workspace", FilePath: null);

    [TestMethod]
    public async Task BuildLoadProjection_OpenedSampleSolution_ProjectsStatusesAndPhysicalDocumentIndex()
    {
        var (workspace, lease) = await new WorkspaceSessionLoader().CreateAndOpenAsync(
            "finalizer-projection", _sampleSolutionPath, globalProperties: null,
            new WorkspaceDiagnosticsSink(200), NullLogger.Instance, Bounded());
        try
        {
            var projection = new WorkspaceLoadFinalizer(new StubRestoreStalenessDetector())
                .BuildLoadProjection(workspace, new ConcurrentQueue<DiagnosticDto>(), NullLogger.Instance);

            var solution = workspace.CurrentSolution;
            Assert.AreEqual(solution.Projects.Count(), projection.ProjectStatuses.Length);
            Assert.IsTrue(projection.ProjectStatuses.Length > 1, "The sample solution must project more than one project.");
            foreach (var project in solution.Projects)
            {
                var status = projection.ProjectStatuses.Single(s => s.Name == ProjectMetadataParser.ResolveProjectName(project));
                Assert.AreEqual(project.Documents.Count(), status.DocumentCount);
                Assert.AreEqual(ProjectMetadataParser.ResolveProjectPath(project), status.FilePath);
            }

            var expectedDocuments = solution.Projects
                .SelectMany(project => project.Documents)
                .Select(document => PhysicalPathResolver.Resolve(document.FilePath!))
                .ToArray();
            Assert.IsTrue(expectedDocuments.Length > 0);
            Assert.IsTrue(projection.DocumentPaths.SetEquals(expectedDocuments));
            Assert.IsFalse(projection.RestoreRequired);
            Assert.IsFalse(projection.BuildRequired);
            Assert.IsTrue(
                projection.DocumentPaths.SetEquals(WorkspaceLoadFinalizer.BuildDocumentPathIndex(solution)),
                "TryApplyChanges republishes through the same static index builder.");
        }
        finally
        {
            workspace.Dispose();
            lease.Dispose();
        }
    }

    [TestMethod]
    public async Task BuildLoadProjection_DetectorReportsRestoreRequired_SurfacesRestoreRequiredNotBuildRequired()
    {
        var (workspace, lease) = await new WorkspaceSessionLoader().CreateAndOpenAsync(
            "finalizer-detector", _sampleSolutionPath, globalProperties: null,
            new WorkspaceDiagnosticsSink(200), NullLogger.Instance, Bounded());
        try
        {
            var detector = new StubRestoreStalenessDetector(restoreRequired: true);

            var projection = new WorkspaceLoadFinalizer(detector)
                .BuildLoadProjection(workspace, new ConcurrentQueue<DiagnosticDto>(), NullLogger.Instance);

            Assert.IsTrue(projection.RestoreRequired);
            Assert.IsFalse(projection.BuildRequired);
            Assert.AreEqual(1, detector.DetectCalls);
        }
        finally
        {
            workspace.Dispose();
            lease.Dispose();
        }
    }

    [TestMethod]
    public async Task BuildLoadProjection_RestoreSymptomDiagnostics_SetRestoreRequiredWithoutDetectorVerdict()
    {
        var (workspace, lease) = await new WorkspaceSessionLoader().CreateAndOpenAsync(
            "finalizer-restore-diagnostics", _sampleSolutionPath, globalProperties: null,
            new WorkspaceDiagnosticsSink(200), NullLogger.Instance, Bounded());
        try
        {
            var diagnostics = new ConcurrentQueue<DiagnosticDto>();
            for (var i = 0; i < 3; i++) diagnostics.Enqueue(Diagnostic("CS0234"));

            var projection = new WorkspaceLoadFinalizer(new StubRestoreStalenessDetector(restoreRequired: false))
                .BuildLoadProjection(workspace, diagnostics, NullLogger.Instance);

            Assert.IsTrue(projection.RestoreRequired, "Three restore-symptom diagnostics must mark the workspace restore-required.");
            Assert.IsFalse(projection.BuildRequired);
        }
        finally
        {
            workspace.Dispose();
            lease.Dispose();
        }
    }

    [TestMethod]
    public async Task BuildLoadProjection_UnresolvedAnalyzerDiagnostic_SetsBuildRequiredNotRestoreRequired()
    {
        var (workspace, lease) = await new WorkspaceSessionLoader().CreateAndOpenAsync(
            "finalizer-build-required", _sampleSolutionPath, globalProperties: null,
            new WorkspaceDiagnosticsSink(200), NullLogger.Instance, Bounded());
        try
        {
            var diagnostics = new ConcurrentQueue<DiagnosticDto>();
            diagnostics.Enqueue(Diagnostic("WORKSPACE_UNRESOLVED_ANALYZER", "Unresolved analyzer reference removed."));

            var projection = new WorkspaceLoadFinalizer(new StubRestoreStalenessDetector(restoreRequired: false))
                .BuildLoadProjection(workspace, diagnostics, NullLogger.Instance);

            Assert.IsTrue(projection.BuildRequired);
            Assert.IsFalse(projection.RestoreRequired, "A missing build output is not a missing NuGet package.");
        }
        finally
        {
            workspace.Dispose();
            lease.Dispose();
        }
    }

    [TestMethod]
    public async Task Load_InjectedRestoreDetector_FlowsThroughTheDefaultFinalizer()
    {
        using var manager = CreateManager(detector: new StubRestoreStalenessDetector(restoreRequired: true));

        var status = await manager.LoadAsync(_sampleSolutionPath, Bounded());

        Assert.IsTrue(status.RestoreRequired, "The manager's detector double must reach the finalizer it constructs.");
        Assert.IsFalse(status.BuildRequired);
    }

    [TestMethod]
    public async Task Reload_FinalizationFailure_DisposesNewWorkspace_AndPriorSessionStaysObservableAndUnchanged()
    {
        var finalizer = new ScriptedFinalizer(new RestoreStalenessDetector()) { ThrowOnCall = 2 };
        using var manager = CreateManager(loadFinalizer: finalizer);
        var loaded = await manager.LoadAsync(_sampleSolutionPath, Bounded());
        var before = manager.GetStatus(loaded.WorkspaceId);
        var solutionBefore = manager.GetCurrentSolution(loaded.WorkspaceId);
        var indexedDocument = solutionBefore.Projects.SelectMany(p => p.Documents).First(d => d.FilePath is not null).FilePath!;

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.ReloadAsync(loaded.WorkspaceId, Bounded()));

        Assert.AreEqual(ScriptedFinalizer.FailureMessage, thrown.Message);
        Assert.IsNotNull(finalizer.RejectedWorkspace, "The failing call must have been handed the freshly opened workspace.");
        Assert.AreNotSame(solutionBefore.Workspace, finalizer.RejectedWorkspace);
        Assert.AreEqual(0, finalizer.RejectedWorkspace.CurrentSolution.ProjectIds.Count,
            "A finalization failure must dispose the new workspace (a disposed MSBuildWorkspace holds an empty solution).");

        var after = manager.GetStatus(loaded.WorkspaceId);
        Assert.IsTrue(after.IsLoaded);
        Assert.AreEqual(before.WorkspaceVersion, after.WorkspaceVersion, "A failed finalization must not swap or bump the version.");
        Assert.AreEqual(before.LoadedPath, after.LoadedPath);
        Assert.AreEqual(before.LoadedAtUtc, after.LoadedAtUtc);
        Assert.AreEqual(before.ProjectCount, after.ProjectCount);
        Assert.AreEqual(before.DocumentCount, after.DocumentCount);
        CollectionAssert.AreEqual(before.Projects.ToArray(), after.Projects.ToArray());
        Assert.AreEqual(before.RestoreRequired, after.RestoreRequired);
        Assert.AreEqual(before.BuildRequired, after.BuildRequired);
        Assert.AreSame(solutionBefore, manager.GetCurrentSolution(loaded.WorkspaceId),
            "The prior workspace must remain the observable one.");
        Assert.IsTrue(solutionBefore.ProjectIds.Count > 0 && manager.GetCurrentSolution(loaded.WorkspaceId).ProjectIds.Count > 0,
            "The prior workspace must not have been disposed.");
        CollectionAssert.AreEqual(
            new[] { loaded.WorkspaceId },
            manager.FindWorkspaceIdsContainingFile(indexedDocument).ToArray(),
            "The prior document-path index must remain published.");

        var recovered = await manager.ReloadAsync(loaded.WorkspaceId, Bounded(60));
        Assert.AreEqual(before.WorkspaceVersion + 1, recovered.WorkspaceVersion, "LoadLock must be released after the failed finalization.");
    }

    private sealed class ScriptedFinalizer(RestoreStalenessDetector detector) : WorkspaceLoadFinalizer(detector)
    {
        internal const string FailureMessage = "scripted finalization failure";

        private int _calls;

        public int ThrowOnCall { get; set; }
        public MSBuildWorkspace? RejectedWorkspace { get; private set; }

        public override WorkspaceLoadProjection BuildLoadProjection(
            MSBuildWorkspace workspace,
            ConcurrentQueue<DiagnosticDto> diagnostics,
            ILogger logger)
        {
            if (Interlocked.Increment(ref _calls) != ThrowOnCall)
            {
                return base.BuildLoadProjection(workspace, diagnostics, logger);
            }

            RejectedWorkspace = workspace;
            throw new InvalidOperationException(FailureMessage);
        }
    }
}
