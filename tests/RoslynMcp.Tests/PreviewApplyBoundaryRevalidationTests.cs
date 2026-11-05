using System.Text.Json.Nodes;
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
                    ct: CancellationToken.None,
                    expectedKind: PreviewKind.SymbolRename,
                    invokedRoute: "rename_apply"));

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
                ct: CancellationToken.None,
                expectedKind: PreviewKind.SymbolRename,
                invokedRoute: "rename_apply");

            StringAssert.Contains(result, "applied");
            Assert.AreEqual(1, gate.WriteCallCount);
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    [TestMethod]
    public async Task ApplyByToken_ClientRootsNarrowing_IsReappliedAtRedemption()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-toctou-client-roots-" + Guid.NewGuid().ToString("N"));
        var configuredRoot = Path.Combine(testRoot, "configured");
        var clientRoot = Path.Combine(configuredRoot, "client");
        var configuredOnly = Path.Combine(configuredRoot, "outside-client");
        Directory.CreateDirectory(clientRoot);
        Directory.CreateDirectory(configuredOnly);
        var documentPath = Path.Combine(configuredOnly, "Program.cs");
        File.WriteAllText(documentPath, "// configured-only target");

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = [configuredRoot] };
            await using var session = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(
                configuredRoot,
                CancellationToken.None,
                clientRootPaths: [clientRoot]);
            using var serverScope = RequestMcpServerContext.Begin(session.Server);
            var (store, token) = StagePreview(documentPath);
            var serviceCallRan = false;

            var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                    new FakeGate(),
                    store,
                    token,
                    serviceCall: _ =>
                    {
                        serviceCallRan = true;
                        return Task.FromResult(new FakeResultDto("must-not-run"));
                    },
                    ct: CancellationToken.None,
                    expectedKind: PreviewKind.SymbolRename,
                    invokedRoute: "rename_apply"));

            StringAssert.Contains(error.Message, "outside the configured sanctioned-root boundary");
            Assert.IsFalse(serviceCallRan);
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    [TestMethod]
    public void PeekChangedPaths_RemovedProject_IncludesEveryDocumentPath()
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Removed", LanguageNames.CSharp);
        var firstPath = Path.Combine(TestTempRoot.Current, "removed-project", "First.cs");
        var secondPath = Path.Combine(TestTempRoot.Current, "removed-project", "Second.cs");
        var first = workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(project.Id),
            "First.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From("class First {}"), VersionStamp.Create())),
            filePath: firstPath));
        workspace.AddDocument(DocumentInfo.Create(
            DocumentId.CreateNewId(project.Id),
            "Second.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From("class Second {}"), VersionStamp.Create())),
            filePath: secondPath));
        var original = workspace.CurrentSolution;
        var modified = original.RemoveProject(first.Project.Id);
        var store = new PreviewStore();
        var token = store.Store(WorkspaceId, original, modified, 1, "remove project", diffTruncated: false);

        var paths = store.PeekChangedPaths(token);

        Assert.IsNotNull(paths);
        CollectionAssert.AreEquivalent(new[] { firstPath, secondPath }, paths.ToArray());
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
                    ct: CancellationToken.None,
                    expectedKind: PreviewKind.SymbolRename,
                    invokedRoute: "rename_apply"));

            // With the grant replayed, redemption re-derives the SAME boundary that admitted the
            // workspace and the apply succeeds.
            RootExpansionGrantRegistry.Grant(WorkspaceId);
            var result = await ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                gate,
                store,
                token,
                serviceCall: _ => Task.FromResult(new FakeResultDto("applied")),
                ct: CancellationToken.None,
                expectedKind: PreviewKind.SymbolRename,
                invokedRoute: "rename_apply");

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
                    ct: CancellationToken.None,
                    expectedKind: PreviewKind.SymbolRename,
                    invokedRoute: "rename_apply"));

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

    // Drive-root-parent fixtures: the sanctioned root sits directly under the filesystem root of the
    // temp volume (the D:\<repo> layout). Nothing is created on disk; the boundary check is
    // path-only and nonexistent components canonicalize lexically.
    private static string DriveRootChild(string name)
        => Path.Combine(Path.GetPathRoot(TestTempRoot.Current)!, name);

    private static async Task<string> ApplyWithDriveRootRootAsync(string documentPath, string sanctionedRoot, bool granted)
    {
        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions
            {
                SanctionedRoots = [sanctionedRoot],
                AllowRootExpansion = true,
            };
            if (granted)
            {
                RootExpansionGrantRegistry.Grant(WorkspaceId);
            }
            else
            {
                RootExpansionGrantRegistry.Revoke(WorkspaceId);
            }

            var (store, token) = StagePreview(documentPath);
            return await ToolDispatch.ApplyByTokenAsync<FakeResultDto>(
                new FakeGate(),
                store,
                token,
                serviceCall: _ => Task.FromResult(new FakeResultDto("applied")),
                ct: CancellationToken.None,
                expectedKind: PreviewKind.SymbolRename,
                invokedRoute: "rename_apply");
        }
        finally
        {
            RootExpansionGrantRegistry.Revoke(WorkspaceId);
            SecurityOptionsSnapshot.Value = previousSnapshot;
        }
    }

    [TestMethod]
    public async Task ApplyByToken_DriveRootParent_GrantedPrefixedSibling_StillApplies()
    {
        var root = DriveRootChild("rmcp-dr-" + Guid.NewGuid().ToString("N"));
        var result = await ApplyWithDriveRootRootAsync(
            Path.Combine(root + "-audit-x", "Program.cs"), root, granted: true);
        StringAssert.Contains(result, "applied");
    }

    [TestMethod]
    public async Task ApplyByToken_DriveRootParent_PrefixedSiblingWithoutGrant_RefusesRedemption()
    {
        var root = DriveRootChild("rmcp-dr-" + Guid.NewGuid().ToString("N"));
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ApplyWithDriveRootRootAsync(Path.Combine(root + "-audit-x", "Program.cs"), root, granted: false));
        StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
    }

    [TestMethod]
    public async Task ApplyByToken_DriveRootParent_NonPrefixedSiblingRefusedEvenWhenGranted()
    {
        var root = DriveRootChild("rmcp-dr-" + Guid.NewGuid().ToString("N"));
        // "<root>2" shares the name prefix but not the "<root>-" separator; "<root>-" has no suffix.
        foreach (var sibling in new[] { root + "2", root + "-" })
        {
            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ApplyWithDriveRootRootAsync(Path.Combine(sibling, "Program.cs"), root, granted: true));
            StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
        }
    }

    [TestMethod]
    public async Task ApplyByToken_DriveRootParent_PrefixedSiblingLinkEscapingElsewhere_RefusesRedemption()
    {
        var root = DriveRootChild("rmcp-dr-" + Guid.NewGuid().ToString("N"));
        var link = root + "-escape";
        var outside = Path.Combine(TestTempRoot.Current, "rmcp-dr-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var linkCreated = false;
        try
        {
            try
            {
                linkCreated = TestFixtureFileSystem.TryCreateDirectoryLink(link, outside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The filesystem root is not writable here (e.g. Linux CI): the link cannot exist.
            }

            if (!linkCreated)
            {
                Assert.Inconclusive("A link directly under the filesystem root cannot be created in this test environment.");
                return;
            }

            // The path is lexically a prefixed sibling, but physically resolves outside the boundary.
            var ex2 = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ApplyWithDriveRootRootAsync(Path.Combine(link, "Program.cs"), root, granted: true));
            StringAssert.Contains(ex2.Message, "outside the configured sanctioned-root boundary");
        }
        finally
        {
            if (linkCreated)
            {
                TestFixtureFileSystem.DeleteDirectoryIfExists(link);
            }

            TestFixtureFileSystem.DeleteDirectoryIfExists(outside);
        }
    }

    // composite-redemption-admission-boundary: the delegate-peek ApplyByTokenAsync overload backs
    // apply_composite and apply_project_mutation. It used to skip the redemption-time
    // sanctioned-root revalidation the IPreviewStore overload performs, so a composite token whose
    // persisted mutation path lay outside the boundary was claimed and written.

    /// <summary>
    /// The core defect: a composite token whose write set names a path outside the sanctioned
    /// roots must be refused at redemption, before the orchestrator runs, and no bytes may land
    /// outside the boundary.
    /// </summary>
    [TestMethod]
    public async Task ApplyComposite_InMemoryToken_PathOutsideSanctionedRoot_RefusesBeforeWrite()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-composite-mem-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var outside = Path.Combine(testRoot, "outside");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "Program.cs");
        File.WriteAllText(outsideFile, OutsideOriginal);

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = [sanctionedRoot] };
            var store = new CompositePreviewStore();
            var token = store.Store(
                WorkspaceId, 1, "out-of-root composite", [new CompositeFileMutation(outsideFile, "// OVERWRITTEN")]);
            var orchestrator = new CompositeApplyOrchestrator(new RecordingWorkspaceManager(), store);

            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                OrchestrationTools.ApplyComposite(new FakeGate(), orchestrator, store, token, CancellationToken.None));

            StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
            Assert.IsFalse(ex.Message.Contains(outsideFile, StringComparison.OrdinalIgnoreCase),
                "The refusal must not echo the offending path.");
            Assert.AreEqual(OutsideOriginal, File.ReadAllText(outsideFile),
                "No bytes may land outside the sanctioned root.");
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    /// <summary>
    /// The disk store's persisted record is the authority <c>Retrieve</c> claims, not the creator's
    /// in-memory copy: a record edited (or forged) in a shared persist directory to name an
    /// out-of-root path must be refused BEFORE the claim — the payload stays on disk — and before
    /// any write.
    /// </summary>
    [TestMethod]
    public async Task ApplyComposite_PersistedPayloadEditedOutsideSanctionedRoot_RefusesBeforeClaimAndWrite()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-composite-disk-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var outside = Path.Combine(testRoot, "outside");
        var persistDirectory = Path.Combine(testRoot, "persist");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(outside);
        var insideFile = Path.Combine(sanctionedRoot, "Program.cs");
        var outsideFile = Path.Combine(outside, "Program.cs");
        File.WriteAllText(insideFile, "// inside original");
        File.WriteAllText(outsideFile, OutsideOriginal);

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = [sanctionedRoot] };
            var ttl = TimeSpan.FromMinutes(5);
            var store = new CompositePreviewStore(20, ttl, new PersistentCompositeStorage(persistDirectory, ttl));
            var token = store.Store(
                WorkspaceId, 1, "tampered composite", [new CompositeFileMutation(insideFile, "// OVERWRITTEN")]);
            var payloadPath = Directory.EnumerateFiles(persistDirectory, token + ".json", SearchOption.AllDirectories).Single();
            RewritePersistedPayload(persistDirectory, token, payload => payload["Mutations"]![0]!["FilePath"] = outsideFile);
            var orchestrator = new CompositeApplyOrchestrator(new RecordingWorkspaceManager(), store);

            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                OrchestrationTools.ApplyComposite(new FakeGate(), orchestrator, store, token, CancellationToken.None));

            StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
            Assert.AreEqual(OutsideOriginal, File.ReadAllText(outsideFile),
                "No bytes may land outside the sanctioned root.");
            Assert.AreEqual("// inside original", File.ReadAllText(insideFile));
            Assert.IsTrue(File.Exists(payloadPath),
                "A refused redemption must not claim (consume) the one-time persisted payload.");
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    /// <summary>
    /// The peek-then-claim race: the pre-claim revalidation sees an in-root record, the shared
    /// persist directory is then swapped to an out-of-root record, and the claim hands THAT record
    /// to the orchestrator. The write set the orchestrator is about to apply is the one that must be
    /// validated, so the swapped record is refused before any write.
    /// </summary>
    [TestMethod]
    public async Task ApplyComposite_RecordSwappedOutOfBoundaryBetweenPeekAndClaim_RefusesBeforeWrite()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-composite-swap-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var outside = Path.Combine(testRoot, "outside");
        var persistDirectory = Path.Combine(testRoot, "persist");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(outside);
        var insideFile = Path.Combine(sanctionedRoot, "Program.cs");
        var outsideFile = Path.Combine(outside, "Program.cs");
        File.WriteAllText(insideFile, "// inside original");
        File.WriteAllText(outsideFile, OutsideOriginal);

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = [sanctionedRoot] };
            var ttl = TimeSpan.FromMinutes(5);
            var inner = new CompositePreviewStore(20, ttl, new PersistentCompositeStorage(persistDirectory, ttl));
            var token = inner.Store(
                WorkspaceId, 1, "swapped composite", [new CompositeFileMutation(insideFile, "// OVERWRITTEN")]);
            var store = new SwapAfterPeekStore(inner, () => RewritePersistedPayload(
                persistDirectory, token, payload => payload["Mutations"]![0]!["FilePath"] = outsideFile));
            var orchestrator = new CompositeApplyOrchestrator(new RecordingWorkspaceManager(), store);

            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                OrchestrationTools.ApplyComposite(new FakeGate(), orchestrator, store, token, CancellationToken.None));

            StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
            Assert.IsTrue(store.Swapped, "Premise: the swap ran between the peek and the claim.");
            Assert.AreEqual(OutsideOriginal, File.ReadAllText(outsideFile),
                "The claimed record's write set must be validated, not the one peeked earlier.");
            Assert.AreEqual("// inside original", File.ReadAllText(insideFile));
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    /// <summary>
    /// The write gate is taken on the workspace the peek reported. A record swapped to name another
    /// workspace between the peek and the claim must not be applied (and reloaded) under a gate that
    /// protects a different session.
    /// </summary>
    [TestMethod]
    public async Task ApplyComposite_RecordSwappedToAnotherWorkspaceBetweenPeekAndClaim_RefusesBeforeWrite()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-composite-wsswap-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var persistDirectory = Path.Combine(testRoot, "persist");
        Directory.CreateDirectory(sanctionedRoot);
        var insideFile = Path.Combine(sanctionedRoot, "Program.cs");
        File.WriteAllText(insideFile, "// inside original");

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = [sanctionedRoot] };
            var ttl = TimeSpan.FromMinutes(5);
            var inner = new CompositePreviewStore(20, ttl, new PersistentCompositeStorage(persistDirectory, ttl));
            var token = inner.Store(
                WorkspaceId, 1, "workspace swap", [new CompositeFileMutation(insideFile, "// OVERWRITTEN")]);
            var store = new SwapAfterPeekStore(inner, () => RewritePersistedPayload(
                persistDirectory, token, payload => payload["WorkspaceId"] = "ws-other"));
            var orchestrator = new CompositeApplyOrchestrator(new RecordingWorkspaceManager(), store);

            await Assert.ThrowsExactlyAsync<PreviewTokenStaleException>(() =>
                OrchestrationTools.ApplyComposite(new FakeGate(), orchestrator, store, token, CancellationToken.None));

            Assert.IsTrue(store.Swapped, "Premise: the swap ran between the peek and the claim.");
            Assert.AreEqual("// inside original", File.ReadAllText(insideFile));
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    private static void RewritePersistedPayload(string persistDirectory, string token, Action<JsonNode> edit)
    {
        var payloadPath = Directory.EnumerateFiles(persistDirectory, token + ".json", SearchOption.AllDirectories).Single();
        var payload = JsonNode.Parse(File.ReadAllText(payloadPath))!;
        edit(payload);
        File.WriteAllText(payloadPath, payload.ToJsonString());
    }

    /// <summary>
    /// Delegating store that runs <c>swap</c> right after the write-set peek returns, modelling a
    /// party with write access to the shared persist directory winning the peek-to-claim window.
    /// </summary>
    private sealed class SwapAfterPeekStore(CompositePreviewStore inner, Action swap) : ICompositePreviewStore
    {
        public bool Swapped { get; private set; }

        public string Store(string workspaceId, int workspaceVersion, string description, IReadOnlyList<CompositeFileMutation> mutations) =>
            inner.Store(workspaceId, workspaceVersion, description, mutations);

        public (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(string token) =>
            inner.Retrieve(token);

        public void Invalidate(string token) => inner.Invalidate(token);

        public string? PeekWorkspaceId(string token) => inner.PeekWorkspaceId(token);

        public IReadOnlyList<string>? PeekChangedPaths(string token)
        {
            var peeked = inner.PeekChangedPaths(token);
            if (!Swapped)
            {
                swap();
                Swapped = true;
            }

            return peeked;
        }
    }

    /// <summary>
    /// Fail-closed: when the persisted record cannot be read (claimed by another host, removed,
    /// expired) the write set is unknown, and the redemption must be refused as a stale token
    /// rather than treated like the IPreviewStore "null means unknown, skip" convention.
    /// </summary>
    [TestMethod]
    public async Task ApplyComposite_PersistedPayloadUnavailable_RefusesAsStaleBeforeOrchestrator()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-composite-gone-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var persistDirectory = Path.Combine(testRoot, "persist");
        Directory.CreateDirectory(sanctionedRoot);
        var insideFile = Path.Combine(sanctionedRoot, "Program.cs");
        File.WriteAllText(insideFile, "// inside original");

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = [sanctionedRoot] };
            var ttl = TimeSpan.FromMinutes(5);
            var store = new CompositePreviewStore(20, ttl, new PersistentCompositeStorage(persistDirectory, ttl));
            var token = store.Store(
                WorkspaceId, 1, "claimed elsewhere", [new CompositeFileMutation(insideFile, "// OVERWRITTEN")]);
            File.Delete(Directory.EnumerateFiles(persistDirectory, token + ".json", SearchOption.AllDirectories).Single());
            var orchestrator = new CompositeApplyOrchestrator(new RecordingWorkspaceManager(), store);

            await Assert.ThrowsExactlyAsync<PreviewTokenStaleException>(() =>
                OrchestrationTools.ApplyComposite(new FakeGate(), orchestrator, store, token, CancellationToken.None));

            Assert.AreEqual("// inside original", File.ReadAllText(insideFile));
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    /// <summary>
    /// Happy path: a composite whose write set stays inside the boundary still redeems through the
    /// disk store — revalidation must not become a false-positive gate or consume the token early.
    /// </summary>
    [TestMethod]
    public async Task ApplyComposite_PersistedWriteSetInsideBoundary_StillApplies()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-composite-ok-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var persistDirectory = Path.Combine(testRoot, "persist");
        Directory.CreateDirectory(sanctionedRoot);
        var insideFile = Path.Combine(sanctionedRoot, "Program.cs");
        File.WriteAllText(insideFile, "// inside original");

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = [sanctionedRoot] };
            var ttl = TimeSpan.FromMinutes(5);
            var store = new CompositePreviewStore(20, ttl, new PersistentCompositeStorage(persistDirectory, ttl));
            var token = store.Store(
                WorkspaceId, 1, "in-root composite", [new CompositeFileMutation(insideFile, "// updated")]);
            var orchestrator = new CompositeApplyOrchestrator(new RecordingWorkspaceManager(), store);

            var result = await OrchestrationTools.ApplyComposite(
                new FakeGate(), orchestrator, store, token, CancellationToken.None);

            StringAssert.Contains(result, "\"success\": true");
            Assert.AreEqual("// updated", File.ReadAllText(insideFile));
            Assert.IsEmpty(Directory.EnumerateFiles(persistDirectory, token + ".json*", SearchOption.AllDirectories),
                "A successful redemption consumes the one-time persisted payload.");
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    /// <summary>
    /// Same mechanism, second caller of the delegate-peek overload: <c>apply_project_mutation</c>
    /// writes <c>ProjectFilePath</c> with no redemption-time boundary check either.
    /// </summary>
    [TestMethod]
    public async Task ApplyProjectMutation_ProjectFileOutsideSanctionedRoot_RefusesBeforeService()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-projmut-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var outside = Path.Combine(testRoot, "outside");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(outside);
        var outsideProject = Path.Combine(outside, "Outside.csproj");
        File.WriteAllText(outsideProject, OutsideOriginal);

        var previousSnapshot = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = [sanctionedRoot] };
            var store = new ProjectMutationPreviewStore();
            var token = store.Store(WorkspaceId, outsideProject, "<Project />", 1, "out-of-root project mutation");
            var service = new RecordingProjectMutationService();

            var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ProjectMutationTools.ApplyProjectMutation(new FakeGate(), service, store, token, CancellationToken.None));

            StringAssert.Contains(ex.Message, "outside the configured sanctioned-root boundary");
            Assert.IsFalse(service.ApplyCalled, "Revalidation must refuse BEFORE the mutation service runs.");
            Assert.AreEqual(OutsideOriginal, File.ReadAllText(outsideProject));
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previousSnapshot;
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    private const string OutsideOriginal = "// outside target - must never be written";

    private sealed class RecordingProjectMutationService : IProjectMutationService
    {
        public bool ApplyCalled { get; private set; }

        public Task<ApplyResultDto> ApplyProjectMutationAsync(string previewToken, CancellationToken ct)
        {
            ApplyCalled = true;
            return Task.FromResult(new ApplyResultDto(true, [], null));
        }

        public Task<RefactoringPreviewDto> PreviewAddPackageReferenceAsync(string workspaceId, AddPackageReferenceDto request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefactoringPreviewDto> PreviewRemovePackageReferenceAsync(string workspaceId, RemovePackageReferenceDto request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefactoringPreviewDto> PreviewAddProjectReferenceAsync(string workspaceId, AddProjectReferenceDto request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefactoringPreviewDto> PreviewRemoveProjectReferenceAsync(string workspaceId, RemoveProjectReferenceDto request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefactoringPreviewDto> PreviewSetProjectPropertyAsync(string workspaceId, SetProjectPropertyDto request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefactoringPreviewDto> PreviewAddTargetFrameworkAsync(string workspaceId, AddTargetFrameworkDto request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefactoringPreviewDto> PreviewRemoveTargetFrameworkAsync(string workspaceId, RemoveTargetFrameworkDto request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefactoringPreviewDto> PreviewSetConditionalPropertyAsync(string workspaceId, SetConditionalPropertyDto request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefactoringPreviewDto> PreviewAddCentralPackageVersionAsync(string workspaceId, AddCentralPackageVersionDto request, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefactoringPreviewDto> PreviewRemoveCentralPackageVersionAsync(string workspaceId, RemoveCentralPackageVersionDto request, CancellationToken ct) => throw new NotSupportedException();
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
