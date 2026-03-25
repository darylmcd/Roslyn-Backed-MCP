using System.ComponentModel;
using System.Text.Json;
using System.Xml.Linq;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class TestCoverageTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "test_coverage", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Run tests with code coverage collection and return coverage metrics per module and class. Requires coverlet.collector NuGet package in test projects.")]
    public static Task<string> RunTestCoverage(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IDotnetCommandRunner commandRunner,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: specific test project name")] string? projectName = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                ProgressHelper.Report(progress, 0, 1);
                var status = workspace.GetStatus(workspaceId);
                var loadedPath = status.LoadedPath ?? throw new InvalidOperationException("Workspace has no loaded path.");

                var coverageDir = Path.Combine(Path.GetTempPath(), "roslyn-mcp-coverage", Guid.NewGuid().ToString("N"));
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

                var execution = await commandRunner.RunAsync(Path.GetDirectoryName(loadedPath)!, targetPath, arguments, c).ConfigureAwait(false);
                ProgressHelper.Report(progress, 0.8f, 1);

                // Find the coverage XML file
                var coverageFiles = Directory.Exists(coverageDir)
                    ? Directory.GetFiles(coverageDir, "coverage.cobertura.xml", SearchOption.AllDirectories)
                    : [];

                if (coverageFiles.Length == 0)
                {
                    ProgressHelper.Report(progress, 1, 1);
                    return JsonSerializer.Serialize(new TestCoverageResultDto(
                        Success: execution.Succeeded,
                        Error: !execution.Succeeded ? $"Tests failed (exit code {execution.ExitCode}). Coverage file not found." : "Coverage file not generated. Ensure coverlet.collector NuGet package is referenced in test projects.",
                        LineCoveragePercent: null,
                        BranchCoveragePercent: null,
                        Modules: []), JsonOptions);
                }

                var latestCoverage = coverageFiles.OrderByDescending(File.GetLastWriteTimeUtc).First();
                var result = ParseCoberturaXml(latestCoverage);
                ProgressHelper.Report(progress, 1, 1);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    private static TestCoverageResultDto ParseCoberturaXml(string path)
    {
        var doc = XDocument.Load(path);
        var coverage = doc.Root!;
        var lineRate = double.TryParse(coverage.Attribute("line-rate")?.Value, out var lr) ? lr * 100 : (double?)null;
        var branchRate = double.TryParse(coverage.Attribute("branch-rate")?.Value, out var br) ? br * 100 : (double?)null;

        var modules = new List<ModuleCoverageDto>();
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

        return new TestCoverageResultDto(
            Success: true,
            Error: null,
            LineCoveragePercent: lineRate.HasValue ? Math.Round(lineRate.Value, 1) : null,
            BranchCoveragePercent: branchRate.HasValue ? Math.Round(branchRate.Value, 1) : null,
            Modules: modules);
    }
}
