using System.ComponentModel;
using System.Text.Json;
using System.Xml.Linq;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
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

                // test-coverage-fail-fast-on-missing-coverlet: partition the in-scope test projects
                // into those with and without coverlet.collector. Pre-fix the tool fail-fasted on
                // the first missing collector and abandoned the whole call; now we run partial
                // coverage on the projects that DO have the collector and list the skipped ones in
                // the response's `coverageGaps` field. Fail-fast remains for the all-projects-lack
                // case (no useful work to do).
                var partition = PartitionTestProjectsByCoverlet(status, projectName);
                if (partition.WithCoverlet.Count == 0 && partition.WithoutCoverlet.Count > 0)
                {
                    ProgressHelper.Report(progress, 1, 1);
                    var summary = $"Coverlet missing: {partition.WithoutCoverlet.Count} test project(s) don't reference coverlet.collector. " +
                        $"Install via `dotnet add package coverlet.collector` in: {string.Join(", ", partition.WithoutCoverlet)}.";
                    return SerializeWithDeprecation(new TestCoverageResultDto(
                        Success: false,
                        Error: summary,
                        LineCoveragePercent: null,
                        BranchCoveragePercent: null,
                        Modules: [],
                        FailureEnvelope: new TestCoverageFailureEnvelopeDto(
                            ErrorKind: "CoverletMissing",
                            IsRetryable: false,
                            Summary: summary,
                            MissingPackages: partition.WithoutCoverlet)), deprecation);
                }

                // test-coverage-fail-fast-on-missing-coverlet: choose between the
                // single-target (whole-solution or single-project) classic path and the
                // partial-coverage per-project path. The latter triggers only when the
                // partition produced both with-coverlet AND without-coverlet entries —
                // otherwise the classic path's `dotnet test` invocation against the
                // solution / single project is cheaper and produces a single Cobertura
                // file we can parse directly.
                CommandExecutionDto execution;
                string[] coverageFiles;
                IReadOnlyList<string>? coverageGaps = null;

                if (partition.WithoutCoverlet.Count == 0)
                {
                    // Classic path — every in-scope project has coverlet. Run once against the
                    // original `targetPath` (loadedPath OR a single-project filter).
                    var targetPath = projectName is not null
                        ? status.Projects.FirstOrDefault(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase))?.FilePath ?? loadedPath
                        : loadedPath;
                    var arguments = new List<string>
                    {
                        "test",
                        targetPath,
                        "--collect",
                        "XPlat Code Coverage",
                        "--results-directory",
                        coverageDir
                    };
                    execution = await commandRunner.RunAsync(Path.GetDirectoryName(loadedPath)!, targetPath, arguments, c).ConfigureAwait(false);
                    ProgressHelper.Report(progress, 0.8f, 1);

                    coverageFiles = Directory.Exists(coverageDir)
                        ? Directory.GetFiles(coverageDir, "coverage.cobertura.xml", SearchOption.AllDirectories)
                        : [];
                }
                else
                {
                    // Partial-coverage path — some projects lack coverlet. Run per-project
                    // sequentially on the projects that DO have it, aggregating coverage XML
                    // files from a shared results directory. We synthesize a single
                    // CommandExecutionDto whose Succeeded reflects whether ANY per-project run
                    // failed and whose ExitCode is the LAST non-zero exit code (so the
                    // no-coverage-file fallback still has a meaningful exit code to surface).
                    var lastExecution = (CommandExecutionDto?)null;
                    var perProjectFailures = 0;
                    var totalProjects = partition.WithCoverlet.Count;
                    for (var i = 0; i < totalProjects; i++)
                    {
                        var project = partition.WithCoverlet[i];
                        var arguments = new List<string>
                        {
                            "test",
                            project.FilePath,
                            "--collect",
                            "XPlat Code Coverage",
                            "--results-directory",
                            coverageDir
                        };
                        var perProjectExecution = await commandRunner.RunAsync(
                            Path.GetDirectoryName(loadedPath)!,
                            project.FilePath,
                            arguments,
                            c).ConfigureAwait(false);
                        lastExecution = perProjectExecution;
                        if (!perProjectExecution.Succeeded)
                            perProjectFailures++;
                        // Progress from 0 to 0.8 across the per-project iterations so the
                        // post-loop coverage-file scan can advance to 1.0.
                        ProgressHelper.Report(progress, 0.8f * (i + 1) / totalProjects, 1);
                    }

                    // Synthesize a representative execution result. If we had at least one
                    // success the synthesized Succeeded=true so the no-coverage-file fallback
                    // can correctly classify a missing-XML state as CoverletMissing rather
                    // than TestFailure. (`perProjectFailures < totalProjects` keeps the prior
                    // semantics where a partial test failure still surfaces in the no-
                    // coverage-file fallback path via the exit code.)
                    execution = lastExecution ?? new CommandExecutionDto(
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
                        // Force Succeeded=false on the synthesized result when every per-project
                        // run failed — same shape as the classic-path's `!execution.Succeeded`
                        // branch in the no-coverage-file fallback below.
                        execution = execution with { Succeeded = false };
                    }

                    coverageFiles = Directory.Exists(coverageDir)
                        ? Directory.GetFiles(coverageDir, "coverage.cobertura.xml", SearchOption.AllDirectories)
                        : [];

                    coverageGaps = partition.WithoutCoverlet;
                }

                if (coverageFiles.Length == 0)
                {
                    ProgressHelper.Report(progress, 1, 1);
                    var errorKind = !execution.Succeeded ? "TestFailure" : "CoverletMissing";
                    var summary = !execution.Succeeded
                        ? $"Tests failed (exit code {execution.ExitCode}). Coverage file not found."
                        : "Coverage file not generated. Ensure coverlet.collector NuGet package is referenced in test projects.";
                    return SerializeWithDeprecation(new TestCoverageResultDto(
                        Success: false,
                        Error: summary,
                        LineCoveragePercent: null,
                        BranchCoveragePercent: null,
                        Modules: [],
                        FailureEnvelope: new TestCoverageFailureEnvelopeDto(
                            ErrorKind: errorKind,
                            IsRetryable: errorKind == "TestFailure",
                            Summary: summary),
                        CoverageGaps: coverageGaps), deprecation);
                }

                // test-coverage-fail-fast-on-missing-coverlet: aggregate ALL coverage XML files
                // from the run, not just the newest. In the classic path there is one file (the
                // OrderByDescending picks the latest of any incidentally-present older files);
                // in the partial path there is one file per with-coverlet project and we want
                // their module entries merged into a single TestCoverageResultDto.
                var result = ParseAndAggregateCoberturaXml(coverageFiles, coverageGaps);
                ProgressHelper.Report(progress, 1, 1);
                return SerializeWithDeprecation(result, deprecation);
            }
            catch (OperationCanceledException)
            {
                // OCE-first ordering: if the generic Exception catch is ordered first the
                // cancellation path would be misclassified as Unknown. Do NOT re-throw —
                // the gate lambda's return type is string and the caller expects a structured
                // envelope identical to the other failure paths above.
                ProgressHelper.Report(progress, 1, 1);
                const string cancelSummary = "test_coverage was cancelled (timeout or caller cancellation).";
                return SerializeWithDeprecation(new TestCoverageResultDto(
                    Success: false,
                    Error: cancelSummary,
                    LineCoveragePercent: null,
                    BranchCoveragePercent: null,
                    Modules: [],
                    FailureEnvelope: new TestCoverageFailureEnvelopeDto(
                        ErrorKind: "Timeout",
                        IsRetryable: false,
                        Summary: cancelSummary)), deprecation);
            }
            catch (Exception ex)
            {
                ProgressHelper.Report(progress, 1, 1);
                var summary = $"test_coverage failed with an unexpected error: {ex.Message}";
                return SerializeWithDeprecation(new TestCoverageResultDto(
                    Success: false,
                    Error: summary,
                    LineCoveragePercent: null,
                    BranchCoveragePercent: null,
                    Modules: [],
                    FailureEnvelope: new TestCoverageFailureEnvelopeDto(
                        ErrorKind: "Unknown",
                        IsRetryable: false,
                        Summary: summary)), deprecation);
            }
        }, ct);
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

    /// <summary>
    /// test-coverage-fail-fast-on-missing-coverlet: result of bucketing the in-scope test
    /// projects by whether they reference <c>coverlet.collector</c>. The mixed case
    /// (<c>WithCoverlet</c> non-empty AND <c>WithoutCoverlet</c> non-empty) is the new
    /// partial-coverage path; the all-without-coverlet case still fail-fasts; the
    /// all-with-coverlet case is the classic single-`dotnet test` invocation.
    /// </summary>
    private sealed record TestProjectPartition(
        IReadOnlyList<ProjectStatusDto> WithCoverlet,
        IReadOnlyList<string> WithoutCoverlet);

    /// <summary>
    /// test-coverage-fail-fast-on-missing-coverlet (formerly
    /// <c>FindTestProjectsWithoutCoverlet</c>): split the in-scope test projects into those
    /// that reference <c>coverlet.collector</c> and those that do not. Replaces the previous
    /// helper that only returned the missing-coverlet names; the caller now needs both halves
    /// to make the fail-fast-vs-partial decision.
    /// </summary>
    private static TestProjectPartition PartitionTestProjectsByCoverlet(WorkspaceStatusDto status, string? projectName)
    {
        var candidates = status.Projects.Where(p =>
        {
            if (!p.IsTestProject) return false;
            if (projectName is null) return true;
            return string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        var withCoverlet = new List<ProjectStatusDto>();
        var withoutCoverlet = new List<string>();
        foreach (var project in candidates)
        {
            if (string.IsNullOrWhiteSpace(project.FilePath) || !File.Exists(project.FilePath))
                continue;

            var text = File.ReadAllText(project.FilePath);
            if (text.Contains("coverlet.collector", StringComparison.OrdinalIgnoreCase))
                withCoverlet.Add(project);
            else
                withoutCoverlet.Add(project.Name);
        }

        return new TestProjectPartition(withCoverlet, withoutCoverlet);
    }

    /// <summary>
    /// test-coverage-fail-fast-on-missing-coverlet: parse one or more Cobertura XML files and
    /// produce a single aggregated <see cref="TestCoverageResultDto"/>. With a single file the
    /// rolled-up rates are read from the document root; with multiple files (the partial-
    /// coverage per-project path) we sum lines across all modules and recompute the rolled-up
    /// rates from the line totals so the aggregate stays consistent with the per-module data.
    /// Branch coverage is averaged across files (weighted by lines), matching how Cobertura
    /// reporters typically treat multi-run merges.
    /// </summary>
    private static TestCoverageResultDto ParseAndAggregateCoberturaXml(IReadOnlyList<string> paths, IReadOnlyList<string>? coverageGaps)
    {
        var modules = new List<ModuleCoverageDto>();
        var perFileLineRates = new List<(double rate, int lines)>();
        var perFileBranchRates = new List<(double rate, int lines)>();

        foreach (var path in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var doc = XDocument.Load(path);
            var coverage = doc.Root!;
            var fileLineRate = double.TryParse(coverage.Attribute("line-rate")?.Value, out var lr) ? lr : (double?)null;
            var fileBranchRate = double.TryParse(coverage.Attribute("branch-rate")?.Value, out var br) ? br : (double?)null;

            foreach (var package in coverage.Descendants("package"))
            {
                var moduleName = package.Attribute("name")?.Value ?? "unknown";
                var moduleLineRate = double.TryParse(package.Attribute("line-rate")?.Value, out var mlr) ? mlr * 100 : 0.0;

                var classes = new List<ClassCoverageDto>();
                foreach (var cls in package.Descendants("class"))
                {
                    var className = cls.Attribute("name")?.Value ?? "unknown";
                    var clsLineRate = double.TryParse(cls.Attribute("line-rate")?.Value, out var clr) ? clr * 100 : 0.0;
                    var lines = cls.Descendants("line").ToList();
                    var linesCovered = lines.Count(l => int.TryParse(l.Attribute("hits")?.Value, out var h) && h > 0);
                    classes.Add(new ClassCoverageDto(className, Math.Round(clsLineRate, 1), linesCovered, lines.Count));
                }

                var totalLines = classes.Sum(c => c.LinesTotal);
                var totalCovered = classes.Sum(c => c.LinesCovered);
                modules.Add(new ModuleCoverageDto(moduleName, Math.Round(moduleLineRate, 1), totalCovered, totalLines, classes));
            }

            // Record per-file rates with a line-count weight so the rolled-up rate respects
            // module size when aggregating across multiple Cobertura outputs.
            var fileTotalLines = coverage.Descendants("class").SelectMany(c => c.Descendants("line")).Count();
            if (fileLineRate.HasValue)
                perFileLineRates.Add((fileLineRate.Value, fileTotalLines));
            if (fileBranchRate.HasValue)
                perFileBranchRates.Add((fileBranchRate.Value, fileTotalLines));
        }

        // Compute the rolled-up rates. With one file we just lift the root attributes; with
        // multiple we weight by line count so a tiny test-project run can't unfairly skew the
        // aggregate against a much larger one.
        double? rolledLineRate;
        double? rolledBranchRate;
        if (paths.Count == 1)
        {
            var doc = XDocument.Load(paths[0]);
            var coverage = doc.Root!;
            rolledLineRate = double.TryParse(coverage.Attribute("line-rate")?.Value, out var lr) ? lr * 100 : (double?)null;
            rolledBranchRate = double.TryParse(coverage.Attribute("branch-rate")?.Value, out var br) ? br * 100 : (double?)null;
        }
        else
        {
            rolledLineRate = WeightedAverage(perFileLineRates) is { } lr ? lr * 100 : (double?)null;
            rolledBranchRate = WeightedAverage(perFileBranchRates) is { } br ? br * 100 : (double?)null;
        }

        return new TestCoverageResultDto(
            Success: true,
            Error: null,
            LineCoveragePercent: rolledLineRate.HasValue ? Math.Round(rolledLineRate.Value, 1) : null,
            BranchCoveragePercent: rolledBranchRate.HasValue ? Math.Round(rolledBranchRate.Value, 1) : null,
            Modules: modules,
            FailureEnvelope: null,
            CoverageGaps: coverageGaps);
    }

    private static double? WeightedAverage(List<(double rate, int lines)> samples)
    {
        if (samples.Count == 0) return null;
        var totalLines = samples.Sum(s => s.lines);
        if (totalLines == 0)
        {
            // Fall back to a straight average when no line counts are available so we still
            // return a meaningful number instead of null on degenerate empty-class files.
            return samples.Average(s => s.rate);
        }
        return samples.Sum(s => s.rate * s.lines) / totalLines;
    }
}
