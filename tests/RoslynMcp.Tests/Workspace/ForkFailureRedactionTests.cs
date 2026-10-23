using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using RoslynMcp.Tests.TestInfrastructure;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkFailureRedactionTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FinalNativeDocumentMismatch_RedactsCapturedGlobalValues(bool closeRefusal)
    {
        var sentinel = "PRIVATE-FORK-GLOBAL-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(TestTempRoot.Current, sentinel);
        var projectPath = Path.Combine(root, "Observed.csproj");
        string? workspaceId = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(projectPath, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><AdditionalFiles Include="Observed.txt" /></ItemGroup></Project>""");
            await File.WriteAllTextAsync(Path.Combine(root, "Entry.cs"), "public class Entry { }" + Environment.NewLine);
            await File.WriteAllTextAsync(Path.Combine(root, "Observed.txt"), "reviewed input" + Environment.NewLine);
            var globals = OwnedNativeLoadContext.Properties(projectPath,
                new Dictionary<string, string> { ["PrivateValue"] = sentinel });
            CommandExecutionDto restore;
            using (var restoreBudget = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                restore = await DotnetCommandRunner.RunAsync(root, projectPath,
                    MsBuildCommandProperties.Append(["restore", projectPath, "--nologo"], globals), restoreBudget.Token);
            }
            Assert.IsTrue(restore.Succeeded, "The native fixture restore must succeed.");
            var loader = (IWorkspaceEvaluationLoader)WorkspaceManager;
            var loaded = await loader.LoadWithPropertiesAsync(projectPath, globals, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
            workspaceId = loaded.WorkspaceId;
            var original = WorkspaceManager.GetCurrentSolution(workspaceId);
            var version = WorkspaceManager.GetCurrentVersion(workspaceId);
            var token = PreviewStore.Store(workspaceId, original, original, version, "observe final physical comparison", [], PreviewKind.Unspecified);
            var paths = new[] { projectPath, Path.Combine(root, "Entry.cs"), Path.Combine(root, "Observed.txt"), Path.Combine(root, "obj", "project.assets.json") };
            var bytes = new Dictionary<string, byte[]>();
            foreach (var path in paths) bytes[path] = await File.ReadAllBytesAsync(path);
            var observer = new ReloadDocumentObserver(WorkspaceManager, loader, (IWorkspaceRecoveryAuthority)WorkspaceManager, closeRefusal ? sentinel : null);
            var logger = new ListLogger<WorkspaceForkApplyService>();
            var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService, TestRunnerService, WorkspaceManager, ChangeTracker);
            var service = new WorkspaceForkApplyService(PathPolicy, observer, WorkspaceExecutionGate,
                PreviewStore, validation, TestRunnerService, new DotnetCommandRunner(), logger, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
            var failure = await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => service.ApplyAsync(workspaceId,
                token, "keep", false, null, "final-comparison", CancellationToken.None));
            Assert.IsTrue(observer.ObservedFinalReload, "The mismatch must exercise the real final native reload boundary.");
            StringAssert.Contains(failure.Message, "document text differs");
            Assert.IsFalse(failure.Message.Contains(sentinel, StringComparison.Ordinal), "Captured globals must not escape final comparison diagnostics.");
            Assert.IsFalse(failure.ToString().Contains(sentinel, StringComparison.Ordinal));
            foreach (System.Collections.DictionaryEntry entry in failure.Data)
            {
                Assert.IsFalse(entry.Key.ToString()!.Contains(sentinel, StringComparison.Ordinal));
                if (entry.Value is string text) Assert.IsFalse(text.Contains(sentinel, StringComparison.Ordinal));
                if (entry.Value is string[] values) foreach (var value in values) Assert.IsFalse(value.Contains(sentinel, StringComparison.Ordinal));
            }
            if (closeRefusal)
            {
                var outcomes = failure.Data[OwnedResourceCleanup.OutcomeDataKey] as string[];
                Assert.IsNotNull(outcomes);
                CollectionAssert.Contains(outcomes, "Failed to close fork workspace: PublicInvalidOperationException");
                Assert.IsTrue(logger.Entries.Any(entry => entry.Exception is not null));
            }
            foreach (var entry in logger.Entries)
            {
                Assert.IsFalse(entry.Message.Contains(sentinel, StringComparison.Ordinal));
                Assert.IsFalse((entry.Exception?.ToString() ?? "").Contains(sentinel, StringComparison.Ordinal));
            }
            foreach (var (path, before) in bytes) CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspaceId));
            Assert.IsNotNull(PreviewStore.Retrieve(token));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native comparison fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (workspaceId is not null) WorkspaceManager.Close(workspaceId); }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class ReloadDocumentObserver(IWorkspaceManager inner, IWorkspaceEvaluationLoader loader,
        IWorkspaceRecoveryAuthority authority, string? closeFailureSentinel) : IWorkspaceManager, IWorkspaceEvaluationLoader, IWorkspaceRecoveryAuthority
    {
        public void AssertForkPathPolicy(ForkInvocationPathPolicy policy) => loader.AssertForkPathPolicy(policy);
        private string? _reloaded;
        internal bool ObservedFinalReload { get; private set; }
        public event Action<string>? WorkspaceClosed { add => inner.WorkspaceClosed += value; remove => inner.WorkspaceClosed -= value; }
        public event Action<string>? WorkspaceReloaded { add => inner.WorkspaceReloaded += value; remove => inner.WorkspaceReloaded -= value; }
        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy policy, CancellationToken ct) => inner.LoadAsync(path, policy, ct);
        public SupplementalPackageReadAuthority CapturePackageReadAuthority(string workspaceId, Solution reviewedSource) => loader.CapturePackageReadAuthority(workspaceId, reviewedSource);
        public Task<WorkspaceStatusDto> LoadWithPropertiesAsync(string path, IDictionary<string, string> properties, SupplementalPackageReadAuthority supplementalPackages, ForkInvocationAuthority? invocation, CancellationToken ct) => loader.LoadWithPropertiesAsync(path, properties, supplementalPackages, invocation, ct);
        public ForkInvocationAuthority CaptureStageInvocationAuthority(string id, Solution source) => loader.CaptureStageInvocationAuthority(id, source);
        public ForkInvocationAuthority CaptureForkInvocationAuthority(string id, Solution source, string path, IReadOnlyDictionary<string, string> properties,
            PhysicalDirectoryScope.OwnedRoot destination, PhysicalGraphLayout layout) => loader.CaptureForkInvocationAuthority(id, source, path, properties, destination, layout);
        public async Task<WorkspaceStatusDto> ReloadWithInvocationAuthorityAsync(string id, ForkInvocationAuthority invocation, CancellationToken ct)
        {
            var result = await loader.ReloadWithInvocationAuthorityAsync(id, invocation, ct);
            _reloaded = id;
            return result;
        }
        public async Task<WorkspaceStatusDto> ReloadAsync(string id, CancellationToken ct)
        {
            var result = await inner.ReloadAsync(id, ct);
            _reloaded = id;
            return result;
        }
        public Task<System.Collections.Immutable.ImmutableArray<RoslynMcp.Roslyn.Contracts.AdmittedModuleImage>> ReadAdmittedImagesAsync(
            string workspaceId, Microsoft.CodeAnalysis.ProjectId projectId, Microsoft.CodeAnalysis.PortableExecutableReference reference, CancellationToken ct) =>
            inner.ReadAdmittedImagesAsync(workspaceId, projectId, reference, ct);

        public Solution GetCurrentSolution(string id)
        {
            var solution = inner.GetCurrentSolution(id);
            if (id != _reloaded) return solution;
            var document = solution.Projects.SelectMany(project => project.AdditionalDocuments).Single(document => document.Name == "Observed.txt");
            ObservedFinalReload = true;
            return solution.WithAdditionalDocumentText(document.Id, SourceText.From("unreviewed native snapshot" + Environment.NewLine));
        }
        public Task AuthorizePathsAsync(string id, IReadOnlyList<string> paths, CancellationToken ct) => authority.AuthorizePathsAsync(id, paths, ct);
        public Task<PhysicalSourceCustody> AcquireSourceCustodyAsync(PhysicalMutationRecovery operation, string sourcePath,
            PhysicalDirectoryScope sourceParent, string leafName, string temporaryName, CancellationToken ct) =>
            authority.AcquireSourceCustodyAsync(operation, sourcePath, sourceParent, leafName, temporaryName, ct);
        public void EnsureMutationAllowed(string id, IReadOnlyList<string> paths) => authority.EnsureMutationAllowed(id, paths);
        public Task<IWorkspaceMutationLease> BeginMutationAsync(PhysicalMutationRecovery operation, CancellationToken ct) => authority.BeginMutationAsync(operation, ct);
        public Task EnsurePreparedOperationCurrentAsync(PhysicalMutationRecovery operation, CancellationToken ct) => authority.EnsurePreparedOperationCurrentAsync(operation, ct);
        public Task EnsureOperationCurrentAsync(PhysicalMutationRecovery operation, CancellationToken ct) => authority.EnsureOperationCurrentAsync(operation, ct);
        public void RestoreCapturedVersion(PhysicalMutationRecovery operation) => authority.RestoreCapturedVersion(operation);
        public Task CommitMutationAsync(PhysicalMutationRecovery operation, Solution? expected, CancellationToken ct) => authority.CommitMutationAsync(operation, expected, ct);
        public Task<IPhysicalRestorationAuthority> StageMutationAsync(PhysicalMutationRecovery operation,
            IReadOnlyList<FileSnapshotDto> desiredFiles, CancellationToken ct) => authority.StageMutationAsync(operation, desiredFiles, ct);
        public Task VerifyMutationNamespacesAsync(PhysicalMutationRecovery operation, IPhysicalRestorationAuthority restoration,
            MutationNamespaceTarget target, CancellationToken ct) => authority.VerifyMutationNamespacesAsync(operation, restoration, target, ct);

        public Task<IPhysicalRestorationAuthority> StageRestorationAsync(PhysicalMutationRecovery operation, IReadOnlyList<FileSnapshotDto> files, PhysicalGraphLayout? layout, CancellationToken ct) => authority.StageRestorationAsync(operation, files, layout, ct);
        public Task CommitRestorationAsync(PhysicalMutationRecovery operation, PhysicalGraphExpectation expected, bool retained, CancellationToken ct) => authority.CommitRestorationAsync(operation, expected, retained, ct);
        public Task<PhysicalMutationRecovery> CaptureRecoveryAsync(string id, string description, IReadOnlyList<FileSnapshotDto> files, CancellationToken ct) => authority.CaptureRecoveryAsync(id, description, files, ct);
        public PhysicalMutationRecovery? GetPendingRecovery(string id) => authority.GetPendingRecovery(id);
        public void RetainRecovery(PhysicalMutationRecovery recovery) => authority.RetainRecovery(recovery);
        public void AssertKnownFileRead(PhysicalMutationRecovery operation, FileSnapshotDto observed) => authority.AssertKnownFileRead(operation, observed);
        public Task EnsureRecoveryCurrentAsync(PhysicalMutationRecovery recovery, CancellationToken ct) => authority.EnsureRecoveryCurrentAsync(recovery, ct);
        public bool ContainsWorkspace(string id) => inner.ContainsWorkspace(id);
        public WorkspaceNotFoundException CreateWorkspaceNotFoundException(string id) => inner.CreateWorkspaceNotFoundException(id);
        public bool IsStale(string id) => inner.IsStale(id);
        public bool Close(string id)
        {
            var result = inner.Close(id);
            if (id == _reloaded && closeFailureSentinel is not null)
                throw new PublicInvalidOperationException("Synthetic close refusal: " + closeFailureSentinel);
            return result;
        }
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => inner.ListWorkspaces();
        public IReadOnlyList<string> FindWorkspaceIdsContainingFile(string path) => inner.FindWorkspaceIdsContainingFile(path);
        public WorkspaceStatusDto GetStatus(string id) => inner.GetStatus(id);
        public Task<WorkspaceStatusDto> GetStatusAsync(string id, CancellationToken cancellationToken = default) => inner.GetStatusAsync(id, cancellationToken);
        public ProjectGraphDto GetProjectGraph(string id) => inner.GetProjectGraph(id);
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string id, string? project, CancellationToken ct) => inner.GetSourceGeneratedDocumentsAsync(id, project, ct);
        public Task<string?> GetSourceTextAsync(string id, string path, CancellationToken ct) => inner.GetSourceTextAsync(id, path, ct);
        public int GetCurrentVersion(string id) => inner.GetCurrentVersion(id);
        public Project? GetProject(string id, string name) => inner.GetProject(id, name);
        public void RestoreVersion(string id, int version) => inner.RestoreVersion(id, version);
    }
}
