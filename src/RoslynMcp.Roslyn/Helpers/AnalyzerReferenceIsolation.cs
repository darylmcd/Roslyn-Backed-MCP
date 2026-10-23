using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Roslyn.Helpers;

internal static class AnalyzerReferenceIsolation
{
    /// <summary>Directory name under the OS temp path shared by all workspaces (each lease gets its own subtree).</summary>
    internal const string ShadowRootDirectoryName = "RoslynMcpAnalyzerShadow";
    private const string ProcessOwnerFileName = ".owner.lock";

    /// <summary>
    /// A shadow root older than this is eligible for sweeping. A live process-owned root is
    /// excluded by its held ownership marker even if no assembly is currently mapped from it.
    /// Other processes' live lease roots remain protected by their loaded shadow assemblies.
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
    private static readonly string s_processShadowRoot = Path.Combine(
        Path.GetTempPath(), ShadowRootDirectoryName,
        "process-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"), "copies");
    private sealed record ProcessShadowNamespace(PhysicalDirectoryScope.OwnedRoot Owner, FileStream Marker);
    // Retain the exact admitted namespace and marker for the entire process lifetime.
    private static readonly Lazy<ProcessShadowNamespace> s_processOwner = new(() =>
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Analyzer shadow isolation is Windows-specific.");
        var processDirectory = Path.GetDirectoryName(s_processShadowRoot)!;
        var owner = CreateShadowRoot([ShadowRootDirectoryName], Path.GetFileName(processDirectory), CancellationToken.None);
        FileStream? marker = null;
        try
        {
            owner.EnsureDirectories("copies", CancellationToken.None);
            owner.AssertCurrentPath(CancellationToken.None);
            marker = new FileStream(Path.Combine(processDirectory, ProcessOwnerFileName), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None);
            return new(owner, marker);
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.DisposePreservingFailure("Analyzer process marker acquisition failed.", failure, marker,
                new AnalyzerShadowLoaderLease(owner, [], 0, null));
            throw;
        }
    }, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly ConcurrentDictionary<string, PhysicalDirectoryScope.OwnedRoot> s_externalOwners = new(StringComparer.OrdinalIgnoreCase);

    private static PhysicalDirectoryScope.OwnedRoot CreateShadowRoot(IReadOnlyList<string> sharedNames, string leaf, CancellationToken ct)
    {
        PhysicalDirectoryScope? temporary = PhysicalDirectoryScope.OpenRoute(Path.GetTempPath());
        PhysicalDirectoryScope? parent = null;
        Exception? primary = null;
        try
        {
            parent = PhysicalDirectoryScope.OpenOrCreateDescendant(temporary, sharedNames, ct);
            var releasing = temporary;
            temporary = null;
            releasing.Dispose();
            var transferred = parent;
            parent = null;
            return PhysicalDirectoryScope.CreateOwnedChild(transferred, leaf);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Analyzer shared parent release failed.", primary, parent, temporary); }
    }

    private static readonly ConcurrentDictionary<string, ShadowCopyAnalyzerAssemblyLoader> s_externalLoaders =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim s_externalGate = new(1, 1);

    internal enum BindingStatus { Bound, PreloadedImageUnbound, ReflectionUnavailable }
    internal sealed class BindingPreflight
    {
        internal BindingStatus Status { get; }
        private readonly Solution _solution;
        private readonly AnalyzerInputCatalog _catalog;
        private readonly ImmutableArray<(AnalyzerFileReference Reference, object? Loader, object? Assembly)> _states;
        internal BindingPreflight(Solution solution, AnalyzerInputCatalog catalog, BindingStatus status,
            ImmutableArray<(AnalyzerFileReference, object?, object?)> states)
            => (_solution, _catalog, Status, _states) = (solution, catalog, status, states);

        internal BindingStatus AssertCurrent(Solution solution, AnalyzerInputCatalog catalog, CancellationToken ct)
        {
            if (!ReferenceEquals(solution, _solution) || !ReferenceEquals(catalog, _catalog))
                throw InvalidOperationErrors.Internal("Analyzer binding preflight belongs to a different snapshot.");
            if (Status != BindingStatus.Bound || !OperatingSystem.IsWindows()) return Status;
            foreach (var state in _states)
            {
                ct.ThrowIfCancellationRequested();
                if (!ReferenceEquals(AssemblyLoaderField!.GetValue(state.Reference), state.Loader)
                    || !ReferenceEquals(LazyAssemblyField!.GetValue(state.Reference), state.Assembly))
                    return BindingStatus.PreloadedImageUnbound;
            }
            return CaptureBindingPreflight(solution, catalog, ct).Status;
        }
    }

    internal sealed class RetargetResult(Solution solution, AnalyzerShadowLoaderLease lease, BindingStatus status,
        ImmutableArray<OwnedAnalyzerReference> references)
    {
        internal Solution Solution { get; } = solution;
        internal AnalyzerShadowLoaderLease Lease { get; } = lease;
        internal BindingStatus Status { get; } = status;
        internal void AssertCurrent(CancellationToken ct)
        {
            if (Status != BindingStatus.Bound) throw new PublicInvalidOperationException("Analyzer executable image binding is unavailable.");
            foreach (var reference in references) reference.AssertCurrent(ct);
        }
    }

    /// <summary>Preserves source provenance while every native metadata and executable read uses held shadow bytes.</summary>
    internal sealed class OwnedAnalyzerReference : AnalyzerReference
    {
        private readonly ShadowCopyAnalyzerAssemblyLoader _loader;
        private readonly string _key;
        internal AnalyzerFileReference FileReference { get; }
        internal OwnedAnalyzerReference(string originalPath, string shadowPath, ShadowCopyAnalyzerAssemblyLoader loader, string key)
        {
            FullPath = originalPath;
            _loader = loader;
            _key = key;
            FileReference = new AnalyzerFileReference(shadowPath, loader);
        }
        public override string FullPath { get; }
        public override object Id => Read(() => FileReference.Id);
        public override string Display => Read(() => FileReference.Display);
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => Read(() => FileReference.GetAnalyzers(language));
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => Read(FileReference.GetAnalyzersForAllLanguages);
        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) => Read(() => FileReference.GetGenerators(language));
        public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages() => Read(FileReference.GetGeneratorsForAllLanguages);
        private T Read<T>(Func<T> read) => _loader.WithPrepared(FullPath, FileReference.FullPath, _key, () =>
        {
            AssertImageBinding(CancellationToken.None);
            return read();
        });
        internal T WithFileReference<T>(Func<AnalyzerFileReference, T> read) => Read(() => read(FileReference));
        internal void AssertCurrent(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _loader.WithPrepared(FullPath, FileReference.FullPath, _key, () =>
            {
                AssertImageBinding(ct);
                return true;
            });
        }
        private void AssertImageBinding(CancellationToken ct)
        {
            var image = LazyAssemblyField!.GetValue(FileReference);
            if (!ReferenceEquals(AssemblyLoaderField!.GetValue(FileReference), _loader)
                || image is not null && (image is not Assembly assembly || !_loader.ProvesImage(FullPath, assembly, _key, ct)))
                throw new PublicInvalidOperationException("An owned analyzer reference lost its executable image binding.");
        }
    }

    internal static AnalyzerFileReference? GetFileReference(AnalyzerReference reference) => reference switch
    {
        OwnedAnalyzerReference owned => owned.FileReference,
        AnalyzerFileReference file => file,
        _ => null
    };

    internal static T WithFileReference<T>(AnalyzerReference reference, Func<AnalyzerFileReference, T> read) => reference switch
    {
        OwnedAnalyzerReference owned => owned.WithFileReference(read),
        AnalyzerFileReference file => read(file),
        _ => throw InvalidOperationErrors.Internal("The analyzer reference has no file-backed provider binding.")
    };

    internal static BindingPreflight CaptureBindingPreflight(Solution solution, AnalyzerInputCatalog catalog, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return new(solution, catalog, BindingStatus.Bound, []);
        if (AssemblyLoaderField is null || LazyAssemblyField is null
            || !typeof(IAnalyzerAssemblyLoader).IsAssignableFrom(AssemblyLoaderField.FieldType)
            || !typeof(Assembly).IsAssignableFrom(LazyAssemblyField.FieldType))
            return new(solution, catalog, BindingStatus.ReflectionUnavailable, []);
        var states = ImmutableArray.CreateBuilder<(AnalyzerFileReference, object?, object?)>();
        foreach (var reference in solution.Projects.SelectMany(project => project.AnalyzerReferences).OfType<AnalyzerFileReference>()
                     .Distinct<AnalyzerFileReference>(ReferenceEqualityComparer.Instance))
        {
            ct.ThrowIfCancellationRequested();
            var loader = AssemblyLoaderField.GetValue(reference);
            var assembly = LazyAssemblyField.GetValue(reference);
            states.Add((reference, loader, assembly));
            if (!catalog.Entries.Any(entry => FileSystemPath.Comparer.Equals(entry.Path, reference.FullPath)))
                return new(solution, catalog, BindingStatus.PreloadedImageUnbound, states.ToImmutable());
            if (assembly is null) continue;
            var key = AnalyzerInputCatalog.ContentIdentity(catalog.DirectoryEntries(reference.FullPath), ct);
            if (assembly is not Assembly image || loader is not ShadowCopyAnalyzerAssemblyLoader owned
                || !s_externalLoaders.TryGetValue(key, out var registered) || !ReferenceEquals(registered, owned)
                || !s_externalOwners.TryGetValue(key, out var owner)
                || !owned.ProvesPermanentImage(reference.FullPath, image, key, ct))
                return new(solution, catalog, BindingStatus.PreloadedImageUnbound, states.ToImmutable());
            owner.AssertCurrentPath(ct);
            s_processOwner.Value.Owner.AssertCurrentPath(ct);
        }
        return new(solution, catalog, BindingStatus.Bound, states.ToImmutable());
    }

    /// <summary>Builds a private analyzer projection; native references are never mutated.</summary>
    public static async Task<RetargetResult> RetargetFileReferencesToShadowLoaderAsync(
        Solution original, Solution projection, string workspaceId, AnalyzerInputCatalog catalog,
        BindingPreflight preflight, ILogger logger, CancellationToken ct)
    {
        var binding = preflight.AssertCurrent(original, catalog, ct);
        if (binding != BindingStatus.Bound || !OperatingSystem.IsWindows())
            return new(projection, AnalyzerShadowLoaderLease.CreateEmpty(), binding, []);
        StartAbandonedRootSweepOnce(logger);
        var workspaceRoots = GetWorkspaceRoots(original, workspaceId, logger);
        var leaseId = Guid.NewGuid().ToString("N");
        var loaders = new Dictionary<string, ShadowCopyAnalyzerAssemblyLoader>(StringComparer.OrdinalIgnoreCase);
        var installed = new List<OwnedAnalyzerReference>();
        PhysicalDirectoryScope.OwnedRoot? shadowOwner = null;
        AnalyzerShadowLoaderLease? preparedLease = null;
        var retargeted = 0;
        try
        {
            var result = projection;
            foreach (var project in projection.Projects)
            {
                var desired = new List<AnalyzerReference>();
                foreach (var reference in project.AnalyzerReferences)
                {
                    ct.ThrowIfCancellationRequested();
                    if (reference is not AnalyzerFileReference file)
                    {
                        desired.Add(reference);
                        continue;
                    }
                    var path = Path.GetFullPath(file.FullPath);
                    var entries = catalog.DirectoryEntries(path);
                    if (!entries.Any(entry => FileSystemPath.Comparer.Equals(entry.Path, path)))
                        throw new PublicInvalidOperationException("An analyzer has no admitted executable image.");
                    var key = AnalyzerInputCatalog.ContentIdentity(entries, ct);
                    ShadowCopyAnalyzerAssemblyLoader loader;
                    if (!IsUnderAnyRoot(path, workspaceRoots))
                    {
                        await s_externalGate.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            if (s_externalLoaders.TryGetValue(key, out var existing)) loader = existing;
                            else
                            {
                                var process = s_processOwner.Value;
                                process.Owner.AssertCurrentPath(ct);
                                var copyOwner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(s_processShadowRoot, key + "-" + Guid.NewGuid().ToString("N")));
                                try
                                {
                                    loader = await ShadowCopyAnalyzerAssemblyLoader.CreateAsync(entries, copyOwner, string.Empty,
                                        logger, isCollectible: false, ct).ConfigureAwait(false);
                                    s_externalOwners[key] = copyOwner;
                                }
                                catch (Exception failure)
                                {
                                    OwnedResourceCleanup.DisposePreservingFailure("Process analyzer preparation cleanup failed.", failure,
                                        new AnalyzerShadowLoaderLease(copyOwner, [], 0, logger));
                                    throw;
                                }
                                s_externalLoaders[key] = loader;
                            }
                        }
                        finally { s_externalGate.Release(); }
                    }
                    else
                    {
                        if (loaders.TryGetValue(key, out var existingOwned)) loader = existingOwned;
                        else
                        {
                            shadowOwner ??= CreateShadowRoot([ShadowRootDirectoryName, workspaceId], leaseId, ct);
                            loader = await ShadowCopyAnalyzerAssemblyLoader.CreateAsync(entries, shadowOwner, key, logger,
                                isCollectible: true, ct).ConfigureAwait(false);
                            loaders[key] = loader;
                        }
                        retargeted++;
                    }
                    var owned = new OwnedAnalyzerReference(path, loader.GetPreparedPath(path, ct), loader, key);
                    installed.Add(owned);
                    desired.Add(owned);
                }
                // AnalyzerFileReference.Equals can compare a different reference kind
                // by FullPath. Clear first so equality cannot preserve the native object.
                result = result.WithProjectAnalyzerReferences(project.Id, [])
                    .WithProjectAnalyzerReferences(project.Id, desired);
                if (!result.GetProject(project.Id)!.AnalyzerReferences.SequenceEqual(desired, ReferenceEqualityComparer.Instance))
                    throw InvalidOperationErrors.Internal("The private analyzer projection retained an unowned reference.");
            }
            preparedLease = new AnalyzerShadowLoaderLease(shadowOwner, [.. loaders.Values], retargeted, logger);
            var prepared = new RetargetResult(result, preparedLease, BindingStatus.Bound, [.. installed]);
            prepared.AssertCurrent(ct);
            return prepared;
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.DisposePreservingFailure("Analyzer isolation preparation cleanup failed.", failure,
                preparedLease ?? new AnalyzerShadowLoaderLease(shadowOwner, [.. loaders.Values], retargeted, logger));
            throw;
        }
    }

    /// <summary>
    /// Best-effort removal of shadow roots abandoned by earlier crashed or pre-fix hosts, so
    /// per-lease disposal does not leave historical leaks in place forever. Only lease
    /// directories older than <see cref="StaleLeaseAge"/> that are not owned by a live lease in
    /// this process are candidates; a live lease in another process keeps its shadow assemblies
    /// locked, so its deletion fails harmlessly. The shared parent and per-workspace parents of
    /// live leases are never deleted.
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

        try
        {
            using var route = PhysicalDirectoryScope.OpenRoute(sharedParent);
            var utcNow = DateTime.UtcNow;
            foreach (var workspaceName in route.Entries())
            {
                try
                {
                    using var workspacePin = route.OpenChild(workspaceName);
                    if (workspacePin is null) continue;
                    IDisposable? ownerProbe = null;
                    var hadOwner = false;
                    if (workspaceName.StartsWith("process-", StringComparison.OrdinalIgnoreCase))
                    {
                        ownerProbe = workspacePin.LockOwner(ProcessOwnerFileName);
                        hadOwner = ownerProbe is not null;
                    }

                    var retainedNames = new HashSet<string>(FileSystemPath.Comparer);
                    try
                    {
                        foreach (var leaseName in workspacePin.Entries())
                        {
                            var leaseDirectory = Path.Combine(workspacePin.Path, leaseName);
                            if (AnalyzerShadowLoaderLease.IsLiveRoot(leaseDirectory)) continue;
                            try
                            {
                                using var leasePin = workspacePin.OpenChild(leaseName);
                                if (leasePin is null || utcNow - leasePin.LastActivityUtc <= StaleLeaseAge) continue;
                                if (!OperatingSystem.IsWindows() && !leasePin.HasReclaimableContent(CancellationToken.None))
                                {
                                    // A content-free skeleton confers no further cleanup work.
                                    retainedNames.Add(leaseName);
                                    continue;
                                }
                                leasePin.DeleteChildren(CancellationToken.None);
                                var outcome = workspacePin.RemoveHeldChild(leaseName, leasePin, CancellationToken.None);
                                if (outcome == PhysicalDirectoryCleanupOutcome.Retained)
                                {
                                    retainedNames.Add(leaseName);
                                    logger.LogDebug("Reclaimed abandoned analyzer shadow content; retained directory namespace.");
                                }
                                else logger.LogDebug("Swept one abandoned analyzer shadow lease.");
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PublicInvalidOperationException)
                            {
                                logger.LogDebug("Could not sweep an abandoned analyzer shadow lease; failure type {FailureType}.", ex.GetType().Name);
                            }
                        }
                    }
                    finally { ownerProbe?.Dispose(); }

                    if (hadOwner && workspacePin.Entries().All(name => name == ProcessOwnerFileName || retainedNames.Contains(name)))
                        workspacePin.TryDeleteFile(ProcessOwnerFileName, CancellationToken.None);
                    if (workspacePin.Entries().Count == 0)
                    {
                        var outcome = route.RemoveHeldChild(workspaceName, workspacePin, CancellationToken.None);
                        if (outcome == PhysicalDirectoryCleanupOutcome.Retained)
                            logger.LogDebug("An empty analyzer workspace namespace was retained safely.");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PublicInvalidOperationException)
                {
                    logger.LogDebug("An analyzer shadow workspace root is owned or unavailable; failure type {FailureType}.", ex.GetType().Name);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PublicInvalidOperationException)
        {
            logger.LogDebug("An analyzer shadow shared root is unavailable; failure type {FailureType}.", ex.GetType().Name);
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
    /// host shutdown can all reach the same session): it unloads every context — containing any
    /// failure so a third-party analyzer's leaked root can never fault workspace close — then
    /// best-effort deletes the lease root with a bounded GC-assisted retry (ALC unload is
    /// asynchronous; mapped shadow files stay locked until collection completes). A root that
    /// remains undeletable is logged at Debug and left for <see cref="SweepAbandonedRoots"/>.
    /// </summary>
    public sealed class AnalyzerShadowLoaderLease : IDisposable
    {
        private const int MaxDeleteAttempts = 4;

        /// <summary>Roots of leases still alive in THIS process; the sweeper never touches them.</summary>
        private static readonly ConcurrentDictionary<string, byte> s_liveRoots = new(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<ShadowCopyAnalyzerAssemblyLoader> _loaders;
        private PhysicalDirectoryScope.OwnedRoot? _cleanupRoot;
        private readonly ILogger? _logger;
        private int _disposed;

        internal AnalyzerShadowLoaderLease(
            PhysicalDirectoryScope.OwnedRoot? cleanupRoot,
            IReadOnlyList<ShadowCopyAnalyzerAssemblyLoader> loaders,
            int retargetedReferenceCount,
            ILogger? logger)
        {
            ShadowRoot = cleanupRoot?.Path;
            _cleanupRoot = cleanupRoot;
            _loaders = loaders;
            RetargetedReferenceCount = retargetedReferenceCount;
            _logger = logger;

            if (ShadowRoot is not null) s_liveRoots[ShadowRoot] = 0;
        }

        /// <summary>Number of analyzer references retargeted onto this lease's loaders.</summary>
        public int RetargetedReferenceCount { get; }

        /// <summary>This lease's unique shadow directory, or null for the no-op empty lease.</summary>
        public string? ShadowRoot { get; }

        /// <summary>No-op lease for the platforms/configurations where isolation cannot run.</summary>
        public static AnalyzerShadowLoaderLease CreateEmpty() => new(null, [], 0, null);

        internal static bool IsLiveRoot(string directory) =>
            s_liveRoots.ContainsKey(directory) ||
            string.Equals(directory, s_processShadowRoot, StringComparison.OrdinalIgnoreCase);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var failures = new List<Exception>();
            foreach (var loader in _loaders)
            {
                try { loader.UnloadAndRelease(); }
                catch (Exception failure) { failures.Add(failure); }
            }

            // Drop the loader references BEFORE the GC-assisted delete retries: the loaders'
            // cached Assembly instances strongly root their collectible contexts, and this
            // lease (alive on the disposing stack) roots the loaders — without this release
            // the retry loop's collections could never complete the unload.
            _loaders = [];

            if (ShadowRoot is null)
            {
                if (failures.Count > 0) throw new AggregateException("Analyzer shadow handle release failed.", failures);
                return;
            }

            s_liveRoots.TryRemove(ShadowRoot, out _);
            try { TryDeleteShadowRoot(); }
            catch (Exception failure) { failures.Add(failure); }
            try { _cleanupRoot?.Dispose(); }
            catch (Exception failure) { failures.Add(failure); }
            _cleanupRoot = null;
            if (failures.Count > 0) throw new AggregateException("Analyzer shadow handle release failed.", failures);
        }

        internal PhysicalDirectoryCleanupOutcome? CleanupOutcome { get; private set; }

        private void TryDeleteShadowRoot()
        {
            if (_cleanupRoot is null) return;
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
                    CleanupOutcome = _cleanupRoot.Delete(CancellationToken.None);
                    if (CleanupOutcome == PhysicalDirectoryCleanupOutcome.Retained)
                        _logger?.LogDebug("Analyzer shadow content was reclaimed; its directory namespace was retained safely.");
                    return;
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
        }
    }

    internal sealed class ShadowCopyAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        private readonly ILogger _logger;
        private readonly bool _isCollectible;
        private string _catalogIdentity = string.Empty;
        private readonly object _loadGate = new();
        private bool _released;
        private bool _closing;
        private int _activeBorrows;
        private readonly ConcurrentDictionary<string, string> _preparedBySource = new(FileSystemPath.Comparer);
        private readonly HashSet<string> _preparedShadows = new(FileSystemPath.Comparer);
        private readonly List<FileStream> _imageHandles = [];
        private IDisposable? _directoryRoute;
        private sealed record Dependency(string Path, string Identity);
        private ImmutableArray<Dependency> _dependencies = [];
        private readonly ConcurrentDictionary<string, Assembly> _assembliesByIdentity = new(StringComparer.OrdinalIgnoreCase);

        // Tracks every context this loader constructed. Collectible lease contexts are unloaded
        // at disposal; process-owned external contexts remain alive with their shared loader.
        private readonly ConcurrentBag<ShadowAnalyzerLoadContext> _contexts = [];

        // These exact host objects establish shared type identity. Other Default
        // assemblies are not evidence that admitted custom executable bytes ran.
        private static readonly Assembly[] SharedContracts = new[]
        {
            typeof(object).Assembly, typeof(ImmutableArray<>).Assembly, typeof(Compilation).Assembly,
            typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly, typeof(AnalyzerFileReference).Assembly,
            typeof(Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider).Assembly,
            typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions).Assembly,
            typeof(System.Reflection.Metadata.MetadataReader).Assembly,
            typeof(System.Composition.ExportAttribute).Assembly
        }.Distinct<Assembly>(ReferenceEqualityComparer.Instance).ToArray();
        private static readonly string RuntimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw InvalidOperationErrors.Internal("The trusted host runtime has no installation coordinate.");
        // TPA includes application assemblies. Only direct members of the actual
        // host runtime installation qualify for the existing runtime boundary.
        private static readonly HashSet<string> RuntimeNames = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => FileSystemPath.Comparer.Equals(Path.GetDirectoryName(path), RuntimeDirectory))
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        private ShadowCopyAnalyzerAssemblyLoader(ILogger logger, bool isCollectible)
        {
            _logger = logger;
            _isCollectible = isCollectible;
        }

        public static async Task<ShadowCopyAnalyzerAssemblyLoader> CreateAsync(
            IReadOnlyList<AnalyzerInputCatalog.Entry> entries, PhysicalDirectoryScope.OwnedRoot owner, string relativeDirectory, ILogger logger, bool isCollectible, CancellationToken ct)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Analyzer shadow isolation is Windows-specific.");
            var loader = new ShadowCopyAnalyzerAssemblyLoader(logger, isCollectible);
            loader._catalogIdentity = AnalyzerInputCatalog.ContentIdentity(entries, ct);
            try
            {
                owner.AssertCurrentPath(ct);
                owner.EnsureDirectories(relativeDirectory, ct);
                var shadowRoot = Path.Combine(owner.Path, relativeDirectory);
                loader._directoryRoute = PhysicalDirectoryLease.OpenRouteWindows(shadowRoot);
                owner.AssertCurrentPath(ct);
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = Path.Combine(shadowRoot, Path.GetFileName(entry.Path));
                    WorkspaceManager.EnsurePhysicalPath(target);
                    // This catalogue has one private materialization. Never overwrite a raced file.
                    await using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
                        4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (!loader._preparedBySource.TryAdd(entry.Path, target))
                        throw InvalidOperationErrors.Internal("An analyzer catalog repeated a physical input.");
                    if (!loader._preparedShadows.Add(target))
                        throw InvalidOperationErrors.Internal("An analyzer catalog repeated a shadow filename.");
                    await stream.WriteAsync(entry.Bytes.AsMemory(), ct).ConfigureAwait(false);
                    // Hold exact owned bytes against writes/deletes until this loader is released.
                    // Pinned directory ancestry also prevents renaming the route and replacing it.
                    await stream.FlushAsync(ct).ConfigureAwait(false);
                    // Runtime image loading does not share writer access. Transfer through a
                    // read-only, deny-delete view of the SAME file object before releasing the
                    // writer, then acquire deny-write access. Never resolve the file entry again.
                    using var intermediate = OwnedShadowImage.OpenReadViewWindows(stream.SafeFileHandle, FileShare.ReadWrite);
                    await stream.DisposeAsync().ConfigureAwait(false);
                    var held = OwnedShadowImage.OpenReadViewWindows(intermediate.SafeFileHandle, FileShare.Read);
                    loader._imageHandles.Add(held);
                    intermediate.Dispose();
                    var hash = await StreamContentHash.ComputeSha256Async(held, ct).ConfigureAwait(false);
                    if (!string.Equals(Convert.ToHexString(hash), entry.ContentHash, StringComparison.Ordinal))
                        throw new PublicInvalidOperationException("An owned analyzer shadow image changed during preparation.");
                    held.Position = 0;
                    WorkspaceManager.EnsurePhysicalPath(target);
                    if (entry.AssemblyIdentity is { } identity) loader._dependencies = loader._dependencies.Add(new(target, identity));
                }
                return loader;
            }
            catch (Exception failure)
            {
                // The caller retains the SAME pre-effect owner and reclaims its namespace
                // only after every borrowed loader has released its handles.
                try { loader.UnloadAndRelease(); }
                catch (Exception cleanup) { OwnedResourceCleanup.AttachOutcomes(failure, ["AnalyzerShadowHandles:" + cleanup.GetType().Name]); }
                throw;
            }
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
            lock (_loadGate)
            {
                if (_released) return;
                _closing = true;
                while (_activeBorrows != 0) Monitor.Wait(_loadGate);
                if (_released) return;
                _released = true;
                foreach (var context in _contexts)
                {
                    try { context.Unload(); }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(
                            "Failed to unload an analyzer shadow load context; failure type {FailureType}.",
                            ex.GetType().Name);
                    }
                }
                _contexts.Clear();
                _assembliesByIdentity.Clear();
                _dependencies = [];
                _preparedBySource.Clear();
                _preparedShadows.Clear();
                var resources = _imageHandles.Cast<IDisposable?>().Reverse().Append(_directoryRoute).ToArray();
                _imageHandles.Clear();
                _directoryRoute = null;
                OwnedResourceCleanup.Dispose("Analyzer shadow handle release failed.", null, resources);
            }
        }

        public void AddDependencyLocation(string fullPath)
        {
            // Roslyn may repeat registrations after publication. Every usable dependency
            // was already admitted and materialized; this callback performs no source IO.
            lock (_loadGate)
            {
                ObjectDisposedException.ThrowIf(_released, this);
                var path = Path.GetFullPath(fullPath);
                if (!_preparedBySource.ContainsKey(path) && !_preparedShadows.Contains(path))
                    throw new FileNotFoundException("The analyzer dependency has no admitted shadow image.");
            }
        }

        public Assembly LoadFromPath(string fullPath)
        {
            lock (_loadGate)
            {
                ObjectDisposedException.ThrowIf(_released, this);
                var path = Path.GetFullPath(fullPath);
                if (!_preparedBySource.TryGetValue(path, out var prepared) && !_preparedShadows.Contains(prepared = path))
                    throw new FileNotFoundException("The analyzer assembly has no admitted shadow image.");
                if (_assembliesByIdentity.TryGetValue(prepared, out var loaded))
                {
                    return loaded;
                }

                var context = new ShadowAnalyzerLoadContext(this);
                _contexts.Add(context);
                loaded = context.LoadFromAssemblyPath(prepared);
                _assembliesByIdentity[prepared] = loaded;
                return loaded;
            }
        }

        internal string GetPreparedPath(string source, CancellationToken ct)
        {
            lock (_loadGate)
            {
                ct.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_released, this);
                return _preparedBySource.TryGetValue(source, out var prepared) ? prepared
                    : throw new PublicInvalidOperationException("An analyzer has no held shadow image.");
            }
        }

        internal void AssertPrepared(string source, string shadow, string key, CancellationToken ct)
        {
            lock (_loadGate)
            {
                ct.ThrowIfCancellationRequested();
                if (_closing || _released || _directoryRoute is null || _catalogIdentity != key
                    || _imageHandles.Count != _preparedBySource.Count
                    || !_preparedBySource.TryGetValue(source, out var prepared) || !FileSystemPath.Comparer.Equals(shadow, prepared))
                    throw new PublicInvalidOperationException("An analyzer shadow image lifetime ended before publication.");
            }
        }

        internal T WithPrepared<T>(string source, string shadow, string key, Func<T> read)
        {
            lock (_loadGate)
            {
                AssertPrepared(source, shadow, key, CancellationToken.None);
                checked { _activeBorrows++; }
            }
            try
            {
                // Native discovery can construct third-party types whose workers
                // resolve admitted dependencies. Keep the lifetime, not the monitor.
                return read();
            }
            finally
            {
                lock (_loadGate)
                {
                    _activeBorrows--;
                    if (_activeBorrows == 0) Monitor.PulseAll(_loadGate);
                }
            }
        }

        internal bool ProvesPermanentImage(string source, Assembly image, string catalogIdentity, CancellationToken ct)
        {
            lock (_loadGate)
            {
                return !_isCollectible && ProvesImage(source, image, catalogIdentity, ct);
            }
        }

        internal bool ProvesImage(string source, Assembly image, string catalogIdentity, CancellationToken ct)
        {
            lock (_loadGate)
            {
                ct.ThrowIfCancellationRequested();
                return !_released && _directoryRoute is not null
                    && _catalogIdentity == catalogIdentity && _imageHandles.Count == _preparedBySource.Count
                    && _preparedBySource.TryGetValue(Path.GetFullPath(source), out var target)
                    && _assembliesByIdentity.TryGetValue(target, out var loaded) && ReferenceEquals(image, loaded);
            }
        }

        internal bool TryResolveDependency(AssemblyName assemblyName, out string dependencyPath)
        {
            dependencyPath = string.Empty;
            if (string.IsNullOrWhiteSpace(assemblyName.Name))
            {
                return false;
            }

            var candidates = _dependencies;
            var selected = AnalyzerAssemblyBinding.Select(assemblyName, candidates.Select(item => new AssemblyName(item.Identity)).ToArray());
            if (selected < 0) return false;
            dependencyPath = candidates[selected].Path;
            return true;
        }

        private Assembly? Resolve(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            lock (_loadGate)
            {
                ObjectDisposedException.ThrowIf(_released, this);
                var selected = AnalyzerAssemblyBinding.Select(assemblyName, SharedContracts.Select(assembly => assembly.GetName()).ToArray());
                if (selected >= 0) return SharedContracts[selected];
                if (TryResolveDependency(assemblyName, out var dependencyPath))
                {
                    // A selected admitted image must either load or fail. Returning
                    // null here would let CLR fallback select a foreign Default image.
                    return context.LoadFromAssemblyPath(dependencyPath);
                }
                if (assemblyName.Name is { } name && RuntimeNames.Contains(name))
                {
                    var runtime = AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
                    if (ReferenceEquals(AssemblyLoadContext.GetLoadContext(runtime), AssemblyLoadContext.Default)
                        && !runtime.IsDynamic && FileSystemPath.Comparer.Equals(Path.GetDirectoryName(runtime.Location), RuntimeDirectory)
                        && AnalyzerAssemblyBinding.Compatible(assemblyName, runtime.GetName())) return runtime;
                    throw new FileLoadException("The runtime assembly did not bind within the trusted host installation.");
                }
                throw new FileNotFoundException("An analyzer dependency has no admitted executable image or trusted host binding.");
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
