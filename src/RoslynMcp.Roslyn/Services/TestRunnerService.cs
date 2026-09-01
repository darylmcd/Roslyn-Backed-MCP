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
    private readonly ITestDiscoveryService _testDiscoveryService;

    public TestRunnerService(
        IWorkspaceManager workspaceManager,
        IGatedCommandExecutor executor,
        ILogger<TestRunnerService> logger,
        ITestDiscoveryService testDiscoveryService,
        ValidationServiceOptions? options = null,
        IUnexpectedExceptionReporter? exceptionReporter = null)
    {
        _workspaceManager = workspaceManager;
        _executor = executor;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
        _exceptionReporter = exceptionReporter;
        _testDiscoveryService = testDiscoveryService ?? throw new ArgumentNullException(nameof(testDiscoveryService));
    }

    public async Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct)
    {
        _logger.LogDebug(
            "TestRunnerService.RunTestsAsync: workspaceId={WorkspaceId} projectName={ProjectName} hasFilter={HasFilter}",
            workspaceId,
            projectName,
            !string.IsNullOrWhiteSpace(filter));
        var status = await _workspaceManager.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
        var testProjects = status.Projects.Where(p => p.IsTestProject).ToList();

        ProjectStatusDto? resolvedProject = null;
        if (projectName is not null)
        {
            resolvedProject = _executor.ResolveProject(workspaceId, projectName);
            if (!resolvedProject.IsTestProject)
            {
                throw new InvalidOperationException(
                    $"Project '{projectName}' is not a test project. " +
                    $"Available test projects: {string.Join(", ", testProjects.Select(p => p.Name))}");
            }
        }
        else if (testProjects.Count == 0)
        {
            throw new InvalidOperationException(
                $"No test projects found in workspace '{workspaceId}'. " +
                "Ensure the workspace contains projects with a test SDK reference (e.g., MSTest, xUnit, NUnit).");
        }
        else if (testProjects.Count == 1)
        {
            // tunit-projectname-null-single-test-project-routing: an omitted projectName is
            // unambiguous whenever the workspace contains exactly one test project, regardless of
            // whether the workspace was loaded from that .csproj, a one-project solution, or a
            // solution that also contains non-test application projects. Route the sole test
            // project through the same per-project MTP plan as an explicitly named target.
            resolvedProject = testProjects[0];
        }
        else if (testProjects.Any(ProjectRequiresMtpNativeExecution))
        {
            // tunit-solution-level-mixed-mtp-refusal: Microsoft documents mixing VSTest and MTP
            // projects in one dotnet test invocation as unsupported, so a genuinely multi-project
            // run can't safely take the MTP branch for everything. But silently staying on the
            // classic VSTest path here would silently skip this MTP-only project's tests (the
            // exact failure tunit-mtp-native-test-run exists to fix) rather than telling the
            // caller. Refuse instead of guessing: the caller can pass projectName to target the
            // MTP-only project individually, or run each project's tests separately.
            throw new PublicInvalidOperationException(
                $"Workspace '{workspaceId}' contains {testProjects.Count} test projects, and at least " +
                "one only supports Microsoft.Testing.Platform (MTP) — TUnit never registers with the " +
                "classic VSTest adapter, and Microsoft does not support mixing VSTest and MTP projects " +
                "in one 'dotnet test' invocation. Running the whole workspace would silently skip that " +
                "project's tests. Pass projectName to target the MTP-only project individually, or run " +
                "each project's tests separately.");
        }

        var targetPath = resolvedProject?.FilePath ?? status.LoadedPath;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        }

        // tunit-mtp-native-test-run: TUnit is MTP-only and never registers with the classic
        // VSTest adapter, so dotnet test's default VSTest mode silently ignores --logger/
        // --filter for it — it needs a different, MTP-native argument shape.
        var mtpPlan = resolvedProject is not null
            ? await ResolveMtpNativeExecutionPlanAsync(resolvedProject, filter, workspaceId, ct).ConfigureAwait(false)
            : MtpNativeExecutionPlan.NotRequired;

        var resultsDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcpTestResults", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultsDirectory);

        try
        {
            // Do not set a fixed TRX/results file name: solution-level runs emit one TRX per
            // test project; a fixed name would overwrite.
            var arguments = mtpPlan.RequiresMtpNative
                ? BuildMtpNativeArguments(targetPath, resultsDirectory, mtpPlan.TreeNodeFilter, mtpPlan.NoRestore)
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

            var trxFiles = CollectTrxFiles(resultsDirectory, execution);
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
    /// was supplied — its translated <c>--treenode-filter</c> equivalent. <see cref="NoRestore"/>
    /// is set when <see cref="ResolveMtpNativeExecutionPlanAsync"/> already ran an explicit
    /// restore to check the resolved <c>TUnit.Engine</c> version — see its
    /// <c>tunit-treenode-filter-version-check-restore-snapshot</c> remarks.
    /// </summary>
    private sealed record MtpNativeExecutionPlan(bool RequiresMtpNative, string? TreeNodeFilter, bool NoRestore = false)
    {
        public static readonly MtpNativeExecutionPlan NotRequired = new(false, null);
    }

    private bool ProjectRequiresMtpNativeExecution(ProjectStatusDto project) =>
        ProjectMetadataParser.RequiresMtpNativeExecution(
            ProjectMetadataParser.LoadProjectDocument(project.FilePath, _logger));

    /// <exception cref="InvalidOperationException">
    /// The project is MTP-only (TUnit) but the run can't currently produce a structured result:
    /// the target repo's <c>global.json</c> doesn't opt into the .NET 10 SDK's native MTP
    /// <c>dotnet test</c> mode, or a supplied <paramref name="filter"/> doesn't translate to
    /// MTP's <c>--treenode-filter</c> syntax — including a multi-test filter when the project's
    /// resolved <c>TUnit.Engine</c> version isn't known to include the OR pre-filter fix, or an
    /// atom that doesn't name a test <see cref="ITestDiscoveryService"/> actually found in this
    /// project (see <see cref="TreeNodeFilterTranslator"/>). Verified against a real TUnit
    /// project: on the .NET 10 SDK, the legacy VSTest-mode MTP bridge
    /// (<c>-p:TestingPlatformDotnetTestSupport=true --</c>) is hard-removed —
    /// <c>"Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on
    /// .NET 10 SDK and later"</c> — so there is no fallback to attempt without the opt-in.
    /// </exception>
    private async Task<MtpNativeExecutionPlan> ResolveMtpNativeExecutionPlanAsync(
        ProjectStatusDto resolvedProject, string? filter, string workspaceId, CancellationToken ct)
    {
        if (!ProjectRequiresMtpNativeExecution(resolvedProject))
        {
            return MtpNativeExecutionPlan.NotRequired;
        }

        var projectDirectory = GatedCommandExecutor.GetWorkingDirectory(resolvedProject.FilePath);
        if (!DotnetTestModeResolver.UsesNativeMtpDotnetTest(projectDirectory, _logger))
        {
            throw new PublicInvalidOperationException(
                $"Project '{resolvedProject.Name}' only supports Microsoft.Testing.Platform (MTP) — TUnit never " +
                "registers with the classic VSTest adapter, and the legacy VSTest-mode MTP bridge is removed on " +
                "the .NET 10 SDK. Add a global.json at or above the project with " +
                "{\"test\": {\"runner\": \"Microsoft.Testing.Platform\"}} to opt into the native dotnet test " +
                "experience, then retry.");
        }

        string? treeNodeFilter = null;
        var noRestore = false;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            // tunit-treenode-filter-version-check-restore-snapshot: TryGetResolvedPackageVersion
            // reads obj/project.assets.json as it stands right now, but plain `dotnet test`
            // performs its own implicit restore before running — a floating/ranged TUnit.Engine
            // version could resolve differently between this check and that implicit restore,
            // authorizing the OR shape against a stale >=1.46.0 snapshot that the real restore
            // then downgrades (or the reverse: rejecting a run a fresh restore would have made
            // safe). Only the OR shape's safety depends on the resolved version, so only an OR
            // filter is worth paying for an explicit restore up front: restore now, read the
            // assets file THAT restore just wrote, then skip dotnet test's own implicit restore
            // (--no-restore) so nothing can change between the check and the execution.
            if (filter.Contains('|'))
            {
                await RestoreProjectAsync(workspaceId, resolvedProject, ct).ConfigureAwait(false);
                noRestore = true;
            }

            var resolvedTUnitEngineVersion = ProjectMetadataParser.TryGetResolvedPackageVersion(
                resolvedProject.FilePath, "TUnit.Engine", _logger);

            // tunit-treenode-filter-requires-known-test: a caller-supplied FullyQualifiedName~
            // filter's operator is "contains", not "equals" — the value might name a whole class
            // (all methods) rather than one test, and that's indistinguishable from a complete
            // test identifier by string shape alone (both are dot-separated). Rather than guess,
            // check the parsed namespace/class/method against what this project's tests actually
            // are: our own internally-synthesized filters (TestDiscoveryService.
            // SynthesizeDotnetTestFilter) always name a real, complete test and pass by
            // construction; an ambiguous or mistyped caller-supplied filter is safely declined
            // instead of silently matching zero (or the wrong) tests.
            var discovery = await _testDiscoveryService.DiscoverTestsAsync(workspaceId, ct).ConfigureAwait(false);
            IReadOnlyCollection<string> knownFullyQualifiedTestNames = discovery.TestProjects
                .Where(p => string.Equals(p.ProjectName, resolvedProject.Name, StringComparison.Ordinal))
                .SelectMany(p => p.Tests.Select(t => t.FullyQualifiedName))
                .ToList();

            treeNodeFilter = TreeNodeFilterTranslator.Translate(filter, resolvedTUnitEngineVersion, knownFullyQualifiedTestNames);
        }

        return new MtpNativeExecutionPlan(true, treeNodeFilter, noRestore);
    }

    /// <exception cref="InvalidOperationException">'dotnet restore' failed.</exception>
    private async Task RestoreProjectAsync(string workspaceId, ProjectStatusDto resolvedProject, CancellationToken ct)
    {
        var execution = await _executor.ExecuteAsync(
            workspaceId,
            resolvedProject.FilePath,
            ["restore", resolvedProject.FilePath],
            _options.BuildTimeout,
            ct).ConfigureAwait(false);

        if (!execution.Succeeded)
        {
            throw new PublicInvalidOperationException(
                $"Project '{resolvedProject.Name}' needs a fresh restore to safely check its resolved " +
                "TUnit.Engine version before translating an OR filter (see " +
                "tunit-treenode-filter-or-requires-tunit-fix), but 'dotnet restore' failed with exit code " +
                $"{execution.ExitCode}. Restore the project directly to see the full error, then retry.");
        }
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
    /// <para>
    /// tunit-native-argv-requires-explicit-project-flag: <c>targetPath</c> must be passed via
    /// <c>--project</c>, not positionally. Confirmed by direct repro on both installed SDKs: on
    /// 10.0.204, a bare positional path fails ("Specifying a project for 'dotnet test' should be
    /// via '--project'"); on 10.0.400 the positional form happens to still work, but <c>--project</c>
    /// succeeds identically on both, so there's no reason to keep the version-fragile shape.
    /// </para>
    /// <para>
    /// <paramref name="noRestore"/> is set when <see cref="ResolveMtpNativeExecutionPlanAsync"/>
    /// already ran an explicit restore to check the resolved <c>TUnit.Engine</c> version for an
    /// OR filter — <c>--no-restore</c> here guarantees that this run uses that exact same
    /// snapshot rather than letting <c>dotnet test</c>'s own implicit restore potentially resolve
    /// a different version in between. Verified directly: <c>--no-restore</c> is honored the
    /// same way under native MTP mode as under classic VSTest mode.
    /// </para>
    /// </summary>
    private static List<string> BuildMtpNativeArguments(string targetPath, string resultsDirectory, string? treeNodeFilter, bool noRestore)
    {
        var arguments = new List<string> { "test", "--project", targetPath, "--report-trx", "--results-directory", resultsDirectory };

        if (noRestore)
        {
            arguments.Add("--no-restore");
        }

        if (!string.IsNullOrWhiteSpace(treeNodeFilter))
        {
            arguments.Add("--treenode-filter");
            arguments.Add(treeNodeFilter);
        }

        return arguments;
    }

    /// <summary>
    /// Collect TRX from this invocation's unique explicit directory first. Some vstest versions
    /// ignore <c>--results-directory</c>; for those hosts, accept only the concrete
    /// <c>Results File: &lt;path&gt;</c> reported by this process. Never scan the target working tree,
    /// because it can contain historical TRX files from unrelated invocations.
    /// </summary>
    private static string[] CollectTrxFiles(string resultsDirectory, CommandExecutionDto execution)
    {
        var fromExplicit = Directory.Exists(resultsDirectory)
            ? Directory.GetFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories)
            : [];

        if (fromExplicit.Length > 0)
            return fromExplicit;

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
