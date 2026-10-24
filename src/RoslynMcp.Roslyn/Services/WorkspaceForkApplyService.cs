using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal delegate Task<string> ForkStoragePathValidator(string workspaceId, string path, CancellationToken ct);

/// <summary>
/// Owns workspace-fork creation, preview replay, restore, validation, retention, and cleanup.
/// </summary>
internal sealed class WorkspaceForkApplyService : IWorkspaceForkApplyService, IAsyncDisposable
{
    private static readonly HashSet<string> DirectoryCopyExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".roslynmcp",
        ForkAdmissionMaterial.DirectoryName,
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

    private readonly Func<string, Func<CancellationToken, Task<WorkspaceForkApplyResultDto>>, CancellationToken, Task<WorkspaceForkApplyResultDto>> _retainSource;
    private readonly object _lifecycleSync = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private int _operationCount;
    private TaskCompletionSource? _operationsDrained;
    private Task? _shutdownTask;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IWorkspaceExecutionGate _workspaceExecutionGate;
    private readonly IPreviewStore _previewStore;
    private readonly IWorkspaceValidationService _validationService;
    private readonly ITestRunnerService _testRunnerService;
    private readonly IDotnetCommandRunner _commandRunner;
    private readonly ForkStoragePathValidator _validateStoragePath;
    private readonly ILogger<WorkspaceForkApplyService> _logger;

    public WorkspaceForkApplyService(
        IWorkspaceManager workspaceManager,
        IWorkspaceExecutionGate workspaceExecutionGate,
        IPreviewStore previewStore,
        IWorkspaceValidationService validationService,
        ITestRunnerService testRunnerService,
        IDotnetCommandRunner commandRunner,
        ForkStoragePathValidator validateStoragePath,
        ILogger<WorkspaceForkApplyService> logger,
        Func<string, Func<CancellationToken, Task<WorkspaceForkApplyResultDto>>, CancellationToken, Task<WorkspaceForkApplyResultDto>> retainSource)
    {
        _retainSource = retainSource;
        _workspaceManager = workspaceManager;
        _workspaceExecutionGate = workspaceExecutionGate;
        _previewStore = previewStore;
        _validationService = validationService;
        _testRunnerService = testRunnerService;
        _commandRunner = commandRunner;
        _validateStoragePath = validateStoragePath;
        _logger = logger;
    }

    public Task<WorkspaceForkApplyResultDto> ApplyAsync(
        string workspaceId,
        string previewToken,
        string retention,
        bool runTests,
        string? testFilter,
        string? forkName,
        CancellationToken ct)
    {
        var normalizedRetention = NormalizeRetention(retention);
        return RunOwnedOperationAsync(token => _retainSource(workspaceId,
            sourceToken => ApplyCoreAsync(workspaceId, previewToken, normalizedRetention, runTests, testFilter, forkName, sourceToken), token), ct);
    }

