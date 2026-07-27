using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;

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

    private static readonly ConcurrentDictionary<string, ForkApplyLock> ForkApplyLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IWorkspaceManager _workspaceManager;
    private readonly IPreviewStore _previewStore;
    private readonly IWorkspaceValidationService _validationService;
    private readonly ITestRunnerService _testRunnerService;
    private readonly IDotnetCommandRunner _commandRunner;
    private readonly ILogger<WorkspaceForkApplyService> _logger;

    public WorkspaceForkApplyService(
        IWorkspaceManager workspaceManager,
        IPreviewStore previewStore,
        IWorkspaceValidationService validationService,
        ITestRunnerService testRunnerService,
        IDotnetCommandRunner commandRunner,
        ILogger<WorkspaceForkApplyService> logger)
    {
        _workspaceManager = workspaceManager;
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
        var normalizedRetention = NormalizeRetention(retention);
        var entry = _previewStore.Retrieve(previewToken);
        if (entry is null)
        {
            throw new PreviewTokenStaleException(
                previewToken,
                $"Preview token '{previewToken}' is invalid, expired, or stale. Re-issue the paired *_preview call against the current workspace.");
        }

        if (!string.Equals(entry.Value.WorkspaceId, workspaceId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Preview token '{previewToken}' belongs to workspace '{entry.Value.WorkspaceId}', not '{workspaceId}'.",
                nameof(previewToken));
        }

        if (entry.Value.DiffTruncated)
        {
            throw new InvalidOperationException(
                "Refusing to fork-apply a truncated preview because the reviewed diff is incomplete. Re-run the preview with a narrower scope before calling workspace_fork_apply.");
        }

        var sourceStatus = _workspaceManager.GetStatus(workspaceId);
        var loadedPath = sourceStatus.LoadedPath
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(loadedPath))
            ?? throw new InvalidOperationException($"Could not resolve source root for workspace '{workspaceId}'.");
        string? forkWorkspaceId = null;
        var cleanupWarnings = new List<string>();
        var retained = false;

        using var sourceRootLock = await AcquireForkApplyLockAsync(sourceRoot, ct).ConfigureAwait(false);
        var forkPath = CreateForkDirectory(sourceRoot, forkName);
        try
        {
            CopyDirectory(sourceRoot, forkPath, ct);
            var appliedFiles = await ReplayPreviewIntoForkAsync(
                entry.Value.OriginalSolution,
                entry.Value.ModifiedSolution,
                sourceRoot,
                forkPath,
                ct).ConfigureAwait(false);
            var forkLoadedPath = MapSourcePathToFork(loadedPath, sourceRoot, forkPath);
            await RestoreForkAsync(forkLoadedPath, ct).ConfigureAwait(false);
            var forkStatus = await _workspaceManager
                .LoadAsync(forkLoadedPath, EvictPolicy.Lru, ct)
                .ConfigureAwait(false);
            forkWorkspaceId = forkStatus.WorkspaceId;

            var validation = await _validationService.ValidateAsync(
                forkWorkspaceId,
                appliedFiles,
                runTests && string.IsNullOrWhiteSpace(testFilter),
                ct,
                summary: true).ConfigureAwait(false);

            TestRunResultDto? explicitTestRun = null;
            if (runTests && !string.IsNullOrWhiteSpace(testFilter))
            {
                explicitTestRun = await _testRunnerService
                    .RunTestsAsync(forkWorkspaceId, projectName: null, filter: testFilter, ct)
                    .ConfigureAwait(false);
            }

            var success = IsValidationSuccess(validation, explicitTestRun);
            retained = ShouldRetainFork(normalizedRetention, success);

            if (!retained)
            {
                CleanupFork(forkWorkspaceId, forkPath, cleanupWarnings, ct);
                forkWorkspaceId = null;
                if (cleanupWarnings.Count > 0)
                {
                    success = false;
                }
            }

            return new WorkspaceForkApplyResultDto(
                success,
                forkWorkspaceId,
                forkPath,
                retained,
                appliedFiles,
                validation,
                explicitTestRun,
                cleanupWarnings);
        }
        catch
        {
            if (!retained)
            {
                CleanupFork(forkWorkspaceId, forkPath, cleanupWarnings, CancellationToken.None);
            }

            throw;
        }
    }

    private static string NormalizeRetention(string? retention)
    {
        var normalized = string.IsNullOrWhiteSpace(retention)
            ? "drop-on-success"
            : retention.Trim().ToLowerInvariant();

        return normalized is "drop-on-success" or "drop-on-failure" or "drop-always" or "keep"
            ? normalized
            : throw new ArgumentException(
                "retention must be one of: drop-on-success, drop-on-failure, drop-always, keep.",
                nameof(retention));
    }

    private static bool ShouldRetainFork(string retention, bool success) => retention switch
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

    private string CreateForkDirectory(string sourceRoot, string? forkName)
    {
        var forkRoot = Path.Combine(sourceRoot, ".roslynmcp", "forks");
        Directory.CreateDirectory(forkRoot);
        SweepExpiredForks(forkRoot, ResolveForkTtlHours(), DateTimeOffset.UtcNow, _logger);

        var slug = SanitizeForkName(forkName);
        var timestamp = DateTimeOffset.UtcNow.ToString(ForkTimestampFormat, CultureInfo.InvariantCulture);
        var forkPath = Path.Combine(forkRoot, $"{timestamp}-{slug}");
        Directory.CreateDirectory(forkPath);
        return forkPath;
    }

    internal static void SweepExpiredForks(string forkRoot) =>
        SweepExpiredForks(forkRoot, ResolveForkTtlHours(), DateTimeOffset.UtcNow, logger: null);

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
        foreach (var directory in Directory.EnumerateDirectories(forkRoot))
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
                DeleteDirectoryIfExists(directory, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning(ex, "Failed to delete expired workspace fork {ForkPath}", directory);
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

    internal static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            ct.ThrowIfCancellationRequested();
            if (IsReparsePoint(file) || IsSecretBearingFile(Path.GetFileName(file)))
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

    private async Task RestoreForkAsync(string forkLoadedPath, CancellationToken ct)
    {
        var workingDirectory = Path.GetDirectoryName(forkLoadedPath)
            ?? throw new InvalidOperationException(
                $"Could not resolve working directory for fork '{forkLoadedPath}'.");
        var timeoutMinutes = ResolveForkRestoreTimeoutMinutes();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));

        CommandExecutionDto execution;
        try
        {
            execution = await _commandRunner.RunAsync(
                workingDirectory,
                forkLoadedPath,
                ["restore", forkLoadedPath, "--nologo"],
                earlyKillPatterns: null,
                executablePath: ResolveForkDotnetPath(),
                ct: timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"dotnet restore for workspace fork exceeded the {timeoutMinutes.ToString(CultureInfo.InvariantCulture)} minute timeout.");
        }

        if (!execution.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(execution.StdErr)
                ? execution.StdOut.Trim()
                : execution.StdErr.Trim();
            throw new InvalidOperationException(
                $"dotnet restore for workspace fork failed with exit code {execution.ExitCode}. {detail}");
        }
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

        return appliedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
        var text = (await document.GetTextAsync(ct).ConfigureAwait(false)).ToString();
        var directory = Path.GetDirectoryName(forkFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(forkFilePath, text, ct).ConfigureAwait(false);
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
