using System.ComponentModel;
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

    [McpServerTool(Name = "test_coverage", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("validation", "stable", false, false,
        "Run coverage collection for test execution."),
     Description("Run tests with code coverage collection and return coverage metrics per module and class. Requires coverlet.collector NuGet package in test projects. Response shape is the TestCoverageResultDto fields (success, error, lineCoveragePercent, branchCoveragePercent, modules, failureEnvelope) with an additional top-level `deprecation` field — null on the canonical tool, populated on aliases (e.g. get_test_coverage_map).")]
    public static Task<string> RunTestCoverage(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IDotnetCommandRunner commandRunner,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: specific test project name")] string? projectName = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return RunTestCoverageCore(gate, workspace, commandRunner, workspaceId, projectName, deprecation: null, progress, ct);
    }

    // roslyn-mcp-sister-tool-name-aliases: shared core invoked by both the canonical
    // `test_coverage` tool and the `get_test_coverage_map` alias. Wraps the
    // <see cref="TestCoverageResultDto"/> in an anonymous envelope so the same JSON shape
    // can carry the `deprecation` field on every emit path (success, coverlet-missing
    // short-circuit, post-run no-coverage-file fallback).
    internal static Task<string> RunTestCoverageCore(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IDotnetCommandRunner commandRunner,
        string workspaceId,
        string? projectName,
        ToolAliasDeprecation? deprecation,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            // test-coverage-timeout-failure-envelope: wrap the runner invocation and the
            // downstream coverage-file scan so that cancellation (MCP timeout, caller cancel),
            // status/partition failures, or an unexpected runner exception is reported as a
            // structured failureEnvelope rather than escaping the gate lambda as a bare
            // invocation error.
            try
            {
                ProgressHelper.Report(progress, 0, 1);
                var status = await workspace.GetStatusAsync(workspaceId, c).ConfigureAwait(false);
                var loadedPath = status.LoadedPath ?? throw new InvalidOperationException("Workspace has no loaded path.");

                var coverageDir = Path.Combine(Path.GetTempPath(), "roslyn-mcp-coverage", Guid.NewGuid().ToString("N"));

                // test-coverage-temp-dir-leak (workspace-fork-apply-security-hardening): the temp
                // coverage results dir is allocated per run and was previously never deleted, leaking a
                // directory into %TEMP% on every invocation. Wrap the whole coverage lifecycle so the
                // dir is removed after aggregation regardless of which return path (coverlet-missing,
                // no-coverage-file, aggregated) fires. Best-effort delete — a lock on a coverage file
                // must not turn a successful coverage run into an error.
                try
                {
                    var partition = TestCoverageCoordinator.PartitionTestProjectsByCoverlet(status, projectName);
                    if (partition.WithCoverlet.Count == 0 && partition.WithoutCoverlet.Count > 0)
                    {
                        ProgressHelper.Report(progress, 1, 1);
                        return SerializeWithDeprecation(
                            TestCoverageCoordinator.BuildCoverletMissingResult(partition.WithoutCoverlet),
                            deprecation);
                    }

                    var (execution, coverageFiles, coverageGaps) = await RunCoveragePassAsync(
                        commandRunner, status, partition, loadedPath, projectName, coverageDir, progress, c).ConfigureAwait(false);

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
                    TryDeleteCoverageDir(coverageDir);
                }
            }
            catch (OperationCanceledException) when (c.IsCancellationRequested)
            {
                // Genuine caller cancellation (not the gate's internal timeout) must propagate
                // as OperationCanceledException rather than being misreported as a timeout
                // envelope — mirrors the split pattern in
                // ValidationBundleTools.RestoreForkAsync.
                throw;
            }
            catch (OperationCanceledException)
            {
                // OCE-first ordering: if the generic Exception catch is ordered first the
                // cancellation path would be misclassified as Unknown. Do NOT re-throw —
                // the gate lambda's return type is string and the caller expects a structured
                // envelope identical to the other failure paths above.
                ProgressHelper.Report(progress, 1, 1);
                return SerializeWithDeprecation(TestCoverageCoordinator.BuildTimeoutResult(), deprecation);
            }
            catch (Exception ex)
            {
                ProgressHelper.Report(progress, 1, 1);
                return SerializeWithDeprecation(TestCoverageCoordinator.BuildUnexpectedErrorResult(ex.Message), deprecation);
            }
        }, ct);
    }

    private static async Task<(CommandExecutionDto execution, string[] coverageFiles, IReadOnlyList<string>? coverageGaps)>
        RunCoveragePassAsync(
            IDotnetCommandRunner commandRunner,
            WorkspaceStatusDto status,
            TestCoverageCoordinator.TestProjectPartition partition,
            string loadedPath,
            string? projectName,
            string coverageDir,
            IProgress<ProgressNotificationValue>? progress,
            CancellationToken ct)
    {
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
            var execution = await commandRunner.RunAsync(Path.GetDirectoryName(loadedPath)!, targetPath, arguments, ct).ConfigureAwait(false);
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
            var perProjectExecution = await commandRunner.RunAsync(
                Path.GetDirectoryName(loadedPath)!, project.FilePath, arguments, ct).ConfigureAwait(false);
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
    [McpServerTool(Name = "get_test_coverage_map", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("validation", "stable", false, false,
        "Alias for test_coverage (cross-MCP-server name compatibility)."),
     Description("Alias for `test_coverage` (cross-MCP-server name compatibility — matches the python-refactor tool name). Returns the canonical test_coverage response envelope with deprecation.canonicalName populated. Prefer `test_coverage` directly in new code.")]
    public static Task<string> GetTestCoverageMap(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IDotnetCommandRunner commandRunner,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: specific test project name")] string? projectName = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return RunTestCoverageCore(
            gate,
            workspace,
            commandRunner,
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
