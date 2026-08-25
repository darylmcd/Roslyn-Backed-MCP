using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// apply-undo-workflow-service-extraction: direct, fake-driven coverage of the outcome-selection
/// logic that <see cref="ApplyUndoWorkflowService"/> now owns (moved out of the Host
/// <c>ApplyWithVerifyTool</c>/<c>UndoTools</c> JSON wrappers). Verifies that each of the four
/// <see cref="ApplyVerifyOutcome"/> variants is selected for the right condition, and that
/// <see cref="ApplyUndoWorkflowService.RevertBySequenceAsync"/> maps the underlying
/// <see cref="RevertBySequenceResult"/> 1:1 onto a <see cref="SequenceRevertOutcome"/>. The
/// cancellation / fresh-token / project-scope behavior is covered separately by
/// <see cref="ApplyWithVerifyCancellationAndScopeTests"/>.
/// </summary>
[TestClass]
public sealed class ApplyUndoWorkflowServiceTests
{
    private static CompileCheckDto CleanCheck() => new(
        Success: true,
        ErrorCount: 0,
        WarningCount: 0,
        TotalDiagnostics: 0,
        ReturnedDiagnostics: 0,
        Offset: 0,
        Limit: 50,
        HasMore: false,
        Diagnostics: Array.Empty<DiagnosticDto>(),
        ElapsedMs: 0);

    private static CompileCheckDto CheckWithError(DiagnosticDto error) => new(
        Success: false,
        ErrorCount: 1,
        WarningCount: 0,
        TotalDiagnostics: 1,
        ReturnedDiagnostics: 1,
        Offset: 0,
        Limit: 50,
        HasMore: false,
        Diagnostics: new[] { error },
        ElapsedMs: 0);

    private static DiagnosticDto NewError() =>
        new("CS0103", "The name 'Nope' does not exist", "Error", "Compiler", "File.cs", 7, 5, 7, 9);

    private static ApplyUndoWorkflowService Create(
        FakeCompileCheck compile,
        FakeUndo undo,
        FakeRefactoring? refactoring = null)
        => new(refactoring ?? new FakeRefactoring(), compile, undo, new FakePreviewStore("ws"));

    [TestMethod]
    public async Task ApplyFailed_ShortCircuits_BeforeVerifyOrRevert()
    {
        var refactoring = new FakeRefactoring { Result = new ApplyResultDto(false, Array.Empty<string>(), "apply blew up") };
        var compile = new FakeCompileCheck(CleanCheck(), CleanCheck());
        var undo = new FakeUndo();
        var service = Create(compile, undo, refactoring);

        var outcome = await service.ApplyWithVerifyAsync("tok", rollbackOnError: true, CancellationToken.None);

        Assert.IsInstanceOfType<ApplyVerifyOutcome.ApplyFailed>(outcome);
        var failed = (ApplyVerifyOutcome.ApplyFailed)outcome;
        Assert.AreEqual("apply blew up", failed.Error);
        Assert.AreEqual(1, compile.Calls, "Only the pre-apply baseline check runs; there is no post-apply verify after an apply failure.");
        Assert.AreEqual(0, undo.RevertCalls, "A failed apply mutated nothing, so no revert is attempted.");
    }

    [TestMethod]
    public async Task NoNewErrors_ReturnsApplied()
    {
        var compile = new FakeCompileCheck(CleanCheck(), CleanCheck());
        var undo = new FakeUndo();
        var refactoring = new FakeRefactoring { Result = new ApplyResultDto(true, new[] { "A.cs", "B.cs" }, null) };
        var service = Create(compile, undo, refactoring);

        var outcome = await service.ApplyWithVerifyAsync("tok", rollbackOnError: true, CancellationToken.None);

        Assert.IsInstanceOfType<ApplyVerifyOutcome.Applied>(outcome);
        var applied = (ApplyVerifyOutcome.Applied)outcome;
        CollectionAssert.AreEqual(new[] { "A.cs", "B.cs" }, applied.AppliedFiles.ToList());
        Assert.AreEqual(0, applied.PreErrorCount);
        Assert.AreEqual(0, applied.PostErrorCount);
        Assert.AreEqual(0, undo.RevertCalls, "A clean apply is not reverted.");
    }

