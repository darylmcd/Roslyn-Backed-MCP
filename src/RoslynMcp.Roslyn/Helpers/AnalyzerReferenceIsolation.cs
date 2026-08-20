using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

namespace RoslynMcp.Roslyn.Helpers;

internal static class AnalyzerReferenceIsolation
{
    /// <summary>Directory name under the OS temp path shared by all workspaces (each lease gets its own subtree).</summary>
    internal const string ShadowRootDirectoryName = "RoslynMcpAnalyzerShadow";

    /// <summary>
    /// A lease root older than this is assumed abandoned (a crashed or killed host) and is
    /// eligible for sweeping. Deliberately long: a live lease in ANOTHER process may be older,
    /// but its loaded shadow assemblies keep the files locked so deletion fails harmlessly.
    /// Mirrors the abandoned-run reaper discipline in <c>TestTempRoot</c>.
    /// </summary>
    private static readonly TimeSpan StaleLeaseAge = TimeSpan.FromDays(1);

    private static readonly FieldInfo? AssemblyLoaderField = typeof(AnalyzerFileReference)
        .GetField("_assemblyLoader", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? LazyAssemblyField = typeof(AnalyzerFileReference)
        .GetField("_lazyAssembly", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>Once-per-process gate for the background abandoned-root sweep.</summary>
    private static int s_sweepStarted;

    /// <summary>
    /// Retargets every file-based analyzer reference in <paramref name="solution"/> onto a
    /// shadow-copy loader whose <see cref="AssemblyLoadContext"/>s and on-disk shadow root are
    /// owned by the returned lease. Disposing the lease unloads the (collectible) load contexts
    /// and best-effort deletes the lease's shadow directory, so workspace close / reload / host
    /// shutdown reclaim both the memory and the disk space (analyzer-shadow-loader-lifecycle;
    /// previously the loaders and their non-collectible contexts were dropped on the floor and
    /// hundreds of orphaned shadow trees accumulated until process exit).
    /// The shadow root is keyed per-LEASE (<c>%TEMP%/RoslynMcpAnalyzerShadow/&lt;workspaceId&gt;/&lt;leaseId&gt;</c>),
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

        var leaseId = Guid.NewGuid().ToString("N");
        var shadowRoot = Path.Combine(Path.GetTempPath(), ShadowRootDirectoryName, workspaceId, leaseId);
        var loaders = new Dictionary<string, ShadowCopyAnalyzerAssemblyLoader>(StringComparer.OrdinalIgnoreCase);
        var retargeted = 0;

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
                        "Analyzer reference {AnalyzerPath} was already loaded before shadow-loader isolation could run.",
                        analyzerPath);
                    continue;
                }

                if (!loaders.TryGetValue(analyzerPath, out var loader))
                {
                    loader = ShadowCopyAnalyzerAssemblyLoader.Create(analyzerPath, shadowRoot, logger);
                    loaders[analyzerPath] = loader;
                }