    private async Task<WorkspaceForkApplyResultDto> RunOwnedOperationAsync(
        Func<CancellationToken, Task<WorkspaceForkApplyResultDto>> operation, CancellationToken ct)
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_shutdownTask is not null, this);
            if (_operationCount++ == 0)
                _operationsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCancellation.Token);
            return await operation(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            // The manager callback has returned, including its source-retention finally.
            lock (_lifecycleSync)
                if (--_operationCount == 0) _operationsDrained!.TrySetResult();
        }
    }

    /// <summary>
    /// Provider teardown drains this service before disposing its gate/runner/watcher graph.
    /// It owns only its operations and cancellation source, never an externally supplied manager.
    /// Providers resolving this service must use asynchronous disposal.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource<Exception?> cancellationIssued;
        Task shutdown;
        lock (_lifecycleSync)
        {
            if (_shutdownTask is not null) return new ValueTask(_shutdownTask);
            cancellationIssued = new TaskCompletionSource<Exception?>();
            shutdown = FinishStopAsync(cancellationIssued.Task, _operationsDrained?.Task ?? Task.CompletedTask);
            _shutdownTask = shutdown;
        }
        Exception? failure = null;
        try { _lifetimeCancellation.Cancel(); }
        catch (Exception exception) { failure = exception; }
        cancellationIssued.SetResult(failure);
        return new ValueTask(shutdown);
    }

    private async Task FinishStopAsync(Task<Exception?> cancellationIssued, Task operationsDrained)
    {
        var failure = await cancellationIssued.ConfigureAwait(false);
        await operationsDrained.ConfigureAwait(false);
        _lifetimeCancellation.Dispose();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private async Task<WorkspaceForkApplyResultDto> ApplyCoreAsync(
        string workspaceId, string previewToken, string normalizedRetention, bool runTests,
        string? testFilter, string? forkName, CancellationToken ct)
    {
        var source = GetRequiredSourceContext(workspaceId);
        string? forkWorkspaceId = null;
        var cleanupWarnings = new List<string>();
        var retained = false;

        var ancestry = ForkAdmissionMaterial.FindContainingOwned(source.LoadedPath);
        var selectedPool = SelectForkPool(source.Root, ancestry);
        var pool = await _validateStoragePath(workspaceId, selectedPool, ct).ConfigureAwait(false);
        var poolIdentity = PhysicalPathResolver.Resolve(pool);
        var candidate = await AuthorizeForkDestinationAsync(workspaceId, pool, source, forkName, ct).ConfigureAwait(false);
        var ownership = await AcquireForkApplyLocksAsync(
            ancestry.Select(owner => owner.ForkRoot).Append(source.Root).Append(pool), ct).ConfigureAwait(false);
        string? forkPath = null;
        try
        {
            ForkAdmissionMaterial.RevalidateContainingOwned(source.LoadedPath, ancestry);
            if (!FileSystemPath.Comparer.Equals(poolIdentity, PhysicalPathResolver.Resolve(pool)) ||
                !FileSystemPath.Comparer.Equals(selectedPool, SelectForkPool(source.Root, ancestry)))
                throw new PublicInvalidOperationException("The fork storage pool changed. Recreate the fork before retrying.");
            foreach (var owner in ancestry)
                if (ForkPathAdmission.Contains(owner.ForkRoot, candidate))
                    throw new PublicInvalidOperationException("The retained fork storage must be outside its source-owned ancestry. Recreate the fork before retrying.");
            // Pool ownership serializes candidate reservation across retained siblings.
            // Reauthorize every collision candidate; never validate one path and use another.
            var ordinal = 0;
            var initialCandidate = candidate;
            while (Directory.Exists(candidate) || File.Exists(candidate))
                candidate = await AuthorizeExactDestinationAsync(workspaceId, pool,
                    initialCandidate + "-" + (++ordinal).ToString(CultureInfo.InvariantCulture), source, ct).ConfigureAwait(false);
            // Capture the source and replay its preview atomically. Once this finishes,
            // Then verify its evaluated paths before restore, loading, or tests can run.
            var preparedFork = await _workspaceExecutionGate.RunWriteAsync(workspaceId, async c =>
            {
                var preview = RetrieveValidatedPreview(previewToken, workspaceId);
                Directory.CreateDirectory(pool);
                SweepExpiredForks(pool, ResolveForkTtlHours(), DateTimeOffset.UtcNow, _logger);
                var path = candidate;
                Directory.CreateDirectory(path);
                forkPath = path;
                CopyDirectory(source.Root, path, c);
                var appliedFiles = await ReplayPreviewIntoForkAsync(
                    preview.OriginalSolution, preview.ModifiedSolution, source.Root, path, c)
                    .ConfigureAwait(false);
                if (ancestry.LastOrDefault() is { } previousAdmission)
                    ForkAdmissionMaterial.StripPreviousImports(path, previousAdmission, c);
                return (Path: path, AppliedFiles: appliedFiles);
            }, ct, applyStalenessPolicy: false).ConfigureAwait(false);
            var forkLoadedPath = MapSourcePathToFork(source.LoadedPath, source.Root, preparedFork.Path);
            await RestoreForkAsync(forkLoadedPath, source.Root, preparedFork.Path, ct).ConfigureAwait(false);
            var forkStatus = await _workspaceManager
                .LoadAsync(forkLoadedPath, EvictPolicy.Lru, ct)
                .ConfigureAwait(false);
            // Own the loaded session before validation/tests can throw or be cancelled.
            forkWorkspaceId = forkStatus.WorkspaceId;
            var validationState = await ValidateForkAsync(
                forkWorkspaceId,
                preparedFork.AppliedFiles,
                runTests,
                testFilter,
                ct).ConfigureAwait(false);
            var success = validationState.Success;
            retained = ShouldRetainFork(normalizedRetention, success);

            if (!retained)
            {
                CleanupFork(forkWorkspaceId, preparedFork.Path, cleanupWarnings, ct);
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
        catch
        {
            if (!retained && forkPath is not null)
            {
                CleanupFork(forkWorkspaceId, forkPath, cleanupWarnings, CancellationToken.None);
            }

            throw;
        }
        finally { ReleaseForkApplyLocks(ownership); }
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

        return explicitTestRun is null || (explicitTestRun.Failed == 0 && explicitTestRun.Total > 0);
    }

    private static string SelectForkPool(string sourceRoot, IReadOnlyList<ForkAdmissionMaterial.Configuration> ancestry)
    {
        if (ancestry.Count == 0) return Path.Combine(sourceRoot, ".roslynmcp", "forks");
        var pool = Path.GetDirectoryName(ancestry[0].ForkRoot);
        if (pool is null)
            throw new PublicInvalidOperationException("The retained fork storage pool could not be verified. Recreate the fork before retrying.");
        // Ownership comes from verified physical admission material. Canonical
        // projection may change the pool's spelling; its name grants no authority.
        return pool;
    }

    private Task<string> AuthorizeForkDestinationAsync(string workspaceId, string pool, SourceContext source, string? forkName, CancellationToken ct)
    {
        var slug = SanitizeForkName(forkName);
        var timestamp = DateTimeOffset.UtcNow.ToString(ForkTimestampFormat, CultureInfo.InvariantCulture);
        return AuthorizeExactDestinationAsync(workspaceId, pool, Path.Combine(pool, $"{timestamp}-{slug}"), source, ct);
    }

    private async Task<string> AuthorizeExactDestinationAsync(string workspaceId, string pool, string candidate, SourceContext source, CancellationToken ct)
    {
        var path = await _validateStoragePath(workspaceId, candidate, ct).ConfigureAwait(false);
        if (!FileSystemPath.Comparer.Equals(Path.GetDirectoryName(path), pool))
            throw new PublicInvalidOperationException("The authorized fork destination must be a direct child of its authorized pool.");
        ValidateNativeRestoreDirectory(Path.GetDirectoryName(MapSourcePathToFork(source.LoadedPath, source.Root, path))!);
        return path;
    }

    internal static void ValidateNativeRestoreDirectory(string workingDirectory)
    {
        // CreateProcessW reserves space for the trailing separator and terminator,
        // even on hosts where long file paths are enabled. Check the actual cwd.
        if (OperatingSystem.IsWindows() && Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory)).Length > 258)
            throw new PublicInvalidOperationException("The generated fork directory cannot be used as a native Windows working directory. Reopen or move the source under a shorter authorized root before retrying.");
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

        var cutoff = now - TimeSpan.FromHours(ttlHours);
        var physicalPool = PhysicalPathResolver.Resolve(forkRoot);
        foreach (var directory in Directory.EnumerateDirectories(physicalPool))
        {
            var name = Path.GetFileName(directory);
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
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                var physicalDirectory = PhysicalPathResolver.Resolve(directory);
                if (!FileSystemPath.Comparer.Equals(Path.GetDirectoryName(physicalDirectory), physicalPool)) continue;
                using var ownership = TryAcquireForkApplyLock(physicalDirectory);
                if (ownership is null)
                {
                    logger?.LogWarning("Retained expired fork admission material while native execution or cleanup is active or uncertain.");
                    continue;
                }
                // A verified source child does not make unrelated timestamp-shaped
                // siblings server-owned. Unknown pre-admission forks stay untouched.
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                    !FileSystemPath.Comparer.Equals(physicalDirectory, PhysicalPathResolver.Resolve(directory)) ||
                    ForkAdmissionMaterial.ReadOwnedAtDirectory(physicalDirectory) is null) continue;
                if (HasPendingAdmissionExecutions(physicalDirectory, logger))
                {
                    logger?.LogWarning("Retained expired fork admission material while native execution or cleanup is active or uncertain.");
                    continue;
                }
                DeleteDirectoryIfExists(physicalDirectory, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PublicInvalidOperationException)
            {
                logger?.LogWarning(ex, "Retained expired workspace fork because ownership, completion, or deletion could not be verified: {ForkPath}", directory);
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
        var key = NormalizeForkOwnershipKey(sourceRoot);
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

    internal static async ValueTask<IReadOnlyList<IDisposable>> AcquireForkApplyLocksAsync(IEnumerable<string> roots, CancellationToken ct)
    {
        var acquired = new List<IDisposable>();
        try
        {
            // Physical ancestors are always shorter than their descendants. The
            // comparer supplies a deterministic order for disjoint equal lengths.
            foreach (var root in roots.Select(NormalizeForkOwnershipKey).Distinct(FileSystemPath.Comparer)
                .OrderBy(path => path.Length).ThenBy(path => path, FileSystemPath.Comparer))
                acquired.Add(await AcquireForkApplyLockAsync(root, ct).ConfigureAwait(false));
            return acquired;
        }
        catch
        {
            ReleaseForkApplyLocks(acquired);
            throw;
        }
    }

    internal static void ReleaseForkApplyLocks(IReadOnlyList<IDisposable> ownership)
    {
        for (var index = ownership.Count - 1; index >= 0; index--) ownership[index].Dispose();
    }

    internal static bool HasForkApplyLock(string sourceRoot) =>
        ForkApplyLocks.ContainsKey(NormalizeForkOwnershipKey(sourceRoot));

    private static string NormalizeForkOwnershipKey(string path) =>
        PhysicalPathResolver.Resolve(Path.GetFullPath(path));

    internal static IDisposable? TryAcquireForkApplyLock(string sourceRoot)
    {
        var key = NormalizeForkOwnershipKey(sourceRoot);
        while (true)
        {
            var entry = ForkApplyLocks.GetOrAdd(key, static _ => new ForkApplyLock());
            if (!entry.TryAddReference())
            {
                ForkApplyLocks.TryRemove(new KeyValuePair<string, ForkApplyLock>(key, entry));
                continue;
            }
            if (entry.Semaphore.Wait(0)) return new ForkApplyLockLease(key, entry);
            ReleaseForkApplyLockReference(key, entry);
            return null;
        }
    }

    private static bool HasPendingAdmissionExecutions(string forkPath, ILogger? logger)
    {
        var executions = Path.Combine(forkPath, ForkAdmissionMaterial.DirectoryName, "executions");
        if (Directory.Exists(executions) && Directory.EnumerateDirectories(executions).Any()) return true;
        if (!Directory.Exists(forkPath)) return false;
        var physicalRoot = PhysicalPathResolver.Resolve(forkPath);
        // The enclosing ownership lock excludes new descendant executions. Discover
        // only verified owned material; authored same-named directories are inert.
        foreach (var material in Directory.EnumerateDirectories(forkPath, ForkAdmissionMaterial.DirectoryName,
            new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
        {
            var owner = Path.GetDirectoryName(PhysicalPathResolver.Resolve(material))!;
            if (!ForkPathAdmission.Contains(physicalRoot, owner)) continue;
            try
            {
                if (ForkAdmissionMaterial.ReadOwnedAtDirectory(owner) is null) continue;
            }
            catch (PublicInvalidOperationException ex)
            {
                logger?.LogWarning(ex, "Retained fork material because claimed descendant admission ownership could not be verified.");
                return true;
            }
            var pending = Path.Combine(material, "executions");
            if (Directory.Exists(pending) && Directory.EnumerateDirectories(pending).Any()) return true;
        }
        return false;
    }

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

    internal static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            ct.ThrowIfCancellationRequested();
            // Linked worktrees use a regular .git pointer file. Copying it would give the
            // validation fork access to the source worktree's index and repository metadata.
            if (IsReparsePoint(file)
                || Path.GetFileName(file).Equals(".git", StringComparison.OrdinalIgnoreCase)
                || IsSecretBearingFile(Path.GetFileName(file)))
            {
                continue;
            }

            var destination = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            ct.ThrowIfCancellationRequested();
            if (IsReparsePoint(directory)
                || DirectoryCopyExclusions.Contains(Path.GetFileName(directory)))
            {
                continue;
            }

            CopyDirectory(
                directory,
                Path.Combine(destinationDirectory, Path.GetFileName(directory)),
                ct);
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

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

    internal async Task RestoreForkAsync(string forkLoadedPath, string sourceRoot, string forkRoot, CancellationToken ct)
    {
        var workingDirectory = Path.GetDirectoryName(forkLoadedPath)
            ?? throw InvalidOperationErrors.Internal(
                $"Could not resolve working directory for fork '{forkLoadedPath}'.");
        var timeoutMinutes = ResolveForkRestoreTimeoutMinutes();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        CommandExecutionDto execution;
        try
        {
            var inventory = ForkGraphPreparation.Validate(forkLoadedPath, sourceRoot, forkRoot,
                () => RestoreInvocationPlanner.RemainingBudget(TimeSpan.FromMinutes(timeoutMinutes), elapsed.Elapsed,
                    TimeSpan.Zero, "The workspace fork restore phase exceeded its time budget."), timeout.Token);
            var config = ForkAdmissionMaterial.Install(sourceRoot, forkRoot, inventory, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            var remaining = RestoreInvocationPlanner.RemainingBudget(TimeSpan.FromMinutes(timeoutMinutes), elapsed.Elapsed,
                TimeSpan.Zero, "The workspace fork restore phase exceeded its time budget.");
            var admissionExecution = await ForkAdmissionExecution.CreateAsync(config, DateTimeOffset.UtcNow + remaining, timeout.Token).ConfigureAwait(false);
            try
            {
                execution = await _commandRunner.RunAsync(
                    workingDirectory,
                    forkLoadedPath,
                    admissionExecution.Arguments(["restore", forkLoadedPath, "--nologo"]),
                    earlyKillPatterns: null,
                    executablePath: ResolveForkDotnetPath(),
                    ct: timeout.Token).ConfigureAwait(false);
                admissionExecution.Complete();
                admissionExecution.ThrowIfRefused();
            }
            finally
            {
                admissionExecution.Dispose();
                if (admissionExecution.CleanupWarning is { } warning) _logger.LogWarning("{Warning}", warning);
            }
            if (admissionExecution.CleanupWarning is not null)
                throw InvalidOperationErrors.Internal(admissionExecution.CleanupWarning);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"dotnet restore for workspace fork exceeded the {timeoutMinutes.ToString(CultureInfo.InvariantCulture)} minute timeout.");
        }

        if (!execution.Succeeded)
        {
            var detail = BuildRestoreFailureSummary(execution);
            _logger.LogWarning(
                "dotnet restore for workspace fork failed with exit code {ExitCode}. Diagnostic: {Diagnostic}",
                execution.ExitCode,
                detail);
            throw InvalidOperationErrors.Internal(
                $"dotnet restore for workspace fork failed with exit code {execution.ExitCode}. {detail}");
        }
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

    private static async Task<IReadOnlyList<string>> ReplayPreviewIntoForkAsync(
        Solution originalSolution,
        Solution modifiedSolution,
        string sourceRoot,
        string forkRoot,
        CancellationToken ct)
    {
        var changes = modifiedSolution.GetChanges(originalSolution);
        var appliedFiles = new List<string>();

        foreach (var projectChange in changes.GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                await WriteModifiedDocumentAsync(
                    modifiedSolution,
                    documentId,
                    sourceRoot,
                    forkRoot,
                    appliedFiles,
                    ct).ConfigureAwait(false);
            }

            foreach (var documentId in projectChange.GetAddedDocuments())
            {
                await WriteModifiedDocumentAsync(
                    modifiedSolution,
                    documentId,
                    sourceRoot,
                    forkRoot,
                    appliedFiles,
                    ct).ConfigureAwait(false);
            }

            foreach (var documentId in projectChange.GetRemovedDocuments())
            {
                var document = originalSolution.GetDocument(documentId);
                if (document?.FilePath is null)
                {
                    continue;
                }

                var forkFilePath = MapSourcePathToFork(document.FilePath, sourceRoot, forkRoot);
                if (File.Exists(forkFilePath))
                {
                    File.Delete(forkFilePath);
                }

                appliedFiles.Add(forkFilePath);
            }
        }

        return appliedFiles.Distinct(FileSystemPath.Comparer).ToArray();
    }

    private static async Task WriteModifiedDocumentAsync(
        Solution modifiedSolution,
        DocumentId documentId,
        string sourceRoot,
        string forkRoot,
        ICollection<string> appliedFiles,
        CancellationToken ct)
    {
        var document = modifiedSolution.GetDocument(documentId);
        if (document?.FilePath is null)
        {
            return;
        }

        var forkFilePath = MapSourcePathToFork(document.FilePath, sourceRoot, forkRoot);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(forkFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(forkFilePath, text.ToString(), text.Encoding ?? new UTF8Encoding(false), ct).ConfigureAwait(false);
        appliedFiles.Add(forkFilePath);
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
        string forkPath,
        ICollection<string> cleanupWarnings,
        CancellationToken ct)
    {
        IDisposable? ownership = null;
        try
        {
            try
            {
                ownership = TryAcquireForkApplyLock(forkPath);
                if (ownership is null || HasPendingAdmissionExecutions(forkPath, _logger))
                {
                    cleanupWarnings.Add("Fork admission material was retained because native execution or cleanup is active or uncertain.");
                    _logger.LogWarning("Retained fork admission material while native execution or cleanup is active or uncertain.");
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                cleanupWarnings.Add("Fork admission completion could not be verified: " + ex.GetType().Name);
                _logger.LogWarning(ex, "Retained fork admission material because completion could not be verified.");
                return;
            }
            try
            {
                if (!string.IsNullOrWhiteSpace(forkWorkspaceId))
                {
                    _workspaceManager.Close(forkWorkspaceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to close fork workspace {WorkspaceId}", forkWorkspaceId);
                cleanupWarnings.Add(
                    $"Failed to close fork workspace '{forkWorkspaceId}': {ex.GetType().Name}");
            }

            try
            {
                DeleteDirectoryIfExists(forkPath, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to delete fork directory {ForkPath}", forkPath);
                cleanupWarnings.Add($"Failed to delete fork directory: {ex.GetType().Name}");
            }
        }
        finally { ownership?.Dispose(); }
    }

    internal static void DeleteDirectoryIfExists(string path, CancellationToken ct)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(path, recursive: true);
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
