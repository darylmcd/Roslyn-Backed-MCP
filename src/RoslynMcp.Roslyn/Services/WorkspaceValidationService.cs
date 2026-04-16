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

    private readonly ICompileCheckService _compile;
    private readonly IDiagnosticService _diagnostics;
    private readonly ITestDiscoveryService _testDiscovery;
    private readonly ITestRunnerService _testRunner;
    private readonly IWorkspaceManager _workspace;
    private readonly IChangeTracker? _changeTracker;

    public WorkspaceValidationService(
        ICompileCheckService compile,
        IDiagnosticService diagnostics,
        ITestDiscoveryService testDiscovery,
        ITestRunnerService testRunner,
        IWorkspaceManager workspace,
        IChangeTracker? changeTracker = null)
    {
        _compile = compile;
        _diagnostics = diagnostics;
        _testDiscovery = testDiscovery;
        _testRunner = testRunner;
        _workspace = workspace;
        _changeTracker = changeTracker;
    }

    public async Task<WorkspaceValidationDto> ValidateAsync(
        string workspaceId,
        IReadOnlyList<string>? changedFilePaths,
        bool runTests,
        CancellationToken ct,
        bool summary = false)
    {
        var (changedFiles, unknownFiles) = ResolveChangedFiles(workspaceId, changedFilePaths);

        // Stage 1: in-memory compile check across the whole workspace.
        var compile = await _compile.CheckAsync(
            workspaceId,
            new CompileCheckOptions(SeverityFilter: "Error", Limit: 200),
            ct).ConfigureAwait(false);

        // Stage 2: harvest error-severity diagnostics. Pull a slim subset of analyzer diagnostics
        // separately so the response captures CA*/IDE* errors (compile_check is compiler-only).
        var diagResult = await _diagnostics.GetDiagnosticsAsync(
            workspaceId, projectFilter: null, fileFilter: null,
            severityFilter: "Error", diagnosticIdFilter: null, ct).ConfigureAwait(false);
        var allErrors = compile.Diagnostics
            .Concat(diagResult.CompilerDiagnostics)
            .Concat(diagResult.AnalyzerDiagnostics)
            .Where(d => string.Equals(d.Severity, "Error", StringComparison.Ordinal))
            .DistinctBy(d => (d.Id, d.FilePath, d.StartLine, d.StartColumn))
            .ToArray();

        // Stage 3: discover related tests for the changed files (no test execution yet).
        var related = changedFiles.Count == 0
            ? new RelatedTestsForFilesDto([], string.Empty)
            : await _testDiscovery
                .FindRelatedTestsForFilesAsync(workspaceId, changedFiles, MaxRelatedTestsCap, ct)
                .ConfigureAwait(false);

        // Stage 4: optionally run the related tests.
        TestRunResultDto? testRunResult = null;
        if (runTests && !string.IsNullOrWhiteSpace(related.DotnetTestFilter))
        {
            try
            {
                testRunResult = await _testRunner
                    .RunTestsAsync(workspaceId, projectName: null, filter: related.DotnetTestFilter, ct)
                    .ConfigureAwait(false);
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

        var status = ComputeOverallStatus(compile, allErrors, testRunResult, runTests);

        // validate-workspace-output-cap-summary-mode: drop per-diagnostic + per-test detail
        // when caller asked for a summary. Counts + status still surface the verdict; the
        // CompileResult and TestRunResult are kept because they already carry their own
        // bounded summaries (compile_check.Diagnostics is capped at 200 by the caller above;
        // test results are aggregate counters, not per-test rows).
        var emittedErrors = summary ? Array.Empty<DiagnosticDto>() : (IReadOnlyList<DiagnosticDto>)allErrors;
        var emittedTests = summary ? Array.Empty<RelatedTestCaseDto>() : related.Tests;

        return new WorkspaceValidationDto(
            OverallStatus: status,
            ChangedFilePaths: changedFiles,
            UnknownFilePaths: unknownFiles,
            CompileResult: compile,
            ErrorDiagnostics: emittedErrors,
            WarningCount: compile.WarningCount,
            DiscoveredTests: emittedTests,
            DotnetTestFilter: string.IsNullOrWhiteSpace(related.DotnetTestFilter) ? null : related.DotnetTestFilter,
            TestRunResult: testRunResult);
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

    private static string ComputeOverallStatus(
        CompileCheckDto compile,
        IReadOnlyList<DiagnosticDto> errors,
        TestRunResultDto? testRunResult,
        bool runTests)
    {
        if (!compile.Success || errors.Any(d => string.Equals(d.Category, "Compiler", StringComparison.Ordinal)))
            return "compile-error";
        if (errors.Any(d => !string.Equals(d.Category, "Compiler", StringComparison.Ordinal)))
            return "analyzer-error";
        if (runTests && testRunResult is not null && testRunResult.Failed > 0)
            return "test-failure";
        return "clean";
    }
}
