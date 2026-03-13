using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Company.RoslynMcp.Roslyn.Services;

public sealed class ValidationService : IValidationService
{
    private readonly SemaphoreSlim _globalCommandGate;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _workspaceCommandGates = new(StringComparer.Ordinal);

    private readonly IWorkspaceManager _workspaceManager;
    private readonly IDotnetCommandRunner _commandRunner;
    private readonly ILogger<ValidationService> _logger;

    public ValidationService(
        IWorkspaceManager workspaceManager,
        IDotnetCommandRunner commandRunner,
        ILogger<ValidationService> logger)
    {
        _workspaceManager = workspaceManager;
        _commandRunner = commandRunner;
        _logger = logger;
        var globalLimit = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);
        _globalCommandGate = new SemaphoreSlim(globalLimit, globalLimit);
    }

    public async Task<BuildResultDto> BuildWorkspaceAsync(string workspaceId, CancellationToken ct)
    {
        var status = _workspaceManager.GetStatus(workspaceId);
        var targetPath = status.LoadedPath ?? throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        var execution = await RunDotnetCommandAsync(workspaceId, targetPath, ["build", targetPath, "--nologo"], ct);
        var diagnostics = DotnetOutputParser.ParseBuildDiagnostics($"{execution.StdOut}{Environment.NewLine}{execution.StdErr}");

        return new BuildResultDto(
            execution,
            diagnostics,
            diagnostics.Count(d => d.Severity == "Error"),
            diagnostics.Count(d => d.Severity == "Warning"));
    }

    public async Task<BuildResultDto> BuildProjectAsync(string workspaceId, string projectName, CancellationToken ct)
    {
        var project = ResolveProject(workspaceId, projectName);
        var execution = await RunDotnetCommandAsync(workspaceId, project.FilePath, ["build", project.FilePath, "--nologo"], ct);
        var diagnostics = DotnetOutputParser.ParseBuildDiagnostics($"{execution.StdOut}{Environment.NewLine}{execution.StdErr}");

        return new BuildResultDto(
            execution,
            diagnostics,
            diagnostics.Count(d => d.Severity == "Error"),
            diagnostics.Count(d => d.Severity == "Warning"));
    }

    public async Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct)
    {
        var solution = _workspaceManager.GetCurrentSolution(workspaceId);
        var testProjectNames = _workspaceManager.GetStatus(workspaceId).Projects
            .Where(project => project.IsTestProject)
            .Select(project => project.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var discoveredProjects = new List<TestProjectDto>();

        foreach (var project in solution.Projects)
        {
            if (!testProjectNames.Contains(project.Name))
            {
                continue;
            }

            var tests = new List<TestCaseDto>();
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync(ct);
                if (root is null)
                {
                    continue;
                }

                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (!HasTestAttribute(method))
                    {
                        continue;
                    }

                    var lineSpan = method.Identifier.GetLocation().GetLineSpan();
                    var containingType = method.Parent switch
                    {
                        ClassDeclarationSyntax cls => cls.Identifier.Text,
                        _ => "Unknown"
                    };
                    tests.Add(new TestCaseDto(
                        DisplayName: method.Identifier.Text,
                        FullyQualifiedName: $"{project.Name}.{containingType}.{method.Identifier.Text}",
                        FilePath: document.FilePath,
                        Line: lineSpan.StartLinePosition.Line + 1));
                }
            }

            discoveredProjects.Add(new TestProjectDto(project.Name, project.FilePath ?? project.Name, tests));
        }

        return new TestDiscoveryDto(discoveredProjects);
    }

    public async Task<IReadOnlyList<TestCaseDto>> FindRelatedTestsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var discovery = await DiscoverTestsAsync(workspaceId, ct);
        var solution = _workspaceManager.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct);
        if (symbol is null)
        {
            return [];
        }

        var searchTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { symbol.Name };
        if (symbol.ContainingType is not null)
        {
            searchTerms.Add(symbol.ContainingType.Name);
        }

        return discovery.TestProjects
            .SelectMany(project => project.Tests)
            .Where(test =>
                searchTerms.Any(term =>
                    test.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    test.FullyQualifiedName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (test.FilePath?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)))
            .ToList();
    }

    public async Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct)
    {
        var status = _workspaceManager.GetStatus(workspaceId);
        var targetPath = projectName is null
            ? status.LoadedPath
            : ResolveProject(workspaceId, projectName).FilePath;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        }

        var resultsDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcpTestResults", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultsDirectory);
        var trxPath = Path.Combine(resultsDirectory, "results.trx");

        try
        {
            var arguments = new List<string>
            {
                "test",
                targetPath,
                "--nologo",
                "--logger",
                $"trx;LogFileName={Path.GetFileName(trxPath)}",
                "--results-directory",
                resultsDirectory
            };

            if (!string.IsNullOrWhiteSpace(filter))
            {
                arguments.Add("--filter");
                arguments.Add(filter);
            }

            var execution = await RunDotnetCommandAsync(workspaceId, targetPath, arguments, ct);
            return DotnetOutputParser.ParseTestRun(execution, trxPath);
        }
        finally
        {
            if (Directory.Exists(resultsDirectory))
            {
                Directory.Delete(resultsDirectory, recursive: true);
            }
        }
    }

    private async Task<CommandExecutionDto> RunDotnetCommandAsync(
        string workspaceId,
        string targetPath,
        IReadOnlyList<string> arguments,
        CancellationToken ct)
    {
        var workspaceGate = _workspaceCommandGates.GetOrAdd(workspaceId, static _ => new SemaphoreSlim(1, 1));
        await _globalCommandGate.WaitAsync(ct);

        try
        {
            await workspaceGate.WaitAsync(ct);

            try
            {
                var workingDirectory = GetWorkingDirectory(targetPath);
                var execution = await _commandRunner.RunAsync(workingDirectory, targetPath, arguments, ct);

                _logger.LogInformation(
                    "Executed dotnet command for {TargetPath}: {Arguments} (ExitCode={ExitCode})",
                    targetPath,
                    string.Join(" ", arguments),
                    execution.ExitCode);

                return execution;
            }
            finally
            {
                workspaceGate.Release();
            }
        }
        finally
        {
            _globalCommandGate.Release();
        }
    }

    private ProjectStatusDto ResolveProject(string workspaceId, string projectName)
    {
        var project = _workspaceManager.GetStatus(workspaceId).Projects
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.FilePath, projectName, StringComparison.OrdinalIgnoreCase));

        return project ?? throw new InvalidOperationException(
            $"Project '{projectName}' was not found in workspace '{workspaceId}'.");
    }

    private static bool HasTestAttribute(MethodDeclarationSyntax method)
    {
        foreach (var attributeList in method.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attributeName = attribute.Name.ToString();
                if (attributeName is "TestMethod" or "Fact" or "Theory" or "Test" or "TestCase")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetWorkingDirectory(string targetPath)
    {
        if (Directory.Exists(targetPath))
        {
            return targetPath;
        }

        var directory = Path.GetDirectoryName(targetPath);
        return string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory;
    }

}
