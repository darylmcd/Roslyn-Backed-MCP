using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// preview-token-route-binding-extraction-family: route-level coverage for the four
/// extraction/move apply tools — <c>extract_method_apply</c>, <c>extract_interface_apply</c>,
/// <c>extract_type_apply</c>, and <c>move_type_to_file_apply</c> — which now declare an
/// <see cref="PreviewKind"/> producer family through
/// <see cref="ToolDispatch.ApplyByTokenAsync{TDto}(IWorkspaceExecutionGate, IPreviewStore, string, Func{CancellationToken, Task{TDto}}, CancellationToken, PreviewKind)"/>.
/// </summary>
/// <remarks>
/// <para>
/// These tests exercise the SHIM methods, not <see cref="ToolDispatch"/> directly, so the binding
/// itself (the <c>expectedKind:</c> argument on each call site) is what is pinned. A regression
/// that drops the argument compiles green — the parameter is optional — and would only be caught
/// here.
/// </para>
/// <para>
/// The fakes are deliberately local duplicates of the ones nested inside <c>ToolDispatchTests</c>:
/// those are <c>private</c> nested types, and widening them into a shared fixture would couple two
/// unrelated test files for four small stubs.
/// </para>
/// </remarks>
[TestClass]
public sealed class ExtractionApplyRouteBindingTests
{
    /// <summary>
    /// A wrong-family token (minted by <c>rename_preview</c>) must be refused by every one of the
    /// four bound routes BEFORE the refactoring service is reached, with a message naming both the
    /// token's real apply route and the route it was wrongly redeemed through.
    /// </summary>
    [TestMethod]
    public async Task ExtractionApplyRoutes_ForeignFamilyToken_RefuseBeforeServiceCall()
    {
        (string RouteName, Func<IWorkspaceExecutionGate, IRefactoringService, IPreviewStore, Task<string>> Invoke)[] routes =
        [
            ("extract_method_apply",
                (gate, svc, store) => ExtractMethodTools.ApplyExtractMethod(gate, svc, store, "tok-xroute", CancellationToken.None)),
            ("extract_interface_apply",
                (gate, svc, store) => InterfaceExtractionTools.ApplyExtractInterface(gate, svc, store, "tok-xroute", CancellationToken.None)),
            ("extract_type_apply",
                (gate, svc, store) => TypeExtractionTools.ApplyExtractType(gate, svc, store, "tok-xroute", CancellationToken.None)),
            ("move_type_to_file_apply",
                (gate, svc, store) => TypeMoveTools.ApplyMoveTypeToFile(gate, svc, store, "tok-xroute", CancellationToken.None)),
        ];

        foreach (var (routeName, invoke) in routes)
        {
            var gate = new FakeGate();
            var service = new ThrowingRefactoringService();
            var store = new FakePreviewStore(token: "tok-xroute", workspaceId: "ws-xr")
            {
                Kind = PreviewKind.SymbolRename,
            };

            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => invoke(gate, service, store));

            StringAssert.Contains(ex.Message, "rename_apply",
                $"{routeName} must name the token's real apply route so the caller can recover.");
            StringAssert.Contains(ex.Message, routeName,
                $"{routeName} must name the route that refused the token.");
            Assert.AreEqual(0, service.ApplyCallCount,
                $"{routeName} must refuse the foreign token BEFORE the refactoring service runs.");
            Assert.AreEqual(1, gate.WriteCallCount,
                $"{routeName} runs its provenance guard INSIDE the write gate.");
        }
    }

    /// <summary>
    /// The permissive arm: a token whose producer recorded no provenance
    /// (<see cref="PreviewKind.Unspecified"/> — the state of every extraction producer today) must
    /// still redeem on a bound route. Binding the routes must not break untagged producers.
    /// </summary>
    [TestMethod]
    public async Task ExtractionApplyRoutes_UnspecifiedProvenanceToken_StillRedeems()
    {
        Func<IWorkspaceExecutionGate, IRefactoringService, IPreviewStore, Task<string>>[] routes =
        [
            (gate, svc, store) => ExtractMethodTools.ApplyExtractMethod(gate, svc, store, "tok-untagged", CancellationToken.None),
            (gate, svc, store) => InterfaceExtractionTools.ApplyExtractInterface(gate, svc, store, "tok-untagged", CancellationToken.None),
            (gate, svc, store) => TypeExtractionTools.ApplyExtractType(gate, svc, store, "tok-untagged", CancellationToken.None),
            (gate, svc, store) => TypeMoveTools.ApplyMoveTypeToFile(gate, svc, store, "tok-untagged", CancellationToken.None),
        ];

        foreach (var invoke in routes)
        {
            var gate = new FakeGate();
            var service = new RecordingRefactoringService();
            var store = new FakePreviewStore(token: "tok-untagged", workspaceId: "ws-ut");

            var json = await invoke(gate, service, store);

            Assert.AreEqual(1, service.ApplyCallCount,
                "An untagged token is permissive by the PreviewKind contract and must still apply.");
            StringAssert.Contains(json, "A.cs", "The route must serialize the service's result.");
        }
    }

    /// <summary>
    /// Same-family redemption is unaffected — a token tagged with the route's own kind applies.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethodApply_MatchingProvenanceToken_Redeems()
    {
        var gate = new FakeGate();
        var service = new RecordingRefactoringService();
        var store = new FakePreviewStore(token: "tok-em", workspaceId: "ws-em")
        {
            Kind = PreviewKind.ExtractMethod,
        };

        _ = await ExtractMethodTools.ApplyExtractMethod(gate, service, store, "tok-em", CancellationToken.None);

        Assert.AreEqual(1, service.ApplyCallCount, "A same-family token must redeem unchanged.");
    }

    /// <summary>Records apply invocations and returns a canned success DTO.</summary>
    private class RecordingRefactoringService : IRefactoringService
    {
        public int ApplyCallCount { get; private set; }

        public virtual Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, CancellationToken ct)
        {
            ApplyCallCount++;
            return Task.FromResult(new ApplyResultDto(true, new[] { "A.cs" }, null));
        }

        public virtual Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, bool force, CancellationToken ct)
            => ApplyRefactoringAsync(previewToken, toolName, ct);

        public Task<RefactoringPreviewDto> PreviewRenameAsync(string workspaceId, SymbolLocator locator, string newName, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewRenameAsync(string workspaceId, SymbolLocator locator, string newName, bool summary, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewOrganizeUsingsAsync(string workspaceId, string filePath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewFormatDocumentAsync(string workspaceId, string filePath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewFormatRangeAsync(string workspaceId, string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewCodeFixAsync(string workspaceId, string diagnosticId, string filePath, int line, int column, string? fixId, CancellationToken ct)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Fails loudly if the provenance guard lets a foreign token through — the apply must never be
    /// reached, so reaching it is itself the failure signal (belt-and-braces alongside the
    /// <c>ApplyCallCount</c> assertion, which would otherwise only fire after the mutation ran).
    /// </summary>
    private sealed class ThrowingRefactoringService : RecordingRefactoringService
    {
        public override Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, CancellationToken ct)
        {
            _ = base.ApplyRefactoringAsync(previewToken, toolName, ct);
            // NotSupportedException on purpose: the guard throws InvalidOperationException, so a
            // distinct type keeps ThrowsExactlyAsync<InvalidOperationException> from passing on a
            // guard that never fired.
            throw new NotSupportedException(
                $"The provenance guard must refuse the token before `{toolName}` reaches the service.");
        }
    }

    /// <summary>
    /// Minimal <see cref="IWorkspaceExecutionGate"/> stand-in recording which verb ran. Mirrors the
    /// private fake nested in <c>ToolDispatchTests</c>.
    /// </summary>
    private sealed class FakeGate : IWorkspaceExecutionGate
    {
        public int WriteCallCount { get; private set; }

        public async Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            return await action(linked.Token).ConfigureAwait(false);
        }

        public async Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true)
        {
            _ = applyStalenessPolicy;
            WriteCallCount++;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            return await action(linked.Token).ConfigureAwait(false);
        }

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => throw new NotSupportedException("Extraction apply routes must not route through RunLoadGateAsync.");

        public void RemoveGate(string workspaceId)
            => throw new NotSupportedException("Extraction apply routes must not call RemoveGate.");
    }

    /// <summary>
    /// Minimal <see cref="IPreviewStore"/> stand-in exposing only the peek surface the apply path
    /// consumes. Mirrors the private fake nested in <c>ToolDispatchTests</c>.
    /// </summary>
    private sealed class FakePreviewStore : IPreviewStore
    {
        private readonly string _token;
        private readonly string _workspaceId;

        public FakePreviewStore(string token, string workspaceId)
        {
            _token = token;
            _workspaceId = workspaceId;
        }

        public PreviewKind Kind { get; init; } = PreviewKind.Unspecified;

        public string? PeekWorkspaceId(string token) => token == _token ? _workspaceId : null;

        public IReadOnlyList<string>? PeekChangedPaths(string token) => null;

        public PreviewKind PeekKind(string token) => token == _token ? Kind : PreviewKind.Unspecified;

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description)
            => throw new NotSupportedException();

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated)
            => throw new NotSupportedException();

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, IReadOnlyList<FileChangeDto> changes)
            => throw new NotSupportedException();

        public (string WorkspaceId, Solution OriginalSolution, Solution ModifiedSolution, int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token)
            => throw new NotSupportedException();

        public void Invalidate(string token) => throw new NotSupportedException();

        public void InvalidateAll(string? workspaceId = null) => throw new NotSupportedException();

        public void InvalidateOnVersionBump(string workspaceId, int newWorkspaceVersion) => throw new NotSupportedException();
    }
}
