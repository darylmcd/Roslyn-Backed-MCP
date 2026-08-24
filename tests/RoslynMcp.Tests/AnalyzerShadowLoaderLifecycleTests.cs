using System.Reflection;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <c>analyzer-shadow-loader-lifecycle</c>: analyzer shadow-copy loaders
/// are leased per workspace load — each load gets its own collectible
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> and uniquely-keyed shadow root under
/// <c>%TEMP%/RoslynMcpAnalyzerShadow/&lt;workspaceId&gt;/&lt;leaseId&gt;</c> that workspace
/// close and reload reclaim. Pre-fix, the loaders' non-collectible contexts were dropped on the
/// floor and every load leaked one shadow tree until process exit (245–272 orphaned trees /
/// ~600 MB per full Release test run).
///
/// <para>
/// ALC unload is asynchronous (mapped shadow files stay locked until the collectible context is
/// collected), so reclamation is asserted with a bounded GC-assisted retry rather than a
/// synchronous check — pre-fix the contexts are non-collectible and the files stay locked
/// forever, so the retry loop times out and the assertion still fails deterministically.
/// </para>
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class AnalyzerShadowLoaderLifecycleTests
{
    private const string _fixtureAnalyzerAssemblyName = "LifecycleFixtureAnalyzer";
    private static readonly TimeSpan _reclamationTimeout = TimeSpan.FromSeconds(30);

    private static string ShadowSharedParent =>
        Path.Combine(Path.GetTempPath(), AnalyzerReferenceIsolation.ShadowRootDirectoryName);

    [TestMethod]
    public void Retarget_LeasesAreUniquelyKeyedPerLoad_AndDisposeIsIdempotent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Analyzer shadow-file lifecycle relies on Windows file-lock semantics.");
            return;
        }

        // Two retargets for the SAME workspace id must produce distinct shadow roots — the
        // per-lease key is what makes disposing an old lease after a reload safe against the
        // new lease's files.
        using var adhoc = new AdhocWorkspace();
        var project = adhoc.AddProject("LeaseProbe", LanguageNames.CSharp);
        var analyzerPath = typeof(WorkspaceSessionLoader).Assembly.Location;
        var reference = new AnalyzerFileReference(analyzerPath, StubAnalyzerAssemblyLoader.Instance);
        var solution = project.Solution.AddAnalyzerReference(project.Id, reference);

        var lease1 = AnalyzerReferenceIsolation.RetargetFileReferencesToShadowLoader(
            solution, "lease-unit-probe", NullLogger.Instance);
        var lease2 = AnalyzerReferenceIsolation.RetargetFileReferencesToShadowLoader(
            solution, "lease-unit-probe", NullLogger.Instance);

        Assert.AreEqual(1, lease1.RetargetedReferenceCount);
        Assert.IsNotNull(lease1.ShadowRoot);
        Assert.IsNotNull(lease2.ShadowRoot);
        StringAssert.Contains(lease1.ShadowRoot, "lease-unit-probe");
        Assert.AreNotEqual(lease1.ShadowRoot, lease2.ShadowRoot,
            "Each lease must own a uniquely-keyed shadow root; a per-workspace key would let old-lease deletion race a reload's new files.");

        // Idempotent release: eviction, explicit close, and host shutdown can all reach the
        // same session, so double-dispose must be a safe no-op (including on a lease whose
        // root never materialized because no analyzer was actually loaded).
        lease1.Dispose();
        lease1.Dispose();
        lease2.Dispose();
        lease2.Dispose();
    }

    [TestMethod]
    public async Task WorkspaceCloseAndReload_ReclaimAnalyzerShadowRoots()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Analyzer shadow-file lifecycle relies on Windows file-lock semantics.");
            return;
        }

        // The fixture is deliberately SELF-CONTAINED: an isolated copy of the sample solution
        // plus an <Analyzer/> item pointing at a runtime-emitted analyzer assembly. Using a
        // production analyzer from the test output makes the fixture environment-sensitive:
        // Coverlet rewrites that assembly and its injected tracker pins the collectible load
        // context, so coverage runs test the collector's lifetime instead of ours. Emitting a
        // minimal external analyzer preserves production-analyzer coverage while isolating the
        // ownership contract this test exercises.
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var solutionPath = TestFixtureFileSystem.CreateSampleSolutionCopy(
            repoRoot, Path.Combine(repoRoot, "samples", "SampleSolution", "SampleSolution.slnx"));
        var copiedRoot = Path.GetDirectoryName(solutionPath)!;

        try
        {
            var analyzerAssemblyPath = InjectFixtureAnalyzer(copiedRoot);

            MsBuildInitializer.EnsureInitialized();
            using var manager = new WorkspaceManager(
                NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(),
                new FileWatcherService(NullLogger<FileWatcherService>.Instance),
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 },
                cacheStore: null,
                sessionLoader: new WorkspaceSessionLoader());

            var status = await manager.LoadAsync(solutionPath, CancellationToken.None);
            Assert.IsTrue(status.IsLoaded, "Fixture solution must load before exercising the lease lifecycle.");
            var workspaceDirectory = Path.Combine(ShadowSharedParent, status.WorkspaceId);

            // (b) Force the shadow assemblies to actually load: retargeting alone creates loaders
            // lazily, and the lease's on-disk root only materializes on the first shadow copy.
            var firstContextRef = ForceAnalyzerLoad(manager, status.WorkspaceId, analyzerAssemblyPath);
            var leaseDirsAfterLoad = ListLeaseDirectories(workspaceDirectory);
            Assert.AreEqual(1, leaseDirsAfterLoad.Count,
                $"Exactly one live shadow root expected after load; saw [{string.Join(", ", leaseDirsAfterLoad)}].");

            // (e) Reload: the old lease's root must be reclaimed while the new lease's root
            // survives — no second leaked tree.
            await manager.ReloadAsync(status.WorkspaceId, CancellationToken.None);
            var secondContextRef = ForceAnalyzerLoad(manager, status.WorkspaceId, analyzerAssemblyPath);
            var leaseDirsAfterReload = ListLeaseDirectories(workspaceDirectory);
            var newLeaseDirs = leaseDirsAfterReload.Except(leaseDirsAfterLoad, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.AreEqual(1, newLeaseDirs.Count,
                $"Reload must materialize exactly one NEW shadow root; saw [{string.Join(", ", newLeaseDirs)}].");
            var (reloadReclaimed, reloadError) = await WaitForReclamationWithErrorOnCleanStackAsync(
                workspaceDirectory,
                surviving: newLeaseDirs[0]);
            Assert.IsTrue(
                reloadReclaimed,
                $"The pre-reload lease's shadow root was still locked after the bounded wait — the old load context did not unload. Last error: {reloadError}; firstContextAlive={firstContextRef.IsAlive}.");
            Assert.IsTrue(Directory.Exists(newLeaseDirs[0]),
                "The reloaded workspace's own shadow root must survive old-lease reclamation.");

            // (c)+(d) Close: every remaining shadow root for this workspace must be reclaimed.
            Assert.IsTrue(manager.Close(status.WorkspaceId));
            var (closeReclaimed, closeError) = await WaitForReclamationWithErrorOnCleanStackAsync(workspaceDirectory, surviving: null);
            Assert.IsTrue(
                closeReclaimed,
                $"The workspace's shadow roots were still locked after close + bounded wait — the load contexts did not unload. Last error: {closeError}; firstContextAlive={firstContextRef.IsAlive}; secondContextAlive={secondContextRef.IsAlive}; allContexts=[{string.Join(", ", System.Runtime.Loader.AssemblyLoadContext.All.Select(c => c.GetType().Name))}]");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task ReclamationTimeout_PreservesLastDeletionError()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Analyzer shadow-file lifecycle relies on Windows file-lock semantics.");
            return;
        }

        var workspaceDirectory = Path.Combine(
            TestTempRoot.Current,
            "analyzer-shadow-reclamation-timeout-" + Guid.NewGuid().ToString("N"));
        var leaseDirectory = Path.Combine(workspaceDirectory, "locked-lease");
        var lockedFilePath = Path.Combine(leaseDirectory, "locked.dll");
        Directory.CreateDirectory(leaseDirectory);

        FileStream? lockedFile = null;
        try
        {
            lockedFile = new FileStream(
                lockedFilePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None);

            var (reclaimed, lastError) = await WaitForReclamationWithErrorOnCleanStackAsync(
                workspaceDirectory,
                surviving: null,
                timeout: TimeSpan.FromMilliseconds(75),
                retryDelay: TimeSpan.FromMilliseconds(10));

            Assert.IsFalse(reclaimed, "The locked fixture must force the bounded reclamation timeout.");
            Assert.IsTrue(
                lastError.StartsWith("IOException: ", StringComparison.Ordinal) ||
                lastError.StartsWith("UnauthorizedAccessException: ", StringComparison.Ordinal),
                $"The timeout must retain the final deletion failure type and message. Actual: {lastError}");
        }
        finally
        {
            lockedFile?.Dispose();
            TestFixtureFileSystem.DeleteDirectoryIfExists(workspaceDirectory);
        }
    }

    /// <summary>
    /// Makes the analyzer's presence a GUARANTEED precondition rather than an assumption:
    /// the copied <c>SampleLib</c> project gets an explicit <c>&lt;Analyzer/&gt;</c> item
    /// pointing at a minimal analyzer emitted into the copied fixture itself. The emitted file
    /// is independent of build configuration and is not rewritten by Coverlet before the test
    /// starts, so collectible-context reclamation has no instrumentation-owned roots.
    /// </summary>
    /// <returns>The absolute path of the analyzer assembly wired into the fixture.</returns>
    private static string InjectFixtureAnalyzer(string copiedRoot)
    {
        const string analyzerSource = """
            using System.Collections.Immutable;
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.Diagnostics;

            [DiagnosticAnalyzer(LanguageNames.CSharp)]
            public sealed class LifecycleFixtureAnalyzer : DiagnosticAnalyzer
            {
                public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
                    ImmutableArray<DiagnosticDescriptor>.Empty;

                public override void Initialize(AnalysisContext context)
                {
                }
            }
            """;

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var compilation = CSharpCompilation.Create(
            _fixtureAnalyzerAssemblyName,
            [CSharpSyntaxTree.ParseText(analyzerSource)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DiagnosticAnalyzer).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzerAssemblyPath = Path.Combine(copiedRoot, $"{_fixtureAnalyzerAssemblyName}.dll");
        using (var stream = File.Create(analyzerAssemblyPath))
        {
            var emit = compilation.Emit(stream);
            Assert.IsTrue(emit.Success,
                $"Fixture analyzer compilation must succeed. Diagnostics: {string.Join("; ", emit.Diagnostics)}");
        }

        var projectPath = Path.Combine(copiedRoot, "SampleLib", "SampleLib.csproj");
        Assert.IsTrue(File.Exists(projectPath),
            $"Fixture precondition: copied sample project '{projectPath}' must exist.");

        var project = XDocument.Load(projectPath);
        project.Root!.Add(new XElement("ItemGroup",
            new XElement("Analyzer", new XAttribute("Include", analyzerAssemblyPath))));
        project.Save(projectPath);

        return analyzerAssemblyPath;
    }

    /// <summary>
    /// Loads the fixture analyzer through the session's shadow loader and asserts the
    /// compatibility witness: shadow-copied analyzers keep a real on-disk
    /// <see cref="System.Reflection.Assembly.Location"/> (path-loaded, never stream-loaded).
    /// Deliberately a separate method so the test body holds no reference that would root the
    /// pre-reload <see cref="Solution"/> graph and keep its collectible context alive.
    /// </summary>
    private static WeakReference ForceAnalyzerLoad(
        WorkspaceManager manager, string workspaceId, string analyzerAssemblyPath)
    {
        var analyzerFileName = Path.GetFileName(analyzerAssemblyPath);
        var solution = manager.GetCurrentSolution(workspaceId);
        var fixtureProject = solution.Projects.First(project => project.Name == "SampleLib");
        var analyzerReference = fixtureProject.AnalyzerReferences
            .OfType<AnalyzerFileReference>()
            .FirstOrDefault(reference =>
                string.Equals(Path.GetFileName(reference.FullPath), analyzerFileName, StringComparison.OrdinalIgnoreCase));

        // Distinguishes a broken FIXTURE (analyzer never reached the loaded project) from a
        // genuine reclamation failure, which is asserted further down the test body.
        Assert.IsNotNull(analyzerReference,
            $"Fixture precondition failed (this is NOT a reclamation failure): the loaded SampleLib project must carry the injected analyzer reference '{analyzerFileName}'. Analyzer references seen: [{string.Join(", ", fixtureProject.AnalyzerReferences.Select(reference => reference.Display ?? reference.Id.ToString()))}].");

        var analyzer = analyzerReference.GetAnalyzers(fixtureProject.Language)
            .FirstOrDefault(candidate => candidate.GetType().Name == _fixtureAnalyzerAssemblyName);
        Assert.IsNotNull(analyzer, "Expected the shadow-copy loader to preserve analyzer discovery.");
        Assert.IsFalse(string.IsNullOrEmpty(analyzer.GetType().Assembly.Location),
            "Shadow-copied analyzers must keep an on-disk Assembly.Location (collectible ALC must not imply stream loading).");
        return new WeakReference(System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(analyzer.GetType().Assembly));
    }

    private static List<string> ListLeaseDirectories(string workspaceDirectory) =>
        Directory.Exists(workspaceDirectory)
            ? Directory.EnumerateDirectories(workspaceDirectory).ToList()
            : [];

    /// <summary>
    /// Bounded GC-assisted wait for shadow-root reclamation. A lease directory that survives
    /// its lease's Dispose retry budget is only reclaimable once the collectible context has
    /// actually been collected, so the poll loop nudges the GC and retries the delete itself —
    /// with pre-fix non-collectible contexts the files stay locked and this times out.
    ///
    /// <para>
    /// The wait MUST run on a clean stack (hence <see cref="Task.Run(Action)"/>): the manager's
    /// load/reload awaits complete with inline continuations, so the test method resumes ON TOP
    /// of the not-yet-unwound <c>MSBuildWorkspace.OpenSolutionAsync</c> /
    /// <c>LoadIntoSessionAsync</c> frames. Those live frames' stack slots pin the just-loaded
    /// <see cref="Solution"/> graph — and through <c>AnalyzerFileReference._lazyAssembly</c> the
    /// analyzer <see cref="Assembly"/> and its collectible context — for as long as the test
    /// thread sits in the poll loop, so an in-place wait deadlocks against its own stack
    /// (diagnosed via SOS <c>gcroot</c>: the only root of the leaked context was the test
    /// thread's own <c>OpenSolutionAsync.MoveNext</c> continuation frames).
    /// </para>
    /// </summary>
    private static Task<(bool Reclaimed, string LastError)> WaitForReclamationWithErrorOnCleanStackAsync(
        string workspaceDirectory,
        string? surviving,
        TimeSpan? timeout = null,
        TimeSpan? retryDelay = null) =>
        Task.Run(() =>
        {
            var reclaimed = WaitForReclamation(
                workspaceDirectory,
                surviving,
                timeout ?? _reclamationTimeout,
                retryDelay ?? TimeSpan.FromMilliseconds(250),
                out var lastError);
            return (reclaimed, lastError);
        });

    private static bool WaitForReclamation(
        string workspaceDirectory,
        string? surviving,
        TimeSpan timeout,
        TimeSpan retryDelay,
        out string lastError)
    {
        lastError = "(none)";
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var remaining = ListLeaseDirectories(workspaceDirectory)
                .Where(directory => !string.Equals(directory, surviving, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (remaining.Count == 0)
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            foreach (var directory in remaining)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Still locked — the context has not been collected yet; retry until deadline.
                    lastError = ex.GetType().Name + ": " + ex.Message;
                }
            }

            var remainingWait = deadline - DateTime.UtcNow;
            if (remainingWait > TimeSpan.Zero)
            {
                Thread.Sleep(remainingWait < retryDelay ? remainingWait : retryDelay);
            }
        }
    }

    private sealed class StubAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        public static StubAnalyzerAssemblyLoader Instance { get; } = new();

        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) =>
            throw new InvalidOperationException(
                "The stub loader must have been replaced by shadow-loader retargeting before any load.");
    }
}