    [TestMethod]
    public async Task NewError_RollbackOnErrorFalse_ReturnsAppliedWithErrors_NoRevert()
    {
        var error = NewError();
        var compile = new FakeCompileCheck(CleanCheck(), CheckWithError(error));
        var undo = new FakeUndo();
        var refactoring = new FakeRefactoring { Result = new ApplyResultDto(true, new[] { "A.cs" }, null) };
        var service = Create(compile, undo, refactoring);

        var outcome = await service.ApplyWithVerifyAsync("tok", rollbackOnError: false, CancellationToken.None);

        Assert.IsInstanceOfType<ApplyVerifyOutcome.AppliedWithErrors>(outcome);
        var withErrors = (ApplyVerifyOutcome.AppliedWithErrors)outcome;
        Assert.AreEqual(1, withErrors.IntroducedErrors.Count);
        Assert.AreEqual("CS0103", withErrors.IntroducedErrors[0].Id);
        Assert.AreEqual(0, withErrors.PreErrorCount);
        Assert.AreEqual(1, withErrors.PostErrorCount);
        Assert.AreEqual(0, undo.RevertCalls, "rollbackOnError=false must preserve the broken state, not revert it.");
    }

    [TestMethod]
    public async Task NewError_RollbackSucceeds_ReturnsRolledBackReverted()
    {
        var error = NewError();
        var compile = new FakeCompileCheck(CleanCheck(), CheckWithError(error));
        var undo = new FakeUndo { RevertReturns = true };
        var refactoring = new FakeRefactoring { Result = new ApplyResultDto(true, new[] { "A.cs" }, null) };
        var service = Create(compile, undo, refactoring);

        var outcome = await service.ApplyWithVerifyAsync("tok", rollbackOnError: true, CancellationToken.None);

        Assert.IsInstanceOfType<ApplyVerifyOutcome.RolledBack>(outcome);
        var rolledBack = (ApplyVerifyOutcome.RolledBack)outcome;
        Assert.IsTrue(rolledBack.Reverted, "A successful revert maps to Reverted=true (status=\"rolled_back\").");
        Assert.AreEqual(1, rolledBack.IntroducedErrors.Count);
        Assert.AreEqual(1, undo.RevertCalls, "Exactly one revert must be issued when new errors appear.");
    }

    [TestMethod]
    public async Task NewError_RollbackFails_ReturnsRolledBackNotReverted()
    {
        var error = NewError();
        var compile = new FakeCompileCheck(CleanCheck(), CheckWithError(error));
        var undo = new FakeUndo { RevertReturns = false };
        var refactoring = new FakeRefactoring { Result = new ApplyResultDto(true, new[] { "A.cs" }, null) };
        var service = Create(compile, undo, refactoring);

        var outcome = await service.ApplyWithVerifyAsync("tok", rollbackOnError: true, CancellationToken.None);

        Assert.IsInstanceOfType<ApplyVerifyOutcome.RolledBack>(outcome);
        var rolledBack = (ApplyVerifyOutcome.RolledBack)outcome;
        Assert.IsFalse(rolledBack.Reverted, "A failed revert maps to Reverted=false (status=\"rollback_failed\").");
        Assert.AreEqual(1, undo.RevertCalls);
    }

    // ── RevertBySequenceAsync 1:1 mapping ──

    [TestMethod]
    public async Task RevertBySequence_Success_MapsAllFields()
    {
        var undo = new FakeUndo
        {
            SequenceResult = new RevertBySequenceResult(
                Reverted: true,
                RevertedOperation: "organize usings",
                AffectedFiles: new[] { "A.cs" },
                Reason: null,
                BlockingSequences: null),
        };
        var service = new ApplyUndoWorkflowService(new FakeRefactoring(), new FakeCompileCheck(CleanCheck(), CleanCheck()), undo, new FakePreviewStore("ws"));

        var outcome = await service.RevertBySequenceAsync("ws", 3, CancellationToken.None);

        Assert.IsTrue(outcome.Reverted);
        Assert.AreEqual("organize usings", outcome.RevertedOperation);
        CollectionAssert.AreEqual(new[] { "A.cs" }, outcome.AffectedFiles.ToList());
        Assert.IsNull(outcome.Reason);
        Assert.IsNull(outcome.BlockingSequences);
    }

