using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tests;

[TestClass]
public class PreviewStoreTests
{
    private PreviewStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _store = new PreviewStore();
    }

    [TestMethod]
    public void StoreContract_RequiresExplicitSnapshotsDiffAndProducerKind()
    {
        foreach (var type in new[] { typeof(PreviewStore), typeof(RoslynMcp.Roslyn.Contracts.IPreviewStore) })
        {
            var methods = type.GetMethods().Where(method => method.Name == "Store").ToArray();
            Assert.HasCount(1, methods, "Every store call must supply all safety state.");
            var parameters = methods[0].GetParameters();
            Assert.HasCount(7, parameters);
            Assert.AreEqual(typeof(Solution), parameters[1].ParameterType);
            Assert.AreEqual(typeof(Solution), parameters[2].ParameterType);
            Assert.AreEqual(typeof(IReadOnlyList<FileChangeDto>), parameters[5].ParameterType);
            Assert.AreEqual(typeof(PreviewKind), parameters[6].ParameterType);
            Assert.IsTrue(parameters.All(parameter => !parameter.IsOptional));
        }
    }

    [TestMethod]
    public void StoreContract_InterfaceRequiresStatefulImplementations()
    {
        var contract = typeof(RoslynMcp.Roslyn.Contracts.IPreviewStore);
        foreach (var name in new[] { "Store", "PeekKind", "PeekChangedPaths" })
            Assert.IsTrue(contract.GetMethod(name)!.IsAbstract,
                $"{name} must not silently discard state through a default interface body.");
    }

    [TestMethod]
    [DataRow("+@@ truncated @@")]
    [DataRow("-@@ truncated @@")]
    [DataRow(" @@ truncated @@")]
    [DataRow("+var text = \"@@ truncated @@\";")]
    public void Store_SourceTextResemblingTruncationMarker_DoesNotBlockApply(string diffLine)
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var token = _store.Store("workspace", solution, solution, 1, "marker text",
            [new FileChangeDto("probe.cs", "--- a/probe.cs\n+++ b/probe.cs\n" + diffLine + "\r\n")], PreviewKind.CodeFix);
        Assert.IsFalse(_store.Retrieve(token)!.Value.DiffTruncated);
    }

    [TestMethod]
    public void Store_PerFileTruncationRetainsSafetyAndInterfaceProvenance()
    {
        using var workspace = new AdhocWorkspace();
        RoslynMcp.Roslyn.Contracts.IPreviewStore store = _store;
        var original = workspace.CurrentSolution;
        var diff = RoslynMcp.Roslyn.Helpers.DiffGenerator.GenerateUnifiedDiff(
            "before", new string('x', 500), "probe.cs", maxChars: 64);
        StringAssert.Contains(diff, "@@ truncated @@");
        var token = store.Store("workspace", original, original, 1, "bounded", [new FileChangeDto("probe.cs", diff)], PreviewKind.CodeFix);
        var entry = store.Retrieve(token);
        Assert.IsNotNull(entry);
        Assert.IsTrue(entry.Value.DiffTruncated);
        Assert.AreEqual(PreviewKind.CodeFix, store.PeekKind(token));
    }

    [TestMethod]
    public void Store_ExplicitOriginalPreservesAuthoritativeGraphInsteadOfNativeWorkspace()
    {
        using var workspace = new AdhocWorkspace();
        var id = ProjectId.CreateNewId();
        var authoritative = workspace.CurrentSolution.AddProject(id, "Authoritative", "Authoritative", LanguageNames.CSharp);
        var modified = authoritative.WithProjectAssemblyName(id, "Modified");
        var token = _store.Store("workspace", authoritative, modified, 1, "authoritative", [], PreviewKind.CodeFix);
        var entry = _store.Retrieve(token);
        Assert.IsNotNull(entry);
        Assert.AreSame(authoritative, entry.Value.OriginalSolution);
        Assert.AreSame(modified, entry.Value.ModifiedSolution);
    }

    [TestMethod]
    public void Store_And_Retrieve_Returns_Entry()
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token = _store.Store("workspace-1", solution, solution, 1, "test", [], PreviewKind.Unspecified);
        var result = _store.Retrieve(token);

        Assert.IsNotNull(result);
        Assert.AreEqual("test", result.Value.Description);
        Assert.AreEqual("workspace-1", result.Value.WorkspaceId);
    }

    [TestMethod]
    public void Retrieve_Returns_Workspace_Version_For_External_Validation()
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token = _store.Store("workspace-1", solution, solution, 1, "test", [], PreviewKind.Unspecified);
        var result = _store.Retrieve(token);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Value.WorkspaceVersion);
    }

    [TestMethod]
    public void Retrieve_After_Invalidate_Returns_Null()
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token = _store.Store("workspace-1", solution, solution, 1, "test", [], PreviewKind.Unspecified);
        _store.Invalidate(token);
        var result = _store.Retrieve(token);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void InvalidateAll_Clears_All_Entries()
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token1 = _store.Store("workspace-1", solution, solution, 1, "test1", [], PreviewKind.Unspecified);
        var token2 = _store.Store("workspace-2", solution, solution, 1, "test2", [], PreviewKind.Unspecified);

        _store.InvalidateAll();

        Assert.IsNull(_store.Retrieve(token1));
        Assert.IsNull(_store.Retrieve(token2));
    }

    [TestMethod]
    public void Retrieve_With_Invalid_Token_Returns_Null()
    {
        var result = _store.Retrieve("nonexistent");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void InvalidateAll_For_Workspace_Clears_Only_Matching_Entries()
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token1 = _store.Store("workspace-1", solution, solution, 1, "test1", [], PreviewKind.Unspecified);
        var token2 = _store.Store("workspace-2", solution, solution, 1, "test2", [], PreviewKind.Unspecified);

        _store.InvalidateAll("workspace-1");

        Assert.IsNull(_store.Retrieve(token1));
        Assert.IsNotNull(_store.Retrieve(token2));
    }

    // ---------------------------------------------------------------
    // format-range-apply-preview-token-lifetime: pinned-version-range
    // tests covering the auto-reload-vs-apply lifetime contract.
    // ---------------------------------------------------------------

    [TestMethod]
    public void InvalidateOnVersionBump_Single_Bump_Within_Default_Span_Keeps_Token_Redeemable()
    {
        // Mirrors the real-world failure documented in the diagnosis:
        //   format_range_preview at workspace version V → file watcher fires within 5 s
        //   → WorkspaceManager.LoadIntoSessionAsync bumps version to V+1 → apply must
        //   still redeem the token. Default DefaultMaxVersionSpan = 1 covers this case.
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var token = _store.Store("workspace-1", solution, solution, workspaceVersion: 1, "format range", [], PreviewKind.Unspecified);

        _store.InvalidateOnVersionBump("workspace-1", newWorkspaceVersion: 2);

        var entry = _store.Retrieve(token);
        Assert.IsNotNull(entry, "token stored at V=1 should survive a single reload bump to V=2 with DefaultMaxVersionSpan=1");
        Assert.AreEqual(1, entry.Value.WorkspaceVersion);
    }

    [TestMethod]
    public void InvalidateOnVersionBump_Two_Bumps_Past_Default_Span_Drops_Token()
    {
        // Bounded-span test: a second auto-reload pushes the workspace version past the
        // pinned ceiling (V + 1 with DefaultMaxVersionSpan = 1), so the token MUST drop.
        // Guards against the range-policy regressing into "tokens never expire on reload."
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var token = _store.Store("workspace-1", solution, solution, workspaceVersion: 1, "format range", [], PreviewKind.Unspecified);

        _store.InvalidateOnVersionBump("workspace-1", newWorkspaceVersion: 3);

        Assert.IsNull(_store.Retrieve(token), "token at V=1 with span=1 has ceiling V=2, so V=3 must drop it");
    }

    [TestMethod]
    public void InvalidateOnVersionBump_Same_Version_Is_Noop()
    {
        // Defensive: if a future caller hands the post-bump version 1 (matching the
        // store-time version), the token must NOT be dropped. This guards against an
        // off-by-one where `newVersion >= ceiling` accidentally replaces `>` and starts
        // dropping tokens at the same version they were stored.
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var token = _store.Store("workspace-1", solution, solution, workspaceVersion: 1, "format range", [], PreviewKind.Unspecified);

        _store.InvalidateOnVersionBump("workspace-1", newWorkspaceVersion: 1);

        Assert.IsNotNull(_store.Retrieve(token));
    }

    [TestMethod]
    public void InvalidateOnVersionBump_Other_Workspace_Tokens_Unaffected()
    {
        // Cross-workspace isolation: a reload of workspace A must not touch tokens
        // belonging to workspace B regardless of version, since the per-workspace version
        // counters are independent.
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var tokenA = _store.Store("workspace-A", solution, solution, workspaceVersion: 1, "preview-A", [], PreviewKind.Unspecified);
        var tokenB = _store.Store("workspace-B", solution, solution, workspaceVersion: 1, "preview-B", [], PreviewKind.Unspecified);

        _store.InvalidateOnVersionBump("workspace-A", newWorkspaceVersion: 99);

        Assert.IsNull(_store.Retrieve(tokenA));
        Assert.IsNotNull(_store.Retrieve(tokenB), "InvalidateOnVersionBump must scope to the target workspaceId");
    }

    [TestMethod]
    public void InvalidateOnVersionBump_Custom_MaxVersionSpan_Of_Zero_Drops_On_Any_Bump()
    {
        // Custom span = 0 means tokens cannot survive any reload bump at all (test sentinel
        // — production callers always go through the default-span ctor, but the knob exists
        // so tests and future tuning can dial up/down without source edits).
        var strict = new PreviewStore(maxEntries: 20, ttl: null, maxVersionSpan: 0);
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var token = strict.Store("workspace-1", solution, solution, workspaceVersion: 1, "preview", [], PreviewKind.Unspecified);

        strict.InvalidateOnVersionBump("workspace-1", newWorkspaceVersion: 2);

        Assert.IsNull(strict.Retrieve(token), "with span=0, the pinned ceiling equals store-time version, so any later bump drops the token");
    }

    [TestMethod]
    public void InvalidateOnVersionBump_Custom_MaxVersionSpan_Of_Two_Survives_Two_Bumps()
    {
        // Sanity check that the span knob lets a deployment widen the window if the default
        // turns out too tight. The asymmetric expectation (span=2 → V=3 redeems, V=4 drops)
        // also pins the off-by-one boundary.
        var lenient = new PreviewStore(maxEntries: 20, ttl: null, maxVersionSpan: 2);
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var token = lenient.Store("workspace-1", solution, solution, workspaceVersion: 1, "preview", [], PreviewKind.Unspecified);

        lenient.InvalidateOnVersionBump("workspace-1", newWorkspaceVersion: 3);
        Assert.IsNotNull(lenient.Retrieve(token), "span=2 ceiling is V=3, must survive V=3");

        lenient.InvalidateOnVersionBump("workspace-1", newWorkspaceVersion: 4);
        Assert.IsNull(lenient.Retrieve(token), "span=2 ceiling is V=3, V=4 must drop");
    }

    [TestMethod]
    public void InvalidateOnVersionBump_Throws_On_Null_Or_Empty_WorkspaceId()
    {
        // Defensive: `null`/empty workspaceId would otherwise drop nothing (entries always
        // have a non-null WorkspaceId), masking a programming error at the call site.
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        _store.Store("workspace-1", solution, solution, 1, "test", [], PreviewKind.Unspecified);

        Assert.ThrowsExactly<ArgumentNullException>(() => _store.InvalidateOnVersionBump(null!, 1));
        Assert.ThrowsExactly<ArgumentException>(() => _store.InvalidateOnVersionBump(string.Empty, 1));
    }

    [TestMethod]
    public void Store_With_Explicit_Unspecified_Roundtrips()
    {
        // preview-token-apply-route-provenance: untagged producers stay permissive.
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token = _store.Store("workspace-1", solution, solution, 1, "test", [], PreviewKind.Unspecified);

        Assert.AreEqual(PreviewKind.Unspecified, _store.PeekKind(token));
    }

    [TestMethod]
    public void Store_With_Kind_Roundtrips_Through_PeekKind()
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token = _store.Store("workspace-1", solution, solution, 1, "test", Array.Empty<FileChangeDto>(), PreviewKind.SymbolRename);

        Assert.AreEqual(PreviewKind.SymbolRename, _store.PeekKind(token));
    }

    [TestMethod]
    public void PeekKind_Does_Not_Consume_Entry_Or_Refresh_Ttl()
    {
        // Mirrors the non-consuming PeekChangedPaths contract: the redeeming service must
        // still be able to Retrieve after a provenance check.
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var token = _store.Store("workspace-1", solution, solution, 1, "test", Array.Empty<FileChangeDto>(), PreviewKind.FormatDocument);

        Assert.AreEqual(PreviewKind.FormatDocument, _store.PeekKind(token));
        Assert.AreEqual(PreviewKind.FormatDocument, _store.PeekKind(token));

        var result = _store.Retrieve(token);
        Assert.IsNotNull(result, "PeekKind must not consume the entry");
        Assert.AreEqual("test", result.Value.Description);
    }

    [TestMethod]
    public void PeekKind_On_Unknown_Or_Expired_Token_Returns_Unspecified()
    {
        Assert.AreEqual(PreviewKind.Unspecified, _store.PeekKind("no-such-token"));

        // BoundedStore coerces a non-positive ttl to the 5-minute default, so use a real
        // (tiny) window and let it elapse.
        var expiring = new PreviewStore(maxEntries: 20, ttl: TimeSpan.FromMilliseconds(1));
        using var workspace = new AdhocWorkspace();
        var token = expiring.Store("workspace-1", workspace.CurrentSolution, workspace.CurrentSolution, 1, "test", Array.Empty<FileChangeDto>(), PreviewKind.CodeFix);

        Thread.Sleep(30);

        Assert.AreEqual(PreviewKind.Unspecified, expiring.PeekKind(token));
    }
}
