using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// apply-with-verify-cancellation-and-compile-scope: fake-driven coverage for the two behaviors
/// that now live in <see cref="ApplyUndoWorkflowService"/> (extracted from <c>ApplyWithVerifyTool</c>
/// by apply-undo-workflow-service-extraction):
/// <list type="number">
/// <item><description>
/// Cancellation that fires AFTER a successful apply but before the verify/revert leg
/// completes must trigger a best-effort revert on a FRESH (non-cancelled) token, and the
/// original <see cref="OperationCanceledException"/> must still surface to the caller. A
/// second failure in the best-effort revert is logged, never swallowed, and never masks
/// the cancellation.
/// </description></item>
/// <item><description>
/// Both <c>compile_check</c> legs must be scoped to the single project the preview touches
/// (via <see cref="CompileCheckOptions.ProjectFilter"/>); zero or many touched projects fall
/// back to a full-solution compile (null filter).
/// </description></item>
/// </list>
/// These use hand-rolled fakes rather than the real workspace harness so the exact token
/// identity and project-filter arguments are observable, and call
/// <see cref="ApplyUndoWorkflowService.ApplyWithVerifyAsync"/> directly (no gate / no Host JSON
/// wrapper) since the cancellation, fresh-token, and compile-scope logic all live in the service.
///
/// apply-with-verify-cancelled-result-compensation: the real <c>CompileCheckService.CheckAsync</c>
/// never lets an <see cref="OperationCanceledException"/> escape — it catches it internally and
/// returns a normal (non-throwing) <c>CompileCheckDto</c> with <c>Cancelled=true</c>. The three
/// tests that simulate cancellation by having <c>FakeCompileCheck</c> throw directly use a shape
/// the real service never produces; the two <c>*_CancelledDto_</c> tests close that gap by
/// returning a <c>Cancelled=true</c> DTO instead of throwing.
/// </summary>
[TestClass]
public sealed class ApplyWithVerifyCancellationAndScopeTests
{
    private static ApplyUndoWorkflowService CreateService(
        ICompileCheckService compile,
        IUndoService undo,
        IPreviewStore previewStore,
        IRefactoringService? refactoring = null,
        ILoggerFactory? loggerFactory = null)
        => new(refactoring ?? new FakeRefactoring(), compile, undo, previewStore, loggerFactory);

    // ── Cancellation-safe revert ──

    [TestMethod]
    public async Task Cancellation_AfterSuccessfulApply_RevertsOnFreshToken_AndRethrows()
    {
        var cts = new CancellationTokenSource();
        var originalCancellation = new OperationCanceledException(cts.Token);

        var compile = new FakeCompileCheck
        {
            // Call 0 = pre-apply baseline (succeeds). Call 1 = post-apply verify: model the
            // caller cancelling mid-verify, after the apply already mutated the workspace.
            OnCall = i =>
            {
                if (i == 1)
                {
                    cts.Cancel();
                    throw originalCancellation;
                }
            },
        };
        var undo = new FakeUndo();
        var previewStore = new FakePreviewStore("ws"); // Retrieve returns null → filter skipped
        var service = CreateService(compile, undo, previewStore);

        var thrown = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.ApplyWithVerifyAsync(previewToken: "tok", rollbackOnError: true, ct: cts.Token));