    [TestMethod]
    public async Task RevertBySequence_DependencyBlocked_MapsReasonAndBlockingSequences()
    {
        var undo = new FakeUndo
        {
            SequenceResult = new RevertBySequenceResult(
                Reverted: false,
                RevertedOperation: null,
                AffectedFiles: new[] { "A.cs" },
                Reason: "dependency-blocked",
                BlockingSequences: new[] { 5, 6 }),
        };
        var service = new ApplyUndoWorkflowService(new FakeRefactoring(), new FakeCompileCheck(CleanCheck(), CleanCheck()), undo, new FakePreviewStore("ws"));

        var outcome = await service.RevertBySequenceAsync("ws", 3, CancellationToken.None);

        Assert.IsFalse(outcome.Reverted);
        Assert.AreEqual("dependency-blocked", outcome.Reason);
        CollectionAssert.AreEqual(new[] { 5, 6 }, outcome.BlockingSequences!.ToList());
    }

    [TestMethod]
    public async Task RevertBySequence_UnknownSequence_MapsReason()
    {
        var undo = new FakeUndo
        {
            SequenceResult = new RevertBySequenceResult(
                Reverted: false,
                RevertedOperation: null,
                AffectedFiles: Array.Empty<string>(),
                Reason: "unknown-sequence",
                BlockingSequences: null),
        };
        var service = new ApplyUndoWorkflowService(new FakeRefactoring(), new FakeCompileCheck(CleanCheck(), CleanCheck()), undo, new FakePreviewStore("ws"));

        var outcome = await service.RevertBySequenceAsync("ws", 99, CancellationToken.None);

        Assert.IsFalse(outcome.Reverted);
        Assert.AreEqual("unknown-sequence", outcome.Reason);
        Assert.IsNull(outcome.BlockingSequences);
        Assert.AreEqual(0, outcome.AffectedFiles.Count);
    }

    // ── fakes ──

    private sealed class FakeCompileCheck : ICompileCheckService
    {
        private readonly CompileCheckDto _pre;
        private readonly CompileCheckDto _post;
        public int Calls { get; private set; }

        public FakeCompileCheck(CompileCheckDto pre, CompileCheckDto post)
        {
            _pre = pre;
            _post = post;
        }

        public Task<CompileCheckDto> CheckAsync(string workspaceId, CompileCheckOptions options, CancellationToken ct)
        {
            var call = Calls++;
            return Task.FromResult(call == 0 ? _pre : _post);
        }
    }

    /// <summary>
    /// preview-token-apply-route-provenance: <c>apply_with_verify</c> is the generic verified
    /// route and its documented supported set is "every token held by <see cref="IPreviewStore"/>".
    /// This pins that the tool matches its own <c>[Description]</c>: a token carrying a CONCRETE,
    /// unrelated producer kind (one that <c>rename_apply</c> would refuse) still redeems here.
    /// Guards against a future edit copying the named routes' producer binding onto this route.
    /// </summary>
    [TestMethod]
    public async Task ApplyWithVerify_IsProducerAgnostic_AcceptsAnyPreviewStoreKind()
    {
        var compile = new FakeCompileCheck(CleanCheck(), CleanCheck());
        var undo = new FakeUndo();
        var refactoring = new FakeRefactoring { Result = new ApplyResultDto(true, new[] { "A.cs" }, null) };
        var gate = new PassThroughGate();
        var store = new FakePreviewStore("ws") { Kind = PreviewKind.CodeFix };
        var service = Create(compile, undo, refactoring);

        var json = await ApplyWithVerifyTool.ApplyWithVerify(
            gate,
            service,
            store,
            previewToken: "tok-any-producer",
            rollbackOnError: true,
            CancellationToken.None);

        StringAssert.Contains(json, "\"applied\"",
            "apply_with_verify must accept a token from any IPreviewStore producer family.");
        Assert.AreEqual(1, gate.WriteCallCount, "The route still runs under the per-workspace write gate.");
    }

