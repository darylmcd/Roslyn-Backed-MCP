using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Owns workspace-fork creation, preview replay, restore, validation, retention, and cleanup.
/// </summary>
internal sealed class WorkspaceForkApplyService : IWorkspaceForkApplyService
{
    private static readonly HashSet<string> DirectoryCopyExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".roslynmcp",
        ".vs",
        ".worktrees",
        "artifacts",
        "bin",
        "obj",
        "TestResults",
    };

    private static readonly HashSet<string> SecretBearingFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env",
        "config.local.json",
        "config.local.yaml",
        "config.local.yml",
        "credentials.json",
        "credentials.txt",
        "id_dsa",
        "id_ecdsa",
        "id_ed25519",
        "id_rsa",
        "secrets.json",
    };

    private static readonly string[] SecretBearingSuffixes =
    [
        ".key",
        ".p12",
        ".pem",
        ".pfx",
        ".pubxml",
        ".pubxml.user",
    ];

    private const double DefaultForkTtlHours = 24;
    private const double DefaultForkRestoreTimeoutMinutes = 2;
    private const string ForkTimestampFormat = "yyyyMMdd'T'HHmmssfff'Z'";
    private const int MaxRestoreDiagnosticLength = 512;
    private static readonly TimeSpan RedactionRegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex UriUserInfoRegex = new(
        @"\b(?<scheme>https?://)[^/\s@]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RedactionRegexTimeout);
    private static readonly Regex SecretValueRegex = new(
        @"(?<prefix>\b[^=&;\s]*(?:token|api[_-]?key|password|secret|credential|signature)[^=&;\s]*=)[^&;\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RedactionRegexTimeout);
    private static readonly Regex AuthorizationValueRegex = new(
        @"\b(?<scheme>Bearer|Basic)\s+\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RedactionRegexTimeout);
    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.CultureInvariant,
        RedactionRegexTimeout);

    private static readonly ConcurrentDictionary<string, ForkApplyLock> ForkApplyLocks =
        new(FileSystemPath.Comparer);

    private readonly IWorkspaceManager _workspaceManager;
    private readonly ForkInvocationPathPolicy _pathPolicy;
    private readonly IWorkspaceExecutionGate _workspaceExecutionGate;
    private readonly IPreviewStore _previewStore;
    private readonly IWorkspaceValidationService _validationService;
    private readonly ITestRunnerService _testRunnerService;
    private readonly IDotnetCommandRunner _commandRunner;
    private readonly ILogger<WorkspaceForkApplyService> _logger;
    private readonly string _forkParent;
    private readonly TimeProvider _timeProvider;

    public WorkspaceForkApplyService(
        ForkInvocationPathPolicy pathPolicy,
        IWorkspaceManager workspaceManager,
        IWorkspaceExecutionGate workspaceExecutionGate,
        IPreviewStore previewStore,
        IWorkspaceValidationService validationService,
        ITestRunnerService testRunnerService,
        IDotnetCommandRunner commandRunner,
        ILogger<WorkspaceForkApplyService> logger,
        string forkParent,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(pathPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        if (!Path.IsPathFullyQualified(forkParent) || Path.GetFileName(Path.TrimEndingDirectorySeparator(forkParent)) != "roslynmcp-forks")
            throw new ArgumentException("Fork storage requires an absolute dedicated roslynmcp-forks service parent.", nameof(forkParent));
        _forkParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(forkParent));
        _pathPolicy = pathPolicy;
        _workspaceManager = workspaceManager;
        _workspaceExecutionGate = workspaceExecutionGate;
        _previewStore = previewStore;
        _validationService = validationService;
        _testRunnerService = testRunnerService;
        _commandRunner = commandRunner;
        _logger = logger;
    }

    public async Task<WorkspaceForkApplyResultDto> ApplyAsync(
        string workspaceId,
        string previewToken,
        string retention,
        bool runTests,
        string? testFilter,
        string? forkName,
        CancellationToken ct)
    {
        var evaluationOwner = _workspaceManager as IWorkspaceEvaluationLoader
            ?? throw InvalidOperationErrors.Internal("Fork operations require workspace-owned evaluation authority.");
        evaluationOwner.AssertForkPathPolicy(_pathPolicy);
        var normalizedRetention = NormalizeRetention(retention);
        var source = GetRequiredSourceContext(workspaceId);
        string? forkWorkspaceId = null;
        var cleanupWarnings = new List<string>();
        var retained = false;

        IDisposable? sourceRootLock = null;
        IDisposable? invocationScope = null;
        IDisposable? preparationScope = null;
        IDisposable? preparationCopyScope = null;
        IDisposable? invocationCopyScope = null;
        PhysicalDirectoryScope.OwnedRoot? forkDirectory = null;
        Exception? operationFailure = null;
        RestoredCompilerInputAuthority? sourceCompilerInputs = null;
        RestoredCompilerInputAuthority? restoredInputs = null;
        Exception SanitizeFailure(Exception failure)
        {
            if (sourceCompilerInputs is not null) failure = sourceCompilerInputs.SanitizeFailure(failure);
            if (restoredInputs is not null) failure = restoredInputs.SanitizeFailure(failure);
            return failure;
        }
        try
        {
            // Capture the source and replay its preview atomically. Once this finishes,
            // restore, loading, and tests operate only on the isolated fork.
            var preparedFork = await _workspaceExecutionGate.RunWriteAsync(workspaceId, async c =>
            {
                var preview = RetrieveValidatedPreview(previewToken, workspaceId);
                var current = _workspaceManager.GetCurrentSolution(workspaceId);
                var sourceAuthority = _workspaceManager as IWorkspaceRecoveryAuthority
                    ?? throw InvalidOperationErrors.Internal("Fork preparation requires workspace-owned mutation authority.");
                sourceAuthority.EnsureMutationAllowed(workspaceId,
                    current.Projects.Select(project => project.FilePath).Append(current.FilePath)
                        .Concat(current.Projects.SelectMany(SolutionFileMutationPlanner.AllDocuments).Select(document => document.FilePath))
                        .OfType<string>().Concat(_previewStore.PeekChangedPaths(previewToken) ?? []).ToArray());
                var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(preview.OriginalSolution,
                    preview.ModifiedSolution, current, c).ConfigureAwait(false);
                var layout = await PhysicalGraphLayout.CreateAsync(workspaceId, source.LoadedPath, rebased, _pathPolicy, c).ConfigureAwait(false);
                await _pathPolicy.ValidateAsync(workspaceId,
                    layout.Packages.PathsRequiringCurrentGrant(layout.ContentRoots.Concat(layout.InputFiles)), c).ConfigureAwait(false);
                sourceRootLock = await AcquireForkApplyLockAsync(layout.LoadedDirectory, c).ConfigureAwait(false);
                var path = GetForkDirectoryPath(forkName);
                var preparation = await ForkGraphPreparation.CreateAsync(workspaceId, source.LoadedPath, preview.OriginalSolution,
                    preview.ModifiedSolution, current, layout, path, _pathPolicy,
                    evaluationOwner.CaptureStageInvocationAuthority(workspaceId, current), _logger, c).ConfigureAwait(false);
                sourceCompilerInputs = preparation.CompilerInputs;
                source = source with { Root = preparation.Layout.Root };
                var copyPlan = preparation.CopyPlan;
                var properties = EvaluatedProjectGraph.Properties(current);
                var evaluationLoader = _workspaceManager as IWorkspaceEvaluationLoader
                    ?? throw InvalidOperationErrors.Internal("The workspace cannot preserve its evaluation and package read authority in a fork.");
                var inheritedPackages = evaluationLoader.CapturePackageReadAuthority(workspaceId, current);
                await inheritedPackages.AssertCurrentAsync(c).ConfigureAwait(false);
                await _pathPolicy.ValidateAsync(workspaceId, copyPlan.Files.Select(file => file.Source).Concat(copyPlan.InputDirectories)
                    .Concat(preparation.Mutations.Select(mutation => mutation.Path)).Distinct(FileSystemPath.Comparer).ToArray(), c).ConfigureAwait(false);
                var issuedDirectory = await PrepareForkDirectoryAsync(workspaceId, current, preparation.Layout, path, c).ConfigureAwait(false);
                forkDirectory = issuedDirectory;
                var forkProperties = properties.ToDictionary(pair => pair.Key,
                    pair => BuildPathRelocation.RelocateGlobal(pair.Key, pair.Value,
                        input => preparation.MapInputPath(input, path), preparation.PathProperties));
                var forkEntry = MapSourcePathToFork(source.LoadedPath, source.Root, path);
                var invocation = evaluationLoader.CaptureForkInvocationAuthority(workspaceId, current, forkEntry, forkProperties,
                    issuedDirectory, preparation.Layout);
                preparationScope = _pathPolicy.Enter(invocation);
                preparationCopyScope = invocation.EnterOwnedCopy(issuedDirectory, preparation.Layout);
                await _pathPolicy.ValidateAsync(workspaceId, copyPlan.Inventory.Concat(preparation.Mutations
                    .SelectMany(mutation => new[] { mutation.Path, preparation.Layout.MapTo(path, mutation.Path) })).Distinct(FileSystemPath.Comparer).ToArray(), c).ConfigureAwait(false);
                await copyPlan.CopyAsync(issuedDirectory, c).ConfigureAwait(false);
                var replay = await preparation.ReplayAsync(path, issuedDirectory, c).ConfigureAwait(false);
                return (Path: path, AppliedFiles: replay.AppliedFiles, BuildInputs: replay.BuildInputs, Preparation: preparation,
                    InheritedPackages: inheritedPackages, Invocation: invocation, Properties: forkProperties, SourceSolution: current);
            }, ct, applyStalenessPolicy: false).ConfigureAwait(false);
            invocationScope = _pathPolicy.Enter(preparedFork.Invocation);
            invocationCopyScope = preparedFork.Invocation.EnterOwnedCopy(forkDirectory!, preparedFork.Preparation.Layout);
            var executionInputs = preparedFork.BuildInputs;
            var comparisonExpected = preparedFork.Preparation.Expected;
            var protectedPaths = preparedFork.Preparation.Mutations.Select(mutation => mutation.Path).ToHashSet(FileSystemPath.Comparer);
            var generatedOutputPaths = preparedFork.Preparation.CompilerInputs.NuGetGeneratedInputs
                .Select(path => preparedFork.Preparation.MapInputPath(path, preparedFork.Path)).ToHashSet(FileSystemPath.Comparer);
            var protectedOutputPaths = protectedPaths.Select(path => preparedFork.Preparation.MapInputPath(path, preparedFork.Path))
                .ToHashSet(FileSystemPath.Comparer);
            string ToReviewed(string path) => preparedFork.Preparation.Layout.MapFrom(preparedFork.Path, path);
            async Task AssertFrozenInputsAsync(CancellationToken token)
            {
                await _pathPolicy.ValidateAsync(workspaceId, executionInputs.Select(input => input.FilePath).ToArray(), token).ConfigureAwait(false);
                await PhysicalInputIdentity.AssertCurrentAsync(executionInputs, token).ConfigureAwait(false);
                await preparedFork.Preparation.ReplayAuthority.AssertCurrentAsync(workspaceId, _pathPolicy, token).ConfigureAwait(false);
                if (restoredInputs is not null) await restoredInputs.AssertCurrentAsync(workspaceId, _pathPolicy, token).ConfigureAwait(false);
            }
            var forkLoadedPath = MapSourcePathToFork(source.LoadedPath, source.Root, preparedFork.Path);
            await AssertFrozenInputsAsync(ct).ConfigureAwait(false);
            var forkStatus = await ((IWorkspaceEvaluationLoader)_workspaceManager).LoadWithPropertiesAsync(
                forkLoadedPath, preparedFork.Properties, preparedFork.InheritedPackages, preparedFork.Invocation, ct).ConfigureAwait(false);
            var loadedForkWorkspaceId = forkStatus.WorkspaceId;
            forkWorkspaceId = loadedForkWorkspaceId;
            await AssertFrozenInputsAsync(ct).ConfigureAwait(false);
            var executionIdentity = EvaluatedExecutionIdentity.Capture(_workspaceManager.GetCurrentSolution(loadedForkWorkspaceId),
                static path => path, preparedFork.Preparation.PathProperties, ct);
            async Task AssertExecutionAuthorityAsync(CancellationToken token)
            {
                await preparedFork.Invocation.AssertCurrentAsync(token).ConfigureAwait(false);
                await AssertFrozenInputsAsync(token).ConfigureAwait(false);
                executionIdentity.AssertEquivalent(_workspaceManager.GetCurrentSolution(loadedForkWorkspaceId), token);
                await AssertFrozenInputsAsync(token).ConfigureAwait(false);
            }
            await AssertExecutionAuthorityAsync(ct).ConfigureAwait(false);
            await SolutionFileMutationPlanner.AssertEquivalentAsync(preparedFork.Preparation.InitialExpected,
                _workspaceManager.GetCurrentSolution(forkWorkspaceId),
                path => preparedFork.Preparation.MapInputPath(path, preparedFork.Path), preparedFork.Preparation.ReplayAuthority.MapActual, verifyDocuments: false, preparedFork.Preparation.PathProperties, ct).ConfigureAwait(false);
            await RunRestorePhaseAsync(async restoreToken =>
            {
                var projects = preparedFork.Preparation.SourceProjects.Select(project =>
                    new RestoreInvocationPlanner.ProjectRestore(MapSourcePathToFork(project.ProjectPath, source.Root, preparedFork.Path),
                        project.PackagesPath, project.TargetFramework, project.EvaluationProperties.ToImmutableDictionary(
                            pair => pair.Key, pair => BuildPathRelocation.RelocateGlobal(pair.Key, pair.Value,
                                input => preparedFork.Preparation.MapInputPath(input, preparedFork.Path), preparedFork.Preparation.PathProperties),
                            StringComparer.OrdinalIgnoreCase), project.AssetsPath is { } assets
                            ? preparedFork.Preparation.MapInputPath(assets, preparedFork.Path) : null)).ToArray();
                var restorePlan = RestoreInvocationPlanner.Plan(forkLoadedPath, projects);
                var forkSolution = _workspaceManager.GetCurrentSolution(loadedForkWorkspaceId);
                var sourceCertificate = NativeCompilerContextCertificate.Require(preparedFork.SourceSolution);
                var forkCertificate = NativeCompilerContextCertificate.Require(forkSolution);
                var restoreGraph = EvaluatedBuildGraph.Source(forkSolution, ImmutableDictionary<string, byte[]>.Empty, restoreToken);
                var restoreInputs = await RestoreInputAuthority.CaptureAsync(restoreGraph.Nodes, restoreGraph.Nodes, forkSolution,
                    (paths, token) => _pathPolicy.ValidateAsync(workspaceId, paths, token), restoreToken).ConfigureAwait(false);
                var restoreDestinations = await RestoreDestinationAuthority.CaptureAsync(restoreGraph.Nodes, projects,
                    (paths, token) => _pathPolicy.ValidateAsync(workspaceId, paths, token), restoreToken).ConfigureAwait(false);
                var restoreIndex = 0;
                async Task AcceptSuccessfulRestoreAsync(bool finalInvocation, CancellationToken token)
                {
                    var invocation = restorePlan[restoreIndex];
                    var currentOutputs = restoreInputs.OutputsFor(invocation).ToHashSet(FileSystemPath.Comparer);
                    await restoreInputs.AcceptSuccessfulAsync(invocation, token).ConfigureAwait(false);
                    // A partial restore has no complete compiler certificate. Advance only
                    // the reviewed NuGet output files, retaining every other frozen input.
                    var outputs = await RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(executionInputs,
                        generatedOutputPaths.Intersect(currentOutputs, FileSystemPath.Comparer).ToHashSet(FileSystemPath.Comparer), protectedOutputPaths,
                        (paths, currentToken) => _pathPolicy.ValidateAsync(workspaceId, paths, currentToken), token).ConfigureAwait(false);
                    executionInputs = outputs.Inputs;
                    restoreIndex++;
                    await AssertExecutionAuthorityAsync(token).ConfigureAwait(false);
                    if (!finalInvocation) return;
                    PhysicalGraphCandidate? candidate = null;
                    Exception? primary = null;
                    try
                    {
                        candidate = await PhysicalGraphCandidate.LoadAsync(loadedForkWorkspaceId, forkLoadedPath,
                            preparedFork.Properties, _pathPolicy, new WorkspaceSessionLoader(), _logger, null, token).ConfigureAwait(false);
                        executionIdentity.AssertEquivalent(candidate.Solution, token);
                        var refreshed = await RestoredCompilerInputAuthority.RefreshAfterSuccessfulRestoreAsync(workspaceId,
                            candidate, ToReviewed, _pathPolicy, finalInvocation, token).ConfigureAwait(false);
                        executionInputs = await refreshed.AdvanceGeneratedOutputEpochAsync(workspaceId, executionInputs,
                            ToReviewed, protectedPaths, _pathPolicy, token).ConfigureAwait(false);
                        restoredInputs = refreshed;
                        await AssertFrozenInputsAsync(token).ConfigureAwait(false);
                        comparisonExpected = await preparedFork.Preparation.CompilerInputs.WithRefreshedInputsAsync(workspaceId,
                            _pathPolicy, preparedFork.Preparation.Expected, candidate, refreshed,
                            path => preparedFork.Preparation.MapInputPath(path, preparedFork.Path), ToReviewed,
                            protectedPaths, preparedFork.Preparation.PathProperties, token).ConfigureAwait(false);
                        await AssertFrozenInputsAsync(token).ConfigureAwait(false);
                    }
                    catch (Exception failure) { primary = failure; throw; }
                    finally { OwnedResourceCleanup.Dispose("Restored compiler candidate cleanup failed.", primary, candidate); }
                }
                async Task AssertRestoreAuthorityAsync(CancellationToken token)
                {
                    await AssertExecutionAuthorityAsync(token).ConfigureAwait(false);
                    if (restoreIndex < restorePlan.Count)
                        await restoreInputs.AuthorizeInvocationAsync(restorePlan[restoreIndex], token).ConfigureAwait(false);
                    else await restoreInputs.AssertCurrentAsync(token).ConfigureAwait(false);
                    await restoreDestinations.AssertCurrentAsync(token).ConfigureAwait(false);
                    await preparedFork.Invocation.AssertCurrentAsync(token).ConfigureAwait(false);
                    var replay = preparedFork.Preparation.ReplayAuthority;
                    var compilerInputs = preparedFork.Preparation.CompilerInputs;
                    var additionalInputs = executionInputs.Concat(replay.FinalInputs)
                        .Concat(compilerInputs.Assets).Concat(compilerInputs.PackageInputs)
                        .Concat(restoredInputs is null ? [] : restoredInputs.Assets.Concat(restoredInputs.PackageInputs)).ToArray();
                    var grantPaths = restoreDestinations.CurrentGrantPaths.Concat(executionInputs.Select(input => input.FilePath))
                        .Concat(replay.FinalGrantPaths).Concat(compilerInputs.FinalGrantPaths)
                        .Concat(restoredInputs is null ? [] : restoredInputs.FinalGrantPaths)
                        .Concat(sourceCertificate.FinalRestoreGrantPaths(preparedFork.SourceSolution))
                        .Concat(forkCertificate.FinalRestoreGrantPaths(forkSolution));
                    await restoreInputs.AuthorizeFinalAsync(grantPaths, token).ConfigureAwait(false);
                    await restoreDestinations.AssertNativeSettingsCurrentAsync(token).ConfigureAwait(false);
                    restoreInputs.AssertNativeMembershipCurrent(token);
                    await sourceCertificate.AssertRestorePhysicalCurrentAsync(preparedFork.SourceSolution, new HashSet<string>(FileSystemPath.Comparer), token).ConfigureAwait(false);
                    await forkCertificate.AssertRestorePhysicalCurrentAsync(forkSolution, restoreInputs.TransitionedOutputPaths, token).ConfigureAwait(false);
                    await restoreInputs.AssertPhysicalCurrentAsync(additionalInputs.Concat(restoreDestinations.ConfigInputs), token).ConfigureAwait(false);
                    restoreDestinations.AssertPhysicalDestinations(token);
                    preparedFork.Invocation.AssertCurrent();

                }
                await RestoreForkAsync(restorePlan, AssertRestoreAuthorityAsync, AcceptSuccessfulRestoreAsync, SanitizeFailure, restoreToken).ConfigureAwait(false);
                await AssertExecutionAuthorityAsync(restoreToken).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
            await ReloadForkAsync(forkWorkspaceId, preparedFork.Invocation, ct).ConfigureAwait(false);
            await AssertExecutionAuthorityAsync(ct).ConfigureAwait(false);
            await SolutionFileMutationPlanner.AssertEquivalentAsync(comparisonExpected,
                _workspaceManager.GetCurrentSolution(forkWorkspaceId),
                path => preparedFork.Preparation.MapInputPath(path, preparedFork.Path), preparedFork.Preparation.ReplayAuthority.MapActual, verifyDocuments: true, preparedFork.Preparation.PathProperties, ct).ConfigureAwait(false);
            await AssertExecutionAuthorityAsync(ct).ConfigureAwait(false);
            var validationState = await ValidateForkAsync(
                forkWorkspaceId,
                preparedFork.AppliedFiles,
                runTests,
                testFilter,
                ct).ConfigureAwait(false);
            await AssertExecutionAuthorityAsync(ct).ConfigureAwait(false);
            var success = validationState.Success;
            retained = ShouldRetainFork(normalizedRetention, success);

            if (!retained)
            {
                CleanupFork(forkWorkspaceId, forkDirectory!, cleanupWarnings, SanitizeFailure, ct);
                forkWorkspaceId = null;
                if (cleanupWarnings.Count > 0)
                {
                    success = false;
                }
            }

            return new WorkspaceForkApplyResultDto(
                success,
                forkWorkspaceId,
                preparedFork.Path,
                retained,
                validationState.AppliedFiles,
                validationState.Validation,
                validationState.ExplicitTestRun,
                cleanupWarnings);
        }
        catch (Exception failure)
        {
            if (!retained && forkDirectory is not null)
            {
                CleanupFork(forkWorkspaceId, forkDirectory, cleanupWarnings, SanitizeFailure, CancellationToken.None);
            }
            OwnedResourceCleanup.AttachOutcomes(failure, cleanupWarnings);
            var sanitized = SanitizeFailure(failure);
            operationFailure = sanitized;
            if (!ReferenceEquals(sanitized, failure)) ExceptionDispatchInfo.Capture(sanitized).Throw();
            throw;
        }
        finally { OwnedResourceCleanup.Dispose("Failed to release fork directory ownership.", operationFailure,
            forkDirectory, invocationCopyScope, preparationCopyScope, invocationScope, preparationScope, sourceRootLock); }
    }

    private ValidatedPreview RetrieveValidatedPreview(string previewToken, string workspaceId)
    {
        var entry = _previewStore.Retrieve(previewToken);
        if (entry is null)
        {
            throw new PreviewTokenStaleException(
                previewToken,
                $"Preview token '{previewToken}' is invalid, expired, or stale. Re-issue the paired *_preview call against the current workspace.");
        }

        if (!string.Equals(entry.Value.WorkspaceId, workspaceId, StringComparison.Ordinal))
        {
            throw new PublicArgumentException(
                "previewToken belongs to a different workspace. Use its original workspaceId or create a new preview in the requested workspace.",
                nameof(previewToken));
        }

        if (entry.Value.DiffTruncated)
        {
            throw new InvalidOperationException(
                "Refusing to fork-apply a truncated preview because the reviewed diff is incomplete. Re-run the preview with a narrower scope before calling workspace_fork_apply.");
        }

        return new ValidatedPreview(
            entry.Value.OriginalSolution,
            entry.Value.ModifiedSolution);
    }

    private SourceContext GetRequiredSourceContext(string workspaceId)
    {
        var loadedPath = _workspaceManager.GetStatus(workspaceId).LoadedPath
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(loadedPath))
            ?? throw new InvalidOperationException(
                $"Could not resolve source root for workspace '{workspaceId}'.");
        return new SourceContext(loadedPath, sourceRoot);
    }

    private async Task<ForkValidationState> ValidateForkAsync(
        string forkWorkspaceId,
        IReadOnlyList<string> appliedFiles,
        bool runTests,
        string? testFilter,
        CancellationToken ct)
    {
        var validation = await _validationService.ValidateAsync(
            forkWorkspaceId,
            appliedFiles,
            runTests && string.IsNullOrWhiteSpace(testFilter),
            ct,
            summary: true).ConfigureAwait(false);
        var explicitTestRun = await RunExplicitTestsAsync(
            forkWorkspaceId,
            runTests,
            testFilter,
            ct).ConfigureAwait(false);

        return new ForkValidationState(
            appliedFiles,
            validation,
            explicitTestRun,
            IsValidationSuccess(validation, explicitTestRun));
    }

    private async Task<TestRunResultDto?> RunExplicitTestsAsync(
        string forkWorkspaceId,
        bool runTests,
        string? testFilter,
        CancellationToken ct)
    {
        if (!runTests || string.IsNullOrWhiteSpace(testFilter))
        {
            return null;
        }

        return await _testRunnerService
            .RunTestsAsync(forkWorkspaceId, projectName: null, filter: testFilter, ct)
            .ConfigureAwait(false);
    }

    private static string NormalizeRetention(string? retention)
    {
        var normalized = string.IsNullOrWhiteSpace(retention)
            ? "drop-on-success"
            : retention.Trim().ToLowerInvariant();

        return normalized is "drop-on-success" or "drop-on-failure" or "drop-always" or "keep"
            ? normalized
            : throw new PublicArgumentException(
                "retention must be one of: drop-on-success, drop-on-failure, drop-always, keep.",
                nameof(retention));
    }

    internal static bool ShouldRetainFork(string retention, bool success) => retention switch
    {
        "drop-on-success" => !success,
        "drop-on-failure" => success,
        "drop-always" => false,
        "keep" => true,
        _ => false,
    };

    private static bool IsValidationSuccess(
        WorkspaceValidationDto validation,
        TestRunResultDto? explicitTestRun)
    {
        if (!string.Equals(validation.OverallStatus, "clean", StringComparison.Ordinal))
        {
            return false;
        }

        return explicitTestRun is null || TestExecutionVerdict.Status(explicitTestRun) == "clean";
    }

    private string GetForkDirectoryPath(string? forkName)
    {
        var forkRoot = _forkParent;
        var slug = SanitizeForkName(forkName);
        var timestamp = DateTimeOffset.UtcNow.ToString(ForkTimestampFormat, CultureInfo.InvariantCulture);
        return Path.Combine(forkRoot, $"{timestamp}-{slug}-{Guid.NewGuid():N}");
    }

    private async Task<PhysicalDirectoryScope.OwnedRoot> PrepareForkDirectoryAsync(
        string workspaceId, Solution source, PhysicalGraphLayout layout, string forkPath, CancellationToken ct)
    {
        var forkRoot = Path.GetDirectoryName(forkPath)!;
        if (!FileSystemPath.Comparer.Equals(forkRoot, _forkParent))
            throw InvalidOperationErrors.Internal("A workspace fork escaped its issued parent.");
        var parent = await PhysicalGraphNamespace.OpenParentAsync(workspaceId, source, layout, forkRoot, forkPath, _pathPolicy, ct).ConfigureAwait(false);
        try
        {
            SweepExpiredForks(parent, ResolveForkTtlHours(), DateTimeOffset.UtcNow, _logger);
            parent.AssertCurrentPath();
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.DisposePreservingFailure("Fork parent acquisition failed.", failure, parent);
            throw;
        }
        var owner = PhysicalDirectoryScope.CreateOwnedChild(parent, Path.GetFileName(forkPath));
        try { owner.AssertCurrentPath(ct); return owner; }
        catch (Exception failure)
        {
            try { owner.Delete(CancellationToken.None); }
            catch (Exception cleanup) { OwnedResourceCleanup.AttachOutcomes(failure, ["ForkAdmissionCleanup:" + cleanup.GetType().Name]); }
            OwnedResourceCleanup.DisposePreservingFailure("Fork admission release failed.", failure, owner);
            throw;
        }
    }

    internal static void SweepExpiredForks(
        string forkRoot,
        double ttlHours,
        DateTimeOffset now,
        ILogger? logger = null)
    {
        if (!double.IsFinite(ttlHours) || ttlHours <= 0 || !Directory.Exists(forkRoot))
        {
            return;
        }

        PhysicalDirectoryScope root;
        try { root = PhysicalDirectoryScope.OpenRoute(forkRoot); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("Expired fork root is unavailable; failure type {FailureType}.", failure.GetType().Name);
            return;
        }
        using (root) SweepExpiredForks(root, ttlHours, now, logger);
    }

    private static void SweepExpiredForks(PhysicalDirectoryScope root, double ttlHours, DateTimeOffset now, ILogger? logger)
    {
        if (!double.IsFinite(ttlHours) || ttlHours <= 0) return;
        var cutoff = now - TimeSpan.FromHours(ttlHours);
        root.AssertCurrentPath();
        foreach (var name in root.Entries())
            {
                var dashIndex = name.IndexOf('-');
                var timestampPart = dashIndex > 0 ? name[..dashIndex] : name;

                if (!DateTimeOffset.TryParseExact(
                        timestampPart,
                        ForkTimestampFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var created)
                    || created >= cutoff)
                {
                    continue;
                }

                try
                {
                    // This observation only skips work; deletion still acquires and
                    // validates its own current object through the shared scope.
                    using (var candidate = root.OpenChild(name))
                    {
                        if (candidate is null && !root.IsLink(name)) continue;
                        if (candidate is not null && !OperatingSystem.IsWindows()
                            && !candidate.HasReclaimableContent(CancellationToken.None)) continue;
                    }
                    if (root.DeleteChild(name, directoriesOnly: true, CancellationToken.None) == PhysicalDirectoryCleanupOutcome.Retained)
                        logger?.LogWarning("Expired fork content was cleaned; directory names were retained because final removal requires exclusive namespace ownership.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger?.LogWarning("Failed to delete an expired workspace fork; failure type {FailureType}.", ex.GetType().Name);
                }
            }
    }

    internal static double ResolveForkTtlHours() =>
        ReadPositiveEnvDouble("ROSLYNMCP_FORK_TTL_HOURS", DefaultForkTtlHours, allowZero: true);

    internal static double ResolveForkRestoreTimeoutMinutes() =>
        ReadPositiveEnvDouble(
            "ROSLYNMCP_FORK_RESTORE_TIMEOUT_MINUTES",
            DefaultForkRestoreTimeoutMinutes,
            allowZero: false);

    internal static string ResolveForkDotnetPath()
    {
        var configured = Environment.GetEnvironmentVariable("ROSLYNMCP_FORK_DOTNET_PATH");
        return string.IsNullOrWhiteSpace(configured) ? "dotnet" : configured.Trim();
    }

    private static double ReadPositiveEnvDouble(string variable, double fallback, bool allowZero)
    {
        var raw = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && double.IsFinite(parsed)
            && (parsed > 0 || (allowZero && parsed == 0)))
        {
            return parsed;
        }

        return fallback;
    }

    internal static async ValueTask<IDisposable> AcquireForkApplyLockAsync(
        string sourceRoot,
        CancellationToken ct)
    {
        var key = Path.GetFullPath(sourceRoot);
        while (true)
        {
            var entry = ForkApplyLocks.GetOrAdd(key, static _ => new ForkApplyLock());
            if (!entry.TryAddReference())
            {
                ForkApplyLocks.TryRemove(new KeyValuePair<string, ForkApplyLock>(key, entry));
                continue;
            }

            try
            {
                await entry.Semaphore.WaitAsync(ct).ConfigureAwait(false);
                return new ForkApplyLockLease(key, entry);
            }
            catch
            {
                ReleaseForkApplyLockReference(key, entry);
                throw;
            }
        }
    }

    internal static bool HasForkApplyLock(string sourceRoot) =>
        ForkApplyLocks.ContainsKey(Path.GetFullPath(sourceRoot));

    private static void ReleaseForkApplyLockReference(string key, ForkApplyLock entry)
    {
        if (!entry.ReleaseReference())
        {
            return;
        }

        ForkApplyLocks.TryRemove(new KeyValuePair<string, ForkApplyLock>(key, entry));
        entry.Dispose();
    }

    private static string SanitizeForkName(string? forkName)
    {
        if (string.IsNullOrWhiteSpace(forkName))
        {
            return Guid.NewGuid().ToString("N")[..8];
        }

        var chars = forkName.Trim()
            .Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        if (slug.Length == 0)
        {
            return Guid.NewGuid().ToString("N")[..8];
        }

        return slug.Length <= 40 ? slug : slug[..40];
    }

    internal static bool IsCopyExcludedDirectory(string name) => DirectoryCopyExclusions.Contains(name);

    /// <summary>
    /// Returns whether a file matches a common secret-bearing convention. A denylist remains
    /// preferable to a source allowlist here because arbitrary solution assets can be required
    /// for restore/build/test; an allowlist would silently produce invalid validation forks.
    /// </summary>
    internal static bool IsSecretBearingFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        if (SecretBearingFileNames.Contains(fileName)
            || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || SecretBearingSuffixes.Any(
                suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return !fileName.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)
                && !fileName.Equals("appsettings.Development.json", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    internal Task ReloadForkAsync(string forkWorkspaceId, ForkInvocationAuthority invocation, CancellationToken ct) =>
        ((IWorkspaceEvaluationLoader)_workspaceManager).ReloadWithInvocationAuthorityAsync(forkWorkspaceId, invocation, ct);

    internal async Task RunRestorePhaseAsync(Func<CancellationToken, Task> restorePhase, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(restorePhase);
        ct.ThrowIfCancellationRequested();
        var timeoutMinutes = ResolveForkRestoreTimeoutMinutes();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes), _timeProvider);
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            await restorePhase(phase.Token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            phase.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            throw;
        }
        catch (TimeoutException) when (ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"dotnet restore for workspace fork exceeded the {timeoutMinutes.ToString(CultureInfo.InvariantCulture)} minute timeout.");
        }
    }

    internal async Task RestoreForkAsync(IReadOnlyList<RestoreInvocationPlanner.Invocation> plan,
        Func<CancellationToken, Task> assertExecutionAuthorityAsync,
        Func<bool, CancellationToken, Task> acceptSuccessfulRestoreAsync,
        Func<Exception, Exception> sanitizeFailure, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assertExecutionAuthorityAsync);
        ArgumentNullException.ThrowIfNull(acceptSuccessfulRestoreAsync);
        ArgumentNullException.ThrowIfNull(sanitizeFailure);
        for (var index = 0; index < plan.Count; index++)
        {
            var invocation = plan[index];
            ct.ThrowIfCancellationRequested();
            await assertExecutionAuthorityAsync(ct).ConfigureAwait(false);
            var workingDirectory = Path.GetDirectoryName(invocation.TargetPath)
                ?? throw new InvalidOperationException("Could not resolve the restore target directory.");
            CommandExecutionDto execution;
            ct.ThrowIfCancellationRequested();
            execution = await _commandRunner.RunAsync(
                workingDirectory,
                invocation.TargetPath,
                invocation.Arguments,
                earlyKillPatterns: null,
                executablePath: ResolveForkDotnetPath(),
                ct: ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (!execution.Succeeded)
            {
                var detail = BuildRestoreFailureSummary(execution with
                {
                    StdOut = MsBuildCommandProperties.Redact(execution.StdOut, invocation.Arguments),
                    StdErr = MsBuildCommandProperties.Redact(execution.StdErr, invocation.Arguments)
                });
                detail = sanitizeFailure(new PublicInvalidOperationException(detail)).Message;
                _logger.LogWarning(
                    "dotnet restore for workspace fork failed with exit code {ExitCode}. Diagnostic: {Diagnostic}",
                    execution.ExitCode,
                    detail);
                throw new InvalidOperationException(
                    $"dotnet restore for workspace fork failed with exit code {execution.ExitCode}. {detail}");
            }
            await acceptSuccessfulRestoreAsync(index == plan.Count - 1, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            await assertExecutionAuthorityAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
        ct.ThrowIfCancellationRequested();
    }

    internal static string BuildRestoreFailureSummary(CommandExecutionDto execution)
    {
        var output = string.IsNullOrWhiteSpace(execution.StdErr)
            ? execution.StdOut
            : execution.StdErr;
        if (string.IsNullOrWhiteSpace(output))
        {
            return "No diagnostic output was produced.";
        }

        var redacted = UriUserInfoRegex.Replace(output, "${scheme}[redacted]@");
        redacted = SecretValueRegex.Replace(redacted, "${prefix}[redacted]");
        redacted = AuthorizationValueRegex.Replace(redacted, "${scheme} [redacted]");
        redacted = WhitespaceRegex.Replace(redacted, " ").Trim();
        return redacted.Length <= MaxRestoreDiagnosticLength
            ? redacted
            : $"{redacted[..(MaxRestoreDiagnosticLength - 1)]}…";
    }

    private static string MapSourcePathToFork(
        string sourcePath,
        string sourceRoot,
        string forkRoot)
    {
        var relative = Path.GetRelativePath(sourceRoot, Path.GetFullPath(sourcePath));
        if (IsOutsideRoot(relative))
        {
            throw new InvalidOperationException(
                $"Preview path '{sourcePath}' is outside the source workspace root '{sourceRoot}'.");
        }

        return Path.GetFullPath(Path.Combine(forkRoot, relative));
    }

    internal static bool IsOutsideRoot(string relativePath) =>
        Path.IsPathRooted(relativePath)
        || string.Equals(relativePath, "..", StringComparison.Ordinal)
        || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);

    private void CleanupFork(
        string? forkWorkspaceId,
        PhysicalDirectoryScope.OwnedRoot forkDirectory,
        ICollection<string> cleanupWarnings,
        Func<Exception, Exception> sanitizeFailure,
        CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(forkWorkspaceId))
            {
                _workspaceManager.Close(forkWorkspaceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(sanitizeFailure(ex), "Failed to close fork workspace");
            cleanupWarnings.Add(
                $"Failed to close fork workspace: {ex.GetType().Name}");
        }

        try
        {
            if (forkDirectory.Delete(ct) == PhysicalDirectoryCleanupOutcome.Retained)
            {
                const string retainedWarning = "Fork content was cleaned; directory names were retained because final removal requires exclusive namespace ownership.";
                _logger.LogWarning("{CleanupOutcome}", retainedWarning);
                cleanupWarnings.Add(retainedWarning);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(sanitizeFailure(ex), "Failed to delete fork directory");
            cleanupWarnings.Add($"Failed to delete fork directory: {ex.GetType().Name}");
        }
    }

    private sealed record ForkValidationState(
        IReadOnlyList<string> AppliedFiles,
        WorkspaceValidationDto Validation,
        TestRunResultDto? ExplicitTestRun,
        bool Success);

    private sealed record ValidatedPreview(
        Solution OriginalSolution,
        Solution ModifiedSolution);

    private sealed record SourceContext(string LoadedPath, string Root);

    private sealed class ForkApplyLock : IDisposable
    {
        private readonly object _sync = new();
        private int _referenceCount;
        private bool _retired;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddReference()
        {
            lock (_sync)
            {
                if (_retired)
                {
                    return false;
                }

                _referenceCount++;
                return true;
            }
        }

        public bool ReleaseReference()
        {
            lock (_sync)
            {
                _referenceCount--;
                if (_referenceCount > 0)
                {
                    return false;
                }

                _retired = true;
                return true;
            }
        }

        public void Dispose() => Semaphore.Dispose();
    }

    private sealed class ForkApplyLockLease(string key, ForkApplyLock entry) : IDisposable
    {
        private ForkApplyLock? _entry = entry;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _entry, null);
            if (current is null)
            {
                return;
            }

            current.Semaphore.Release();
            ReleaseForkApplyLockReference(key, current);
        }
    }
}