        Assert.AreSame(originalCancellation, thrown,
            "The ORIGINAL cancellation must surface, not a wrapped/replacement exception.");
        Assert.AreEqual(1, undo.RevertCalls,
            "A best-effort revert must run exactly once after a post-apply cancellation.");
        Assert.IsFalse(undo.LastRevertToken.IsCancellationRequested,
            "The revert must use a FRESH, non-cancelled token — not the caller's cancelled token.");
        Assert.AreNotEqual(cts.Token, undo.LastRevertToken,
            "The revert token must not be the caller's (already-cancelled) token.");
    }

    [TestMethod]
    public async Task Cancellation_BestEffortRevertThrows_IsLogged_AndOriginalCancellationSurfaces()
    {
        var cts = new CancellationTokenSource();
        var originalCancellation = new OperationCanceledException(cts.Token);

        var compile = new FakeCompileCheck
        {
            OnCall = i =>
            {
                if (i == 1)
                {
                    cts.Cancel();
                    throw originalCancellation;
                }
            },
        };
        // The best-effort revert itself blows up — this must be logged, must NOT be swallowed
        // silently, and must NOT mask the original cancellation.
        var undo = new FakeUndo { RevertThrows = new InvalidOperationException("revert boom") };
        var loggerFactory = new CapturingLoggerFactory();
        var previewStore = new FakePreviewStore("ws");
        var service = CreateService(compile, undo, previewStore, loggerFactory: loggerFactory);

        var thrown = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.ApplyWithVerifyAsync(previewToken: "tok", rollbackOnError: true, ct: cts.Token));

        Assert.AreSame(originalCancellation, thrown,
            "A failing best-effort revert must not mask the original cancellation.");
        Assert.AreEqual(1, undo.RevertCalls, "The revert must have been attempted.");
        CollectionAssert.Contains(loggerFactory.Logger.Entries, LogLevel.Error,
            "A best-effort revert failure must be logged at Error, not swallowed (Directive #3).");
    }

    [TestMethod]
    public async Task PreApplyCancellation_DoesNotAttemptRevert()
    {
        var cts = new CancellationTokenSource();

        var compile = new FakeCompileCheck
        {
            // Call 0 = pre-apply baseline: cancel BEFORE any apply happens. Nothing is applied,
            // so no revert must be attempted — the cancellation just propagates.
            OnCall = i =>
            {
                if (i == 0)
                {
                    cts.Cancel();
                    throw new OperationCanceledException(cts.Token);
                }
            },
        };
        var undo = new FakeUndo();
        var refactoring = new FakeRefactoring();
        var previewStore = new FakePreviewStore("ws");
        var service = CreateService(compile, undo, previewStore, refactoring);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.ApplyWithVerifyAsync(previewToken: "tok", rollbackOnError: true, ct: cts.Token));

        Assert.AreEqual(0, refactoring.ApplyCalls, "Apply must not run when the baseline is cancelled.");
        Assert.AreEqual(0, undo.RevertCalls,
            "No revert may be attempted for a cancellation that happens before anything is applied.");
    }

    [TestMethod]
    public async Task PreApplyCancellation_CancelledDto_DoesNotApplyOrRevert()
    {
        // apply-with-verify-cancelled-result-compensation: models the REAL CompileCheckService
        // shape — a returned Cancelled=true DTO from the pre-apply baseline check, not a thrown
        // OperationCanceledException. Nothing has been applied yet, so this must surface the
        // cancellation with zero apply/revert calls, matching PreApplyCancellation_DoesNotAttemptRevert.
        var cts = new CancellationTokenSource();
        var compile = new FakeCompileCheck
        {
            CancelledAtCall = 0,
            OnCall = i =>
            {
                if (i == 0)
                {
                    cts.Cancel();
                }
            },
        };
        var undo = new FakeUndo();
        var refactoring = new FakeRefactoring();
        var previewStore = new FakePreviewStore("ws");
        var service = CreateService(compile, undo, previewStore, refactoring);

        var thrown = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.ApplyWithVerifyAsync(previewToken: "tok", rollbackOnError: true, ct: cts.Token));

        Assert.AreEqual(cts.Token, thrown.CancellationToken,
            "The thrown exception must carry the caller's own token.");
        Assert.AreEqual(0, refactoring.ApplyCalls,
            "Apply must not run when the pre-apply baseline check reports Cancelled=true.");
        Assert.AreEqual(0, undo.RevertCalls,
            "No revert may be attempted for a pre-apply cancellation — nothing was applied.");
    }

    [TestMethod]
    public async Task PostApplyCancellation_CancelledDto_RevertsExactlyOnce_AndRethrows()
    {
        // apply-with-verify-cancelled-result-compensation: models the REAL CompileCheckService
        // shape for the post-apply verify leg — a returned Cancelled=true DTO rather than a
        // thrown exception. The apply already succeeded and mutated the workspace, so this must
        // perform exactly ONE best-effort revert on a fresh (non-cancelled) token before
        // surfacing the cancellation, matching Cancellation_AfterSuccessfulApply_RevertsOnFreshToken_AndRethrows.
        var cts = new CancellationTokenSource();
        var compile = new FakeCompileCheck
        {
            // Call 0 = pre-apply baseline (succeeds). Call 1 = post-apply verify: report
            // Cancelled=true via the returned DTO instead of throwing.
            CancelledAtCall = 1,
            OnCall = i =>
            {
                if (i == 1)
                {
                    cts.Cancel();
                }
            },
        };
        var undo = new FakeUndo();
        var previewStore = new FakePreviewStore("ws");
        var service = CreateService(compile, undo, previewStore);

        var thrown = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.ApplyWithVerifyAsync(previewToken: "tok", rollbackOnError: true, ct: cts.Token));

        Assert.AreEqual(cts.Token, thrown.CancellationToken,
            "The thrown exception must carry the caller's own token.");
        Assert.AreEqual(1, undo.RevertCalls,
            "A best-effort revert must run EXACTLY ONCE after a post-apply Cancelled=true DTO — not zero, not two.");
        Assert.IsFalse(undo.LastRevertToken.IsCancellationRequested,
            "The revert must use a FRESH, non-cancelled token — not the caller's cancelled token.");
        Assert.AreNotEqual(cts.Token, undo.LastRevertToken,
            "The revert token must not be the caller's (already-cancelled) token.");
    }

    // ── Project-scoped compile_check ──

    [TestMethod]
    public async Task CompileCheck_SingleTouchedProject_ScopesFilterToThatProject()
    {
        var (original, modified, _) = BuildSolution(("ProjA", true), ("ProjB", false));
        var compile = new FakeCompileCheck();
        var previewStore = new FakePreviewStore("ws", original, modified);
        var service = CreateService(compile, new FakeUndo(), previewStore);

        var outcome = await service.ApplyWithVerifyAsync(
            previewToken: "tok", rollbackOnError: true, ct: CancellationToken.None);

        Assert.IsInstanceOfType<ApplyVerifyOutcome.Applied>(outcome);
        Assert.AreEqual(2, compile.RecordedFilters.Count, "Both pre- and post-apply legs must run.");
        Assert.IsTrue(compile.RecordedFilters.All(f => f == "ProjA"),
            $"Both legs must scope to the single touched project. Got: [{string.Join(", ", compile.RecordedFilters)}]");
    }

    [TestMethod]
    public async Task CompileCheck_MultipleTouchedProjects_UsesNullFilter()
    {
        var (original, modified, _) = BuildSolution(("ProjA", true), ("ProjB", true));
        var compile = new FakeCompileCheck();
        var previewStore = new FakePreviewStore("ws", original, modified);
        var service = CreateService(compile, new FakeUndo(), previewStore);

        var outcome = await service.ApplyWithVerifyAsync(
            previewToken: "tok", rollbackOnError: true, ct: CancellationToken.None);

        Assert.IsInstanceOfType<ApplyVerifyOutcome.Applied>(outcome);
        Assert.AreEqual(2, compile.RecordedFilters.Count);
        Assert.IsTrue(compile.RecordedFilters.All(f => f is null),
            $"A multi-project touch must fall back to a full-solution compile (null filter). Got: [{string.Join(", ", compile.RecordedFilters.Select(f => f ?? "null"))}]");
    }

    [TestMethod]
    public async Task CompileCheck_ZeroTouchedProjects_UsesNullFilter()
    {
        // Original == Modified → GetProjectChanges yields nothing → full-solution fallback.
        var (original, _, sameAsOriginal) = BuildSolution(("ProjA", false));
        var compile = new FakeCompileCheck();
        var previewStore = new FakePreviewStore("ws", original, sameAsOriginal);
        var service = CreateService(compile, new FakeUndo(), previewStore);

        var outcome = await service.ApplyWithVerifyAsync(
            previewToken: "tok", rollbackOnError: true, ct: CancellationToken.None);

        Assert.IsInstanceOfType<ApplyVerifyOutcome.Applied>(outcome);
        Assert.AreEqual(2, compile.RecordedFilters.Count);
        Assert.IsTrue(compile.RecordedFilters.All(f => f is null),
            "A zero-project diff must fall back to a full-solution compile (null filter).");
    }

    // ── Complete-diagnostic-baseline (regression sorting past the default page) ──

    [TestMethod]
    public async Task Verify_RegressionBeyondDefaultPage_IsCaught_AndRolledBack()
    {
        // apply-with-verify-complete-diagnostic-baseline: seed >50 pre-existing Error diagnostics
        // present in BOTH legs, plus one newly-introduced Error positioned PAST slot 50 in the
        // post-apply full list. The paginating fake mirrors the real CompileCheckService's
        // Skip(Offset).Take(Limit) truncation. Pre-fix (the record default Limit:50) the new error
        // is truncated out of the post-apply page → excluded from postErrors → never diffed →
        // apply_with_verify reports "applied" with a live regression. Post-fix (Limit:int.MaxValue)
        // the COMPLETE error set is compared → the regression is caught and rolled back.
        const int preExistingCount = 55;
        var preExisting = Enumerable.Range(0, preExistingCount)
            .Select(i => new DiagnosticDto(
                "CS9999", $"pre-existing error {i}", "Error", "Compiler", "Existing.cs", i, 1, i, 10))
            .ToList();

        // The single introduced error — distinct identity (id|file|line), appended LAST so it sits
        // at index 55, comfortably beyond the default 50-item page.
        var introduced = new DiagnosticDto(
            "CS0103", "The name 'X' does not exist in the current context", "Error", "Compiler",
            "New.cs", 999, 1, 999, 5);

        var preLegFull = new List<DiagnosticDto>(preExisting);                    // pre-apply: 55 errors
        var postLegFull = new List<DiagnosticDto>(preExisting) { introduced };    // post-apply: 55 + 1 new

        var compile = new PaginatingFakeCompileCheck(preLegFull, postLegFull);
        var previewStore = new FakePreviewStore("ws"); // Retrieve → null → null project filter
        var service = CreateService(compile, new FakeUndo(), previewStore);

        var outcome = await service.ApplyWithVerifyAsync(
            previewToken: "tok", rollbackOnError: true, ct: CancellationToken.None);

        var rolledBack = outcome as ApplyVerifyOutcome.RolledBack;
        Assert.IsNotNull(rolledBack,
            "A regression sorting past the default 50-diagnostic page must still be caught and rolled " +
            "back. This assertion FAILS against the pre-fix Limit:50 default (the new error is truncated " +
            "out of the post-apply page, so the outcome is a false 'applied').");
        Assert.IsTrue(
            rolledBack.IntroducedErrors.Any(d => d.Id == "CS0103" && d.FilePath == "New.cs"),
            "The introduced error must surface in IntroducedErrors, not be silently dropped.");

        // Regression guard: both legs must request a page large enough to cover the COMPLETE
        // synthetic set. A finite cap ≤ the pre-existing error count silently reintroduces the bug.
        Assert.AreEqual(2, compile.RecordedLimits.Count, "Both pre- and post-apply legs must run.");
        Assert.IsTrue(
            compile.RecordedLimits.All(limit => limit > preExistingCount + 1),
            $"Both legs must request the COMPLETE diagnostic set (Limit > {preExistingCount + 1}); a " +
            $"finite cap reintroduces the truncation bug. Got: [{string.Join(", ", compile.RecordedLimits)}]");
    }

    // ── helpers ──

    /// <summary>
    /// Builds an in-memory (Adhoc) solution pair. Each tuple names a project and whether the
    /// "modified" solution changes a document inside it. Returns (original, modified, original)
    /// — the third element is the unchanged original again, handy for the zero-diff case.
    /// </summary>
    private static (Solution Original, Solution Modified, Solution OriginalAgain) BuildSolution(
        params (string Name, bool Changed)[] projects)
    {
        using var ws = new AdhocWorkspace();
        var solution = ws.CurrentSolution;
        var docIds = new List<(DocumentId Id, bool Changed)>();

        foreach (var (name, changed) in projects)
        {
            var pid = ProjectId.CreateNewId();
            var did = DocumentId.CreateNewId(pid);
            solution = solution
                .AddProject(pid, name, name, LanguageNames.CSharp)
                .AddDocument(did, $"{name}.cs", $"class {name} {{ }}");
            docIds.Add((did, changed));
        }

        var modified = solution;
        foreach (var (id, changed) in docIds)
        {
            if (changed)
            {
                modified = modified.WithDocumentText(id, SourceText.From("class Changed { int x; }"));
            }
        }

        return (solution, modified, solution);
    }

    // ── fakes ──

    private sealed class FakeCompileCheck : ICompileCheckService
    {
        public readonly List<string?> RecordedFilters = new();
        public Action<int>? OnCall;

        /// <summary>
        /// apply-with-verify-cancelled-result-compensation: when set, the call at this 0-based
        /// index returns a <c>Cancelled=true</c> DTO instead of a normal success DTO — modeling
        /// the REAL <c>CompileCheckService.CheckAsync</c>'s cancellation shape (a returned DTO,
        /// never a thrown exception). Distinct from <see cref="OnCall"/>, which simulates the
        /// non-production thrown-exception shape used by the tests above.
        /// </summary>
        public int? CancelledAtCall;

        private int _n;

        public Task<CompileCheckDto> CheckAsync(string workspaceId, CompileCheckOptions options, CancellationToken ct)
        {
            RecordedFilters.Add(options.ProjectFilter);
            var callIndex = _n++;
            OnCall?.Invoke(callIndex);
            var cancelled = CancelledAtCall == callIndex;
            return Task.FromResult(new CompileCheckDto(
                Success: !cancelled,
                ErrorCount: 0,
                WarningCount: 0,
                TotalDiagnostics: 0,
                ReturnedDiagnostics: 0,
                Offset: 0,
                Limit: 50,
                HasMore: false,
                Diagnostics: Array.Empty<DiagnosticDto>(),
                ElapsedMs: 0,
                Cancelled: cancelled));
        }
    }

    /// <summary>
    /// apply-with-verify-complete-diagnostic-baseline: a fake that mirrors the REAL
    /// <c>CompileCheckService.CheckAsync</c> pagination — it holds the COMPLETE diagnostic set per
    /// call and returns only <c>full.Skip(options.Offset).Take(options.Limit)</c>, exactly like the
    /// real service truncates its materialized <c>acc.Diagnostics</c> before building the DTO. This
    /// is what lets the test prove that a regression sorting past the requested page is missed when
    /// the caller under-requests. Each constructor argument is the full list for the corresponding
    /// 0-based call (call 0 = pre-apply baseline, call 1 = post-apply verify); the last entry is
    /// reused for any further calls.
    /// </summary>
    private sealed class PaginatingFakeCompileCheck : ICompileCheckService
    {
        private readonly IReadOnlyList<DiagnosticDto>[] _fullListsPerCall;
        public readonly List<int> RecordedLimits = new();
        private int _n;

        public PaginatingFakeCompileCheck(params IReadOnlyList<DiagnosticDto>[] fullListsPerCall)
            => _fullListsPerCall = fullListsPerCall;

        public Task<CompileCheckDto> CheckAsync(string workspaceId, CompileCheckOptions options, CancellationToken ct)
        {
            RecordedLimits.Add(options.Limit);
            var full = _fullListsPerCall[Math.Min(_n, _fullListsPerCall.Length - 1)];
            _n++;

            var page = full.Skip(options.Offset).Take(options.Limit).ToList();
            var errorCount = full.Count(d =>
                string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(new CompileCheckDto(
                Success: true,
                ErrorCount: errorCount,
                WarningCount: 0,
                TotalDiagnostics: full.Count,
                ReturnedDiagnostics: page.Count,
                Offset: options.Offset,
                Limit: options.Limit,
                HasMore: page.Count < full.Count,
                Diagnostics: page,
                ElapsedMs: 0,
                Cancelled: false));
        }
    }

    private sealed class FakeRefactoring : IRefactoringService
    {
        public int ApplyCalls;
        public ApplyResultDto Result = new(true, new[] { "Changed.cs" }, null);

        public Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, CancellationToken ct)
        {
            ApplyCalls++;
            return Task.FromResult(Result);
        }

        public Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, string toolName, bool force, CancellationToken ct)
        {
            ApplyCalls++;
            return Task.FromResult(Result);
        }

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
        public CancellationToken LastRevertToken;
        public bool RevertReturns = true;
        public Exception? RevertThrows;

        public Task<bool> RevertAsync(string workspaceId, CancellationToken cancellationToken = default)
        {
            RevertCalls++;
            LastRevertToken = cancellationToken;
            if (RevertThrows is not null)
            {
                throw RevertThrows;
            }
            return Task.FromResult(RevertReturns);
        }

        public void CaptureBeforeApply(string workspaceId, string description, object? preApplySolution, IReadOnlyList<FileSnapshotDto>? fileSnapshots = null) { }

        public UndoEntry? GetLastOperation(string workspaceId) => null;

        public Task<RevertBySequenceResult> RevertBySequenceAsync(string workspaceId, int sequenceNumber, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public void CommitPendingCapture(string workspaceId, int sequenceNumber, IReadOnlyList<string> affectedFiles) { }

        public void Clear(string workspaceId) { }
    }

    private sealed class FakePreviewStore : IPreviewStore
    {
        private readonly string _workspaceId;
        private readonly Solution? _original;
        private readonly Solution? _modified;

        public FakePreviewStore(string workspaceId, Solution? original = null, Solution? modified = null)
        {
            _workspaceId = workspaceId;
            _original = original;
            _modified = modified;
        }

        public string? PeekWorkspaceId(string token) => _workspaceId;

        public (string WorkspaceId, Solution OriginalSolution, Solution ModifiedSolution, int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token)
            => _original is not null && _modified is not null
                ? (_workspaceId, _original, _modified, 1, "fake preview", false)
                : null;

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

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public readonly CapturingLogger Logger = new();

        public ILogger CreateLogger(string categoryName) => Logger;

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }
    }

    private sealed class CapturingLogger : ILogger
    {
        public readonly List<LogLevel> Entries = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(logLevel);
    }
}
