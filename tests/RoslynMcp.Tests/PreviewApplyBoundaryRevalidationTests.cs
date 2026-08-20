using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// preview-apply-token-write-path-toctou: redeeming a preview token must revalidate the preview's
/// write set against the sanctioned-root boundary AT REDEMPTION TIME (under the write gate),
/// instead of trusting the boundary check performed at preview time — which could be a full
/// preview-TTL (default 5 minutes) earlier, leaving the whole TTL as a validate-to-write window
/// for a symlink/junction swap. Also pins the sibling half of the row: <c>apply_multi_file_edit</c>
/// must persist to the canonical link-resolved target the validator returned, not to the
/// client-supplied path string.
/// </summary>
/// <remarks>
/// [DoNotParallelize]: these tests set the process-global <see cref="SecurityOptionsSnapshot"/> and
/// <see cref="RootExpansionGrantRegistry"/>, both of which
/// <see cref="ToolDispatch.RevalidateChangedPathsAsync"/> reads on every token apply — a
/// concurrently-running class redeeming preview tokens would otherwise be validated against this
/// class's temporary boundary.
/// </remarks>
[DoNotParallelize]
[TestClass]
public sealed class PreviewApplyBoundaryRevalidationTests
{
    /// <summary>Workspace id every staged preview in this class is stored under.</summary>
    private const string WorkspaceId = "ws-toctou";

    private sealed record FakeResultDto(string Message);

