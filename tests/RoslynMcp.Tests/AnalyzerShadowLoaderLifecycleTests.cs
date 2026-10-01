using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
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
// [DoNotParallelize] retained: every test method here drives analyzer shadow-copy loading
// through AnalyzerReferenceIsolation, whose lease root is Path.Combine(Path.GetTempPath(),
// ShadowRootDirectoryName) — a machine-global directory OUTSIDE TestTempRoot.Current, shared
// by every WorkspaceManager load in the process (WorkspaceSessionLoader.CreateAndOpenAsync
// calls RetargetFileReferencesToShadowLoader on every load, including the assembly-shared
// Fixture WorkspaceManager other test classes use concurrently). SweepAbandonedRoots also
// enumerates that same shared parent directly. Concurrent runs would race the shared
// filesystem tree and the collectible-ALC file-lock reclamation this class asserts.
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

        var projectRoot = Path.Combine(TestTempRoot.Current, "analyzer-owned-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectRoot);
        try
        {
            // Two retargets for the SAME workspace id must produce distinct shadow roots — the
            // per-lease key is what makes disposing an old lease after a reload safe against the
            // new lease's files.
            var analyzerPath = Path.Combine(projectRoot, "OwnedAnalyzer.dll");
            File.Copy(typeof(WorkspaceSessionLoader).Assembly.Location, analyzerPath);
            using var adhoc = new AdhocWorkspace();
            var project = adhoc.AddProject(ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "LeaseProbe",
                "LeaseProbe",
                LanguageNames.CSharp,
                filePath: Path.Combine(projectRoot, "LeaseProbe.csproj")));
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
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(projectRoot);
        }
    }

    [TestMethod]
    public void Retarget_ExternalAnalyzer_UsesSharedProcessShadowLoader()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Analyzer shadow-file isolation is Windows-specific.");
            return;
        }

        var projectRoot = Path.Combine(TestTempRoot.Current, "analyzer-external-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectRoot);
        try
        {
            using var adhoc = new AdhocWorkspace();
            var project = adhoc.AddProject(ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "ExternalProbe",
                "ExternalProbe",
                LanguageNames.CSharp,
                filePath: Path.Combine(projectRoot, "ExternalProbe.csproj")));
            var originalLoader = StubAnalyzerAssemblyLoader.Instance;
            var reference = new AnalyzerFileReference(
                typeof(WorkspaceSessionLoader).Assembly.Location,
                originalLoader);
            var solution = project.Solution.AddAnalyzerReference(project.Id, reference);

            using var lease = AnalyzerReferenceIsolation.RetargetFileReferencesToShadowLoader(
                solution, "external-unit-probe", NullLogger.Instance);
            var processLoader = GetAnalyzerAssemblyLoader(reference);
            using var secondLease = AnalyzerReferenceIsolation.RetargetFileReferencesToShadowLoader(
                solution, "external-unit-probe-2", NullLogger.Instance);

            Assert.AreEqual(0, lease.RetargetedReferenceCount,
                "External references are owned by the process, not the collectible lease.");
            Assert.IsNull(lease.ShadowRoot,
                "A workspace with only external analyzers must not allocate a collectible lease root.");
            Assert.AreNotSame(originalLoader, GetAnalyzerAssemblyLoader(reference),
                "External analyzer references must be retargeted away from the original package path.");
            Assert.AreSame(processLoader, GetAnalyzerAssemblyLoader(reference),
                "Repeated retargets must share the process loader.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(projectRoot);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WorkspaceClose_ExternalGenerator_ReleasesPackagePathAndReusesProcessCopy(bool drainProcesses)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Mapped assembly deletion is a Windows-only contract.");
            return;
        }

        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var solutionPath = TestFixtureFileSystem.CreateSampleSolutionCopy(
            repoRoot, Path.Combine(repoRoot, "samples", "SampleSolution", "SampleSolution.slnx"));
        var copiedRoot = Path.GetDirectoryName(solutionPath)!;
        var packageRoot = Path.Combine(TestTempRoot.Current, "external-generator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packageRoot);

        try
        {
            var generatorPath = InjectExternalGenerator(copiedRoot, packageRoot);
            MsBuildInitializer.EnsureInitialized();
            using var manager = new WorkspaceManager(
                NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(),
                new FileWatcherService(NullLogger<FileWatcherService>.Instance),
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 },
                cacheStore: null,
                sessionLoader: new WorkspaceSessionLoader());
            using var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);
            var runner = new SuccessfulDrainRunner();

            string? firstShadowPath = null;
            for (var load = 0; load < 2; load++)
            {
                var status = await manager.LoadAsync(solutionPath, CancellationToken.None);
                Assert.IsTrue(status.IsLoaded);
                var project = manager.GetCurrentSolution(status.WorkspaceId).Projects
                    .Single(candidate => candidate.Name == "SampleLib");
                var snapshot = await SourceGeneratorCompilation.CreateAsync(project, CancellationToken.None);
                Assert.IsNotNull(snapshot);
                Assert.IsTrue(snapshot.Compilation.SyntaxTrees.Any(tree =>
                    tree.ToString().Contains("ExternalGeneratorMarker", StringComparison.Ordinal)),
                    "The external generator must run through SourceGeneratorCompilation.");

                var reference = project.AnalyzerReferences.OfType<AnalyzerFileReference>()
                    .Single(candidate => string.Equals(candidate.FullPath, generatorPath, StringComparison.OrdinalIgnoreCase));
                var generator = reference.GetGenerators(LanguageNames.CSharp).Single();
                var loadedAssembly = generator.GetType().Assembly;
                Assert.IsFalse(System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(loadedAssembly)!.IsCollectible,
                    "Roslyn may retain external generator types in process-wide caches.");
                Assert.IsFalse(loadedAssembly.Location.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase),
                    "The generator must be mapped from a shadow copy.");
                firstShadowPath ??= loadedAssembly.Location;
                Assert.AreEqual(firstShadowPath, loadedAssembly.Location,
                    "Repeated workspace loads must reuse the same process copy.");
                var processRoot = Path.GetDirectoryName(Path.GetDirectoryName(loadedAssembly.Location))!;
                Assert.IsTrue(AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease.IsLiveRoot(processRoot),
                    "The abandoned-root sweep must exclude the live process copy root.");

                var response = await WorkspaceTools.CloseWorkspaceCore(
                    gate, manager, runner, status.WorkspaceId, drainProcesses,
                    loggerFactory: null, exceptionReporter: null, getProcessesByName: _ => [],
                    processDrainTimeout: TimeSpan.FromSeconds(5), ct: CancellationToken.None);
                using var result = JsonDocument.Parse(response);
                Assert.IsTrue(result.RootElement.GetProperty("success").GetBoolean());
            }

            Assert.AreEqual(drainProcesses ? 2 : 0, runner.CallCount);
            using var current = Process.GetCurrentProcess();
            Assert.IsFalse(current.Modules.Cast<ProcessModule>().Any(module =>
                module.FileName.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase)),
                "No module may remain mapped from the package directory after workspace close.");
            Directory.Delete(packageRoot, recursive: true);
            Assert.IsFalse(Directory.Exists(packageRoot), "The original package folder must be deletable.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(packageRoot);
            TestFixtureFileSystem.DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Retarget_ConcurrentExternalFirstLoads_ShareLoaderAndCopy()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("External shadow isolation is Windows-specific.");
            return;
        }

        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var solutionPath = TestFixtureFileSystem.CreateSampleSolutionCopy(
            repoRoot, Path.Combine(repoRoot, "samples", "SampleSolution", "SampleSolution.slnx"));
        var copiedRoot = Path.GetDirectoryName(solutionPath)!;
        var packageRoot = Path.Combine(TestTempRoot.Current, "concurrent-generator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packageRoot);
        try
        {
            var generatorPath = InjectExternalGenerator(copiedRoot, packageRoot);
            using var adhoc = new AdhocWorkspace();
            var project = adhoc.AddProject(ProjectInfo.Create(
                ProjectId.CreateNewId(), VersionStamp.Create(), "ConcurrentProbe", "ConcurrentProbe",
                LanguageNames.CSharp, filePath: Path.Combine(copiedRoot, "ConcurrentProbe.csproj")));

            var firstLoads = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            {
                var reference = new AnalyzerFileReference(generatorPath, StubAnalyzerAssemblyLoader.Instance);
                var solution = project.Solution.AddAnalyzerReference(project.Id, reference);
                using var lease = AnalyzerReferenceIsolation.RetargetFileReferencesToShadowLoader(
                    solution, "concurrent-external-probe", NullLogger.Instance);
                var generator = reference.GetGenerators(LanguageNames.CSharp).Single();
                return (Loader: GetAnalyzerAssemblyLoader(reference), Location: generator.GetType().Assembly.Location);
            })));

            Assert.IsTrue(firstLoads.All(result => ReferenceEquals(result.Loader, firstLoads[0].Loader)),
                "Concurrent first loads of one package directory must use one process loader.");
            Assert.IsTrue(firstLoads.All(result =>
                string.Equals(result.Location, firstLoads[0].Location, StringComparison.OrdinalIgnoreCase)),
                "One source identity must map to one shadow copy across concurrent first loads.");
            Assert.AreEqual(1, Directory.EnumerateFiles(Path.GetDirectoryName(firstLoads[0].Location)!, "*.dll").Count(),
                "The source identity's shadow directory must contain only one assembly copy.");
            Assert.IsFalse(firstLoads[0].Location.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase));
            Directory.Delete(packageRoot, recursive: true);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(packageRoot);
            TestFixtureFileSystem.DeleteDirectoryIfExists(copiedRoot);
        }
    }

    private static string InjectExternalGenerator(string copiedRoot, string packageRoot)
    {
        const string source = """
            using Microsoft.CodeAnalysis;

            [Generator]
            public sealed class ExternalPackageGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }
                public void Execute(GeneratorExecutionContext context) =>
                    context.AddSource("ExternalGeneratorMarker.g.cs", "public class ExternalGeneratorMarker { }");
            }
            """;
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var compilation = CSharpCompilation.Create(
            "ExternalPackageGenerator",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(ISourceGenerator).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(packageRoot, "ExternalPackageGenerator.dll");
        using (var stream = File.Create(path))
        {
            var emit = compilation.Emit(stream);
            Assert.IsTrue(emit.Success, string.Join("; ", emit.Diagnostics));
        }

        var projectPath = Path.Combine(copiedRoot, "SampleLib", "SampleLib.csproj");
        var project = XDocument.Load(projectPath);
        project.Root!.Add(new XElement("ItemGroup",
            new XElement("Analyzer", new XAttribute("Include", path))));
        project.Save(projectPath);
        return path;
    }

    private sealed class SuccessfulDrainRunner : IDotnetCommandRunner
    {
        public int CallCount { get; private set; }

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new CommandExecutionDto(
                "dotnet", arguments, workingDirectory, targetPath,
                ExitCode: 0, Succeeded: true, DurationMs: 0, StdOut: string.Empty, StdErr: string.Empty));
        }
    }

    [TestMethod]
    public void SweepAbandonedRoots_LockedStaleLease_LogsRedactedFailureAndContinues()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Analyzer shadow-file lifecycle relies on Windows file-lock semantics.");
            return;
        }

        var sharedParent = Path.Combine(
            TestTempRoot.Current,
            "analyzer-shadow-sweep-" + Guid.NewGuid().ToString("N"));
        var workspaceDirectory = Path.Combine(sharedParent, "workspace-probe");
        var leaseDirectory = Path.Combine(workspaceDirectory, "stale-lease");
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
            lockedFile.Flush(flushToDisk: true);

            var staleTimestamp = DateTime.UtcNow - TimeSpan.FromDays(2);
            Directory.SetCreationTimeUtc(leaseDirectory, staleTimestamp);
            Directory.SetLastWriteTimeUtc(leaseDirectory, staleTimestamp);

            var logger = new ListLogger<AnalyzerShadowLoaderLifecycleTests>();
            AnalyzerReferenceIsolation.SweepAbandonedRoots(sharedParent, logger);

            var failures = logger.Entries
                .Where(entry => entry.Message.Contains(
                    "Could not sweep an abandoned analyzer shadow lease",
                    StringComparison.Ordinal))
                .ToList();
            Assert.AreEqual(1, failures.Count,
                "The locked stale lease must emit one actionable cleanup failure.");
            Assert.AreEqual(LogLevel.Debug, failures[0].Level);
            Assert.IsNull(failures[0].Exception,
                "Best-effort cleanup logs must not attach raw exception payloads.");
            Assert.IsTrue(
                failures[0].Message.Contains("IOException", StringComparison.Ordinal) ||
                failures[0].Message.Contains("UnauthorizedAccessException", StringComparison.Ordinal),
                $"The cleanup log must retain the safe failure type. Actual: {failures[0].Message}");
            Assert.IsFalse(failures[0].Message.Contains(sharedParent, StringComparison.OrdinalIgnoreCase),
                "Cleanup failure logs must not disclose analyzer shadow paths.");
            Assert.IsTrue(Directory.Exists(leaseDirectory),
                "A failed best-effort sweep must leave the locked lease for a later retry.");
        }
        finally
        {
            lockedFile?.Dispose();
            TestFixtureFileSystem.DeleteDirectoryIfExists(sharedParent);
        }
    }

    [TestMethod]
    public void SweepAbandonedRoots_StaleProcessCopyRoot_IsReclaimed()
    {
        var sharedParent = Path.Combine(
            TestTempRoot.Current, "analyzer-process-sweep-" + Guid.NewGuid().ToString("N"));
        var processCopyRoot = Path.Combine(sharedParent, "process-exited", "copies");
        Directory.CreateDirectory(processCopyRoot);
        File.WriteAllText(Path.Combine(processCopyRoot, "unused.dll"), "stale copy");
        try
        {
            var staleTimestamp = DateTime.UtcNow - TimeSpan.FromDays(2);
            Directory.SetCreationTimeUtc(processCopyRoot, staleTimestamp);
            Directory.SetLastWriteTimeUtc(processCopyRoot, staleTimestamp);

            AnalyzerReferenceIsolation.SweepAbandonedRoots(sharedParent, NullLogger.Instance);

            Assert.IsFalse(Directory.Exists(processCopyRoot),
                "A process copy root from an exited host must be reclaimed by the two-level sweep.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(sharedParent);
        }
    }

    [TestMethod]
    public async Task SweepAbandonedRoots_LiveOtherProcess_PreservesStaleProcessCopies()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The cross-process ownership probe uses a Windows cmd.exe child.");
            return;
        }

        var sharedParent = Path.Combine(
            TestTempRoot.Current, "analyzer-live-process-sweep-" + Guid.NewGuid().ToString("N"));
        var processDirectory = Path.Combine(sharedParent, "process-other-host");
        var copyRoot = Path.Combine(processDirectory, "copies");
        Directory.CreateDirectory(copyRoot);
        File.WriteAllText(Path.Combine(copyRoot, "unloaded.dll"), "unmapped copy");
        var readyPath = Path.Combine(TestTempRoot.Current, "process-owner-ready-" + Guid.NewGuid().ToString("N"));
        // cmd.exe starts in milliseconds, unlike a PowerShell cold start that exceeded the ready
        // deadline on loaded hosted runners. The group-wide `9>>` redirection keeps the lock file
        // open for the lifetime of the group, which is all the sweeper's exclusive-open probe
        // (FileShare.None) needs to treat the root as owned by a live host.
        var ownerScriptPath = Path.Combine(TestTempRoot.Current, "process-owner-" + Guid.NewGuid().ToString("N") + ".cmd");
        File.WriteAllText(
            ownerScriptPath,
            "@echo off\r\n" +
            "( (echo ready>\"%RMCP_OWNER_READY%\") & ping -n 120 127.0.0.1 >nul ) 9>>\"%RMCP_OWNER_ROOT%\\.owner.lock\"\r\n");

        using var owner = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ownerScriptPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            }
        };
        owner.StartInfo.Environment["RMCP_OWNER_ROOT"] = processDirectory;
        owner.StartInfo.Environment["RMCP_OWNER_READY"] = readyPath;

        try
        {
            Assert.IsTrue(owner.Start(), "The second host must start.");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (!File.Exists(readyPath) && !owner.HasExited && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.IsTrue(File.Exists(readyPath),
                owner.HasExited ? await owner.StandardError.ReadToEndAsync() : "The second host did not acquire its ownership marker.");
            var staleTimestamp = DateTime.UtcNow - TimeSpan.FromDays(2);
            Directory.SetCreationTimeUtc(copyRoot, staleTimestamp);
            Directory.SetLastWriteTimeUtc(copyRoot, staleTimestamp);

            AnalyzerReferenceIsolation.SweepAbandonedRoots(sharedParent, NullLogger.Instance);
            Assert.IsTrue(Directory.Exists(copyRoot),
                "A live second host owns this process root even when its copies are old and no assembly file is mapped.");

            owner.Kill(entireProcessTree: true);
            Assert.IsTrue(owner.WaitForExit(10_000));
            // The lock handle is held by the cmd.exe group and its ping child: the group's exit is
            // observable a few milliseconds before the last handle closes, so a single immediate
            // sweep can still see the root as owned. Re-sweep until the reclaim lands; the bounded
            // wait only separates "released shortly after the host exited" from "never released".
            var reclaimDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            do
            {
                AnalyzerReferenceIsolation.SweepAbandonedRoots(sharedParent, NullLogger.Instance);
                if (!Directory.Exists(copyRoot)) break;
                await Task.Delay(50);
            }
            while (DateTime.UtcNow < reclaimDeadline);

            Assert.IsFalse(Directory.Exists(copyRoot), "The exited host's stale copies must be reclaimed.");
            Assert.IsFalse(Directory.Exists(processDirectory),
                "The exited host's ownership marker and empty process parent must be reclaimed.");
        }
        finally
        {
            if (!owner.HasExited)
            {
                owner.Kill(entireProcessTree: true);
                owner.WaitForExit(10_000);
            }

            File.Delete(readyPath);
            File.Delete(ownerScriptPath);
            TestFixtureFileSystem.DeleteDirectoryIfExists(sharedParent);
        }
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

    private static object? GetAnalyzerAssemblyLoader(AnalyzerFileReference reference) =>
        typeof(AnalyzerFileReference)
            .GetField("_assemblyLoader", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(reference);

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
