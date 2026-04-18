using System.Diagnostics;
using System.Text;
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

    public Task<WorkspaceValidationDto> ValidateAsync(
        string workspaceId,
        IReadOnlyList<string>? changedFilePaths,
        bool runTests,
        CancellationToken ct,
        bool summary = false)
        => ValidateInternalAsync(workspaceId, changedFilePaths, runTests, ct, summary, warnings: Array.Empty<string>());

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
        var (gitFiles, gitWarnings) = await CollectGitChangedFilesAsync(solutionDir, ct).ConfigureAwait(false);

        // No-fallback path: git produced a (possibly empty) list of touched files. Forward
        // that list verbatim. An empty list is a meaningful signal (clean tree) — we do NOT
        // silently widen to full-workspace scope because that would mask a clean tree as a
        // heavy full-workspace verify.
        if (gitWarnings.Count == 0)
        {
            return await ValidateInternalAsync(workspaceId, gitFiles, runTests, ct, summary, warnings: Array.Empty<string>())
                .ConfigureAwait(false);
        }

        // Fallback: git was unavailable / repo not found / git exited with error. Widen to
        // full-workspace scope (changedFilePaths=null → change-tracker fallback → tool behaves
        // like validate_workspace did before this feature) and surface the warning so the
        // caller can tell why the scope is full instead of narrow.
        return await ValidateInternalAsync(workspaceId, changedFilePaths: null, runTests, ct, summary, warnings: gitWarnings)
            .ConfigureAwait(false);
    }

    private async Task<WorkspaceValidationDto> ValidateInternalAsync(
        string workspaceId,
        IReadOnlyList<string>? changedFilePaths,
        bool runTests,
        CancellationToken ct,
        bool summary,
        IReadOnlyList<string> warnings)
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
            TestRunResult: testRunResult,
            Warnings: warnings);
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
    private static async Task<(IReadOnlyList<string> Files, IReadOnlyList<string> Warnings)> CollectGitChangedFilesAsync(
        string solutionDirectory,
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
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
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
}