    /// <summary>
    /// The link-swap regression this row exists for: the preview's changed document sits inside
    /// the sanctioned root at preview time; the containing directory link is then re-pointed at
    /// an out-of-boundary target; redemption must be REFUSED with the boundary
    /// <see cref="ArgumentException"/> before the service call runs, and no bytes may land
    /// outside the root.
    /// </summary>
    [TestMethod]
    public async Task ApplyByToken_LinkSwappedOutOfBoundaryAfterPreview_RefusesRedemption()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-toctou-swap-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var inside = Path.Combine(sanctionedRoot, "real");
        var outside = Path.Combine(testRoot, "outside");
        Directory.CreateDirectory(inside);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(inside, "Program.cs"), "// inside target");
        File.WriteAllText(Path.Combine(outside, "Program.cs"), "// outside target — must never be written");

        var link = Path.Combine(sanctionedRoot, "link");
        if (!TestFixtureFileSystem.TryCreateDirectoryLink(link, inside))
        {
            Assert.Inconclusive("Directory links are unavailable in this test environment.");
            return;
        }

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = [sanctionedRoot] };

            var documentPath = Path.Combine(link, "Program.cs");
            var (store, token) = StagePreview(documentPath);

            // PeekChangedPaths must surface the write set without consuming the entry.
            var changedPaths = store.PeekChangedPaths(token);
            Assert.IsNotNull(changedPaths);
            Assert.AreEqual(1, changedPaths.Count);
            Assert.AreEqual(documentPath, changedPaths[0]);
            Assert.IsNotNull(store.Retrieve(token),
                "PeekChangedPaths must be non-consuming — the redeeming service's Retrieve must still succeed.");

            // The swap: re-point the containing link outside the boundary AFTER the preview
            // (deterministic stand-in for the race inside the preview-TTL window).
            Directory.Delete(link);
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(link, outside))
            {
                Assert.Inconclusive("Directory link re-pointing is unavailable in this test environment.");
                return;
            }

            var gate = new FakeGate();
            var serviceCallRan = false;

            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                    gate,
                    store,
                    token,
                    serviceCall: _ =>
                    {
                        serviceCallRan = true;
                        return Task.FromResult(new FakeResultDto("must-not-run"));
                    },
                    ct: CancellationToken.None));

            StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
            Assert.IsFalse(serviceCallRan,
                "Redemption-time boundary revalidation must refuse the apply BEFORE the service call runs.");
            Assert.AreEqual(1, gate.WriteCallCount,
                "Revalidation must run INSIDE the write gate, holding the same lock as the persistence write.");
            Assert.AreEqual("// outside target — must never be written",
                File.ReadAllText(Path.Combine(outside, "Program.cs")),
                "No bytes may land outside the sanctioned root.");
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    /// <summary>
    /// Happy path: a preview whose write set stays inside the sanctioned root still redeems
    /// normally — revalidation must not turn into a false-positive gate.
    /// </summary>
    [TestMethod]
    public async Task ApplyByToken_WriteSetInsideBoundary_StillApplies()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-toctou-happy-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        Directory.CreateDirectory(sanctionedRoot);
        var documentPath = Path.Combine(sanctionedRoot, "Program.cs");
        File.WriteAllText(documentPath, "// original");

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = [sanctionedRoot] };

            var (store, token) = StagePreview(documentPath);
            var gate = new FakeGate();

            var result = await ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                gate,
                store,
                token,
                serviceCall: _ => Task.FromResult(new FakeResultDto("applied")),
                ct: CancellationToken.None);

            StringAssert.Contains(result, "applied");
            Assert.AreEqual(1, gate.WriteCallCount);
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    /// <summary>
    /// Sibling half of the row: <c>apply_multi_file_edit</c> must pass the boundary-canonicalized
    /// (fully link-resolved) target the validator returned to the edit service — per the
    /// validator's documented contract ("callers that go on to write MUST persist to this returned
    /// value") — not the client-supplied path string, whose link components would be re-resolved
    /// at write time.
    /// </summary>
    [TestMethod]
    public async Task ApplyMultiFileEdit_PassesCanonicalLinkResolvedTarget_ToEditService()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-toctou-multi-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var real = Path.Combine(sanctionedRoot, "real");
        Directory.CreateDirectory(real);
        var realFile = Path.Combine(real, "Target.cs");
        File.WriteAllText(realFile, "// target");

        var link = Path.Combine(sanctionedRoot, "link");
        try
        {
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(link, real))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
                transportName: "toctou-multi-file-edit",
                clientCapabilities: new ClientCapabilities(),
                clientHandlers: new McpClientHandlers(),
                disposalFailureContext: "toctou-multi-file-edit",
                cancellationToken: CancellationToken.None,
                serverServicesFactory: () => new ServiceCollection()
                    .AddSingleton(new SecurityOptions { SanctionedRoots = [sanctionedRoot] })
                    .BuildServiceProvider());

            var clientSuppliedPath = Path.Combine(link, "Target.cs");
            var editService = new CapturingEditService();

            await MultiFileEditTools.ApplyMultiFileEdit(
                harness.Server,
                new FakeGate(),
                editService,
                workspaceId: "ws-multi",
                fileEdits: [new FileEditsDto(clientSuppliedPath, [new TextEditDto(1, 1, 1, 1, "// edited\n")])],
                ct: CancellationToken.None);

            Assert.IsNotNull(editService.ReceivedFileEdits);
            Assert.AreEqual(1, editService.ReceivedFileEdits.Count);
            var receivedPath = editService.ReceivedFileEdits[0].FilePath;
            var canonical = ClientRootPathValidator.ResolvePath(clientSuppliedPath);
            Assert.AreNotEqual(
                Path.GetFullPath(clientSuppliedPath),
                canonical,
                "Test premise: the link must make the client-supplied and canonical paths diverge.");
            Assert.AreEqual(canonical, receivedPath,
                "apply_multi_file_edit must hand the edit service the canonical link-resolved target " +
                "the validator returned, not the client-supplied path string.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    /// <summary>
    /// Regression for the boundary-narrowing defect the fix cycle caught: a workspace admitted via
    /// <c>workspace_load(expandSanctionedRoots: true)</c> legitimately holds every document under
    /// the WIDENED parent of a configured root (the sibling-worktree load mode). Redemption must
    /// re-derive that same widened boundary — re-deriving the narrower configured-roots-only
    /// boundary would permanently refuse every <c>*_apply</c> on such a workspace.
    /// </summary>
    [TestMethod]
    public async Task ApplyByToken_ExpansionLoadedWorkspace_WriteSetUnderWidenedParent_StillApplies()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-toctou-expand-" + Guid.NewGuid().ToString("N"));
        var widenedParent = Path.Combine(testRoot, "repos");
        var sanctionedRoot = Path.Combine(widenedParent, "primary");
        var siblingWorktree = Path.Combine(widenedParent, "worktree");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(siblingWorktree);
        var documentPath = Path.Combine(siblingWorktree, "Program.cs");
        File.WriteAllText(documentPath, "// original");

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions
            {
                SanctionedRoots = [sanctionedRoot],
                AllowRootExpansion = true,
            };

            var (store, token) = StagePreview(documentPath);
            var gate = new FakeGate();

            // Premise: without the recorded load-time grant the boundary narrows to the configured
            // root alone and the sibling-worktree write set falls outside it.
            RootExpansionGrantRegistry.Revoke(WorkspaceId);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                    gate,
                    store,
                    token,
                    serviceCall: _ => Task.FromResult(new FakeResultDto("must-not-run")),
                    ct: CancellationToken.None));

            // With the grant replayed, redemption re-derives the SAME boundary that admitted the
            // workspace and the apply succeeds.
            RootExpansionGrantRegistry.Grant(WorkspaceId);
            var result = await ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                gate,
                store,
                token,
                serviceCall: _ => Task.FromResult(new FakeResultDto("applied")),
                ct: CancellationToken.None);

            StringAssert.Contains(result, "applied");
        }
        finally
        {
            RootExpansionGrantRegistry.Revoke(WorkspaceId);
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    /// <summary>
    /// The grant must not degrade the check into a no-op: on an expansion-loaded workspace a write
    /// set that escapes even the WIDENED boundary is still refused at redemption time.
    /// </summary>
    [TestMethod]
    public async Task ApplyByToken_ExpansionLoadedWorkspace_WriteSetOutsideWidenedParent_RefusesRedemption()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-toctou-expand-esc-" + Guid.NewGuid().ToString("N"));
        var widenedParent = Path.Combine(testRoot, "repos");
        var sanctionedRoot = Path.Combine(widenedParent, "primary");
        var outside = Path.Combine(testRoot, "outside");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(outside);
        var documentPath = Path.Combine(outside, "Program.cs");
        File.WriteAllText(documentPath, "// outside target — must never be written");

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions
            {
                SanctionedRoots = [sanctionedRoot],
                AllowRootExpansion = true,
            };
            RootExpansionGrantRegistry.Grant(WorkspaceId);

            var (store, token) = StagePreview(documentPath);
            var gate = new FakeGate();
            var serviceCallRan = false;

            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                    gate,
                    store,
                    token,
                    serviceCall: _ =>
                    {
                        serviceCallRan = true;
                        return Task.FromResult(new FakeResultDto("must-not-run"));
                    },
                    ct: CancellationToken.None));

            StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
            Assert.IsFalse(serviceCallRan,
                "The expansion grant widens the boundary by exactly one level — it must not disable the check.");
        }
        finally
        {
            RootExpansionGrantRegistry.Revoke(WorkspaceId);
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    /// <summary>
    /// Stages a real <see cref="PreviewStore"/> entry whose single changed document carries
    /// <paramref name="documentFilePath"/>, mirroring how every in-tree preview service stores an
    /// original/modified solution pair.
    /// </summary>
    private static (PreviewStore Store, string Token) StagePreview(string documentFilePath)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("P", LanguageNames.CSharp);
        var documentInfo = DocumentInfo.Create(
            DocumentId.CreateNewId(project.Id),
            name: Path.GetFileName(documentFilePath),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From("// original"), VersionStamp.Create())),
            filePath: documentFilePath);
        var document = workspace.AddDocument(documentInfo);

        var original = workspace.CurrentSolution;
        var modified = original.WithDocumentText(document.Id, SourceText.From("// modified"));

        var store = new PreviewStore();
        var token = store.Store(WorkspaceId, original, modified, workspaceVersion: 1, "toctou test preview", diffTruncated: false);
        return (store, token);
    }

    /// <summary>
    /// Minimal write-gate stand-in mirroring <c>ToolDispatchTests.FakeGate</c>: runs the action
    /// inline and records the verb so the tests can assert the revalidation happened in-gate.
    /// </summary>
    private sealed class FakeGate : IWorkspaceExecutionGate
    {
        public int WriteCallCount { get; private set; }

        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => action(ct);

        public Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true)
        {
            WriteCallCount++;
            return action(ct);
        }

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => throw new NotSupportedException();

        public void RemoveGate(string workspaceId)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Captures the <see cref="FileEditsDto"/> batch <c>apply_multi_file_edit</c> forwards, so the
    /// canonical-target pin can be asserted without a loaded workspace.
    /// </summary>
    private sealed class CapturingEditService : IEditService
    {
        public IReadOnlyList<FileEditsDto>? ReceivedFileEdits { get; private set; }

        public Task<MultiFileEditResultDto> ApplyMultiFileTextEditsAsync(
            string workspaceId,
            IReadOnlyList<FileEditsDto> fileEdits,
            string toolName,
            CancellationToken ct,
            bool skipSyntaxCheck = false,
            bool verify = false,
            bool autoRevertOnError = false)
        {
            ReceivedFileEdits = fileEdits;
            var files = fileEdits.Select(f => new FileEditSummaryDto(f.FilePath, f.Edits.Count, null)).ToList();
            return Task.FromResult(new MultiFileEditResultDto(true, files.Count, files));
        }

        public Task<TextEditResultDto> ApplyTextEditsAsync(
            string workspaceId,
            string filePath,
            IReadOnlyList<TextEditDto> edits,
            string toolName,
            CancellationToken ct,
            bool skipSyntaxCheck = false,
            bool verify = false,
            bool autoRevertOnError = false,
            string? canonicalWritePath = null)
            => throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewMultiFileTextEditsAsync(
            string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct, bool skipSyntaxCheck = false)
            => throw new NotSupportedException();
    }
}
