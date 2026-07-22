using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests;

/// <summary>
/// apply-with-verify-cancellation-and-compile-scope: fake-driven coverage for the two
/// bug fixes in <see cref="ApplyWithVerifyTool"/>:
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
/// identity and project-filter arguments are observable.
///
/// apply-with-verify-cancelled-result-compensation: the real <c>CompileCheckService.CheckAsync</c>
/// never lets an <see cref="OperationCanceledException"/> escape — it catches it internally and
/// returns a normal (non-throwing) <c>CompileCheckDto</c> with <c>Cancelled=true</c>. The three
/// tests above simulate cancellation by having <c>FakeCompileCheck</c> throw directly, a shape
/// the real service never produces, so they do not exercise that production failure mode. The
/// two <c>*_CancelledDto_</c> tests below close that gap by returning a <c>Cancelled=true</c> DTO
/// instead of throwing.
/// </summary>
[TestClass]
public sealed class ApplyWithVerifyCancellationAndScopeTests
{
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

        var thrown = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            ApplyWithVerifyTool.ApplyWithVerify(
                new FakeGate(),
                new FakeRefactoring(),
                compile,
                undo,
                previewStore,
                previewToken: "tok",
                rollbackOnError: true,
                loggerFactory: null,
                ct: cts.Token));

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

        var thrown = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            ApplyWithVerifyTool.ApplyWithVerify(
                new FakeGate(),
                new FakeRefactoring(),
                compile,
                undo,
                previewStore,
                previewToken: "tok",
                rollbackOnError: true,
                loggerFactory: loggerFactory,
                ct: cts.Token));

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

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            ApplyWithVerifyTool.ApplyWithVerify(
                new FakeGate(),
                refactoring,
                compile,
                undo,
                previewStore,
                previewToken: "tok",
                rollbackOnError: true,
                loggerFactory: null,
                ct: cts.Token));

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

        var thrown = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            ApplyWithVerifyTool.ApplyWithVerify(
                new FakeGate(),
                refactoring,
                compile,
                undo,
                previewStore,
                previewToken: "tok",
                rollbackOnError: true,
                loggerFactory: null,
                ct: cts.Token));

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

        var thrown = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            ApplyWithVerifyTool.ApplyWithVerify(
                new FakeGate(),
                new FakeRefactoring(),
                compile,
                undo,
                previewStore,
                previewToken: "tok",
                rollbackOnError: true,
                loggerFactory: null,
                ct: cts.Token));

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

        var json = await ApplyWithVerifyTool.ApplyWithVerify(
            new FakeGate(),
            new FakeRefactoring(),
            compile,
            new FakeUndo(),
            previewStore,
            previewToken: "tok",
            rollbackOnError: true,
            loggerFactory: null,
            ct: CancellationToken.None);

        AssertStatus(json, "applied");
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

        var json = await ApplyWithVerifyTool.ApplyWithVerify(
            new FakeGate(),
            new FakeRefactoring(),
            compile,
            new FakeUndo(),
            previewStore,
            previewToken: "tok",
            rollbackOnError: true,
            loggerFactory: null,
            ct: CancellationToken.None);

        AssertStatus(json, "applied");
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

        var json = await ApplyWithVerifyTool.ApplyWithVerify(
            new FakeGate(),
            new FakeRefactoring(),
            compile,
            new FakeUndo(),
            previewStore,
            previewToken: "tok",
            rollbackOnError: true,
            loggerFactory: null,
            ct: CancellationToken.None);

        AssertStatus(json, "applied");
        Assert.AreEqual(2, compile.RecordedFilters.Count);
        Assert.IsTrue(compile.RecordedFilters.All(f => f is null),
            "A zero-project diff must fall back to a full-solution compile (null filter).");
    }

    // ── helpers ──

    private static void AssertStatus(string json, string expected)
    {
        using var doc = JsonDocument.Parse(json);
        var status = doc.RootElement.GetProperty("status").GetString();
        Assert.AreEqual(expected, status, $"Unexpected status. Full JSON: {json}");
    }

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

    private sealed class FakeGate : IWorkspaceExecutionGate
    {
        public Task<T> RunWriteAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct, bool applyStalenessPolicy = true)
            => action(ct);

        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => action(ct);

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => action(ct);

        public void RemoveGate(string workspaceId) { }
    }

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
