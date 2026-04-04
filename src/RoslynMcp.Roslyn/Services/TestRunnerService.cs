using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class TestRunnerService : ITestRunnerService
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IGatedCommandExecutor _executor;
    private readonly ILogger<TestRunnerService> _logger;
    private readonly ValidationServiceOptions _options;

    public TestRunnerService(
        IWorkspaceManager workspaceManager,
        IGatedCommandExecutor executor,
        ILogger<TestRunnerService> logger,
        ValidationServiceOptions? options = null)
    {
        _workspaceManager = workspaceManager;
        _executor = executor;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
    }

    public async Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct)
    {
        var status = _workspaceManager.GetStatus(workspaceId);

        if (projectName is not null)
        {
            var resolved = _executor.ResolveProject(workspaceId, projectName);
            if (!resolved.IsTestProject)
            {
                throw new InvalidOperationException(
                    $"Project '{projectName}' is not a test project. " +
                    $"Available test projects: {string.Join(", ", status.Projects.Where(p => p.IsTestProject).Select(p => p.Name))}");
            }
        }
        else if (!status.Projects.Any(p => p.IsTestProject))
        {
            throw new InvalidOperationException(
                $"No test projects found in workspace '{workspaceId}'. " +
                "Ensure the workspace contains projects with a test SDK reference (e.g., MSTest, xUnit, NUnit).");
        }

        var targetPath = projectName is null
            ? status.LoadedPath
            : _executor.ResolveProject(workspaceId, projectName).FilePath;

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

            var execution = await _executor.ExecuteAsync(
                workspaceId,
                targetPath,
                arguments,
                _options.TestTimeout,
                ct).ConfigureAwait(false);

            var trxFiles = Directory.GetFiles(resultsDirectory, "*.trx", SearchOption.TopDirectoryOnly);
            return DotnetOutputParser.ParseTestRun(execution, trxFiles);
        }
        finally
        {
            if (Directory.Exists(resultsDirectory))
            {
                Directory.Delete(resultsDirectory, recursive: true);
            }
        }
    }
}
