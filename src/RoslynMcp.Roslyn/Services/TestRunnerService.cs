using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class TestRunnerService : ITestRunnerService
{
    // Item 4: MSBuild retries MSB3027/MSB3021 internally 10 times with a 1s delay. Fast-fail
    // the child dotnet process as soon as the first retry line appears so callers see the
    // FailureEnvelope within 200ms instead of ~10s. The opt-out env var restores the legacy
    // behavior by skipping pattern construction.
    [GeneratedRegex(@"MSB(3027|3021)", RegexOptions.Compiled)]
    private static partial Regex FileLockFastFailRegex();

    private static readonly bool _fastFailFileLockEnabled = !string.Equals(
        Environment.GetEnvironmentVariable("ROSLYNMCP_FAST_FAIL_FILE_LOCK"),
        "false",
        StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<EarlyKillPattern>? FastFailPatterns { get; } = _fastFailFileLockEnabled
        ? [new EarlyKillPattern(FileLockFastFailRegex(), "MSBuild file lock (MSB3027/MSB3021)")]
        : null;

    private readonly IWorkspaceManager _workspaceManager;
    private readonly IGatedCommandExecutor _executor;
    private readonly ILogger<TestRunnerService> _logger;
    private readonly ValidationServiceOptions _options;
    private readonly IUnexpectedExceptionReporter? _exceptionReporter;

    public TestRunnerService(
        IWorkspaceManager workspaceManager,
        IGatedCommandExecutor executor,
        ILogger<TestRunnerService> logger,
        ValidationServiceOptions? options = null,
        IUnexpectedExceptionReporter? exceptionReporter = null)
    {
        _workspaceManager = workspaceManager;
        _executor = executor;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
        _exceptionReporter = exceptionReporter;
    }

    public async Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct)
    {
        _logger.LogDebug(
            "TestRunnerService.RunTestsAsync: workspaceId={WorkspaceId} projectName={ProjectName} hasFilter={HasFilter}",
            workspaceId,
            projectName,
            !string.IsNullOrWhiteSpace(filter));
        var status = await _workspaceManager.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);

        ProjectStatusDto? resolvedProject = null;
        if (projectName is not null)
        {
            resolvedProject = _executor.ResolveProject(workspaceId, projectName);
            if (!resolvedProject.IsTestProject)
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

        var targetPath = projectName is null ? status.LoadedPath : resolvedProject!.FilePath;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        }

        // tunit-mtp-native-test-run: TUnit is MTP-only and never registers with the classic
        // VSTest adapter, so dotnet test's default VSTest mode silently ignores --logger/
        // --filter for it — it needs a different, MTP-native argument shape. Solution-level
        // runs (projectName is null) stay on the classic path: Microsoft documents mixing
        // VSTest and MTP projects in one dotnet test invocation as unsupported, so only a
        // single resolved project can safely take the MTP branch.
        var mtpPlan = resolvedProject is not null
            ? ResolveMtpNativeExecutionPlan(resolvedProject, filter)
            : MtpNativeExecutionPlan.NotRequired;

        var resultsDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcpTestResults", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultsDirectory);

        try
        {
            // Do not set a fixed TRX/results file name: solution-level runs emit one TRX per
            // test project; a fixed name would overwrite.
            var arguments = mtpPlan.RequiresMtpNative
                ? BuildMtpNativeArguments(targetPath, resultsDirectory, mtpPlan.TreeNodeFilter)
                : BuildVsTestArguments(targetPath, resultsDirectory, filter);

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
                //
                // test-runner-timeout-error-detail-redaction: the raw exception message minted
                // by GatedCommandExecutor carries the fully-materialized argv (absolute target
                // path, absolute temp results directory, and the caller-supplied --filter), so
                // it is never copied into the public envelope's Summary or StdErr. The full
                // exception topology is routed to the opt-in server diagnostic sink instead,
                // and the client gets a deterministic, secret-safe summary. Caller cancellation
                // never enters this path — GatedCommandExecutor only reclassifies to
                // TimeoutException when the caller token is NOT cancelled.
                var details = UnexpectedExceptionReporting.Report(
                    _exceptionReporter, ex, UnexpectedExceptionCategory.TestRun);
                var budget = _options.TestTimeout.TotalMinutes.ToString(
                    "0.##", CultureInfo.InvariantCulture);
                var timeoutSummary =
                    $"'dotnet test' did not complete within the configured timeout budget of {budget} minute(s). " +
                    "This failure is not retryable as-is: narrow the run with --filter or raise the configured test timeout, then retry. " +
                    $"correlationId={details.Public.CorrelationId}";
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
                    StdErr: string.Empty);
                return DotnetOutputParser.BuildTimeoutResult(shell, timeoutSummary);
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
    /// Whether <paramref name="resolvedProject"/> needs the MTP-native <c>dotnet test</c>
    /// argument shape rather than today's classic VSTest-style invocation, and — when a filter
    /// was supplied — its translated <c>--treenode-filter</c> equivalent.
    /// </summary>
    private sealed record MtpNativeExecutionPlan(bool RequiresMtpNative, string? TreeNodeFilter)
    {
        public static readonly MtpNativeExecutionPlan NotRequired = new(false, null);
    }

    /// <exception cref="InvalidOperationException">
    /// The project is MTP-only (TUnit) but the run can't currently produce a structured result:
    /// the target repo's <c>global.json</c> doesn't opt into the .NET 10 SDK's native MTP
    /// <c>dotnet test</c> mode, or a supplied <paramref name="filter"/> doesn't translate to
    /// MTP's <c>--treenode-filter</c> syntax (see <see cref="TreeNodeFilterTranslator"/>).
    /// Verified against a real TUnit project: on the .NET 10 SDK, the legacy VSTest-mode MTP
    /// bridge (<c>-p:TestingPlatformDotnetTestSupport=true --</c>) is hard-removed —
    /// <c>"Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on
    /// .NET 10 SDK and later"</c> — so there is no fallback to attempt without the opt-in.
    /// </exception>
    private MtpNativeExecutionPlan ResolveMtpNativeExecutionPlan(ProjectStatusDto resolvedProject, string? filter)
    {
        var projectDocument = ProjectMetadataParser.LoadProjectDocument(resolvedProject.FilePath, _logger);
        if (!ProjectMetadataParser.RequiresMtpNativeExecution(projectDocument))
        {
            return MtpNativeExecutionPlan.NotRequired;
        }

        var projectDirectory = GatedCommandExecutor.GetWorkingDirectory(resolvedProject.FilePath);
        if (!DotnetTestModeResolver.UsesNativeMtpDotnetTest(projectDirectory, _logger))
        {
            throw new InvalidOperationException(
                $"Project '{resolvedProject.Name}' only supports Microsoft.Testing.Platform (MTP) — TUnit never " +
                "registers with the classic VSTest adapter, and the legacy VSTest-mode MTP bridge is removed on " +
                "the .NET 10 SDK. Add a global.json at or above the project with " +
                "{\"test\": {\"runner\": \"Microsoft.Testing.Platform\"}} to opt into the native dotnet test " +
                "experience, then retry.");
        }

        var treeNodeFilter = string.IsNullOrWhiteSpace(filter) ? null : TreeNodeFilterTranslator.Translate(filter);
        return new MtpNativeExecutionPlan(true, treeNodeFilter);
    }

    private static List<string> BuildVsTestArguments(string targetPath, string resultsDirectory, string? filter)
    {
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

        return arguments;
    }

    /// <summary>
    /// Verified against a real TUnit project run under the .NET 10 SDK's native MTP
    /// <c>dotnet test</c> mode (<c>global.json</c>'s <c>test.runner</c> opt-in). No <c>--nologo</c>:
    /// unlike VSTest mode's build-target dispatch, native MTP mode forwards any argument it
    /// doesn't recognize straight to the test host, and the host rejects <c>--nologo</c>
    /// outright ("Unknown option '--nologo'"), producing a false "zero tests ran" — confirmed
    /// by direct repro, not by reading the docs. <paramref name="treeNodeFilter"/> is the
    /// already-translated MTP filter expression (see <see cref="TreeNodeFilterTranslator"/>),
    /// passed through to <c>--treenode-filter</c> as-is when present.
    /// </summary>
    private static List<string> BuildMtpNativeArguments(string targetPath, string resultsDirectory, string? treeNodeFilter)
    {
        var arguments = new List<string> { "test", targetPath, "--report-trx", "--results-directory", resultsDirectory };

        if (!string.IsNullOrWhiteSpace(treeNodeFilter))
        {
            arguments.Add("--treenode-filter");
            arguments.Add(treeNodeFilter);
        }

        return arguments;
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
