using System.Xml.Linq;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Stateless helpers that pre- and post-process a test-coverage run for
/// <c>RoslynMcp.Host.Stdio.Tools.TestCoverageTools</c>:
///
/// <list type="bullet">
///   <item>Partitioning in-scope test projects by whether they reference
///         <c>coverlet.collector</c>.</item>
///   <item>Parsing one or more Cobertura XML files into the rolled-up
///         <see cref="TestCoverageResultDto"/>.</item>
///   <item>Constructing the recurring failure envelopes (coverlet-missing,
///         no-coverage-file, timeout, unexpected error).</item>
/// </list>
///
/// All members are pure (modulo Cobertura XML file reads); none of them
/// touch the workspace gate, the workspace manager, or
/// <c>IDotnetCommandRunner</c> — those stay in the orchestrator.
/// </summary>
public static class TestCoverageCoordinator
{
    public sealed record TestProjectPartition(
        IReadOnlyList<ProjectStatusDto> WithCoverlet,
        IReadOnlyList<string> WithoutCoverlet);

    public static TestProjectPartition PartitionTestProjectsByCoverlet(WorkspaceStatusDto status, string? projectName)
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

    public static TestCoverageResultDto ParseAndAggregateCoberturaXml(
        IReadOnlyList<string> paths,
        IReadOnlyList<string>? coverageGaps)
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

            var fileTotalLines = coverage.Descendants("class").SelectMany(c => c.Descendants("line")).Count();
            if (fileLineRate.HasValue)
                perFileLineRates.Add((fileLineRate.Value, fileTotalLines));
            if (fileBranchRate.HasValue)
                perFileBranchRates.Add((fileBranchRate.Value, fileTotalLines));
        }

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

    public static TestCoverageResultDto BuildCoverletMissingResult(IReadOnlyList<string> missingProjects)
    {
        var summary = $"Coverlet missing: {missingProjects.Count} test project(s) don't reference coverlet.collector. " +
            $"Install via `dotnet add package coverlet.collector` in: {string.Join(", ", missingProjects)}.";
        return new TestCoverageResultDto(
            Success: false,
            Error: summary,
            LineCoveragePercent: null,
            BranchCoveragePercent: null,
            Modules: [],
            FailureEnvelope: new TestCoverageFailureEnvelopeDto(
                ErrorKind: "CoverletMissing",
                IsRetryable: false,
                Summary: summary,
                MissingPackages: missingProjects));
    }

    public static TestCoverageResultDto BuildNoCoverageFileResult(bool runSucceeded, int exitCode, IReadOnlyList<string>? coverageGaps)
    {
        var errorKind = runSucceeded ? "CoverletMissing" : "TestFailure";
        var summary = runSucceeded
            ? "Coverage file not generated. Ensure coverlet.collector NuGet package is referenced in test projects."
            : $"Tests failed (exit code {exitCode}). Coverage file not found.";
        return new TestCoverageResultDto(
            Success: false,
            Error: summary,
            LineCoveragePercent: null,
            BranchCoveragePercent: null,
            Modules: [],
            FailureEnvelope: new TestCoverageFailureEnvelopeDto(
                ErrorKind: errorKind,
                IsRetryable: errorKind == "TestFailure",
                Summary: summary),
            CoverageGaps: coverageGaps);
    }

    public static TestCoverageResultDto BuildTimeoutResult()
    {
        const string summary = "test_coverage was cancelled (timeout or caller cancellation).";
        return new TestCoverageResultDto(
            Success: false,
            Error: summary,
            LineCoveragePercent: null,
            BranchCoveragePercent: null,
            Modules: [],
            FailureEnvelope: new TestCoverageFailureEnvelopeDto(
                ErrorKind: "Timeout",
                IsRetryable: false,
                Summary: summary));
    }

    public static TestCoverageResultDto BuildUnexpectedErrorResult(string message)
    {
        var summary = $"test_coverage failed with an unexpected error: {message}";
        return new TestCoverageResultDto(
            Success: false,
            Error: summary,
            LineCoveragePercent: null,
            BranchCoveragePercent: null,
            Modules: [],
            FailureEnvelope: new TestCoverageFailureEnvelopeDto(
                ErrorKind: "Unknown",
                IsRetryable: false,
                Summary: summary));
    }

    private static double? WeightedAverage(List<(double rate, int lines)> samples)
    {
        if (samples.Count == 0) return null;
        var totalLines = samples.Sum(s => s.lines);
        if (totalLines == 0)
        {
            return samples.Average(s => s.rate);
        }
        return samples.Sum(s => s.rate * s.lines) / totalLines;
    }
}