    /// <summary>
    /// Pass-through <see cref="IWorkspaceExecutionGate"/> so the tool shim can be exercised without
    /// a live workspace. Load-gate and gate-removal verbs throw: <c>apply_with_verify</c> must not
    /// route through them.
    /// </summary>
    private sealed class PassThroughGate : IWorkspaceExecutionGate
    {
        public int WriteCallCount { get; private set; }

        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => action(ct);

        public Task<T> RunWriteAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct, bool applyStalenessPolicy = true)
        {
            WriteCallCount++;
            return action(ct);
        }

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => throw new NotSupportedException("apply_with_verify must not route through RunLoadGateAsync.");

        public void RemoveGate(string workspaceId)
            => throw new NotSupportedException("apply_with_verify must not call RemoveGate.");
    }

    private sealed class FakeRefactoring : IRefactoringService
    {
        public ApplyResultDto Result = new(true, new[] { "A.cs" }, null);

        public Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, CancellationToken ct)
            => Task.FromResult(Result);

        public Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, bool force, CancellationToken ct)
            => Task.FromResult(Result);

        public Task<RefactoringPreviewDto> PreviewRenameAsync(string workspaceId, SymbolLocator locator, string newName, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<RefactoringPreviewDto> PreviewRenameAsync(string workspaceId, SymbolLocator locator, string newName, bool summary, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<RefactoringPreviewDto> PreviewOrganizeUsingsAsync(string workspaceId, string filePath, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<RefactoringPreviewDto> PreviewFormatDocumentAsync(string workspaceId, string filePath, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<RefactoringPreviewDto> PreviewFormatRangeAsync(string workspaceId, string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<RefactoringPreviewDto> PreviewCodeFixAsync(string workspaceId, string diagnosticId, string filePath, int line, int column, string? fixId, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class FakeUndo : IUndoService
    {
        public int RevertCalls;
        public bool RevertReturns = true;
        public RevertBySequenceResult SequenceResult =
            new(false, null, Array.Empty<string>(), "unknown-sequence", null);

        public Task<bool> RevertAsync(string workspaceId, CancellationToken cancellationToken = default)
        {
            RevertCalls++;
            return Task.FromResult(RevertReturns);
        }

        public void CaptureBeforeApply(string workspaceId, string description, object? preApplySolution, IReadOnlyList<FileSnapshotDto>? fileSnapshots = null) { }

        public UndoEntry? GetLastOperation(string workspaceId) => null;

        public Task<RevertBySequenceResult> RevertBySequenceAsync(string workspaceId, int sequenceNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(SequenceResult);

        public void CommitPendingCapture(string workspaceId, int sequenceNumber, IReadOnlyList<string> affectedFiles) { }

        public void Clear(string workspaceId) { }
    }

    private sealed class FakePreviewStore : IPreviewStore
    {
        private readonly string _workspaceId;

        public FakePreviewStore(string workspaceId) => _workspaceId = workspaceId;

        /// <summary>
        /// preview-token-apply-route-provenance: producer family the fake reports. The generic
        /// verified route is producer-agnostic, so a concrete value here must NOT block redemption.
        /// </summary>
        public PreviewKind Kind { get; init; } = PreviewKind.Unspecified;

        public string? PeekWorkspaceId(string token) => _workspaceId;

        public PreviewKind PeekKind(string token) => Kind;

        // Retrieve returns null → project-filter derivation is skipped (full-solution compile).
        public (string WorkspaceId, Solution OriginalSolution, Solution ModifiedSolution, int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token)
            => null;

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description)
            => throw new NotImplementedException();

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated)
            => throw new NotImplementedException();

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, IReadOnlyList<FileChangeDto> changes)
            => throw new NotImplementedException();

        public void Invalidate(string token) { }

        public void InvalidateAll(string? workspaceId = null) { }

        public void InvalidateOnVersionBump(string workspaceId, int newWorkspaceVersion) { }
    }
}
