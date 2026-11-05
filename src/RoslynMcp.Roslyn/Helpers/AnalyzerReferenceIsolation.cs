using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

namespace RoslynMcp.Roslyn.Helpers;

internal static class AnalyzerReferenceIsolation
{
    /// <summary>Directory name under the OS temp path shared by all workspaces (each lease gets its own subtree).</summary>
    internal const string ShadowRootDirectoryName = "RoslynMcpAnalyzerShadow";
    private const string ProcessOwnerFileName = ".owner.lock";

    /// <summary>
    /// Suffix of a workspace-owned lease's ownership marker, a sibling FILE
    /// <c>&lt;workspaceDirectory&gt;/&lt;leaseId&gt;.owner.lock</c> (never inside the lease directory,
    /// which would block that directory's own deletion). The lease holds it with
    /// <see cref="FileShare.None"/> from before any reference is bound until its tree is reclaimed.
    /// </summary>
    private const string LeaseOwnerMarkerSuffix = ".owner.lock";

    /// <summary>
    /// A shadow root older than this is eligible for sweeping. Age is not liveness: a live
    /// process-owned root is excluded by its held ownership marker, and a live workspace-owned
    /// lease by its held per-lease marker, even if no assembly is currently mapped from it and
    /// the directory is older than this. A markerless child of a live peer host is preserved
    /// until that host exits (an older host that predates markers cannot prove liveness).
    /// Mirrors the abandoned-run reaper discipline in <c>TestTempRoot</c>.
    /// </summary>
    private static readonly TimeSpan StaleLeaseAge = TimeSpan.FromDays(1);