                AssemblyLoaderField.SetValue(reference, loader);
                retargeted++;
            }
        }

        return new AnalyzerShadowLoaderLease(shadowRoot, [.. loaders.Values], retargeted, logger);
    }

    /// <summary>
    /// Best-effort removal of shadow roots abandoned by earlier crashed or pre-fix hosts, so
    /// per-lease disposal does not leave historical leaks in place forever. Only lease
    /// directories older than <see cref="StaleLeaseAge"/> that are not owned by a live lease in
    /// this process are candidates; a live lease in another process keeps its shadow assemblies
    /// locked, so its deletion fails harmlessly. The shared parent and per-workspace parents of
    /// live leases are never deleted.
    /// </summary>
    internal static void SweepAbandonedRoots(ILogger logger)
    {
        var sharedParent = Path.Combine(Path.GetTempPath(), ShadowRootDirectoryName);
        if (!Directory.Exists(sharedParent))
        {
            return;
        }

        var utcNow = DateTime.UtcNow;
        foreach (var workspaceDirectory in EnumerateDirectoriesSafe(sharedParent))
        {
            foreach (var leaseDirectory in EnumerateDirectoriesSafe(workspaceDirectory))
            {
                if (AnalyzerShadowLoaderLease.IsLiveRoot(leaseDirectory))
                {
                    continue;
                }

                if (!IsStale(leaseDirectory, utcNow))
                {
                    continue;
                }

                // A live lease in another process holds file locks on its loaded shadow copies;
                // losing to that lock (or to a concurrent sweeper) is harmless — the next sweep retries.
                try
                {
                    Directory.Delete(leaseDirectory, recursive: true);
                    logger.LogDebug("Swept abandoned analyzer shadow root {ShadowRoot}", leaseDirectory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            // Best-effort removal of a now-empty per-workspace parent. Never the shared parent.
            try
            {
                if (!Directory.EnumerateFileSystemEntries(workspaceDirectory).Any())
                {
                    Directory.Delete(workspaceDirectory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
            }
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
                logger.LogDebug(ex, "Abandoned analyzer shadow root sweep failed.");
            }
        });
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string parent)
    {
        try
        {
            return Directory.EnumerateDirectories(parent).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return [];
        }
    }

    private static bool IsStale(string directory, DateTime utcNow)
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
        private readonly ILogger? _logger;
        private int _disposed;

        internal AnalyzerShadowLoaderLease(
            string? shadowRoot,
            IReadOnlyList<ShadowCopyAnalyzerAssemblyLoader> loaders,
            int retargetedReferenceCount,
            ILogger? logger)
        {
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
        public static AnalyzerShadowLoaderLease CreateEmpty() => new(null, [], 0, null);

        internal static bool IsLiveRoot(string directory) => s_liveRoots.ContainsKey(directory);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
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

            if (ShadowRoot is null)
            {
                return;
            }

            s_liveRoots.TryRemove(ShadowRoot, out _);
            TryDeleteShadowRoot();
        }

        private void TryDeleteShadowRoot()
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
                        Directory.Delete(ShadowRoot, recursive: true);
                    }

                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt == MaxDeleteAttempts)
                    {
                        _logger?.LogDebug(
                            ex,
                            "Analyzer shadow root {ShadowRoot} is still locked after unload; leaving it for the abandoned-root sweeper.",
                            ShadowRoot);
                    }
                }
            }
        }
    }

    internal sealed class ShadowCopyAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        private readonly string _shadowRoot;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, string> _dependenciesByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Assembly> _assembliesByIdentity = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _shadowCopiesByIdentity = new(StringComparer.OrdinalIgnoreCase);

        // Every context ever constructed, including GetOrAdd race losers, so the owning lease
        // can unload them all. ALC unload only ever completes via GC, so tracking a loser here
        // costs nothing beyond what the race already leaked pre-fix.
        private readonly ConcurrentBag<ShadowAnalyzerLoadContext> _contexts = [];

        private ShadowCopyAnalyzerAssemblyLoader(string shadowRoot, ILogger logger)
        {
            _shadowRoot = shadowRoot;
            _logger = logger;
        }

        public static ShadowCopyAnalyzerAssemblyLoader Create(string analyzerPath, string shadowRoot, ILogger logger)
        {
            var loader = new ShadowCopyAnalyzerAssemblyLoader(shadowRoot, logger);
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
                    _logger.LogDebug(ex, "Failed to unload analyzer shadow load context under {ShadowRoot}", _shadowRoot);
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
            return _assembliesByIdentity.GetOrAdd(identity, _ =>
            {
                var context = new ShadowAnalyzerLoadContext(this);
                _contexts.Add(context);
                return context.LoadFromAssemblyPath(ShadowCopy(normalized));
            });
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
            return _shadowCopiesByIdentity.GetOrAdd(identity, _ =>
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
                var targetDirectory = Path.Combine(_shadowRoot, hash);
                Directory.CreateDirectory(targetDirectory);

                var targetPath = Path.Combine(targetDirectory, Path.GetFileName(fullPath));
                try
                {
                    File.Copy(fullPath, targetPath, overwrite: true);
                }
                catch (IOException) when (File.Exists(targetPath))
                {
                    // Another concurrent request populated the same shadow path.
                }

                return targetPath;
            });
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
                _logger.LogDebug(ex, "Failed to shadow-load analyzer dependency {DependencyPath}", dependencyPath);
                return null;
            }
        }

        private sealed class ShadowAnalyzerLoadContext : AssemblyLoadContext
        {
            private readonly ShadowCopyAnalyzerAssemblyLoader _loader;

            // Collectible so the owning lease's Dispose can actually unload analyzer assemblies
            // on workspace close/reload. Collectibility does NOT change Assembly.Location:
            // loading stays LoadFromAssemblyPath over an on-disk shadow copy, preserving
            // adjacent-resource and native-dependency probing (the LoadFromStream shortcut is
            // deliberately not taken — see ValidationIntegrationTests' Location assertion).
            public ShadowAnalyzerLoadContext(ShadowCopyAnalyzerAssemblyLoader loader)
                : base(isCollectible: true)
            {
                _loader = loader;
            }

            protected override Assembly? Load(AssemblyName assemblyName) =>
                _loader.Resolve(this, assemblyName);
        }
    }
}
