using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Item 5 implementation. Composes the four primitives an agent typically runs after an edit:
/// <list type="number">
///   <item><description><see cref="ICompileCheckService.CheckAsync"/> for the changed scope.</description></item>
///   <item><description>Filtering of compiler/analyzer diagnostics down to severity <c>Error</c>.</description></item>
///   <item><description><see cref="ITestDiscoveryService.FindRelatedTestsForFilesAsync"/> over the changed file set.</description></item>
///   <item><description>Optional <see cref="ITestRunnerService.RunTestsAsync"/> with the discovered filter when <c>runTests=true</c>.</description></item>
/// </list>
/// Returns one aggregate envelope so callers don't make four separate round-trips.
/// </summary>
public sealed class WorkspaceValidationService : IWorkspaceValidationService
{
    private const int MaxRelatedTestsCap = 50;
    private static readonly TimeSpan DefaultGitStatusTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultValidationPhaseTimeout = TimeSpan.FromSeconds(25);

    private static readonly Action<ILogger, int, string, Exception?> LogProcessKillFailed =
        LoggerMessage.Define<int, string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogProcessKillFailed)),
            "Failed to kill git process tree for process {ProcessId} after {KillReason} while collecting git-changed files.");

    private readonly ICompileCheckService _compile;
    private readonly IDiagnosticService _diagnostics;
    private readonly ITestDiscoveryService _testDiscovery;
    private readonly ITestRunnerService _testRunner;
    private readonly IWorkspaceManager _workspace;
    private readonly IChangeTracker? _changeTracker;
    private readonly TimeSpan _gitStatusTimeout;
    private readonly TimeSpan _validationPhaseTimeout;
    private readonly ILogger<WorkspaceValidationService>? _logger;
    private readonly Action<Process> _killProcessTree;

    public WorkspaceValidationService(
        ICompileCheckService compile,
        IDiagnosticService diagnostics,
        ITestDiscoveryService testDiscovery,
        ITestRunnerService testRunner,
        IWorkspaceManager workspace,
        IChangeTracker? changeTracker = null,
        ILogger<WorkspaceValidationService>? logger = null)
        : this(
            compile,
            diagnostics,
            testDiscovery,
            testRunner,
            workspace,
            changeTracker,
            DefaultGitStatusTimeout,
            DefaultValidationPhaseTimeout,
            logger)
    {
    }

    internal WorkspaceValidationService(
        ICompileCheckService compile,
        IDiagnosticService diagnostics,
        ITestDiscoveryService testDiscovery,
        ITestRunnerService testRunner,
        IWorkspaceManager workspace,
        IChangeTracker? changeTracker,
        TimeSpan gitStatusTimeout,
        TimeSpan validationPhaseTimeout,
        ILogger<WorkspaceValidationService>? logger = null,
        Action<Process>? killProcessTree = null)
    {
        _compile = compile;
        _diagnostics = diagnostics;
        _testDiscovery = testDiscovery;
        _testRunner = testRunner;
        _workspace = workspace;
        _changeTracker = changeTracker;
        _gitStatusTimeout = gitStatusTimeout > TimeSpan.Zero ? gitStatusTimeout : DefaultGitStatusTimeout;
        _validationPhaseTimeout = validationPhaseTimeout > TimeSpan.Zero ? validationPhaseTimeout : DefaultValidationPhaseTimeout;
        _logger = logger;
        _killProcessTree = killProcessTree ?? KillProcessTree;
    }

    private static void KillProcessTree(Process process) => process.Kill(entireProcessTree: true);

    public async Task<WorkspaceValidationDto> ValidateAsync(
        string workspaceId,
        IReadOnlyList<string>? changedFilePaths,
        bool runTests,
        CancellationToken ct,
        bool summary = false)
    {
        // validate-workspace-25s-internalvalidationtimeoutexception-on-medium-solution (gh #759):
        // a validation phase that exceeds the 25-second internal timeout throws the private
        // InternalValidationTimeoutException. Pre-fix the exception escaped here as an unhandled
        // throw and surfaced as a bare SDK error string at the MCP transport. Mirror the catch
        // already present in ValidateRecentGitChangesAsync so both entry points return the
        // structured timeout DTO. OperationCanceledException is intentionally NOT caught — that
        // is the cooperative-cancellation signal and must propagate to the caller.
        try
        {
            return await ValidateInternalAsync(
                new ValidationRequestContext(workspaceId, changedFilePaths, runTests, summary, Array.Empty<string>()),
                ct).ConfigureAwait(false);
        }
        catch (InternalValidationTimeoutException ex)
        {
            return CreateTimeoutResult(ex, changedFilePaths ?? Array.Empty<string>(), Array.Empty<string>());
        }
    }

    /// <summary>
    /// post-edit-validate-workspace-scoped-to-touched-files: auto-derives the changed-file set
    /// from <c>git status --porcelain</c> in the solution directory, then forwards to the
    /// scope-taking <see cref="ValidateAsync"/> path. On git-unavailable / non-git-repo
    /// conditions we fall back to full-workspace scope and surface the reason in
    /// <see cref="WorkspaceValidationDto.Warnings"/>.
    /// </summary>
    public async Task<WorkspaceValidationDto> ValidateRecentGitChangesAsync(
        string workspaceId,
        bool runTests,
        CancellationToken ct,
        bool summary = false)
    {
        var solutionDir = ResolveSolutionDirectory(workspaceId);
        var (gitFiles, gitWarnings) = await CollectGitChangedFilesAsync(solutionDir, _gitStatusTimeout, ct)
            .ConfigureAwait(false);

        // No-fallback path: git produced a (possibly empty) list of touched files. Forward
        // that list verbatim. An empty list is a meaningful signal (clean tree) — we do NOT
        // silently widen to full-workspace scope because that would mask a clean tree as a
        // heavy full-workspace verify.
        if (gitWarnings.Count == 0)
        {
            try
            {
                return await ValidateInternalAsync(
                    new ValidationRequestContext(workspaceId, gitFiles, runTests, summary, Array.Empty<string>()),
                    ct).ConfigureAwait(false);
            }
            catch (InternalValidationTimeoutException ex)
            {
                return CreateTimeoutResult(ex, gitFiles, Array.Empty<string>());
            }
        }

        // Fallback: git was unavailable / repo not found / git exited with error. Widen to
        // full-workspace scope (changedFilePaths=null → change-tracker fallback → tool behaves
        // like validate_workspace did before this feature) and surface the warning so the
        // caller can tell why the scope is full instead of narrow.
        try
        {
            return await ValidateInternalAsync(
                new ValidationRequestContext(workspaceId, ChangedFilePaths: null, runTests, summary, gitWarnings),
                ct).ConfigureAwait(false);
        }
        catch (InternalValidationTimeoutException ex)
        {
            return CreateTimeoutResult(ex, Array.Empty<string>(), gitWarnings);
        }
    }

    /// <summary>
    /// Parameter object for <see cref="ValidateInternalAsync"/>. Folds the five per-call inputs
    /// (<see cref="Warnings"/> carries any pre-accrued git-fallback warnings; the ambient
    /// <see cref="CancellationToken"/> stays a separate argument). <c>unknownFiles</c> is NOT a
    /// member — it is derived locally from <see cref="ResolveChangedFiles"/> inside the method.
    /// </summary>
    private readonly record struct ValidationRequestContext(
        string WorkspaceId,
        IReadOnlyList<string>? ChangedFilePaths,
        bool RunTests,
        bool Summary,
        IReadOnlyList<string> Warnings);

    private async Task<WorkspaceValidationDto> ValidateInternalAsync(
        ValidationRequestContext request,
        CancellationToken ct)
    {
        var (changedFiles, unknownFiles) = ResolveChangedFiles(request.WorkspaceId, request.ChangedFilePaths);

        // When the caller omitted changedFilePaths we fell back to the ChangeTracker's
        // append-only log; reconcile that list against `git status` before validating.
        if (request.ChangedFilePaths is null or { Count: 0 } && changedFiles.Count > 0)
        {
            changedFiles = await ReconcileChangeTrackerFilesAsync(request.WorkspaceId, changedFiles, ct)
                .ConfigureAwait(false);
        }

        // Stage 1: in-memory compile check across the whole workspace.
        var compile = await RunValidationPhaseAsync(
            "compile_check",
            token => _compile.CheckAsync(
                request.WorkspaceId,
                new CompileCheckOptions(SeverityFilter: "Error", Limit: 200),
                token),
            ct).ConfigureAwait(false);

        // Stage 2: harvest error-severity diagnostics. Pull a slim subset of analyzer diagnostics
        // separately so the response captures CA*/IDE* errors (compile_check is compiler-only).
        var diagResult = await RunValidationPhaseAsync(
            "project_diagnostics",
            token => _diagnostics.GetDiagnosticsAsync(
                request.WorkspaceId, projectFilter: null, fileFilter: null,
                severityFilter: "Error", diagnosticIdFilter: null, token),
            ct).ConfigureAwait(false);
        var allErrors = compile.Diagnostics
            .Concat(diagResult.CompilerDiagnostics)
            .Concat(diagResult.AnalyzerDiagnostics)
            .Where(d => string.Equals(d.Severity, "Error", StringComparison.Ordinal))
            .DistinctBy(d => (d.Id, d.FilePath, d.StartLine, d.StartColumn))
            .ToArray();

        // Stages 3+4: discover related tests and (optionally) run them.
        var (related, testRunResult) = await DiscoverAndOptionallyRunTestsAsync(
            request.WorkspaceId, changedFiles, request.RunTests, ct).ConfigureAwait(false);

        var status = ComputeOverallStatus(compile, allErrors, testRunResult, request.RunTests);

        var emittedWarnings = AppendTestZeroRunWarning(
            request.Warnings, testRunResult, request.RunTests, related.DotnetTestFilter);

        // validate-workspace-output-cap-summary-mode: drop per-diagnostic + per-test detail
        // when caller asked for a summary. Counts + status still surface the verdict; the
        // CompileResult and TestRunResult are kept because they already carry their own
        // bounded summaries (compile_check.Diagnostics is capped at 200 by the caller above;
        // test results are aggregate counters, not per-test rows).
        var emittedErrors = request.Summary ? Array.Empty<DiagnosticDto>() : (IReadOnlyList<DiagnosticDto>)allErrors;
        var emittedTests = request.Summary ? Array.Empty<RelatedTestCaseDto>() : related.Tests;

        return new WorkspaceValidationDto(
            OverallStatus: status,
            ChangedFilePaths: changedFiles,
            UnknownFilePaths: unknownFiles,
            CompileResult: compile,
            ErrorDiagnostics: emittedErrors,
            // validate-workspace-overallstatus-analyzer-error-with-empty-errordiagnostics:
            // ErrorCount mirrors the always-populated WarningCount field below. Counted off
            // allErrors (the full merged set), NOT emittedErrors, so summary=true callers
            // still see the count that drove the OverallStatus verdict.
            ErrorCount: allErrors.Length,
            WarningCount: compile.WarningCount,
            DiscoveredTests: emittedTests,
            DotnetTestFilter: string.IsNullOrWhiteSpace(related.DotnetTestFilter) ? null : related.DotnetTestFilter,
            TestRunResult: testRunResult,
            Warnings: emittedWarnings);
    }

    /// <summary>
    /// validate-workspace-changetracker-no-disk-reconcile-after-git-checkout (gh #738):
    /// when the caller omits changedFilePaths we fall back to the ChangeTracker's
    /// append-only log. The tracker has no mechanism to drop entries for files that
    /// were subsequently reverted out-of-band (e.g. <c>git checkout -- foo.cs</c>), so the
    /// raw fallback list can include files that are now clean on disk. Reconcile the
    /// tracker-derived list against <c>git status</c> and return only files git still
    /// reports as dirty. On git-unavailable / non-git-repo conditions we degrade gracefully:
    /// the unfiltered tracker list is returned unchanged so the no-git path keeps its
    /// existing semantics. Callers rebind their local — this method does NOT mutate a
    /// captured variable.
    /// </summary>
    private async Task<IReadOnlyList<string>> ReconcileChangeTrackerFilesAsync(
        string workspaceId,
        IReadOnlyList<string> changedFiles,
        CancellationToken ct)
    {
        string? solutionDir;
        try
        {
            solutionDir = ResolveSolutionDirectory(workspaceId);
        }
        catch
        {
            solutionDir = null;
        }

        if (solutionDir is null)
            return changedFiles;

        var (gitFiles, gitWarnings) = await CollectGitChangedFilesAsync(solutionDir, _gitStatusTimeout, ct)
            .ConfigureAwait(false);

        // git unavailable / repo not found / git error — preserve existing unfiltered behavior
        // (do NOT add a warning here; this branch is the default validate_workspace path, not
        // validate_recent_git_changes; the git-status reconcile is a best-effort precision
        // improvement, not a mandatory step).
        if (gitWarnings.Count != 0)
            return changedFiles;

        // Build the dirty set with case-insensitive normalized paths so the intersection
        // survives platform path-separator + casing differences.
        var dirty = new HashSet<string>(
            gitFiles.Select(NormalizePathForReconcile),
            StringComparer.OrdinalIgnoreCase);
        return changedFiles
            .Where(p => dirty.Contains(NormalizePathForReconcile(p)))
            .ToArray();
    }

    /// <summary>
    /// Stage 3 (discover related tests for the changed files) plus optional Stage 4 (run them
    /// when <paramref name="runTests"/> is set and a filter was discovered). Returns the
    /// discovered-tests DTO and the test-run result (null when tests were not run).
    /// </summary>
    private async Task<(RelatedTestsForFilesDto Related, TestRunResultDto? TestRunResult)> DiscoverAndOptionallyRunTestsAsync(
        string workspaceId,
        IReadOnlyList<string> changedFiles,
        bool runTests,
        CancellationToken ct)
    {
        // Stage 3: discover related tests for the changed files (no test execution yet).
        var related = changedFiles.Count == 0
            ? new RelatedTestsForFilesDto(
                [],
                string.Empty,
                new PaginationInfo(0, 0, false),
                // test-related-files-empty-result-explainability: empty changed-file set is not
                // a heuristic miss — flag it explicitly so the caller doesn't second-guess.
                new RelatedTestsDiagnosticsDto(
                    ScannedTestProjects: 0,
                    HeuristicsAttempted: [],
                    MissReasons: ["no changed source files supplied; related-test discovery skipped"]))
            : await RunValidationPhaseAsync(
                "test_related_files",
                token => _testDiscovery.FindRelatedTestsForFilesAsync(
                    workspaceId, changedFiles, MaxRelatedTestsCap, token),
                ct).ConfigureAwait(false);

        // Stage 4: optionally run the related tests.
        TestRunResultDto? testRunResult = null;
        if (runTests && !string.IsNullOrWhiteSpace(related.DotnetTestFilter))
        {
            try
            {
                testRunResult = await RunValidationPhaseAsync(
                    "test_run",
                    token => _testRunner.RunTestsAsync(
                        workspaceId, projectName: null, filter: related.DotnetTestFilter, token),
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Surface the failure as a synthetic result rather than throwing — the validation
                // bundle should always return a structured envelope so the agent can act on it.
                testRunResult = new TestRunResultDto(
                    new CommandExecutionDto(
                        Command: "dotnet",
                        Arguments: ["test", "--filter", related.DotnetTestFilter],
                        WorkingDirectory: string.Empty,
                        TargetPath: string.Empty,
                        ExitCode: -1,
                        Succeeded: false,
                        DurationMs: 0,
                        StdOut: string.Empty,
                        StdErr: ex.Message),
                    Total: 0, Passed: 0, Failed: 1, Skipped: 0,
                    Failures: [],
                    FailureEnvelope: new TestRunFailureEnvelopeDto(
                        ErrorKind: "Unknown", IsRetryable: false,
                        Summary: $"validate_workspace failed to invoke dotnet test: {ex.Message}",
                        StdOutTail: null, StdErrTail: null));
            }
        }

        return (related, testRunResult);
    }

    /// <summary>
    /// validate-workspace-runtests-total-zero: when the "test-zero-run" verdict fires, append a
    /// diagnostic warning that names the discovered filter and points at the most likely cause.
    /// Helps the caller decide whether to re-run test_run standalone (which tends to succeed
    /// because it does not race with the workspace's IChangeTracker refresh / dotnet test
    /// working-directory resolution). Returns <paramref name="warnings"/> unchanged when the
    /// verdict does not apply.
    /// </summary>
    private static IReadOnlyList<string> AppendTestZeroRunWarning(
        IReadOnlyList<string> warnings,
        TestRunResultDto? testRunResult,
        bool runTests,
        string? filter)
    {
        if (runTests && testRunResult is not null && testRunResult.Total == 0 && !string.IsNullOrWhiteSpace(filter))
        {
            return warnings
                .Concat(new[]
                {
                    $"validate_workspace: runTests=true produced testRunResult.total=0 with filter '{filter}'; "
                    + "this likely indicates filter resolution failure (working-directory or IChangeTracker timing). "
                    + "Run test_run with the same filter to confirm."
                })
                .ToArray();
        }

        return warnings;
    }

    /// <summary>
    /// Partition caller-supplied paths into (known-to-workspace, unknown-to-workspace). When the
    /// caller omits the list we fall back to the change-tracker set — those entries are already
    /// materialized from Roslyn document operations, so every path is known by construction.
    ///
    /// dr-9-8-bug-validate-fabricated-accepts-fabricated-silen: pre-fix the service returned only
    /// a single list and silently dropped unknown paths inside the test-discovery stage. Unknown
    /// paths are now surfaced as a separate list in the response so callers can tell that part of
    /// their requested scope was ignored (typo in path, stale audit-report reference, etc.).
    /// </summary>
    private (IReadOnlyList<string> Known, IReadOnlyList<string> Unknown) ResolveChangedFiles(
        string workspaceId, IReadOnlyList<string>? caller)
    {
        if (caller is { Count: > 0 })
        {
            var deduped = caller
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (deduped.Length == 0)
                return (Array.Empty<string>(), Array.Empty<string>());

            var solution = _workspace.GetCurrentSolution(workspaceId);
            var workspacePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    if (!string.IsNullOrEmpty(document.FilePath))
                    {
                        workspacePaths.Add(Path.GetFullPath(document.FilePath));
                    }
                }
            }

            var known = new List<string>(deduped.Length);
            var unknown = new List<string>();
            foreach (var path in deduped)
            {
                string normalized;
                try
                {
                    normalized = Path.GetFullPath(path);
                }
                catch (Exception) when (path is not null)
                {
                    // An unrooted / malformed path is definitively unknown.
                    unknown.Add(path);
                    continue;
                }

                if (workspacePaths.Contains(normalized))
                    known.Add(path);
                else
                    unknown.Add(path);
            }

            return (known, unknown);
        }

        // Change-tracker fallback: every entry originates from a Roslyn document mutation inside
        // this process, so there is no unknown-path set to surface.
        if (_changeTracker is null)
            return (Array.Empty<string>(), Array.Empty<string>());

        var tracked = _changeTracker
            .GetChanges(workspaceId)
            .SelectMany(c => c.AffectedFiles ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (tracked, Array.Empty<string>());
    }

    // validate-workspace-overallstatus-false-positive: compile_check can report Success=false
    // with ErrorCount=0 when CompletedProjects==0 (e.g. empty project filter) — a distinct
    // "no work" signal, not a compiler failure. Gating on !Success alone would emit
    // overallStatus: compile-error with zero error diagnostics. Use ErrorCount and the
    // merged error-severity rows instead.
    //
    // validate-workspace-runtests-total-zero: when runTests=true and the test-run reports
    // Total=0, dotnet test matched no tests for the discovered filter — almost always a
    // filter-resolution failure (working-directory mismatch or IChangeTracker timing race),
    // not a real pass. Pre-fix this branch returned "clean" because Failed==0 fired the
    // final fall-through. The new "test-zero-run" verdict surfaces the symptom explicitly
    // so a caller exact-matching "clean" doesn't treat a zero-run as success. This is a
    // BREAKING change for callers that exact-match "clean" as the only passing value; the
    // CHANGELOG flags it as such.
    //
    // validate-workspace-compiler-category-status-mismatch: the merged `errors` list is built
    // from TWO independent diagnostic harvests — compile_check (CompileCheckService, which
    // fetches compilations directly and computes an unbounded ErrorCount) and
    // project_diagnostics (DiagnosticService, which goes through its own version-keyed
    // compilation cache). When the two disagree, the second harvest can surface a
    // Category=="Compiler" row that the authoritative compile pass never saw, producing the
    // observed "overallStatus: compile-error / Compile errors: 0" contradiction. compile.ErrorCount
    // is now the SOLE compile-error signal; an uncorroborated Compiler-category row from the
    // second harvest falls through to "analyzer-error" (branch 2 widened from a
    // Category!="Compiler" predicate to a plain non-empty check) so it is downgraded, never
    // silently dropped from the verdict. Note compile.Diagnostics is paged (Limit=200) while
    // ErrorCount is the true total, so identity-matching against the paged list would be
    // strictly less reliable than trusting ErrorCount.
    internal static string ComputeOverallStatus(
        CompileCheckDto compile,
        IReadOnlyList<DiagnosticDto> errors,
        TestRunResultDto? testRunResult,
        bool runTests)
    {
        if (compile.ErrorCount > 0)
            return "compile-error";
        if (errors.Count > 0)
            return "analyzer-error";
        if (runTests && testRunResult is not null && testRunResult.Failed > 0)
            return "test-failure";
        if (runTests && testRunResult is not null && testRunResult.Total == 0)
            return "test-zero-run";
        return "clean";
    }

    /// <summary>
    /// Resolves the directory that contains the loaded solution / project file. Used as the
    /// working directory for the git invocation so <c>git status</c> reports changes relative
    /// to the right repository (not the MCP host process's CWD).
    /// </summary>
    private string ResolveSolutionDirectory(string workspaceId)
    {
        var status = _workspace.GetStatus(workspaceId);
        var loadedPath = status.LoadedPath
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        return Path.GetDirectoryName(Path.GetFullPath(loadedPath))
            ?? throw new InvalidOperationException(
                $"Could not resolve solution directory for workspace '{workspaceId}' (LoadedPath='{loadedPath}').");
    }

    /// <summary>
    /// post-edit-validate-workspace-scoped-to-touched-files: shells out to
    /// <c>git status --porcelain=v1 -z -uall</c> to enumerate touched files. The <c>-z</c>
    /// NUL-terminator variant avoids quoting of paths containing whitespace or non-ASCII. We
    /// filter to extensions that influence workspace validation (<c>.cs</c>, <c>.csproj</c>,
    /// <c>.slnx</c>, <c>.sln</c>, <c>.props</c>, <c>.targets</c>) — other touched files cannot
    /// change Roslyn compilation output, so passing them would just widen the scope for no
    /// verification benefit.
    ///
    /// Returns a (files, warnings) tuple:
    /// <list type="bullet">
    ///   <item>warnings empty → files is the authoritative touched set (possibly empty on clean tree).</item>
    ///   <item>warnings non-empty → git was unavailable / solution is outside a git repo / git exited with error. Caller should fall back to full-workspace scope.</item>
    /// </list>
    /// </summary>
    private async Task<(IReadOnlyList<string> Files, IReadOnlyList<string> Warnings)> CollectGitChangedFilesAsync(
        string solutionDirectory,
        TimeSpan timeout,
        CancellationToken ct)
    {
        // Fast pre-check: a `.git` directory / file (submodule, worktree) must exist somewhere
        // at or above the solution directory. If none, we're demonstrably outside a repo and
        // can skip the git invocation entirely — saves ~20 ms and gives a precise warning
        // instead of the noisier "git exited 128" message.
        if (!IsInsideGitRepository(solutionDirectory))
        {
            return (Array.Empty<string>(), new[]
            {
                $"git repository not found at or above '{solutionDirectory}'; validated full workspace."
            });
        }

        ProcessStartInfo startInfo;
        try
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = solutionDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.ArgumentList.Add("status");
            startInfo.ArgumentList.Add("--porcelain=v1");
            startInfo.ArgumentList.Add("-z");
            startInfo.ArgumentList.Add("-uall");
        }
        catch (Exception ex)
        {
            return (Array.Empty<string>(), new[]
            {
                $"git invocation failed to configure: {ex.Message}; validated full workspace."
            });
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return (Array.Empty<string>(), new[]
                {
                    "git failed to start; validated full workspace."
                });
            }
        }
        catch (Exception ex)
        {
            // File-not-found (git not on PATH), Win32Exception, etc.
            return (Array.Empty<string>(), new[]
            {
                $"git not available on PATH ({ex.Message}); validated full workspace."
            });
        }

        string stdout;
        string stderr;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKillProcessTree(process, "timeout");
            return (Array.Empty<string>(), new[]
            {
                $"git status exceeded the timeout of {timeout.TotalSeconds:F0} second(s); retryable=true; validated full workspace."
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            TryKillProcessTree(process, "failure");
            return (Array.Empty<string>(), new[]
            {
                $"git status failed: {ex.Message}; validated full workspace."
            });
        }

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? $"exit {process.ExitCode}" : stderr.Trim();
            return (Array.Empty<string>(), new[]
            {
                $"git status exited non-zero ({detail}); validated full workspace."
            });
        }

        return (ParseGitPorcelainZ(stdout, solutionDirectory), Array.Empty<string>());
    }

    /// <summary>
    /// Best-effort termination of the git process tree. A kill failure is logged at
    /// <see cref="LogLevel.Warning"/> (with the process id and reason) when a logger is present,
    /// mirroring <c>DotnetCommandRunner.TryKillProcessTree</c>; it is never surfaced to the caller.
    /// </summary>
    internal void TryKillProcessTree(Process process, string killReason)
    {
        try
        {
            _killProcessTree(process);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                LogProcessKillFailed(_logger, process.Id, killReason, ex);
            }
        }
    }

    /// <summary>
    /// Walks from <paramref name="startDirectory"/> upward looking for a <c>.git</c> entry (a
    /// directory for a normal clone, a file for a submodule / linked worktree). Returns false
    /// when we hit the filesystem root without finding one.
    /// </summary>
    private static bool IsInsideGitRepository(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            return false;

        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(startDirectory);
        }
        catch
        {
            return false;
        }

        while (current is not null)
        {
            var gitEntry = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitEntry) || File.Exists(gitEntry))
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// Parses the NUL-terminated output of <c>git status --porcelain=v1 -z</c>. Each entry is
    /// of the form <c>XY&#160;path</c> where <c>X</c> is the staged status byte, <c>Y</c> the
    /// unstaged status byte, and <c>path</c> is the repo-relative path. Renamed entries
    /// (<c>R</c>) emit two NUL-terminated paths per record (new then old); we take only the
    /// new path (the current location on disk). Filtering is strict to extensions that can
    /// affect Roslyn workspace validation.
    /// </summary>
    private static IReadOnlyList<string> ParseGitPorcelainZ(string stdout, string solutionDirectory)
    {
        if (string.IsNullOrEmpty(stdout))
            return Array.Empty<string>();

        var entries = stdout.Split('\0', StringSplitOptions.None);
        var results = new List<string>();

        var index = 0;
        while (index < entries.Length)
        {
            var entry = entries[index];
            index++;
            if (entry.Length == 0)
                continue;
            if (entry.Length < 3)
                continue;

            // `XY<space>path` (status bytes + literal space).
            var staged = entry[0];
            var unstaged = entry[1];
            var pathPart = entry[3..]; // skip the two status chars + the space

            // Renamed / copied entries emit the old path as the NEXT NUL-terminated record.
            // Consume it so we don't mistake it for a fresh status line.
            if (staged == 'R' || staged == 'C' || unstaged == 'R' || unstaged == 'C')
            {
                if (index < entries.Length)
                    index++;
            }

            if (string.IsNullOrWhiteSpace(pathPart))
                continue;
            if (!HasValidationRelevantExtension(pathPart))
                continue;
            if (IsBuildOutputPath(pathPart))
                continue;

            var absolute = Path.GetFullPath(Path.Combine(solutionDirectory, pathPart));
            results.Add(absolute);
        }

        return results;
    }

    /// <summary>
    /// Filter out MSBuild intermediate / final output directories (<c>obj/</c> and <c>bin/</c>).
    /// These hold auto-generated artifacts (AssemblyInfo, GlobalUsings, PE outputs) that are
    /// normally gitignored — but `git status -uall` picks them up in repos without the
    /// standard .gitignore and passing them into workspace validation is always wrong.
    /// </summary>
    private static bool IsBuildOutputPath(string repoRelativePath)
    {
        var normalized = repoRelativePath.Replace('\\', '/');
        // Check for `obj/` or `bin/` at any path segment — we don't anchor to the start
        // because the solution may live under a subdirectory (e.g. `samples/Foo/...`).
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// validate-workspace-changetracker-no-disk-reconcile-after-git-checkout (gh #738):
    /// path normalization for ChangeTracker reconciliation. Both sides (tracker list
    /// and git-derived list) come back as absolute paths via different code paths; the
    /// tracker stores whatever the writer passed in (e.g. <c>Path.GetFullPath</c>'d
    /// during apply), while git emits forward-slash relative paths that we
    /// <c>Path.GetFullPath(Path.Combine(...))</c> into absolute form. On Windows the
    /// resulting strings can disagree on (1) path separator style and (2) intermediate
    /// dot-segments. Normalize via <c>GetFullPath</c> so segments collapse, then replace
    /// backslashes with forward slashes; case is handled by the caller's
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> HashSet.
    /// </summary>
    private static string NormalizePathForReconcile(string path)
    {
        try
        {
            return Path.GetFullPath(path).Replace('\\', '/');
        }
        catch
        {
            return path.Replace('\\', '/');
        }
    }

    private static bool HasValidationRelevantExtension(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
            return false;

        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<T> RunValidationPhaseAsync<T>(
        string phase,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_validationPhaseTimeout);
        try
        {
            return await action(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new InternalValidationTimeoutException(phase, _validationPhaseTimeout);
        }
    }

    private static WorkspaceValidationDto CreateTimeoutResult(
        InternalValidationTimeoutException timeout,
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<string> priorWarnings)
    {
        // validate-workspace-25s-internalvalidationtimeoutexception-on-medium-solution (gh #759):
        // shared by both ValidateAsync and ValidateRecentGitChangesAsync. The original warning
        // text referenced "git-derived scope", which is misleading when ValidateAsync supplies
        // an explicit changedFilePaths list (or when the change-tracker fallback path supplied
        // it). Use neutral phrasing — the changedFiles list is whatever scope the caller
        // landed on at the time of the timeout.
        var warning = $"validation phase '{timeout.Phase}' exceeded the internal timeout of "
            + $"{timeout.Timeout.TotalSeconds:F0} second(s); retryable=true; changedFilePaths reflects the scope that was being validated.";
        var warnings = priorWarnings.Concat(new[] { warning }).ToArray();
        var command = new CommandExecutionDto(
            Command: "validate_workspace",
            Arguments: [timeout.Phase],
            WorkingDirectory: string.Empty,
            TargetPath: string.Empty,
            ExitCode: -1,
            Succeeded: false,
            DurationMs: (long)timeout.Timeout.TotalMilliseconds,
            StdOut: string.Empty,
            StdErr: warning);
        var failure = new TestRunResultDto(
            command,
            Total: 0,
            Passed: 0,
            Failed: 1,
            Skipped: 0,
            Failures: [],
            FailureEnvelope: new TestRunFailureEnvelopeDto(
                ErrorKind: "Timeout",
                IsRetryable: true,
                Summary: warning,
                StdOutTail: null,
                StdErrTail: warning));

        return new WorkspaceValidationDto(
            OverallStatus: "timeout",
            ChangedFilePaths: changedFiles,
            UnknownFilePaths: Array.Empty<string>(),
            CompileResult: new CompileCheckDto(
                Success: false,
                ErrorCount: 0,
                WarningCount: 0,
                TotalDiagnostics: 0,
                ReturnedDiagnostics: 0,
                Offset: 0,
                Limit: 200,
                HasMore: false,
                Diagnostics: Array.Empty<DiagnosticDto>(),
                ElapsedMs: (long)timeout.Timeout.TotalMilliseconds,
                Cancelled: true),
            ErrorDiagnostics: Array.Empty<DiagnosticDto>(),
            ErrorCount: 0,
            WarningCount: 0,
            DiscoveredTests: Array.Empty<RelatedTestCaseDto>(),
            DotnetTestFilter: null,
            TestRunResult: failure,
            Warnings: warnings);
    }

    // validate-workspace-25s-internalvalidationtimeoutexception-on-medium-solution (gh #759):
    // both ValidateAsync and ValidateRecentGitChangesAsync catch this and convert it to a
    // structured timeout DTO. Neutral phrasing in the exception message — pre-fix it
    // hard-coded "validate_recent_git_changes" which was inaccurate for the validate_workspace
    // call path.
    private sealed class InternalValidationTimeoutException(string phase, TimeSpan timeout) : Exception(
        $"validation phase '{phase}' exceeded the internal timeout of {timeout.TotalSeconds:F0} second(s).")
    {
        public string Phase { get; } = phase;
        public TimeSpan Timeout { get; } = timeout;
    }
}
