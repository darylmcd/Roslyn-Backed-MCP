using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class TestCoverageTools
{

    /// <remarks>
    /// Response shape is the <see cref="TestCoverageResultDto"/> fields (success, error,
    /// lineCoveragePercent, branchCoveragePercent, modules, failureEnvelope) with an additional
    /// top-level <c>deprecation</c> field — null on this canonical tool, populated on aliases
    /// (e.g. <c>get_test_coverage_map</c>).
    /// </remarks>
    [McpServerTool(Name = "test_coverage", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("validation", "stable", false, false,
        "Run coverage collection for test execution."),
     Description("Run tests with code coverage collection and return coverage metrics per module and class. Requires the coverlet.collector NuGet package in test projects.")]
    public static Task<string> RunTestCoverage(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IGatedCommandExecutor commandExecutor,
        ValidationServiceOptions options,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: specific test project name")] string? projectName = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return RunTestCoverageCore(
            gate, workspace, commandExecutor, options, workspaceId, projectName, deprecation: null,
            progress, ct);
    }

    // roslyn-mcp-sister-tool-name-aliases: shared core invoked by both the canonical
    // `test_coverage` tool and the `get_test_coverage_map` alias. Wraps the
    // <see cref="TestCoverageResultDto"/> in an anonymous envelope so the same JSON shape
    // can carry the `deprecation` field on every emit path (success, coverlet-missing
    // short-circuit, post-run no-coverage-file fallback).
    /// <remarks>
    /// Three phases so the workspace gate is NOT held across the long <c>dotnet test</c>: (1) a
    /// gated read resolves workspace status, the coverlet partition and the coverage directory,
    /// (2) the coverage commands run ungated through <see cref="IGatedCommandExecutor"/> under one
    /// <see cref="ValidationServiceOptions.TestTimeout"/> budget shared by every project (its own
    /// command gates serialize them, and the budget includes queue wait), (3) the coverage files are
    /// parsed without any gate. Holding the gate for all three armed the 2-minute request timeout
    /// before the tests started, so it beat the 10-minute test budget while pinning a throttle slot
    /// and the reader lock.
    /// </remarks>
    internal static async Task<string> RunTestCoverageCore(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IGatedCommandExecutor commandExecutor,
        ValidationServiceOptions options,
        string workspaceId,
        string? projectName,
        ToolAliasDeprecation? deprecation,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        // test-coverage-timeout-failure-envelope: wrap the command invocation and the
        // downstream coverage-file scan so that a command-budget or gate timeout is reported as a
        // structured Timeout failureEnvelope. Any other exception propagates to the shared
        // StructuredCallToolFilter, which reports it and returns isError=true with _meta
        // (test-coverage-unexpected-error-not-iserror).
        try
        {
            ProgressHelper.Report(progress, 0, 1);
            var prepared = await gate.RunReadAsync(
                workspaceId, c => PrepareCoverageRunAsync(workspace, workspaceId, projectName, c), ct).ConfigureAwait(false);

            if (prepared.EarlyResult is { } earlyResult)
            {
                ProgressHelper.Report(progress, 1, 1);
                return SerializeWithDeprecation(earlyResult, deprecation);
            }

            var plan = prepared.Plan!;

            // test-coverage-temp-dir-leak (workspace-fork-apply-security-hardening): the temp
            // coverage results dir is allocated per run and was previously never deleted, leaking a
            // directory into %TEMP% on every invocation. Wrap the whole coverage lifecycle so the
            // dir is removed after aggregation regardless of which return path (no-coverage-file,
            // aggregated) fires. Best-effort delete — a lock on a coverage file must not turn a
            // successful coverage run into an error.
            try
            {
                var (execution, coverageFiles, coverageGaps) = await RunCoveragePassAsync(
                    commandExecutor, options, workspaceId, plan, projectName, progress, ct).ConfigureAwait(false);

                if (coverageFiles.Length == 0)
                {
                    ProgressHelper.Report(progress, 1, 1);
                    return SerializeWithDeprecation(
                        TestCoverageCoordinator.BuildNoCoverageFileResult(execution.Succeeded, execution.ExitCode, coverageGaps),
                        deprecation);
                }

                var result = TestCoverageCoordinator.ParseAndAggregateCoberturaXml(coverageFiles, coverageGaps);
                ProgressHelper.Report(progress, 1, 1);
                return SerializeWithDeprecation(result, deprecation);
            }
            finally
            {
                TryDeleteCoverageDir(plan.CoverageDir);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Genuine caller cancellation (not the command budget or the gate's internal timeout)
            // must propagate as OperationCanceledException rather than being misreported as a
            // timeout envelope — mirrors the split pattern in
            // ValidationBundleTools.RestoreForkAsync.
            throw;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            // Timeout branch stays a structured Timeout envelope (success=false, non-retryable),
            // consistent with test_run keeping its timeout result (TestRunnerService), instead of
            // an isError frame. GatedCommandExecutor and WorkspaceExecutionGate reclassify their
            // internal timeout CTS into TimeoutException (not OCE), so TimeoutException is the real
            // production shape; a non-caller OCE is kept for a gate-internal cancel that was not
            // reclassified. Ordered after the caller-cancellation rethrow and before the generic
            // catch below so neither is rethrown as an unexpected failure.
            ProgressHelper.Report(progress, 1, 1);
            return SerializeWithDeprecation(TestCoverageCoordinator.BuildTimeoutResult(), deprecation);
        }
        catch
        {
            // Every OperationCanceledException is handled by the arms above, so this arm only
            // sees unexpected failures: close the progress sequence, then let the shared filter
            // format the failure (isError=true, schemaHint, _meta) - mirrors ValidationTools.
            ProgressHelper.Report(progress, 1, 1);
            throw;
        }
    }

    /// <summary>
    /// Gated-read phase: resolves the workspace status and coverlet partition and allocates the
    /// coverage directory path (the directory itself is created by <c>dotnet test</c>). Returns the
    /// coverlet-missing result directly when no selected test project can collect coverage.
    /// </summary>
    private static async Task<PreparedCoverageRun> PrepareCoverageRunAsync(
        IWorkspaceManager workspace,
        string workspaceId,
        string? projectName,
        CancellationToken ct)
    {
        var status = await workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
        var loadedPath = status.LoadedPath ?? throw new InvalidOperationException("Workspace has no loaded path.");
        var partition = TestCoverageCoordinator.PartitionTestProjectsByCoverlet(status, projectName);
        if (partition.WithCoverlet.Count == 0 && partition.WithoutCoverlet.Count > 0)
        {
            return new PreparedCoverageRun(
                Plan: null,
                EarlyResult: TestCoverageCoordinator.BuildCoverletMissingResult(partition.WithoutCoverlet));
        }

        var coverageDir = Path.Combine(Path.GetTempPath(), "roslyn-mcp-coverage", Guid.NewGuid().ToString("N"));
        return new PreparedCoverageRun(
            new CoverageRunPlan(status, partition, loadedPath, coverageDir),
            EarlyResult: null);
    }

    private sealed record CoverageRunPlan(
        WorkspaceStatusDto Status,
        TestCoverageCoordinator.TestProjectPartition Partition,
        string LoadedPath,
        string CoverageDir);

    private sealed record PreparedCoverageRun(CoverageRunPlan? Plan, TestCoverageResultDto? EarlyResult);

    private static async Task<(CommandExecutionDto execution, string[] coverageFiles, IReadOnlyList<string>? coverageGaps)>
        RunCoveragePassAsync(
            IGatedCommandExecutor commandExecutor,
            ValidationServiceOptions options,
            string workspaceId,
            CoverageRunPlan plan,
            string? projectName,
            IProgress<ProgressNotificationValue>? progress,
            CancellationToken ct)
    {
        var (status, partition, loadedPath, coverageDir) = plan;

        // One TestTimeout budget covers every project's dotnet test (queue wait included): each
        // command gets the time that is left. An exhausted budget floors at zero, which the
        // command executor reports as a timeout without starting the process.
        var budget = Stopwatch.StartNew();
        TimeSpan RemainingBudget() =>
            TimeSpan.FromTicks(Math.Max(0, (options.TestTimeout - budget.Elapsed).Ticks));

        // test-coverage-fail-fast-on-missing-coverlet: choose between the
        // single-target (whole-solution or single-project) classic path and the
        // partial-coverage per-project path. The latter triggers only when the
        // partition produced both with-coverlet AND without-coverlet entries —
        // otherwise the classic path's `dotnet test` invocation against the
        // solution / single project is cheaper and produces a single Cobertura
        // file we can parse directly.
        if (partition.WithoutCoverlet.Count == 0)
        {
            var targetPath = projectName is not null
                ? status.Projects.FirstOrDefault(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase))?.FilePath ?? loadedPath
                : loadedPath;
            var arguments = new List<string>
            {
                "test", targetPath, "--collect", "XPlat Code Coverage", "--results-directory", coverageDir
            };
            var execution = await commandExecutor.ExecuteAsync(
                workspaceId, targetPath, arguments, RemainingBudget(), ct).ConfigureAwait(false);
            ProgressHelper.Report(progress, 0.8f, 1);

            var coverageFiles = Directory.Exists(coverageDir)
                ? Directory.GetFiles(coverageDir, "coverage.cobertura.xml", SearchOption.AllDirectories)
                : [];
            return (execution, coverageFiles, coverageGaps: null);
        }

        // Partial-coverage path — some projects lack coverlet. Run per-project
        // sequentially on the projects that DO have it, aggregating coverage XML
        // files from a shared results directory. We synthesize a single
        // CommandExecutionDto whose Succeeded reflects whether ANY per-project run
        // failed and whose ExitCode is the LAST non-zero exit code (so the
        // no-coverage-file fallback still has a meaningful exit code to surface).
        CommandExecutionDto? lastExecution = null;
        var perProjectFailures = 0;
        var totalProjects = partition.WithCoverlet.Count;
        for (var i = 0; i < totalProjects; i++)
        {
            var project = partition.WithCoverlet[i];
            var arguments = new List<string>
            {
                "test", project.FilePath, "--collect", "XPlat Code Coverage", "--results-directory", coverageDir
            };
            var perProjectExecution = await commandExecutor.ExecuteAsync(
                workspaceId, project.FilePath, arguments, RemainingBudget(), ct).ConfigureAwait(false);
            lastExecution = perProjectExecution;
            if (!perProjectExecution.Succeeded)
                perProjectFailures++;
            ProgressHelper.Report(progress, 0.8f * (i + 1) / totalProjects, 1);
        }

        // Synthesize a representative execution result. If we had at least one
        // success the synthesized Succeeded=true so the no-coverage-file fallback
        // can correctly classify a missing-XML state as CoverletMissing rather
        // than TestFailure. (`perProjectFailures < totalProjects` keeps the prior
        // semantics where a partial test failure still surfaces in the no-
        // coverage-file fallback path via the exit code.)
        var partialExecution = lastExecution ?? new CommandExecutionDto(
            Command: "dotnet",
            Arguments: [],
            WorkingDirectory: Path.GetDirectoryName(loadedPath) ?? string.Empty,
            TargetPath: loadedPath,
            ExitCode: 0,
            Succeeded: true,
            DurationMs: 0,
            StdOut: string.Empty,
            StdErr: string.Empty);
        if (perProjectFailures > 0 && perProjectFailures == totalProjects)
        {
            partialExecution = partialExecution with { Succeeded = false };
        }

        var partialCoverageFiles = Directory.Exists(coverageDir)
            ? Directory.GetFiles(coverageDir, "coverage.cobertura.xml", SearchOption.AllDirectories)
            : [];

        return (partialExecution, partialCoverageFiles, partition.WithoutCoverlet);
    }

    // roslyn-mcp-sister-tool-name-aliases: thin alias for callers carrying the python-refactor
    // (Jedi) tool name `get_test_coverage_map`. Delegates to the canonical `test_coverage`
    // implementation and surfaces the migration path inline via the `deprecation` envelope.
    /// <remarks>
    /// Returns the canonical <c>test_coverage</c> response envelope with
    /// <c>deprecation.canonicalName</c> populated.
    /// </remarks>
    [McpServerTool(Name = "get_test_coverage_map", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("validation", "stable", false, false,
        "Alias for test_coverage (cross-MCP-server name compatibility)."),
     Description("Alias for `test_coverage` (cross-MCP-server name compatibility — matches the python-refactor tool name). Prefer `test_coverage` directly in new code.")]
    public static Task<string> GetTestCoverageMap(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IGatedCommandExecutor commandExecutor,
        ValidationServiceOptions options,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: specific test project name")] string? projectName = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return RunTestCoverageCore(
            gate,
            workspace,
            commandExecutor,
            options,
            workspaceId,
            projectName,
            ToolAliasDeprecation.ForSisterAlias("test_coverage"),
            progress,
            ct);
    }

    // roslyn-mcp-sister-tool-name-aliases: project the TestCoverageResultDto's record fields
    // onto an anonymous envelope so we can splice the top-level `deprecation` field without
    // mutating the DTO record (which is part of the Core models surface). Field names are
    // emitted lower-camel-cased to match the rest of the JSON the tool emits via JsonDefaults.
    // Stays in the tool class because `JsonDefaults` is internal to RoslynMcp.Host.Stdio.
    private static string SerializeWithDeprecation(TestCoverageResultDto result, ToolAliasDeprecation? deprecation)
    {
        return JsonSerializer.Serialize(new
        {
            success = result.Success,
            error = result.Error,
            lineCoveragePercent = result.LineCoveragePercent,
            branchCoveragePercent = result.BranchCoveragePercent,
            modules = result.Modules,
            failureEnvelope = result.FailureEnvelope,
            coverageGaps = result.CoverageGaps,
            deprecation,
        }, JsonDefaults.Indented);
    }

    // test-coverage-temp-dir-leak (workspace-fork-apply-security-hardening): best-effort deletion
    // of the per-run temp coverage results dir. Internal for direct assertion in tests. Swallows
    // IO/access failures — the dir is under %TEMP% and a leftover on a locked file is non-fatal.
    internal static void TryDeleteCoverageDir(string coverageDir)
    {
        try
        {
            if (Directory.Exists(coverageDir))
            {
                Directory.Delete(coverageDir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort — a locked coverage file must not fail an otherwise-successful run.
        }
    }
}
