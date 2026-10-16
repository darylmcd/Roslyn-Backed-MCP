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
/// that changes the declared kind remains covered here, while the required parameter now makes a
/// dropped binding fail at compile time across every apply shim.
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
            var gate = new RecordingDispatchGate();
            var service = new ThrowingRefactoringService();
            var store = new DispatchPreviewStore(token: "tok-xroute", workspaceId: "ws-xr")
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
    /// (<see cref="PreviewKind.Unspecified"/>) must still redeem on a bound route. Binding the
    /// routes must not break out-of-tree or legacy untagged producers.
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
            var gate = new RecordingDispatchGate();
            var service = new RecordingApplyRefactoringService("A.cs");
            var store = new DispatchPreviewStore(token: "tok-untagged", workspaceId: "ws-ut");

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
        var gate = new RecordingDispatchGate();
        var service = new RecordingApplyRefactoringService("A.cs");
        var store = new DispatchPreviewStore(token: "tok-em", workspaceId: "ws-em")
        {
            Kind = PreviewKind.ExtractMethod,
        };

        _ = await ExtractMethodTools.ApplyExtractMethod(gate, service, store, "tok-em", CancellationToken.None);

        Assert.AreEqual(1, service.ApplyCallCount, "A same-family token must redeem unchanged.");
    }

    /// <summary>
    /// Fails loudly if the provenance guard lets a foreign token through — the apply must never be
    /// reached, so reaching it is itself the failure signal (belt-and-braces alongside the
    /// <c>ApplyCallCount</c> assertion, which would otherwise only fire after the mutation ran).
    /// </summary>
    private sealed class ThrowingRefactoringService : RecordingApplyRefactoringService
    {
        public ThrowingRefactoringService() : base("A.cs") { }

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

}
