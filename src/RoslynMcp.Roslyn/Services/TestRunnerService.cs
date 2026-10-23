using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
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

    private readonly ForkInvocationPathPolicy _pathPolicy;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IGatedCommandExecutor _executor;
    private readonly ILogger<TestRunnerService> _logger;
    private readonly ValidationServiceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IUnexpectedExceptionReporter? _exceptionReporter;
    private readonly ITestDiscoveryService _testDiscoveryService;
    private readonly Action<PhysicalDirectoryScope.OwnedRoot> _deleteResultsDirectory;

    public TestRunnerService(
        ForkInvocationPathPolicy pathPolicy,
        IWorkspaceManager workspaceManager,
        IGatedCommandExecutor executor,
        ILogger<TestRunnerService> logger,
        ITestDiscoveryService testDiscoveryService,
        ValidationServiceOptions? options = null,
        IUnexpectedExceptionReporter? exceptionReporter = null)
        : this(pathPolicy, workspaceManager, executor, logger, testDiscoveryService, options, exceptionReporter,
            static owner => owner.Delete(CancellationToken.None), TimeProvider.System)
    {
    }

    internal TestRunnerService(
        ForkInvocationPathPolicy pathPolicy,
        IWorkspaceManager workspaceManager,
        IGatedCommandExecutor executor,
        ILogger<TestRunnerService> logger,
        ITestDiscoveryService testDiscoveryService,
        ValidationServiceOptions? options,
        IUnexpectedExceptionReporter? exceptionReporter,
        Action<PhysicalDirectoryScope.OwnedRoot> deleteResultsDirectory,
        TimeProvider timeProvider)
    {
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
        _workspaceManager = workspaceManager;
        _executor = executor;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _exceptionReporter = exceptionReporter;
        _testDiscoveryService = testDiscoveryService ?? throw new ArgumentNullException(nameof(testDiscoveryService));
        _deleteResultsDirectory = deleteResultsDirectory ?? throw new ArgumentNullException(nameof(deleteResultsDirectory));
    }

    public async Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct)
    {
        _logger.LogDebug(
            "TestRunnerService.RunTestsAsync: workspaceId={WorkspaceId} projectName={ProjectName} hasFilter={HasFilter}",
            workspaceId,
            projectName,
            !string.IsNullOrWhiteSpace(filter));
        var plan = await PrepareTestRunAsync(workspaceId, projectName, filter, ct).ConfigureAwait(false);
        var result = await RunTestCommandAsync(plan, ct).ConfigureAwait(false);
        try
        {
            return await CompleteTestRunAsync(plan, result, ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            // The workspace closed or was evicted while the command ran: the parsed result is
            // still valid, so return it with the workspaceChangedDuringRun warning.
            return CompleteTestRunWithoutWorkspace(result);
        }
    }

    public async Task<TestRunPlan> PrepareTestRunAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct)
    {
        var status = await _workspaceManager.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
        var solution = _workspaceManager.GetCurrentSolution(workspaceId);
        var properties = EvaluatedProjectGraph.Properties(solution).ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        var testProjects = status.Projects.Where(p => p.IsTestProject).ToList();

        ProjectStatusDto? resolvedProject = null;
        if (projectName is not null)
        {
            resolvedProject = _executor.ResolveProject(workspaceId, projectName);
            if (!resolvedProject.IsTestProject)
            {
                throw new PublicInvalidOperationException(
                    "The selected project is not a test project. Use workspace_status with verbose=true to find test projects, " +
                    "then pass a test project's name as projectName.");
            }
        }
        else if (testProjects.Count == 0)
        {
            throw new PublicInvalidOperationException(
                "No test projects found in the workspace. " +
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

        if (resolvedProject is not null)
            properties = EvaluatedBuildGraph.Resolve(ResolveLoadedProject(solution, resolvedProject), ct).Globals;
        var targetPath = resolvedProject?.FilePath ?? status.LoadedPath;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new PublicInvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        }

        // tunit-mtp-native-test-run: TUnit is MTP-only and never registers with the classic
        // VSTest adapter, so dotnet test's default VSTest mode silently ignores --logger/
        // --filter for it — it needs a different, MTP-native argument shape.
        var context = await DotnetTestModeResolver.ResolveAsync(workspaceId, GatedCommandExecutor.GetWorkingDirectory(targetPath), _pathPolicy, ct).ConfigureAwait(false);
        var executionPlan = await ResolveTestExecutionPlanAsync(resolvedProject is null ? testProjects : [resolvedProject],
            filter, workspaceId, solution, context, ct).ConfigureAwait(false);
        context = context with { ExecutionInputs = executionPlan.ExecutionInputs };
        await executionPlan.Packages.AssertCurrentAsync((paths, token) => _pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
        await DotnetTestModeResolver.ValidateAsync(workspaceId, GatedCommandExecutor.GetWorkingDirectory(targetPath),
            context, _pathPolicy, ct).ConfigureAwait(false);
        var plan = new TestRunPlan(
            workspaceId,
            projectName,
            filter,
            properties,
            context,
            executionPlan.NativeArguments,
            targetPath,
            executionPlan.RequiresMtpNative,
            executionPlan.NoRestore,
            _workspaceManager.GetCurrentVersion(workspaceId));
        if (executionPlan.RestoreInputs is { } restoredInputs)
            await restoredInputs.AssertCurrentAsync(ct).ConfigureAwait(false);
        executionPlan.Membership.AssertCurrent(ct);
        if (executionPlan.Certificate is { } certificate)
            await certificate.AssertCurrentAsync(solution, workspaceId, _pathPolicy, ct).ConfigureAwait(false);
        var receipt = new TestDependencyReceipt(executionPlan.Packages, executionPlan.Certificate, solution,
            executionPlan.SourceAuthority, executionPlan.Membership, executionPlan.RestoreInputs);
        await AssertFinalTestAuthorityAsync(workspaceId, receipt, context, ct).ConfigureAwait(false);
        DependencyReceipts.Add(plan, receipt);
        return plan;
    }

    private async Task AssertFinalTestAuthorityAsync(string workspaceId, TestDependencyReceipt receipt,
        PreparedDotnetTestContext context, CancellationToken ct)
    {
        var modeInputs = context.Candidates.Concat(context.ExecutionInputs)
            .Select(input => new PhysicalInputIdentity(input.FilePath, input.Exists, input.ContentHash)).ToArray();
        var paths = modeInputs.Select(input => input.FilePath).Concat(receipt.Packages.RequiresCurrentGrant)
            .Concat(receipt.SourceAuthority is { } source ? source.Certificate.FinalRestoreGrantPaths(source.Solution) : [])
            .Distinct(FileSystemPath.Comparer).ToArray();
        if (receipt.RestoreInputs is { } restoredInputs)
            await restoredInputs.AuthorizeFinalAsync(paths, ct).ConfigureAwait(false);
        else
            await _pathPolicy.ValidateAsync(workspaceId, paths, ct).ConfigureAwait(false);
        // Every policy callback precedes native membership reconstruction and the
        // complete physical proof. No callback may reopen this admission window.
        receipt.Membership.AssertCurrent(ct);
        receipt.RestoreInputs?.AssertNativeMembershipCurrent(ct);
        if (receipt.SourceAuthority is { } sourceAuthority)
            await sourceAuthority.Certificate.AssertRestorePhysicalCurrentAsync(sourceAuthority.Solution,
                receipt.RestoreInputs?.TransitionedOutputPaths ?? ImmutableHashSet<string>.Empty, ct).ConfigureAwait(false);
        if (receipt.RestoreInputs is { } restored)
            await restored.AssertPhysicalCurrentAsync(modeInputs.Concat(receipt.Packages.Inputs), ct).ConfigureAwait(false);
        await PhysicalInputCaptureBatch.AssertCurrentAsync(
            PhysicalInputIdentity.MergeConsistent(modeInputs.Concat(receipt.Packages.Inputs)), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }

    internal async Task AssertPreparedTestInputsAsync(TestRunPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var targetPath = plan.TargetPath
            ?? throw new ArgumentException("The test run plan has no target path; create it with PrepareTestRunAsync.", nameof(plan));
        var workspaceId = plan.WorkspaceId;
        if (!DependencyReceipts.TryGetValue(plan, out var receipt))
            throw new PublicInvalidOperationException("The test dependency receipt is unavailable. Prepare the invocation again.");
        if (receipt.RestoreInputs is { } restoredInputs)
            await restoredInputs.AssertCurrentAsync(ct).ConfigureAwait(false);
        if (receipt.Certificate is not null)
            await receipt.Certificate.AssertCurrentAsync(receipt.Solution, workspaceId, _pathPolicy, ct).ConfigureAwait(false);
        await receipt.Packages.AssertCurrentAsync((paths, grantToken) => _pathPolicy.ValidateAsync(workspaceId, paths, grantToken), ct).ConfigureAwait(false);
        await DotnetTestModeResolver.ValidateAsync(workspaceId, GatedCommandExecutor.GetWorkingDirectory(targetPath),
            plan.Context, _pathPolicy, ct).ConfigureAwait(false);
        receipt.Membership.AssertCurrent(ct);
        await DotnetTestModeResolver.ValidateAsync(workspaceId, GatedCommandExecutor.GetWorkingDirectory(targetPath),
            plan.Context, _pathPolicy, ct).ConfigureAwait(false);
        await AssertFinalTestAuthorityAsync(workspaceId, receipt, plan.Context, ct).ConfigureAwait(false);
    }

    public async Task<TestRunResultDto> RunTestCommandAsync(TestRunPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var targetPath = plan.TargetPath
            ?? throw new ArgumentException(
                "The test run plan has no target path; create it with PrepareTestRunAsync.", nameof(plan));
        var workspaceId = plan.WorkspaceId;
        if (plan.NativeTestArguments.IsDefault || plan.RequiresMtpNative != plan.Context.UsesNativeMtp
            || !plan.NoRestore || plan.Context.ExecutionInputs.IsDefaultOrEmpty
            || plan.RequiresMtpNative && plan.NativeTestArguments.IsEmpty
            || !plan.RequiresMtpNative && !plan.NativeTestArguments.IsEmpty)
            throw new PublicInvalidOperationException("The prepared test protocol does not match its authorized dotnet configuration. Prepare the invocation again.");
        if (!DependencyReceipts.TryGetValue(plan, out var receipt))
            throw new PublicInvalidOperationException("The test dependency receipt is unavailable. Prepare the invocation again.");
        Task AuthorizeStartAsync(CancellationToken token) => AssertPreparedTestInputsAsync(plan, token);
        await AuthorizeStartAsync(ct).ConfigureAwait(false);

        var resultsDirectory = Path.Combine(Path.GetTempPath(), "roslynmcp-test-results-" + Guid.NewGuid().ToString("N"));
        if (receipt.SourceAuthority is { } compiler)
            await compiler.Certificate.AssertTemporaryPathDisjointAsync(compiler.Solution, workspaceId, _pathPolicy,
                resultsDirectory, receipt.Membership.RecordedTraversalRoots, ct).ConfigureAwait(false);
        else
            await receipt.Membership.AssertTemporaryPathDisjointAsync(resultsDirectory, [],
                (paths, token) => _pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
        var resultsOwner = PhysicalDirectoryScope.CreateOwnedRoot(resultsDirectory);
        async Task AuthorizeTestStartAsync(CancellationToken token)
        {
            await AuthorizeStartAsync(token).ConfigureAwait(false);
            resultsOwner.AssertCurrentPath(token);
        }

        Exception? primary = null;
        try
        {
            // Do not set a fixed TRX/results file name: solution-level runs emit one TRX per
            // test project; a fixed name would overwrite.
            var commandArguments = plan.RequiresMtpNative
                ? BuildMtpNativeArguments(targetPath, resultsDirectory, plan.NativeTestArguments, plan.NoRestore)
                : BuildVsTestArguments(targetPath, resultsDirectory, plan.Filter);
            var arguments = MsBuildCommandProperties.Append(commandArguments, plan.EvaluationProperties);

            var stopwatch = Stopwatch.StartNew();

            TestRunResultDto result;
            try
            {
                result = await _executor.ExecuteTestAsync(
                    workspaceId,
                    targetPath,
                    arguments,
                    (execution, token) => TestRunOutputReader.ParseAsync(resultsOwner, execution, token),
                    _options.TestTimeout,
                    FastFailPatterns,
                    AuthorizeTestStartAsync,
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
                    _exceptionReporter, new MsBuildCommandProperties.Redactor(arguments).Failure(ex), UnexpectedExceptionCategory.TestRun);
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
                return MsBuildCommandProperties.Redact(DotnetOutputParser.BuildTimeoutResult(shell, timeoutSummary), arguments) with
                {
                    CommandDurationMs = stopwatch.ElapsedMilliseconds,
                };
            }

            if (result.Total == 0 && result.Execution.Succeeded && !string.IsNullOrWhiteSpace(plan.Filter))
            {
                result = result with
                {
                    Warnings = [.. result.Warnings ?? [],
                        "The supplied filter reported zero tests. Check the filter using test_discover before treating this run as verification."],
                };
            }
            return result with { CommandDurationMs = stopwatch.ElapsedMilliseconds };
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            try
            {
                _deleteResultsDirectory(resultsOwner);
            }
            catch (DirectoryNotFoundException)
            {
                // A test host may already have removed its results directory.
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Cleanup is secondary to the parsed result or the exception already in flight.
                UnexpectedExceptionReporting.Report(
                    _exceptionReporter, new MsBuildCommandProperties.Redactor(MsBuildCommandProperties.Append([], plan.EvaluationProperties)).Failure(exception), UnexpectedExceptionCategory.TestRun);
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Test result namespace release failed.", primary, resultsOwner);
            }
        }
    }

    public Task<TestRunResultDto> CompleteTestRunAsync(TestRunPlan plan, TestRunResultDto result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        return Task.FromResult(
            _workspaceManager.GetCurrentVersion(plan.WorkspaceId) == plan.WorkspaceVersion
                ? result
                : CompleteTestRunWithoutWorkspace(result));
    }

    public TestRunResultDto CompleteTestRunWithoutWorkspace(TestRunResultDto result) =>
        TestRunWarnings.WithWorkspaceChanged(result);

    /// <exception cref="InvalidOperationException">'dotnet restore' failed.</exception>
    private async Task RestoreProjectsAsync(string workspaceId, string targetPath,
        IReadOnlyList<EvaluatedBuildGraph.Node> nodes, RestoreInputAuthority inputs,
        Func<CancellationToken, Task> assertInputsCurrent,
        IReadOnlyList<string> finalCertificateGrantPaths, Func<IReadOnlySet<string>, CancellationToken, Task> assertFinalCertificateCurrent, long started, CancellationToken ct)
    {
        await _pathPolicy.ValidateAsync(workspaceId, nodes.Select(node => node.Inputs.AssetsPath).OfType<string>()
            .Distinct(FileSystemPath.Comparer).ToArray(), ct).ConfigureAwait(false);
        var projects = RestoreInvocationPlanner.CaptureNodes(nodes, ct);
        var plan = RestoreInvocationPlanner.Plan(targetPath, projects);
        var destinations = await RestoreDestinationAuthority.CaptureAsync(nodes, projects,
            (paths, token) => _pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
        foreach (var invocation in plan)
        {
            await destinations.AssertCurrentAsync(ct).ConfigureAwait(false);
            var context = await DotnetTestModeResolver.ResolveAsync(workspaceId, GatedCommandExecutor.GetWorkingDirectory(invocation.TargetPath),
                _pathPolicy, ct).ConfigureAwait(false);
            var assets = nodes.Select(node => node.Inputs.AssetsPath).OfType<string>().Distinct(FileSystemPath.Comparer).ToArray();
            var selectingInputs = new List<PhysicalInputIdentity>();
            foreach (var path in assets)
            {
                await _pathPolicy.ValidateAsync(workspaceId, [path], ct).ConfigureAwait(false);
                selectingInputs.Add(await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false));
            }
            async Task AuthorizeRestoreStartAsync(CancellationToken token)
            {
                await assertInputsCurrent(token).ConfigureAwait(false);
                await _pathPolicy.ValidateAsync(workspaceId, assets, token).ConfigureAwait(false);
                await PhysicalInputIdentity.AssertCurrentAsync(selectingInputs, token).ConfigureAwait(false);
                await inputs.AuthorizeInvocationAsync(invocation, token).ConfigureAwait(false);
                await destinations.AssertCurrentAsync(token).ConfigureAwait(false);
                await DotnetTestModeResolver.ValidateAsync(workspaceId, GatedCommandExecutor.GetWorkingDirectory(invocation.TargetPath),
                    context, _pathPolicy, token).ConfigureAwait(false);
                var modeInputs = context.Candidates.Concat(context.ExecutionInputs)
                    .Select(input => new PhysicalInputIdentity(input.FilePath, input.Exists, input.ContentHash)).ToArray();
                await inputs.AuthorizeFinalAsync(destinations.CurrentGrantPaths.Concat(assets)
                    .Concat(modeInputs.Select(input => input.FilePath)).Concat(finalCertificateGrantPaths), token).ConfigureAwait(false);
                await destinations.AssertNativeSettingsCurrentAsync(token).ConfigureAwait(false);
                inputs.AssertNativeMembershipCurrent(token);
                await assertFinalCertificateCurrent(inputs.TransitionedOutputPaths, token).ConfigureAwait(false);
                await inputs.AssertPhysicalCurrentAsync(destinations.ConfigInputs.Concat(selectingInputs).Concat(modeInputs), token).ConfigureAwait(false);
                destinations.AssertPhysicalDestinations(token);
            }
            var remaining = _options.BuildTimeout - _timeProvider.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException("The test project's restore exceeded its total time budget.");
            var execution = await _executor.ExecuteAuthorizedAsync(
                workspaceId, invocation.TargetPath, invocation.Arguments, remaining, AuthorizeRestoreStartAsync, ct).ConfigureAwait(false);
            if (!execution.Succeeded)
            {
                throw new PublicInvalidOperationException(
                    "A selected test project needs a fresh restore to establish its test execution inputs, " +
                    "but 'dotnet restore' failed with exit code " +
                    $"{execution.ExitCode}. Restore the project directly to see the full error, then retry.");
            }
            await inputs.AcceptSuccessfulAsync(invocation, ct).ConfigureAwait(false);
        }
    }

    private static List<string> BuildVsTestArguments(string targetPath, string resultsDirectory, string? filter)
    {
        var arguments = new List<string>
        {
            "test",
            targetPath,
            "--no-restore",
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
    /// by direct repro, not by reading the docs. <paramref name="nativeArguments"/> contains the frozen framework-specific reporter and filter protocol.
    /// <para>
    /// tunit-native-argv-requires-explicit-project-flag: <c>targetPath</c> must be passed via
    /// <c>--project</c>, not positionally. Confirmed by direct repro on both installed SDKs: on
    /// 10.0.204, a bare positional path fails ("Specifying a project for 'dotnet test' should be
    /// via '--project'"); on 10.0.400 the positional form happens to still work, but <c>--project</c>
    /// succeeds identically on both, so there's no reason to keep the version-fragile shape.
    /// </para>
    /// <para>
    /// <paramref name="noRestore"/> is set when <see cref="ResolveTestExecutionPlanAsync"/>
    /// bound the authorized restored dependencies and froze the framework, reporter,
    /// filter capability, and execution-input evidence. <c>--no-restore</c> prevents implicit
    /// restore from replacing that reviewed dependency snapshot. Earlier direct TUnit repros
    /// established native CLI handling of this switch; MSTest and xUnit protocol fixtures
    /// provide separate framework-specific argument controls, whose runtime results must be checked.
    /// </para>
    /// </summary>
    private static List<string> BuildMtpNativeArguments(string targetPath, string resultsDirectory, ImmutableArray<string> nativeArguments, bool noRestore)
    {
        var selector = targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? "--project" : "--solution";
        var arguments = new List<string> { "test", selector, targetPath, "--results-directory", resultsDirectory };
        arguments.AddRange(nativeArguments);

        if (noRestore)
        {
            arguments.Add("--no-restore");
        }

        return arguments;
    }

}