    private static readonly FieldInfo? AssemblyLoaderField = typeof(AnalyzerFileReference)
        .GetField("_assemblyLoader", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? LazyAssemblyField = typeof(AnalyzerFileReference)
        .GetField("_lazyAssembly", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>Once-per-process gate for the background abandoned-root sweep.</summary>
    private static int s_sweepStarted;

    // External analyzers may be retained by Roslyn's process-wide caches. Keep their loader
    // non-collectible, but map its assemblies from one process-owned shadow root instead of
    // locking the package directory for the lifetime of the host.
    private static readonly string s_processIdentity =
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
    private static readonly string s_processShadowRoot = Path.Combine(
        Path.GetTempPath(), ShadowRootDirectoryName,
        "process-" + s_processIdentity, "copies");
    private static readonly Lazy<FileStream> s_processOwner = new(() =>
    {
        var processDirectory = Path.GetDirectoryName(s_processShadowRoot)!;
        Directory.CreateDirectory(processDirectory);
        return new FileStream(
            Path.Combine(processDirectory, ProcessOwnerFileName),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly ConcurrentDictionary<string, Lazy<ShadowCopyAnalyzerAssemblyLoader>> s_externalLoaders =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Retargets every file-based analyzer reference in <paramref name="solution"/> onto a
    /// shadow-copy loader. Workspace-owned references use a loader owned by the returned lease;
    /// external references use a process-owned non-collectible loader. Disposing the lease unloads its collectible load contexts
    /// and best-effort deletes the lease's shadow directory, so workspace close / reload / host
    /// shutdown reclaim the workspace-owned memory and disk space (analyzer-shadow-loader-lifecycle;
    /// previously the loaders and their non-collectible contexts were dropped on the floor and
    /// hundreds of orphaned shadow trees accumulated until process exit).
    /// The shadow root is keyed per-LEASE
    /// (<c>%TEMP%/RoslynMcpAnalyzerShadow/workspace-&lt;PID&gt;-&lt;processGUID&gt;-&lt;workspaceId&gt;/&lt;leaseId&gt;</c>),
    /// not per-workspace, so disposing an old lease after a reload can never race the new
    /// lease's files. On the non-Windows and reflection-unavailable early returns an empty
    /// no-op lease is returned so callers never need null checks.
    /// </summary>
    public static AnalyzerShadowLoaderLease RetargetFileReferencesToShadowLoader(
        Solution solution,
        string workspaceId,
        ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
        {
            return AnalyzerShadowLoaderLease.CreateEmpty();
        }

        if (AssemblyLoaderField is null)
        {
            logger.LogWarning("Could not locate AnalyzerFileReference loader field; analyzer references will use Roslyn's default loader.");
            return AnalyzerShadowLoaderLease.CreateEmpty();
        }

        StartAbandonedRootSweepOnce(logger);

        var workspaceRoots = GetWorkspaceRoots(solution, workspaceId, logger);
        var leaseId = Guid.NewGuid().ToString("N");
        var shadowRoot = Path.Combine(GetWorkspaceShadowDirectory(workspaceId), leaseId);
        var loaders = new Dictionary<string, ShadowCopyAnalyzerAssemblyLoader>(StringComparer.OrdinalIgnoreCase);
        var retargeted = 0;
        var externalReferences = 0;

        // Held from before the first workspace-owned reference is bound to a lease loader until the
        // lease takes ownership. Acquired lazily (a solution with no workspace-owned analyzers
        // creates nothing) and BEFORE any binding, so a marker failure cannot leave references
        // bound to loaders that no lease will ever dispose.
        FileStream? ownerMarker = null;
        try
        {
            foreach (var project in solution.Projects)
            {
                foreach (var reference in project.AnalyzerReferences.OfType<AnalyzerFileReference>())
                {
                    if (!TryGetExistingFullPath(reference.FullPath, out var analyzerPath))
                    {
                        continue;
                    }

                    if (LazyAssemblyField?.GetValue(reference) is not null)
                    {
                        logger.LogWarning(
                            "Workspace {WorkspaceId}: an analyzer reference was already loaded before shadow-loader isolation could run.",
                            workspaceId);
                        continue;
                    }

                    // Process-wide Roslyn caches can retain external analyzer types across workspace
                    // closes. Their ALC must remain non-collectible, while its file mapping must be
                    // moved away from the package source path.
                    if (!IsUnderAnyRoot(analyzerPath, workspaceRoots))
                    {
                        var directory = Path.GetDirectoryName(analyzerPath)!;
                        var processLoader = s_externalLoaders.GetOrAdd(directory, path =>
                            new Lazy<ShadowCopyAnalyzerAssemblyLoader>(() =>
                            {
                                // Hold the marker before creating any copy. The stale-root sweeper in
                                // another host can then distinguish this live process from an exit.
                                _ = s_processOwner.Value;
                                return ShadowCopyAnalyzerAssemblyLoader.Create(
                                    analyzerPath, s_processShadowRoot, logger, isCollectible: false);
                            },
                                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
                        processLoader.AddDependencyLocation(analyzerPath);
                        AssemblyLoaderField.SetValue(reference, processLoader);
                        externalReferences++;
                        continue;
                    }

                    ownerMarker ??= AnalyzerShadowLoaderLease.AcquireOwnerMarker(shadowRoot);
                    if (!loaders.TryGetValue(analyzerPath, out var loader))
                    {
                        loader = ShadowCopyAnalyzerAssemblyLoader.Create(
                            analyzerPath, shadowRoot, logger, isCollectible: true);
                        loaders[analyzerPath] = loader;
                    }

                    AssemblyLoaderField.SetValue(reference, loader);
                    retargeted++;
                }
            }

            if (externalReferences > 0)
            {
                logger.LogDebug(
                    "Workspace {WorkspaceId}: retargeted {Count} external analyzer reference(s) to the process shadow loader.",
                    workspaceId,
                    externalReferences);
            }

            // ownerMarker is non-null exactly when a workspace-owned reference was retargeted.
            return ownerMarker is null
                ? AnalyzerShadowLoaderLease.CreateEmpty()
                : new AnalyzerShadowLoaderLease(shadowRoot, ownerMarker, [.. loaders.Values], retargeted, logger);
        }
        catch
        {
            // The lease never took ownership: release the marker so no handle outlives the
            // failed load with nobody left to close it.
            AnalyzerShadowLoaderLease.AbandonUnownedMarker(ownerMarker, shadowRoot, logger);
            throw;
        }
    }

    internal static string GetWorkspaceShadowDirectory(string workspaceId) =>
        Path.Combine(Path.GetTempPath(), ShadowRootDirectoryName, "workspace-" + s_processIdentity + "-" + workspaceId);

    internal enum CreatorLiveness { Live, Absent, Unknown }

    /// <summary>Recognize only layouts that persist a validated local creator PID.</summary>
    internal static int? GetCreatorProcessId(string directoryName)
    {
        var isProcess = directoryName.StartsWith("process-", StringComparison.Ordinal);
        var isWorkspace = directoryName.StartsWith("workspace-", StringComparison.Ordinal);
        if (!isProcess && !isWorkspace) return null;

        var parts = directoryName.Split('-', isProcess ? 3 : 4);
        if (parts.Length != (isProcess ? 3 : 4) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0 ||
            parts[2].Length != 32 || !Guid.TryParseExact(parts[2], "N", out _) ||
            (isWorkspace && parts[3].Length == 0))
        {
            return null;
        }

        return pid;
    }

    internal static CreatorLiveness ObserveCreator(string directoryName)
    {
        var pid = GetCreatorProcessId(directoryName);
        if (pid is null) return CreatorLiveness.Unknown;
        if (pid == Environment.ProcessId) return CreatorLiveness.Live;

        Process creator;
        try
        {
            creator = Process.GetProcessById(pid.Value);
        }
        catch (ArgumentException)
        {
            // Only a validated local lookup's no-running-process result proves absence.
            return CreatorLiveness.Absent;
        }
        catch (Win32Exception)
        {
            return CreatorLiveness.Unknown;
        }
        catch (PlatformNotSupportedException)
        {
            return CreatorLiveness.Unknown;
        }

        using (creator) return CreatorLiveness.Live;
    }

    internal static bool CanPruneParent(string directoryName, CreatorLiveness creator) =>
        GetCreatorProcessId(directoryName) is not null && creator == CreatorLiveness.Absent;

    /// <summary>
    /// Best-effort removal of shadow roots abandoned by earlier crashed or pre-fix hosts, so
    /// per-lease disposal does not leave historical leaks in place forever. Only lease
    /// directories older than <see cref="StaleLeaseAge"/> that are not owned by a live lease in
    /// this process are candidates. A workspace-owned lease in another process is excluded by its
    /// held per-lease marker (see <see cref="SweepLease"/>), not by whichever of its files happen
    /// to be mapped at the moment. The shared parent and per-workspace parents of live leases are
    /// never deleted.
    /// </summary>
    internal static void SweepAbandonedRoots(ILogger logger) =>
        SweepAbandonedRoots(Path.Combine(Path.GetTempPath(), ShadowRootDirectoryName), logger);

    /// <summary>
    /// Sweeps an explicit shared parent. Kept internal so lifecycle tests can exercise failure
    /// handling against an isolated root without inspecting or mutating other hosts' temp data.
    /// </summary>
    internal static void SweepAbandonedRoots(string sharedParent, ILogger logger)
    {
        if (!Directory.Exists(sharedParent))
        {
            return;
        }

        using var sharedHandle = NoFollowDirectoryDeleter.TryHoldOwnedDirectory(sharedParent, logger);
        if (OperatingSystem.IsWindows() && sharedHandle is null) return;

        var utcNow = DateTime.UtcNow;
        foreach (var workspaceDirectory in EnumerateDirectoriesSafe(sharedParent, logger))
        {
            using var workspaceHandle = NoFollowDirectoryDeleter.TryHoldOwnedDirectory(workspaceDirectory, logger);
            if (OperatingSystem.IsWindows() && workspaceHandle is null) continue;

            var directoryName = Path.GetFileName(workspaceDirectory);
            var canPruneParent = false;
            var isProcessRoot = directoryName.StartsWith("process-", StringComparison.OrdinalIgnoreCase);
            var creatorIsLivePeer = false;
            var ownerPath = Path.Combine(workspaceDirectory, ProcessOwnerFileName);
            FileStream? ownerProbe = null;
            if (isProcessRoot)
            {
                try
                {
                    // A live host holds this file with FileShare.None. Keep our probe open
                    // throughout deletion so another sweeper cannot claim the same root.
                    ownerProbe = new FileStream(ownerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    canPruneParent = true;
                }
                catch (FileNotFoundException)
                {
                    // Directory creation precedes marker establishment. Missing is not released:
                    // a live or unknown creator may still be initializing this unique root.
                    canPruneParent = CanPruneParent(directoryName, ObserveCreator(directoryName));
                    if (!canPruneParent) continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug("An analyzer shadow process root is owned or unavailable; failure type {FailureType}.", ex.GetType().Name);
                    continue;
                }
            }
            else
            {
                // Creator liveness gates parent pruning, and whether a MARKERLESS child may be
                // swept (below). A held or released per-lease marker is the authority for a child
                // whose creator is another live host; the creator being this process is covered by
                // the in-process registry.
                var creator = ObserveCreator(directoryName);
                canPruneParent = CanPruneParent(directoryName, creator);
                creatorIsLivePeer = creator == CreatorLiveness.Live &&
                    GetCreatorProcessId(directoryName) != Environment.ProcessId;
            }

            try
            {
                foreach (var leaseDirectory in EnumerateDirectoriesSafe(workspaceDirectory, logger))
                {
                    if (AnalyzerShadowLoaderLease.IsLiveRoot(leaseDirectory) ||
                        !IsStale(leaseDirectory, utcNow, logger))
                    {
                        continue;
                    }

                    SweepLease(leaseDirectory, requireLeaseAuthority: !isProcessRoot, creatorIsLivePeer, logger);
                }

                // Before the prune check below: a released marker whose tree is already gone
                // would otherwise keep the workspace directory non-empty forever.
                if (!isProcessRoot)
                {
                    ReapOrphanLeaseMarkers(workspaceDirectory, logger);
                }
            }
            finally
            {
                ownerProbe?.Dispose();
            }

            // Best-effort pruning requires released ownership or proven creator absence.
            // An empty snapshot or old timestamp cannot authorize deleting an initializing parent.
            if (!canPruneParent) continue;
            try
            {
                if (ownerProbe is not null && !Directory.EnumerateDirectories(workspaceDirectory).Any())
                {
                    File.Delete(ownerPath);
                }

                if (!Directory.EnumerateFileSystemEntries(workspaceDirectory).Any())
                {
                    // The held handle prevents replacement while we inspect and remove marker
                    // files. Release it only for the final empty-directory removal; deleting a
                    // replacement junction at this point removes the link, not its target.
                    workspaceHandle?.Dispose();
                    Directory.Delete(workspaceDirectory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                logger.LogDebug(
                    "Could not remove an empty analyzer shadow workspace directory; failure type {FailureType}.",
                    ex.GetType().Name);
            }
        }
    }

    private enum LeaseMarkerState { Missing, Held, Acquired }

    internal static string GetLeaseOwnerMarkerPath(string leaseDirectory) =>
        leaseDirectory + LeaseOwnerMarkerSuffix;

    /// <summary>
    /// Tries to take a lease's ownership marker from the sweeper side. <c>Held</c> means a live
    /// lease owns it (or another sweeper does, or it is delete-pending): the lease is not ours to
    /// touch. <c>Acquired</c> means the marker exists and is released, so its lease is disposed or
    /// its host exited. The probe shares only <see cref="FileShare.Delete"/>, which still excludes
    /// the lease's <see cref="FileShare.None"/> handle and a second sweeper (both request read and
    /// write), and lets the holder delete the marker while it is still open: on Windows a file
    /// open with <see cref="FileShare.None"/> cannot be deleted by its own holder. On Unix the lease's
    /// exclusive advisory lock rejects the probe's shared one.
    /// </summary>
    private static LeaseMarkerState TryAcquireLeaseOwnerMarker(
        string leaseDirectory, ILogger logger, out FileStream? probe)
    {
        probe = null;
        try
        {
            probe = new FileStream(
                GetLeaseOwnerMarkerPath(leaseDirectory), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
            return LeaseMarkerState.Acquired;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return LeaseMarkerState.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug("An analyzer shadow lease is owned or unavailable; failure type {FailureType}.", ex.GetType().Name);
            return LeaseMarkerState.Held;
        }
    }

    /// <summary>
    /// Sweeps one stale lease directory. Age alone is not liveness (a lease open for more than
    /// <see cref="StaleLeaseAge"/> can still copy a late dependency before loading it, and a mapped
    /// sibling protects neither earlier nor unmapped entries), so for workspace-owned leases the
    /// authority is the per-lease marker: held => skip; released => sweep and reclaim the marker
    /// only once the tree is gone; absent => a creator that is another live host keeps its child
    /// (it may predate markers), any other creator falls back to the age rule.
    /// </summary>
    private static void SweepLease(
        string leaseDirectory, bool requireLeaseAuthority, bool creatorIsLivePeer, ILogger logger)
    {
        FileStream? marker = null;
        try
        {
            if (requireLeaseAuthority)
            {
                switch (TryAcquireLeaseOwnerMarker(leaseDirectory, logger, out marker))
                {
                    case LeaseMarkerState.Held:
                        return;
                    case LeaseMarkerState.Missing when creatorIsLivePeer:
                        return;
                }
            }

            // A failed delete (a still-mapped file) leaves the tree and its released marker for
            // the next sweep.
            try
            {
                NoFollowDirectoryDeleter.Delete(leaseDirectory, CancellationToken.None);
                logger.LogDebug("Swept one abandoned analyzer shadow lease.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(
                    "Could not sweep an abandoned analyzer shadow lease; failure type {FailureType}.",
                    ex.GetType().Name);
                return;
            }

            if (marker is not null)
            {
                TryDeleteOwnerMarker(GetLeaseOwnerMarkerPath(leaseDirectory), logger);
            }
        }
        finally
        {
            marker?.Dispose();
        }
    }

    /// <summary>
    /// Removes released lease markers whose lease directory no longer exists (a dispose that
    /// removed the tree but could not delete the marker, or a host that died before its first
    /// copy). A marker that cannot be acquired belongs to a live lease and is never touched.
    /// </summary>
    private static void ReapOrphanLeaseMarkers(string workspaceDirectory, ILogger logger)
    {
        IReadOnlyList<string> markers;
        try
        {
            markers = Directory.EnumerateFiles(workspaceDirectory, "*" + LeaseOwnerMarkerSuffix).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            logger.LogDebug(
                "Could not enumerate analyzer shadow lease markers; failure type {FailureType}.",
                ex.GetType().Name);
            return;
        }

        foreach (var markerPath in markers)
        {
            if (!markerPath.EndsWith(LeaseOwnerMarkerSuffix, StringComparison.Ordinal)) continue;
            var leaseDirectory = markerPath[..^LeaseOwnerMarkerSuffix.Length];
            if (Path.GetFileName(leaseDirectory).Length == 0 || Directory.Exists(leaseDirectory)) continue;

            if (TryAcquireLeaseOwnerMarker(leaseDirectory, logger, out var probe) != LeaseMarkerState.Acquired) continue;
            using (probe)
            {
                TryDeleteOwnerMarker(markerPath, logger);
            }
        }
    }

    private static void TryDeleteOwnerMarker(string markerPath, ILogger? logger)
    {
        try
        {
            File.Delete(markerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: a leftover released marker without a directory is reaped by the next sweep.
            logger?.LogDebug(
                "Could not delete an analyzer shadow lease marker; failure type {FailureType}.",
                ex.GetType().Name);
        }
    }

    private static void StartAbandonedRootSweepOnce(ILogger logger)
    {
        if (Interlocked.Exchange(ref s_sweepStarted, 1) != 0)
        {
            return;
        }

        // Off the load path: sweeping hundreds of stale directories from pre-fix runs can take
        // seconds, and workspace load latency must not pay for historical leaks.
        _ = Task.Run(() =>
        {
            try
            {
                SweepAbandonedRoots(logger);
            }
            catch (Exception ex)
            {
                logger.LogDebug(
                    "Abandoned analyzer shadow root sweep failed; failure type {FailureType}.",
                    ex.GetType().Name);
            }
        });
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string parent, ILogger logger)
    {
        try
        {
            return Directory.EnumerateDirectories(parent).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            logger.LogDebug(
                "Could not enumerate an analyzer shadow directory; failure type {FailureType}.",
                ex.GetType().Name);
            return [];
        }
    }

    private static bool IsStale(string directory, DateTime utcNow, ILogger logger)
    {
        try
        {
            // Newest of create/write time: shadow copies are written lazily after the root is
            // created, so a recently-populated root must not look abandoned.
            var lastActivityUtc = Directory.GetLastWriteTimeUtc(directory);
            var createdUtc = Directory.GetCreationTimeUtc(directory);
            if (createdUtc > lastActivityUtc)
            {
                lastActivityUtc = createdUtc;
            }

            return utcNow - lastActivityUtc > StaleLeaseAge;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cannot establish age — treat as live rather than risk deleting a running host's lease.
            logger.LogDebug(
                "Could not establish analyzer shadow lease age; treating the lease as live. Failure type {FailureType}.",
                ex.GetType().Name);
            return false;
        }
    }

    private static bool TryGetExistingFullPath(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> GetWorkspaceRoots(
        Solution solution,
        string workspaceId,
        ILogger logger)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddContainingDirectory(solution.FilePath, workspaceId, roots, logger);
        foreach (var project in solution.Projects)
        {
            AddContainingDirectory(project.FilePath, workspaceId, roots, logger);
        }

        return [.. roots];
    }

    private static void AddContainingDirectory(
        string? filePath,
        string workspaceId,
        HashSet<string> roots,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                roots.Add(directory);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            logger.LogWarning(
                "Workspace {WorkspaceId}: ignored an invalid solution/project path while determining analyzer isolation boundaries; failure type {FailureType}.",
                workspaceId,
                ex.GetType().Name);
        }
    }

    private static bool IsUnderAnyRoot(string candidatePath, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            var relative = Path.GetRelativePath(root, candidatePath);
            var firstSegment = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                2)[0];
            if (relative != "." && firstSegment != ".." && !Path.IsPathRooted(relative))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Owns one workspace load's shadow-copy loaders: the collectible
    /// <see cref="AssemblyLoadContext"/>s they created and the uniquely-keyed on-disk shadow
    /// root they copy into. <see cref="Dispose"/> is idempotent (eviction, explicit close, and
    /// host shutdown can all reach the same session): it unloads every context, containing
    /// context-unload failures, then best-effort deletes the lease root with a bounded GC-assisted
    /// retry. Unexpected loader cleanup failures propagate and leave the lease retryable. ALC unload is
    /// asynchronous; mapped shadow files stay locked until collection completes. A root that
    /// remains undeletable is logged at Debug and left for <see cref="SweepAbandonedRoots"/>.
    /// </summary>
    public sealed class AnalyzerShadowLoaderLease : IDisposable
    {
        private const int MaxDeleteAttempts = 4;

        /// <summary>Roots of leases still alive in THIS process; the sweeper never touches them.</summary>
        private static readonly ConcurrentDictionary<string, byte> s_liveRoots = new(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<ShadowCopyAnalyzerAssemblyLoader> _loaders;
        private readonly ILogger? _logger;
        private readonly object _disposeGate = new();
        private FileStream? _ownerMarker;
        private bool _disposed;

        /// <param name="ownerMarker">
        /// The lease's held ownership marker from <see cref="AcquireOwnerMarker"/>, taken before any
        /// reference was bound to this lease's loaders; the lease owns and releases it. Null exactly
        /// when <paramref name="shadowRoot"/> is null (the no-op lease).
        /// </param>
        internal AnalyzerShadowLoaderLease(
            string? shadowRoot,
            FileStream? ownerMarker,
            IReadOnlyList<ShadowCopyAnalyzerAssemblyLoader> loaders,
            int retargetedReferenceCount,
            ILogger? logger)
        {
            // A lease holds an ownership marker exactly when it owns a shadow root.
            if (shadowRoot is not null) ArgumentNullException.ThrowIfNull(ownerMarker);
            if (ownerMarker is not null) ArgumentNullException.ThrowIfNull(shadowRoot);

            _ownerMarker = ownerMarker;
            ShadowRoot = shadowRoot;
            _loaders = loaders;
            RetargetedReferenceCount = retargetedReferenceCount;
            _logger = logger;

            if (shadowRoot is not null)
            {
                s_liveRoots[shadowRoot] = 0;
            }
        }

        /// <summary>Number of analyzer references retargeted onto this lease's loaders.</summary>
        public int RetargetedReferenceCount { get; }

        /// <summary>This lease's unique shadow directory, or null for the no-op empty lease.</summary>
        public string? ShadowRoot { get; }

        /// <summary>No-op lease for the platforms/configurations where isolation cannot run.</summary>
        public static AnalyzerShadowLoaderLease CreateEmpty() => new(null, null, [], 0, null);

        internal static bool IsLiveRoot(string directory) =>
            s_liveRoots.ContainsKey(directory) ||
            string.Equals(directory, s_processShadowRoot, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Creates and holds <paramref name="shadowRoot"/>'s sibling ownership marker, exclusively.
        /// The lease directory itself does not exist until the first shadow copy, so a foreign
        /// sweeper can never enumerate a child of this lease before the marker is held. A failure
        /// propagates: a lease whose liveness cannot be proven must not exist.
        /// </summary>
        internal static FileStream AcquireOwnerMarker(string shadowRoot)
        {
            var markerPath = GetLeaseOwnerMarkerPath(shadowRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            return new FileStream(markerPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        }

        /// <summary>Releases a marker no lease ever took ownership of (no tree exists to protect).</summary>
        internal static void AbandonUnownedMarker(FileStream? marker, string shadowRoot, ILogger logger)
        {
            if (marker is null) return;
            marker.Dispose();
            TryDeleteOwnerMarker(GetLeaseOwnerMarkerPath(shadowRoot), logger);
        }

        public void Dispose()
        {
            lock (_disposeGate)
            {
                if (_disposed)
                {
                    return;
                }

                foreach (var loader in _loaders)
                {
                    loader.UnloadAndRelease();
                }

                // Drop the loader references BEFORE the GC-assisted delete retries: the loaders'
                // cached Assembly instances strongly root their collectible contexts, and this
                // lease (alive on the disposing stack) roots the loaders — without this release
                // the retry loop's collections could never complete the unload.
                _loaders = [];

                if (ShadowRoot is not null)
                {
                    var removed = TryDeleteShadowRoot();
                    s_liveRoots.TryRemove(ShadowRoot, out _);
                    ReleaseOwnerMarker(ShadowRoot, treeRemoved: removed);
                }

                // A failure in any cleanup step leaves the lease retryable. The lock also
                // prevents concurrent disposals from unloading or deleting the same root.
                _disposed = true;
            }
        }

        /// <summary>
        /// Closes the ownership marker. Only when the tree is gone is the marker file deleted too;
        /// a tree left behind keeps its now-released marker, which is exactly what authorizes a
        /// foreign sweeper (including a live peer's) to reclaim it later. A loader failure thrown
        /// before this point keeps the marker held, so the retryable lease stays protected.
        /// </summary>
        private void ReleaseOwnerMarker(string shadowRoot, bool treeRemoved)
        {
            var marker = _ownerMarker;
            _ownerMarker = null;
            if (marker is null) return;

            marker.Dispose();
            if (treeRemoved)
            {
                TryDeleteOwnerMarker(GetLeaseOwnerMarkerPath(shadowRoot), _logger);
            }
        }

        /// <returns>True when the lease directory no longer exists.</returns>
        private bool TryDeleteShadowRoot()
        {
            for (var attempt = 1; attempt <= MaxDeleteAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    // AssemblyLoadContext.Unload is asynchronous: the memory-mapped shadow
                    // assemblies stay locked until the collectible context is actually
                    // collected. Nudge the GC between bounded attempts.
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                try
                {
                    if (Directory.Exists(ShadowRoot))
                    {
                        NoFollowDirectoryDeleter.Delete(ShadowRoot, CancellationToken.None);
                    }

                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt == MaxDeleteAttempts)
                    {
                        _logger?.LogDebug(
                            "Analyzer shadow root is still locked after unload; leaving it for the abandoned-root sweeper. Failure type {FailureType}.",
                            ex.GetType().Name);
                    }
                }
            }

            return false;
        }
    }

    internal sealed class ShadowCopyAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        private readonly string _shadowRoot;
        private readonly ILogger _logger;
        private readonly bool _isCollectible;
        private readonly object _loadGate = new();
        private readonly ConcurrentDictionary<string, string> _dependenciesByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Assembly> _assembliesByIdentity = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _shadowCopiesByIdentity = new(StringComparer.OrdinalIgnoreCase);

        // Tracks every context this loader constructed. Collectible lease contexts are unloaded
        // at disposal; process-owned external contexts remain alive with their shared loader.
        private readonly ConcurrentBag<ShadowAnalyzerLoadContext> _contexts = [];

        private ShadowCopyAnalyzerAssemblyLoader(string shadowRoot, ILogger logger, bool isCollectible)
        {
            _shadowRoot = shadowRoot;
            _logger = logger;
            _isCollectible = isCollectible;
        }

        public static ShadowCopyAnalyzerAssemblyLoader Create(
            string analyzerPath, string shadowRoot, ILogger logger, bool isCollectible)
        {
            var loader = new ShadowCopyAnalyzerAssemblyLoader(shadowRoot, logger, isCollectible);
            loader.RegisterAnalyzerDirectory(analyzerPath);
            return loader;
        }

        /// <summary>
        /// Initiates unload of every load context this loader created and releases the cached
        /// <see cref="Assembly"/>/context references that would otherwise keep the collectible
        /// contexts strongly rooted (an <see cref="Assembly"/> roots its load context, so a
        /// loader that keeps its cache can never actually be collected). Unload is contained
        /// per-context: a third-party analyzer that leaked a rooted reference must degrade to
        /// "context never collects" (the sweeper reclaims the disk later), never fault
        /// workspace close.
        /// </summary>
        public void UnloadAndRelease()
        {
            foreach (var context in _contexts)
            {
                try
                {
                    context.Unload();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        "Failed to unload an analyzer shadow load context; failure type {FailureType}.",
                        ex.GetType().Name);
                }
            }

            _contexts.Clear();
            _assembliesByIdentity.Clear();
            _shadowCopiesByIdentity.Clear();
            _dependenciesByName.Clear();
        }

        public void AddDependencyLocation(string fullPath)
        {
            if (!TryGetExistingFullPath(fullPath, out var normalized))
            {
                return;
            }

            var simpleName = TryGetAssemblySimpleName(normalized) ?? Path.GetFileNameWithoutExtension(normalized);
            if (!string.IsNullOrWhiteSpace(simpleName))
            {
                _dependenciesByName[simpleName] = normalized;
            }
        }

        public Assembly LoadFromPath(string fullPath)
        {
            if (!TryGetExistingFullPath(fullPath, out var normalized))
            {
                throw new FileNotFoundException($"Analyzer assembly was not found: {fullPath}", fullPath);
            }

            AddDependencyLocation(normalized);
            var identity = BuildFileIdentity(normalized);
            lock (_loadGate)
            {
                if (_assembliesByIdentity.TryGetValue(identity, out var loaded))
                {
                    return loaded;
                }

                var context = new ShadowAnalyzerLoadContext(this);
                _contexts.Add(context);
                loaded = context.LoadFromAssemblyPath(ShadowCopy(normalized));
                _assembliesByIdentity[identity] = loaded;
                return loaded;
            }
        }

        private void RegisterAnalyzerDirectory(string analyzerPath)
        {
            AddDependencyLocation(analyzerPath);

            var directory = Path.GetDirectoryName(analyzerPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            foreach (var candidate in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                AddDependencyLocation(candidate);
            }
        }

        private bool TryResolveDependency(AssemblyName assemblyName, out string dependencyPath)
        {
            dependencyPath = string.Empty;
            if (string.IsNullOrWhiteSpace(assemblyName.Name))
            {
                return false;
            }

            if (!_dependenciesByName.TryGetValue(assemblyName.Name, out var foundPath))
            {
                return false;
            }

            dependencyPath = foundPath;
            return true;
        }

        private string ShadowCopy(string fullPath)
        {
            var identity = BuildFileIdentity(fullPath);
            lock (_loadGate)
            {
                if (_shadowCopiesByIdentity.TryGetValue(identity, out var existing))
                {
                    return existing;
                }

                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
                var targetDirectory = Path.Combine(_shadowRoot, hash);
                Directory.CreateDirectory(targetDirectory);

                var targetPath = Path.Combine(targetDirectory, Path.GetFileName(fullPath));
                // The gate serializes writers within this process. Overwrite also repairs a
                // partial copy left by an interrupted prior attempt for this identity.
                File.Copy(fullPath, targetPath, overwrite: true);
                _shadowCopiesByIdentity[identity] = targetPath;
                return targetPath;
            }
        }

        private static string BuildFileIdentity(string fullPath)
        {
            var info = new FileInfo(fullPath);
            return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }

        private static string? TryGetAssemblySimpleName(string fullPath)
        {
            try
            {
                return AssemblyName.GetAssemblyName(fullPath).Name;
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                return null;
            }
        }

        private Assembly? Resolve(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            foreach (var loaded in AssemblyLoadContext.Default.Assemblies)
            {
                if (AssemblyName.ReferenceMatchesDefinition(loaded.GetName(), assemblyName))
                {
                    return loaded;
                }
            }

            if (!TryResolveDependency(assemblyName, out var dependencyPath))
            {
                return null;
            }

            try
            {
                return context.LoadFromAssemblyPath(ShadowCopy(dependencyPath));
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or FileLoadException)
            {
                _logger.LogDebug(
                    "Failed to shadow-load an analyzer dependency; failure type {FailureType}.",
                    ex.GetType().Name);
                return null;
            }
        }

        private sealed class ShadowAnalyzerLoadContext : AssemblyLoadContext
        {
            private readonly ShadowCopyAnalyzerAssemblyLoader _loader;

            // Workspace loaders are collectible so the owning lease can unload them; external
            // loaders are process-owned because Roslyn may retain their reflection state.
            // Collectibility does NOT change Assembly.Location:
            // loading stays LoadFromAssemblyPath over an on-disk shadow copy, preserving
            // adjacent-resource and native-dependency probing (the LoadFromStream shortcut is
            // deliberately not taken — see ValidationIntegrationTests' Location assertion).
            public ShadowAnalyzerLoadContext(ShadowCopyAnalyzerAssemblyLoader loader)
                : base(isCollectible: loader._isCollectible)
            {
                _loader = loader;
            }

            protected override Assembly? Load(AssemblyName assemblyName) =>
                _loader.Resolve(this, assemblyName);
        }
    }
}
