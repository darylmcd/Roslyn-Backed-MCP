using System.Text.RegularExpressions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class TestRunnerService : ITestRunnerService
{
    // Item 4: MSBuild retries MSB3027/MSB3021 internally 10 times with a 1s delay. Fast-fail
    // the child dotnet process as soon as the first retry line appears so callers see the
    // FailureEnvelope within 200ms instead of ~10s. The opt-out env var restores the legacy
    // behavior by skipping pattern construction.
    [GeneratedRegex(@"MSB(3027|3021)", RegexOptions.Compiled)]
    private static partial Regex FileLockFastFailRegex();

    private static readonly bool FastFailFileLockEnabled = !string.Equals(
        Environment.GetEnvironmentVariable("ROSLYNMCP_FAST_FAIL_FILE_LOCK"),
        "false",
        StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<EarlyKillPattern>? FastFailPatterns { get; } = FastFailFileLockEnabled
        ? [new EarlyKillPattern(FileLockFastFailRegex(), "MSBuild file lock (MSB3027/MSB3021)")]
        : null;

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
        var status = await _workspaceManager.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);

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

        try
        {
            // Do not set LogFileName: solution-level runs emit one TRX per test project; a fixed name would overwrite.
            var arguments = new List<string>
            {
                "test",
                targetPath,
                "--nologo",
                "--logger",
                "trx",
                "--results-directory",
                resultsDirectory
            };

            if (!string.IsNullOrWhiteSpace(filter))
            {
                arguments.Add("--filter");
                arguments.Add(filter);
            }

            CommandExecutionDto execution;
            try
            {
                execution = await _executor.ExecuteAsync(
                    workspaceId,
                    targetPath,
                    arguments,
                    _options.TestTimeout,
                    FastFailPatterns,
                    ct).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                // Synthesize an execution shell so the parser can emit a Timeout envelope
                // rather than letting the exception escape to ToolErrorHandler as a bare
                // invocation error. The caller still gets exit code, working directory,
                // and the configured timeout in the DTO.
                var timeoutWorkingDirectory = Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory;
                var shell = new CommandExecutionDto(
                    Command: "dotnet",
                    Arguments: arguments,
                    WorkingDirectory: timeoutWorkingDirectory,
                    TargetPath: targetPath,
                    ExitCode: -1,
                    Succeeded: false,
                    DurationMs: (long)_options.TestTimeout.TotalMilliseconds,
                    StdOut: string.Empty,
                    StdErr: ex.Message);
                return DotnetOutputParser.BuildTimeoutResult(shell, ex.Message);
            }

            var dotnetWorkingDirectory = GatedCommandExecutor.GetWorkingDirectory(targetPath);
            var trxFiles = CollectTrxFiles(resultsDirectory, dotnetWorkingDirectory, execution);
            // FLAG-N1: always pass through to the parser — it handles the no-TRX failure case
            // by emitting a structured TestRunFailureEnvelopeDto instead of throwing. See
            // test-run-failure-envelope backlog row (2026-04-08 MSB3027 Windows file-lock audits).
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

    /// <summary>
    /// Some vstest versions ignore <c>--results-directory</c> for the TRX logger and emit under
    /// <c>TestResults</c> next to the project instead. Collect TRX from the explicit directory first,
    /// then fall back to the dotnet working directory (and its <c>TestResults</c> subtree).
    /// </summary>
    private static string[] CollectTrxFiles(string resultsDirectory, string workingDirectory, CommandExecutionDto execution)
    {
        var fromExplicit = Directory.Exists(resultsDirectory)
            ? Directory.GetFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories)
            : [];

        if (fromExplicit.Length > 0)
            return fromExplicit;

        var runDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddDir(string? p)
        {
            if (!string.IsNullOrWhiteSpace(p))
                runDirs.Add(p);
        }
        AddDir(workingDirectory);
        AddDir(execution.WorkingDirectory);
        AddDir(Path.Combine(workingDirectory, "TestResults"));
        if (!string.IsNullOrWhiteSpace(execution.WorkingDirectory))
            AddDir(Path.Combine(execution.WorkingDirectory, "TestResults"));

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in runDirs)
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var p in Directory.GetFiles(dir, "*.trx", SearchOption.AllDirectories))
                set.Add(p);
        }

        if (set.Count > 0)
            return [.. set];

        return TryTrxFromStdOut(execution.StdOut);
    }

    /// <summary>
    /// When TRX lands outside our results directory (host-specific vstest layout), dotnet still prints
    /// <c>Results File: &lt;path&gt;</c> to stdout — use it as a last-resort discovery path.
    /// </summary>
    private static string[] TryTrxFromStdOut(string? stdOut)
    {
        if (string.IsNullOrEmpty(stdOut))
            return [];

        var match = ResultsFileRegex().Match(stdOut);
        if (!match.Success)
            return [];

        var path = match.Groups[1].Value.Trim();
        return File.Exists(path) ? [path] : [];
    }

    [GeneratedRegex(@"Results\s+File:\s*(.+?)\s*(?:\r|\n|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ResultsFileRegex();
}
