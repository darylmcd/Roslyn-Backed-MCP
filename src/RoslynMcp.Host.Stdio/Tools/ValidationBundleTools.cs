using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Formatters;
using RoslynMcp.Roslyn.Contracts;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Item 5 (v1.18, <c>roslyn-mcp-post-edit-validation-bundle</c>): one-call composite that
/// chains compile_check + project_diagnostics (errors) + test_related_files + optional
/// test_run. Returns a single envelope with an aggregate <c>overallStatus</c>. WS1 phase
/// 1.6 — each shim body delegates to
/// <see cref="ToolDispatch.ReadByWorkspaceIdAsync{TDto}"/> instead of carrying the
/// dispatch boilerplate inline.
/// </summary>
[McpServerToolType]
public static class ValidationBundleTools
{
    private static readonly HashSet<string> DirectoryCopyExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".roslynmcp",
        ".vs",
        ".worktrees",
        "artifacts",
        "bin",
        "obj",
        "TestResults",
    };

    // workspace-fork-apply-security-hardening: default TTL (hours) for retained forks when
    // ROSLYNMCP_FORK_TTL_HOURS is unset/invalid. Kept forks older than this are swept on the
    // next fork-apply write path (bounded O(fork-count)).
    private const double DefaultForkTtlHours = 24;

    // Default dotnet restore timeout (minutes) when ROSLYNMCP_FORK_RESTORE_TIMEOUT_MINUTES
    // is unset/invalid.
    private const double DefaultForkRestoreTimeoutMinutes = 2;

    // The exact wire format CreateForkDirectory stamps as the fork-directory-name prefix.
    // Quoted 'T'/'Z' so the literals are matched (not interpreted) on the parse side.
    private const string ForkTimestampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    // workspace-fork-apply-robustness-cancellation: serialize concurrent fork-apply
    // invocations against the same source root. Without this, two concurrent
    // workspace_fork_apply calls for the same workspace race on CopyDirectory /
    // DeleteDirectoryIfExists against the same <sourceRoot>/.roslynmcp/forks tree.
    // Keyed by the normalized (full-path, case-insensitive-on-Windows) source root.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ForkApplyLocks =
        new(StringComparer.OrdinalIgnoreCase);

    [McpServerTool(Name = "validate_workspace", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "experimental", true, false,
        "Composite post-edit validation: compile_check + project_diagnostics (errors) + test_related_files (+ optional test_run)."),
     Description("One-call post-edit validation bundle. Runs an in-memory compile_check, harvests Error-severity compiler+analyzer diagnostics, discovers tests related to the changed file set, and (when runTests=true) executes those tests. Returns an aggregate envelope with overallStatus = clean | compile-error | analyzer-error | test-failure | timeout. `timeout` indicates a validation phase exceeded the 25-second internal cap; the response carries `compileResult.cancelled=true` plus a `warnings` entry naming the phase and is safe to retry. Pass `summary=true` on multi-project solutions where the default response (per-diagnostic detail + per-test rows) exceeds the MCP cap (Jellyfin: 135 KB). Pass `responseFormat=\"markdown\"` for a compact ~30-line summary table when the JSON envelope is overkill — verdict (overallStatus) is preserved across both shapes.")]
    public static Task<string> ValidateWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: explicit list of changed file paths. Pass as a native JSON array of absolute file paths, not a JSON-encoded string. Example: [\"/abs/path/a.cs\", \"/abs/path/b.cs\"]. When omitted, the change tracker's session-wide change set is used. Empty list means no test discovery.")] string[]? changedFilePaths = null,
        [Description("When true, runs the discovered related tests via dotnet test --filter. Default: false (discovery only).")] bool runTests = false,
        [Description("When true, drops the per-diagnostic ErrorDiagnostics list and per-test DiscoveredTests list to keep the response under the MCP cap on large solutions. OverallStatus + counts still surface the verdict. Default false preserves the v1.18 shape.")] bool summary = false,
        [Description("Response shape: \"json\" (default) returns the indented JSON envelope; \"markdown\" returns a compact summary table built from the same DTO so the verdict is identical across shapes. Case-insensitive.")] string? responseFormat = null,
        CancellationToken ct = default)
        => gate.RunReadAsync(workspaceId, async c =>
        {
            var dto = await validationService.ValidateAsync(workspaceId, changedFilePaths, runTests, c, summary).ConfigureAwait(false);
            return RenderValidationResponse(dto, responseFormat);
        }, ct);

    [McpServerTool(Name = "validate_recent_git_changes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "experimental", true, false,
        "post-edit-validate-workspace-scoped-to-touched-files: auto-scoped validation companion that derives changedFilePaths from git status --porcelain, falls back to full-workspace scope with a warning when git is unavailable or the solution is outside a git repo."),
     Description("Post-edit validation bundle that auto-derives the changed-file set from `git status --porcelain` in the solution directory, eliminating the manual path-enumeration step. Internally forwards to validate_workspace with the derived list. Falls back to full-workspace scope and surfaces the fallback via the Warnings field when git is unavailable (not on PATH, solution outside a git repo, git exited non-zero). Prefer this over validate_workspace when you have uncommitted edits and want the bundle scoped to the touched-file set.")]
    public static async Task<string> ValidateRecentGitChanges(
        IWorkspaceExecutionGate gate,
        IWorkspaceValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("When true, runs the discovered related tests via dotnet test --filter. Default: false (discovery only).")] bool runTests = false,
        [Description("When true, drops the per-diagnostic ErrorDiagnostics list and per-test DiscoveredTests list to keep the response under the MCP cap on large solutions. OverallStatus + counts still surface the verdict. Default false preserves the full response shape.")] bool summary = false,
        CancellationToken ct = default)
    {
        // validate-recent-git-changes-bare-error-envelope: belt-and-suspenders structured-envelope
        // wrap. The StructuredCallToolFilter already catches handler exceptions and formats them
        // via ToolErrorHandler.ClassifyAndFormat + InjectMetaIfPossible, but field reports across
        // 4 audits (self, TradeWise, NetworkDoc, FirewallAnalyzer) observed the bare SDK
        // "An error occurred invoking 'validate_recent_git_changes'." string instead of the
        // structured envelope. To guarantee the envelope regardless of filter wiring or SDK
        // transport quirks, we wrap the dispatch in an in-handler try/catch that classifies the
        // exception locally and returns the JSON envelope directly from the tool body. When the
        // filter IS active, this is a no-op pass-through on the success path; on the failure
        // path, the filter sees a well-formed JSON string (not an exception) and just injects
        // _meta. OperationCanceledException still propagates (cooperative cancellation is a
        // protocol signal, not a tool error) — matching the filter's own contract.
        try
        {
            return await ToolDispatch.ReadByWorkspaceIdAsync(
                gate,
                workspaceId,
                c => validationService.ValidateRecentGitChangesAsync(workspaceId, runTests, c, summary),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var envelope = ToolErrorHandler.ClassifyAndFormat(ex, "validate_recent_git_changes");
            return ToolErrorHandler.InjectMetaIfPossible(envelope, "validate_recent_git_changes");
        }
    }

    [McpServerTool(Name = "workspace_fork_apply", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("validation", "experimental", false, false,
        "Fork a loaded workspace, replay a preview token into the fork, validate it, and retain/drop the fork by policy."),
     Description("Experimental validation-bundle tool. Creates a server-owned fork under <workspace>/.roslynmcp/forks, replays an existing preview token into that fork without mutating the source workspace, loads the fork as a workspace, runs validate_workspace, and retains or deletes the fork according to retention. retention values: drop-on-success (default), drop-on-failure, drop-always, keep.")]
    public static Task<string> WorkspaceForkApply(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspaceManager,
        IPreviewStore previewStore,
        IWorkspaceValidationService validationService,
        ITestRunnerService testRunnerService,
        [Description("The source workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("The preview token returned by an *_preview tool for the source workspace")] string previewToken,
        [Description("Fork retention policy: drop-on-success, drop-on-failure, drop-always, or keep. Default: drop-on-success.")] string retention = "drop-on-success",
        [Description("When true, runs related tests discovered by validate_workspace. If testFilter is supplied, runs that explicit filter instead.")] bool runTests = false,
        [Description("Optional explicit dotnet test filter to run against the fork when runTests=true.")] string? testFilter = null,
        [Description("Optional stable suffix for the fork directory. Sanitized by the server.")] string? forkName = null,
        CancellationToken ct = default)
    {
        var normalizedRetention = NormalizeRetention(retention);

        return gate.RunWriteAsync(workspaceId, async c =>
        {
            var entry = previewStore.Retrieve(previewToken);
            if (entry is null)
            {
                throw new PreviewTokenStaleException(
                    previewToken,
                    $"Preview token '{previewToken}' is invalid, expired, or stale. Re-issue the paired *_preview call against the current workspace.");
            }

            if (!string.Equals(entry.Value.WorkspaceId, workspaceId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Preview token '{previewToken}' belongs to workspace '{entry.Value.WorkspaceId}', not '{workspaceId}'.",
                    nameof(previewToken));
            }

            if (entry.Value.DiffTruncated)
            {
                throw new InvalidOperationException(
                    "Refusing to fork-apply a truncated preview because the reviewed diff is incomplete. Re-run the preview with a narrower scope before calling workspace_fork_apply.");
            }

            var sourceStatus = workspaceManager.GetStatus(workspaceId);
            var loadedPath = sourceStatus.LoadedPath
                ?? throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
            var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(loadedPath))
                ?? throw new InvalidOperationException($"Could not resolve source root for workspace '{workspaceId}'.");
            var forkPath = CreateForkDirectory(sourceRoot, forkName);
            string? forkWorkspaceId = null;
            var cleanupWarnings = new List<string>();
            var retained = false;
            var success = false;

            var sourceRootLock = ForkApplyLocks.GetOrAdd(
                Path.GetFullPath(sourceRoot),
                _ => new SemaphoreSlim(1, 1));
            await sourceRootLock.WaitAsync(c).ConfigureAwait(false);
            try
            {
                CopyDirectory(sourceRoot, forkPath, c);
                var appliedFiles = await ReplayPreviewIntoForkAsync(
                    entry.Value.OriginalSolution,
                    entry.Value.ModifiedSolution,
                    sourceRoot,
                    forkPath,
                    c).ConfigureAwait(false);
                var forkLoadedPath = MapSourcePathToFork(loadedPath, sourceRoot, forkPath);
                await RestoreForkAsync(forkLoadedPath, c).ConfigureAwait(false);
                var forkStatus = await workspaceManager.LoadAsync(forkLoadedPath, EvictPolicy.Lru, c).ConfigureAwait(false);
                forkWorkspaceId = forkStatus.WorkspaceId;

                var validation = await validationService.ValidateAsync(
                    forkWorkspaceId,
                    appliedFiles,
                    runTests && string.IsNullOrWhiteSpace(testFilter),
                    c,
                    summary: true).ConfigureAwait(false);

                TestRunResultDto? explicitTestRun = null;
                if (runTests && !string.IsNullOrWhiteSpace(testFilter))
                {
                    explicitTestRun = await testRunnerService
                        .RunTestsAsync(forkWorkspaceId, projectName: null, filter: testFilter, c)
                        .ConfigureAwait(false);
                }

                success = IsValidationSuccess(validation, explicitTestRun);
                retained = ShouldRetainFork(normalizedRetention, success);

                if (!retained)
                {
                    CleanupFork(workspaceManager, forkWorkspaceId, forkPath, cleanupWarnings, c);
                    forkWorkspaceId = null;
                }

                if (cleanupWarnings.Count > 0 && !retained)
                {
                    success = false;
                }

                return JsonSerializer.Serialize(new
                {
                    success,
                    forkWorkspaceId,
                    forkPath,
                    retained,
                    appliedFiles,
                    validation,
                    explicitTestRun,
                    cleanupWarnings,
                }, JsonDefaults.Indented);
            }
            catch
            {
                if (!retained)
                {
                    // Best-effort cleanup on the exception path: use CancellationToken.None so
                    // an already-cancelled `c` does not abort cleanup and mask the original
                    // exception with an OperationCanceledException from mid-delete.
                    CleanupFork(workspaceManager, forkWorkspaceId, forkPath, cleanupWarnings, CancellationToken.None);
                }
                throw;
            }
            finally
            {
                sourceRootLock.Release();
            }
        }, ct, applyStalenessPolicy: false);
    }

    /// <summary>
    /// response-format-json-markdown-parameter: branch on the
    /// <paramref name="responseFormat"/> string and serialize the DTO accordingly.
    /// Default (null / empty / "json") preserves the indented JSON wire shape so
    /// existing callers see no change. "markdown" returns the formatter output as
    /// a plain string. Unknown values fall through to JSON rather than throwing,
    /// matching the permissive contract used by other optional shape parameters
    /// (e.g. <c>summary</c>).
    /// </summary>
    internal static string RenderValidationResponse(Core.Services.WorkspaceValidationDto dto, string? responseFormat)
    {
        if (!string.IsNullOrWhiteSpace(responseFormat)
            && string.Equals(responseFormat, "markdown", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateWorkspaceMarkdownFormatter.Format(dto);
        }
        return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
    }

    private static string NormalizeRetention(string? retention)
    {
        var normalized = string.IsNullOrWhiteSpace(retention)
            ? "drop-on-success"
            : retention.Trim().ToLowerInvariant();

        return normalized is "drop-on-success" or "drop-on-failure" or "drop-always" or "keep"
            ? normalized
            : throw new ArgumentException(
                "retention must be one of: drop-on-success, drop-on-failure, drop-always, keep.",
                nameof(retention));
    }

    private static bool ShouldRetainFork(string retention, bool success) => retention switch
    {
        "drop-on-success" => !success,
        "drop-on-failure" => success,
        "drop-always" => false,
        "keep" => true,
        _ => false,
    };

    private static bool IsValidationSuccess(
        Core.Services.WorkspaceValidationDto validation,
        TestRunResultDto? explicitTestRun)
    {
        if (!string.Equals(validation.OverallStatus, "clean", StringComparison.Ordinal))
        {
            return false;
        }

        return explicitTestRun is null || (explicitTestRun.Failed == 0 && explicitTestRun.Total > 0);
    }

    private static string CreateForkDirectory(string sourceRoot, string? forkName)
    {
        var forkRoot = Path.Combine(sourceRoot, ".roslynmcp", "forks");
        Directory.CreateDirectory(forkRoot);

        // workspace-fork-apply-security-hardening: lazily expire stale retained forks before
        // minting a new one so retention=keep no longer accumulates forks indefinitely.
        SweepExpiredForks(forkRoot);

        var slug = SanitizeForkName(forkName);
        var timestamp = DateTimeOffset.UtcNow.ToString(ForkTimestampFormat, CultureInfo.InvariantCulture);
        var forkPath = Path.Combine(forkRoot, $"{timestamp}-{slug}");
        Directory.CreateDirectory(forkPath);
        return forkPath;
    }

    /// <summary>
    /// workspace-fork-apply-security-hardening: TTL sweep for retained forks. Reads
    /// <c>ROSLYNMCP_FORK_TTL_HOURS</c> (default 24h) and deletes fork dirs whose
    /// <see cref="ForkTimestampFormat"/> name-prefix is older than the cutoff. Fork dirs with an
    /// unparseable prefix are kept (fail-safe: never delete something we cannot date). A
    /// non-positive TTL disables the sweep. Best-effort — a delete failure is swallowed so a
    /// locked stale fork never blocks a fresh fork-apply.
    /// </summary>
    internal static void SweepExpiredForks(string forkRoot)
        => SweepExpiredForks(forkRoot, ResolveForkTtlHours(), DateTimeOffset.UtcNow);

    internal static void SweepExpiredForks(string forkRoot, double ttlHours, DateTimeOffset now)
    {
        if (ttlHours <= 0 || !Directory.Exists(forkRoot))
        {
            return;
        }

        var cutoff = now - TimeSpan.FromHours(ttlHours);
        foreach (var dir in Directory.EnumerateDirectories(forkRoot))
        {
            var name = Path.GetFileName(dir);
            var dashIndex = name.IndexOf('-');
            var timestampPart = dashIndex > 0 ? name[..dashIndex] : name;

            if (!DateTimeOffset.TryParseExact(
                    timestampPart,
                    ForkTimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var created))
            {
                // Unparseable prefix — keep it. We only expire forks we can positively date.
                continue;
            }

            if (created < cutoff)
            {
                try
                {
                    DeleteDirectoryIfExists(dir, CancellationToken.None);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best-effort — a locked stale fork must not block minting a new one.
                }
            }
        }
    }

    internal static double ResolveForkTtlHours()
        => ReadPositiveEnvDouble("ROSLYNMCP_FORK_TTL_HOURS", DefaultForkTtlHours, allowZero: true);

    internal static double ResolveForkRestoreTimeoutMinutes()
        => ReadPositiveEnvDouble("ROSLYNMCP_FORK_RESTORE_TIMEOUT_MINUTES", DefaultForkRestoreTimeoutMinutes, allowZero: false);

    internal static string ResolveForkDotnetPath()
    {
        var configured = Environment.GetEnvironmentVariable("ROSLYNMCP_FORK_DOTNET_PATH");
        return string.IsNullOrWhiteSpace(configured) ? "dotnet" : configured.Trim();
    }

    private static double ReadPositiveEnvDouble(string variable, double fallback, bool allowZero)
    {
        var raw = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && (parsed > 0 || (allowZero && parsed == 0)))
        {
            return parsed;
        }

        return fallback;
    }

    private static string SanitizeForkName(string? forkName)
    {
        if (string.IsNullOrWhiteSpace(forkName))
        {
            return Guid.NewGuid().ToString("N")[..8];
        }

        var chars = forkName.Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        if (slug.Length == 0)
        {
            return Guid.NewGuid().ToString("N")[..8];
        }

        return slug.Length <= 40 ? slug : slug[..40];
    }

    internal static void CopyDirectory(string sourceDir, string destinationDir, CancellationToken ct)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            ct.ThrowIfCancellationRequested();

            // workspace-fork-apply-security-hardening: never copy secret-bearing files into a
            // server-writable fork dir. This is a denylist (known secret filename shapes), not an
            // allowlist — see IsSecretBearingFile for the documented limitation.
            if (IsSecretBearingFile(Path.GetFileName(file)))
            {
                continue;
            }

            var destination = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDir))
        {
            ct.ThrowIfCancellationRequested();
            if (DirectoryCopyExclusions.Contains(Path.GetFileName(directory)))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)), ct);
        }
    }

    /// <summary>
    /// workspace-fork-apply-security-hardening: true when <paramref name="fileName"/> matches a
    /// known secret-bearing filename shape that must not be copied into a server-writable fork
    /// dir. All comparisons are case-insensitive (<see cref="StringComparison.OrdinalIgnoreCase"/>).
    /// <para>
    /// KNOWN LIMITATION: this is a denylist of common secret-file shapes, not an allowlist. A
    /// project that stashes secrets in a non-matching filename (e.g. <c>config.local.yaml</c>,
    /// <c>credentials.txt</c>) will still have that file copied into the fork. The fork dir lives
    /// under the source root and is deleted by retention policy / TTL sweep, so exposure is
    /// bounded, but this predicate should be extended as new secret conventions surface.
    /// </para>
    /// Excluded shapes: <c>.env</c>, <c>.env.*</c>, <c>secrets.json</c>, <c>*.pubxml</c>,
    /// <c>*.pubxml.user</c>, <c>*.pfx</c>, <c>*.key</c>, and <c>appsettings.*.json</c> EXCEPT the
    /// non-secret base <c>appsettings.json</c> and <c>appsettings.Development.json</c>.
    /// </summary>
    internal static bool IsSecretBearingFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        if (fileName.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("secrets.json", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pubxml", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pubxml.user", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // appsettings.*.json — environment-specific settings often carry connection strings /
        // secrets. The base appsettings.json and the conventionally-non-secret
        // appsettings.Development.json are retained.
        if (fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return !fileName.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)
                && !fileName.Equals("appsettings.Development.json", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static async Task RestoreForkAsync(string forkLoadedPath, CancellationToken ct)
    {
        var workingDirectory = Path.GetDirectoryName(forkLoadedPath)
            ?? throw new InvalidOperationException($"Could not resolve working directory for fork '{forkLoadedPath}'.");

        // workspace-fork-apply-security-hardening: dotnet path + restore timeout are env-configurable
        // (ROSLYNMCP_FORK_DOTNET_PATH / ROSLYNMCP_FORK_RESTORE_TIMEOUT_MINUTES) so pinned-SDK and
        // slow-network environments are not stuck with a hardcoded "dotnet" on PATH and a fixed 2-min cap.
        var dotnetPath = ResolveForkDotnetPath();
        var timeoutMinutes = ResolveForkRestoreTimeoutMinutes();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));

        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetPath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Same defense as DotnetCommandRunner (dotnet-runner-nodereuse-pipe-hang): fresh
        // reusable MSBuild worker nodes inherit the redirected pipe handles and outlive the
        // restore by up to 15 minutes, so the stream reads below would otherwise wait for an
        // EOF that never comes and burn the whole restore timeout. A one-shot restore gains
        // nothing from node reuse.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(forkLoadedPath);
        startInfo.ArgumentList.Add("--nologo");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet restore for workspace fork.");
        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var stdoutTask = ReadRedirectedStreamAsync(process.StandardOutput, drainCts.Token);
        var stderrTask = ReadRedirectedStreamAsync(process.StandardError, drainCts.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Best-effort cleanup of the timed-out fork process tree. A Kill failure (e.g. the
            // process already exited, or a race on the process handle) is not actionable here —
            // we are already throwing TimeoutException — so the swallow is deliberate.
            try { process.Kill(entireProcessTree: true); } catch { /* see comment above — non-actionable on the timeout path */ }
            throw new TimeoutException(
                $"dotnet restore for workspace fork exceeded the {timeoutMinutes.ToString(CultureInfo.InvariantCulture)} minute timeout.");
        }

        // The restore has exited; only handle-inheriting descendants can still hold the pipe
        // open, and the child's own buffered output flushes within milliseconds — bound the
        // drain instead of waiting for their EOF.
        drainCts.CancelAfter(TimeSpan.FromSeconds(5));
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
            throw new InvalidOperationException($"dotnet restore for workspace fork failed with exit code {process.ExitCode}. {detail}");
        }
    }

    /// <summary>
    /// Reads a redirected stream chunk-wise, returning whatever arrived if the drain token
    /// fires — <see cref="StreamReader.ReadToEndAsync(CancellationToken)"/> would discard its
    /// buffered prefix on cancellation, losing the restore output the failure path reports.
    /// </summary>
    private static async Task<string> ReadRedirectedStreamAsync(StreamReader reader, CancellationToken drainCt)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), drainCt).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                builder.Append(buffer, 0, count);
            }
        }
        catch (OperationCanceledException)
        {
            // Drain expiry (or restore timeout, which WaitForExitAsync already converted to
            // TimeoutException on the caller's path): keep the output the child actually wrote.
        }

        return builder.ToString();
    }

    private static async Task<IReadOnlyList<string>> ReplayPreviewIntoForkAsync(
        Solution originalSolution,
        Solution modifiedSolution,
        string sourceRoot,
        string forkRoot,
        CancellationToken ct)
    {
        var changes = modifiedSolution.GetChanges(originalSolution);
        var appliedFiles = new List<string>();

        foreach (var projectChange in changes.GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                var document = modifiedSolution.GetDocument(documentId);
                if (document?.FilePath is null)
                {
                    continue;
                }

                var forkFilePath = MapSourcePathToFork(document.FilePath, sourceRoot, forkRoot);
                var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                await WriteForkFileAsync(forkFilePath, text, ct).ConfigureAwait(false);
                appliedFiles.Add(forkFilePath);
            }

            foreach (var documentId in projectChange.GetAddedDocuments())
            {
                var document = modifiedSolution.GetDocument(documentId);
                if (document?.FilePath is null)
                {
                    continue;
                }

                var forkFilePath = MapSourcePathToFork(document.FilePath, sourceRoot, forkRoot);
                var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
                await WriteForkFileAsync(forkFilePath, text, ct).ConfigureAwait(false);
                appliedFiles.Add(forkFilePath);
            }

            foreach (var documentId in projectChange.GetRemovedDocuments())
            {
                var document = originalSolution.GetDocument(documentId);
                if (document?.FilePath is null)
                {
                    continue;
                }

                var forkFilePath = MapSourcePathToFork(document.FilePath, sourceRoot, forkRoot);
                if (File.Exists(forkFilePath))
                {
                    File.Delete(forkFilePath);
                }
                appliedFiles.Add(forkFilePath);
            }
        }

        return appliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task WriteForkFileAsync(string path, string text, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, text, ct).ConfigureAwait(false);
    }

    private static string MapSourcePathToFork(string sourcePath, string sourceRoot, string forkRoot)
    {
        var relative = Path.GetRelativePath(sourceRoot, Path.GetFullPath(sourcePath));
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                $"Preview path '{sourcePath}' is outside the source workspace root '{sourceRoot}'.");
        }

        return Path.GetFullPath(Path.Combine(forkRoot, relative));
    }

    private static void CleanupFork(
        IWorkspaceManager workspaceManager,
        string? forkWorkspaceId,
        string forkPath,
        List<string> cleanupWarnings,
        CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(forkWorkspaceId))
            {
                workspaceManager.Close(forkWorkspaceId);
            }
        }
        catch (Exception ex)
        {
            cleanupWarnings.Add($"Failed to close fork workspace '{forkWorkspaceId}': {ex.Message}");
        }

        try
        {
            DeleteDirectoryIfExists(forkPath, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            cleanupWarnings.Add($"Failed to delete fork directory '{forkPath}': {ex.Message}");
        }
    }

    private static void DeleteDirectoryIfExists(string path, CancellationToken ct)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}
