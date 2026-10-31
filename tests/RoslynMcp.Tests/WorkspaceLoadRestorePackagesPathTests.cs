using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>workspace-restore-packages-path</c>: auto-restore must pass the
/// <c>project.restore.packagesPath</c> recorded in each project's existing
/// <c>obj/project.assets.json</c> to <c>dotnet restore --packages</c>, so a worktree scratch package
/// folder is preserved instead of silently switching to the host's default folder. Includes real
/// local-feed restore fixtures and recording runners behind <see cref="GatedCommandExecutor"/>.
/// </summary>
[TestClass]
public sealed class WorkspaceLoadRestorePackagesPathTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AutoRestore_GraphFactoryBudgetFailure_PreservesTimeoutOrCallerCancellation(bool cancelCaller)
    {
        using var fixture = new RestoreFixture(nonCompilationChain: false);
        using var manager = fixture.CreateManager();
        var status = await manager.LoadAsync(fixture.TargetPath, CancellationToken.None);
        var runner = new RecordingRestoreRunner();
        using var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
        using var cancellation = new CancellationTokenSource();
        var budgetReads = 0;
        // Begin reads the clock once. The next two reads admit phase capture and graph
        // construction; the fourth read occurs in the graph's project-instance factory.
        var clock = new CaptureBudgetTimeProvider(() =>
        {
            if (++budgetReads != 4) return;
            if (cancelCaller)
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
            throw new TimeoutException("Restore inventory phase exhausted.");
        });
        using var deadline = RequestDeadline.Begin(clock, TimeSpan.FromMinutes(1));
        var restore = () => WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
            status with { RestoreRequired = true }, true, NullLogger.Instance, cancellation.Token);

        if (cancelCaller) await Assert.ThrowsAsync<OperationCanceledException>(restore);
        else await Assert.ThrowsExactlyAsync<TimeoutException>(restore);
        Assert.IsTrue(budgetReads >= 4);
        Assert.IsEmpty(runner.Invocations, "Capture must fail before any restore command can run.");
    }

    private sealed class CaptureBudgetTimeProvider(Action onRead) : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()
        {
            onRead();
            return _now;
        }
    }

    [TestMethod]
    public async Task AutoRestore_ReferencePropertyContext_UsesContextualActiveAssets()
    {
        using var fixture = new RestoreFixture(nonCompilationChain: true);
        var dependencyDirectory = Path.GetDirectoryName(fixture.Projects[1])!;
        File.WriteAllText(Path.Combine(dependencyDirectory, "Directory.Build.props"),
            "<Project><PropertyGroup Condition=\"'$(Configuration)' == 'Release' And '$(Flavor)' == 'Blue'\"><MSBuildProjectExtensionsPath>obj-blue/</MSBuildProjectExtensionsPath></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "references.targets"),
            "<Project><ItemGroup><ProjectReference Include=\"../B/B.proj\" ReferenceOutputAssembly=\"false\" AdditionalProperties=\"Configuration=Release;Platform=x64;Flavor=Blue\"/></ItemGroup></Project>");
        await fixture.RestoreInitialAsync();
        var contextual = await fixture.Runner.RunAsync(dependencyDirectory, fixture.Projects[1],
            ["restore", fixture.Projects[1], "--nologo", "--no-dependencies", "--packages", fixture.CacheFor(fixture.Projects[1]),
                "-p:Configuration=Release", "-p:Platform=x64", "-p:Flavor=Blue"], CancellationToken.None);
        Assert.IsTrue(contextual.Succeeded, contextual.StdOut + contextual.StdErr);
        WriteAssetsWithPackagesPath(fixture.Projects[1], Path.Combine(fixture.Root, "stale-default-cache"));
        fixture.Runner.Invocations.Clear();
        using var manager = fixture.CreateManager();
        var status = await manager.LoadAsync(fixture.TargetPath, CancellationToken.None);
        using var executor = new GatedCommandExecutor(manager, fixture.Runner, NullLogger<GatedCommandExecutor>.Instance);
        await WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
            status with { RestoreRequired = true }, true, NullLogger.Instance, CancellationToken.None);

        Assert.HasCount(3, fixture.Runner.Invocations);
        var argv = fixture.Runner.Invocations.Single(a => a[1] == fixture.Projects[1]);
        CollectionAssert.Contains(argv.ToList(), "-p:Configuration=Release");
        CollectionAssert.Contains(argv.ToList(), "-p:Platform=x64");
        CollectionAssert.Contains(argv.ToList(), "-p:Flavor=Blue");
        Assert.AreEqual(fixture.CacheFor(fixture.Projects[1]), argv[argv.ToList().IndexOf("--packages") + 1]);
        using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(dependencyDirectory, "obj-blue", "project.assets.json")));
        Assert.AreEqual(fixture.CacheFor(fixture.Projects[1]), assets.RootElement.GetProperty("project").GetProperty("restore").GetProperty("packagesPath").GetString());
    }

    [TestMethod]
    public async Task AutoRestore_CaseVariantCaches_UsesPlatformPathIdentity()
    {
        using var fixture = new RestoreFixture(nonCompilationChain: false);
        fixture.CacheOverrides[fixture.Projects[0]] = Path.Combine(fixture.Root, "Cache");
        fixture.CacheOverrides[fixture.Projects[1]] = Path.Combine(fixture.Root, "cache");
        await fixture.RestoreInitialAsync();
        using var manager = fixture.CreateManager();
        var status = await manager.LoadAsync(fixture.TargetPath, CancellationToken.None);
        using var executor = new GatedCommandExecutor(manager, fixture.Runner, NullLogger<GatedCommandExecutor>.Instance);
        await WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
            status with { RestoreRequired = true }, true, NullLogger.Instance, CancellationToken.None);
        Assert.HasCount(OperatingSystem.IsWindows() ? 1 : 2, fixture.Runner.Invocations);
        foreach (var project in fixture.Projects)
        {
            Assert.IsTrue(RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer.Equals(fixture.CacheFor(project), fixture.ReadPackageRoot(project)));
        }
    }

    [TestMethod]
    public async Task AutoRestore_FrameworkConditionedClosure_RetainsFullOuterAssets()
    {
        using var fixture = new RestoreFixture(nonCompilationChain: true);
        File.WriteAllText(fixture.TargetPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include=\"Inventory.Probe\" Version=\"1.0.0\"/></ItemGroup><ItemGroup Condition=\"'$(TargetFramework)' == 'net10.0'\"><ProjectReference Include=\"../B/B.proj\" ReferenceOutputAssembly=\"false\"/></ItemGroup><ItemGroup Condition=\"'$(TargetFramework)' == 'net10.0-windows'\"><ProjectReference Include=\"../C/C.proj\" ReferenceOutputAssembly=\"false\"/></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.Projects[1])!, "references.targets"), "<Project/>");
        await fixture.RestoreInitialAsync();
        using var manager = fixture.CreateManager();
        var status = await manager.LoadAsync(fixture.TargetPath, CancellationToken.None);
        using var executor = new GatedCommandExecutor(manager, fixture.Runner, NullLogger<GatedCommandExecutor>.Instance);
        await WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
            status with { RestoreRequired = true }, true, NullLogger.Instance, CancellationToken.None);

        Assert.HasCount(3, fixture.Runner.Invocations, "Inner nodes discover dependencies; the outer project restores once.");
        using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "obj", "project.assets.json")));
        Assert.AreEqual(2, assets.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject().Count());
        foreach (var project in fixture.Projects)
        {
            Assert.AreEqual(fixture.CacheFor(project), fixture.ReadPackageRoot(project));
        }
    }

    [TestMethod]
    public async Task AutoRestore_FrameworkSpecificAssets_RetainsEachRestoreContext()
    {
        using var fixture = new RestoreFixture(nonCompilationChain: false);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "Directory.Build.props"),
            "<Project><PropertyGroup><MSBuildProjectExtensionsPath>$(MSBuildProjectDirectory)/obj/$(TargetFramework)/</MSBuildProjectExtensionsPath></PropertyGroup></Project>");
        File.WriteAllText(fixture.TargetPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include=\"Inventory.Probe\" Version=\"1.0.0\"/></ItemGroup><Import Project=\"references.targets\"/></Project>");
        await fixture.RestoreInitialAsync();
        var roots = new Dictionary<string, string>();
        foreach (var framework in new[] { "net10.0", "net10.0-windows" })
        {
            var cache = Path.Combine(fixture.Root, "cache-" + framework);
            roots.Add(framework, cache);
            var restored = await fixture.Runner.RunAsync(Path.GetDirectoryName(fixture.TargetPath)!, fixture.TargetPath,
                ["restore", fixture.TargetPath, "--nologo", "--no-dependencies", "--packages", cache, "-p:TargetFramework=" + framework], CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, restored.StdOut + restored.StdErr);
            Assert.AreEqual(cache, RestoreStalenessDetector.TryReadRestorePackagesPath(fixture.TargetPath,
                new Dictionary<string, string> { ["TargetFramework"] = framework }));
        }
        fixture.Runner.Invocations.Clear();
        using var manager = fixture.CreateManager();
        var status = await manager.LoadAsync(fixture.TargetPath, CancellationToken.None);
        using var executor = new GatedCommandExecutor(manager, fixture.Runner, NullLogger<GatedCommandExecutor>.Instance);
        await WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
            status with { RestoreRequired = true }, true, NullLogger.Instance, CancellationToken.None);

        foreach (var (framework, cache) in roots)
        {
            var invocation = fixture.Runner.Invocations.Single(a => a.Contains("-p:TargetFramework=" + framework));
            Assert.AreEqual(cache, invocation[invocation.ToList().IndexOf("--packages") + 1]);
            using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "obj", framework, "project.assets.json")));
            Assert.AreEqual(cache, assets.RootElement.GetProperty("project").GetProperty("restore").GetProperty("packagesPath").GetString());
        }
    }

    [TestMethod]
    public async Task AutoRestore_GraphOnlyCondition_DoesNotChangeCommandContext()
    {
        using var fixture = new RestoreFixture(nonCompilationChain: false);
        var references = Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "references.targets");
        File.WriteAllText(references, File.ReadAllText(references).Replace("<ItemGroup>", "<ItemGroup Condition=\"'$(IsGraphBuild)' == ''\">", StringComparison.Ordinal));
        await fixture.RestoreInitialAsync();
        using var manager = fixture.CreateManager();
        var status = await manager.LoadAsync(fixture.TargetPath, CancellationToken.None);
        using var executor = new GatedCommandExecutor(manager, fixture.Runner, NullLogger<GatedCommandExecutor>.Instance);
        await WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
            status with { RestoreRequired = true }, true, NullLogger.Instance, CancellationToken.None);
        Assert.HasCount(2, fixture.Runner.Invocations);
        Assert.AreEqual(fixture.CacheFor(fixture.Projects[1]), fixture.ReadPackageRoot(fixture.Projects[1]));
    }

    [TestMethod]
    public async Task AutoRestore_NullAndRecordedRoot_KeepsBothIdentities()
    {
        using var fixture = new RestoreFixture(nonCompilationChain: false);
        await fixture.RestoreInitialAsync();
        File.Delete(Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, "obj", "project.assets.json"));
        using var manager = fixture.CreateManager();
        var status = await manager.LoadAsync(fixture.TargetPath, CancellationToken.None);
        var runner = new RecordingRestoreRunner();
        using var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
        await WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
            status with { RestoreRequired = true }, true, NullLogger.Instance, CancellationToken.None);
        Assert.HasCount(2, runner.Invocations);
        var rootInvocation = runner.Invocations.Single(a => a[1] == fixture.TargetPath);
        CollectionAssert.DoesNotContain(rootInvocation.ToList(), "--packages");
        foreach (var argv in runner.Invocations)
        {
            CollectionAssert.Contains(argv.ToList(), "--no-dependencies");
        }
    }

    [TestMethod]
    public async Task AutoRestore_CorruptDependencyAssets_RefusesBeforeAnyCommand()
    {
        using var fixture = new RestoreFixture(nonCompilationChain: false);
        await fixture.RestoreInitialAsync();
        using var manager = fixture.CreateManager();
        var status = await manager.LoadAsync(fixture.TargetPath, CancellationToken.None);
        var dependencyAssets = Path.Combine(Path.GetDirectoryName(fixture.Projects[1])!, "obj", "project.assets.json");
        File.WriteAllText(dependencyAssets, "{ not json");
        var runner = new RecordingRestoreRunner();
        using var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);

        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
                status with { RestoreRequired = true }, true, NullLogger.Instance, CancellationToken.None));
        Assert.IsEmpty(runner.Invocations, "An unreadable active dependency must refuse the whole captured plan.");
    }

    [TestMethod]
    [DataRow("directory")]
    [DataRow("missing-restore")]
    [DataRow("non-string-root")]
    public async Task AutoRestore_UnusableExistingDependencyAssets_RefusesBeforeAnyCommand(string failure)
    {
        using var fixture = new RestoreFixture(nonCompilationChain: false);
        await fixture.RestoreInitialAsync();
        using var manager = fixture.CreateManager();
        var status = await manager.LoadAsync(fixture.TargetPath, CancellationToken.None);
        var assets = Path.Combine(Path.GetDirectoryName(fixture.Projects[1])!, "obj", "project.assets.json");
        if (failure == "directory")
        {
            File.Delete(assets);
            Directory.CreateDirectory(assets);
        }
        else
        {
            File.WriteAllText(assets, failure == "missing-restore"
                ? "{\"project\":{}}"
                : "{\"project\":{\"restore\":{\"packagesPath\":42}}}");
        }

        var runner = new RecordingRestoreRunner();
        using var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
                status with { RestoreRequired = true }, true, NullLogger.Instance, CancellationToken.None));
        Assert.IsEmpty(runner.Invocations);
    }

    [TestMethod]
    [DataRow("extensions")]
    [DataRow("explicit")]
    public async Task AutoRestore_CorruptEvaluatedDependencyAssets_RefusesBeforeAnyCommand(string layout)
    {
        using var fixture = new RestoreFixture(nonCompilationChain: false);
        await fixture.RestoreInitialAsync();
        var dependencyDirectory = Path.GetDirectoryName(fixture.Projects[1])!;
        var activeAssets = Path.Combine(dependencyDirectory, "active",
            layout == "extensions" ? "project.assets.json" : "reviewed.assets.json");
        File.WriteAllText(Path.Combine(dependencyDirectory, "Directory.Build.props"), layout == "extensions"
            ? "<Project><PropertyGroup><MSBuildProjectExtensionsPath>active/</MSBuildProjectExtensionsPath></PropertyGroup></Project>"
            : "<Project><PropertyGroup><ProjectAssetsFile>active/reviewed.assets.json</ProjectAssetsFile></PropertyGroup></Project>");
        Directory.CreateDirectory(Path.GetDirectoryName(activeAssets)!);
        File.Copy(Path.Combine(dependencyDirectory, "obj", "project.assets.json"), activeAssets);
        using var manager = fixture.CreateManager();
        var status = await manager.LoadAsync(fixture.TargetPath, CancellationToken.None);
        Assert.AreEqual(activeAssets, RoslynMcp.Roslyn.Helpers.ProjectMetadataParser.GetExistingAssetsPath(
            fixture.Projects[1], RoslynMcp.Roslyn.Helpers.MsBuildMetadataHelper.DefaultGlobalProperties));
        File.WriteAllText(activeAssets, "{ not json");

        var runner = new RecordingRestoreRunner();
        using var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
                status with { RestoreRequired = true }, true, NullLogger.Instance, CancellationToken.None));
        Assert.IsEmpty(runner.Invocations, "A valid stale default file must not replace the evaluated active authority.");
    }

    [TestMethod]
    public async Task AutoRestore_RealReferencedAssets_PreservesEachPackageCache()
    {
        using var fixture = new RestoreFixture(nonCompilationChain: false);
        var parentDirectory = Path.Combine(fixture.Root, "Z.Consumer");
        var dependencyDirectory = Path.Combine(fixture.Root, "A.Dependency");
        Directory.Move(Path.GetDirectoryName(fixture.Projects[0])!, parentDirectory);
        Directory.Move(Path.GetDirectoryName(fixture.Projects[1])!, dependencyDirectory);
        var parent = Path.Combine(parentDirectory, "Z.Consumer.csproj");
        var dependency = Path.Combine(dependencyDirectory, "A.Dependency.csproj");
        File.Move(Path.Combine(parentDirectory, "A.csproj"), parent);
        File.Move(Path.Combine(dependencyDirectory, "B.csproj"), dependency);
        fixture.Projects[0] = parent;
        fixture.Projects[1] = dependency;
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(parent)!, "references.targets"),
            "<Project><ItemGroup><ProjectReference Include=\"../A.Dependency/A.Dependency.csproj\"/></ItemGroup></Project>");
        await fixture.RestoreInitialAsync();
        using var manager = fixture.CreateManager();
        var solution = Path.Combine(fixture.Root, "References.slnx");
        File.WriteAllText(solution, "<Solution><Project Path=\"A.Dependency/A.Dependency.csproj\"/><Project Path=\"Z.Consumer/Z.Consumer.csproj\"/></Solution>");
        var status = await manager.LoadAsync(solution, CancellationToken.None);
        Assert.HasCount(2, status.Projects);
        Assert.AreEqual(dependency, status.Projects[0].FilePath, "Exercise the untouched real dependency-first solution inventory.");
        foreach (var cacheFile in Directory.EnumerateFiles(fixture.Root, "*.nuget.cache", SearchOption.AllDirectories))
        {
            File.Delete(cacheFile);
        }
        using var executor = new GatedCommandExecutor(manager, fixture.Runner, NullLogger<GatedCommandExecutor>.Instance);

        await WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
            status with { RestoreRequired = true }, true, NullLogger.Instance, CancellationToken.None);

        foreach (var project in fixture.Projects)
        {
            Assert.AreEqual(fixture.CacheFor(project), fixture.ReadPackageRoot(project),
                $"Restore rewrote the real assets cache for {Path.GetFileName(project)}.");
            Assert.IsTrue(File.Exists(Path.Combine(fixture.ReadPackageRoot(project), "inventory.probe", "1.0.0", "content", "probe.txt")));
        }
    }

    [TestMethod]
    public async Task AutoRestore_RealNonCompilationTransitiveReferences_RestoresCompleteInventory()
    {
        using var fixture = new RestoreFixture(nonCompilationChain: true);
        await fixture.RestoreInitialAsync();
        using var manager = fixture.CreateManager();
        var status = await manager.LoadAsync(fixture.TargetPath, CancellationToken.None);
        Assert.HasCount(1, status.Projects, "The .proj dependencies must really be absent from Roslyn's inventory.");
        fixture.Runner.Invocations.Clear();
        using var executor = new GatedCommandExecutor(manager, fixture.Runner, NullLogger<GatedCommandExecutor>.Instance);

        await WorkspaceTools.RestoreAndReloadIfRequiredAsync(executor, ValidationOptions, manager,
            status with { RestoreRequired = true }, true, NullLogger.Instance, CancellationToken.None);

        Assert.HasCount(3, fixture.Runner.Invocations, "Every evaluated non-compilation dependency requires its own isolated restore.");
        CollectionAssert.AreEquivalent(fixture.Projects, fixture.Runner.Invocations.Select(a => a[1]).ToArray());
        foreach (var argv in fixture.Runner.Invocations)
        {
            CollectionAssert.Contains(argv.ToList(), "--no-dependencies");
            Assert.AreEqual(fixture.CacheFor(argv[1]), fixture.ReadPackageRoot(argv[1]));
        }
    }

    [TestMethod]
    public async Task AutoRestore_AssetsRecordScratchPackagesPath_PassesPackagesFlag()
    {
        var (solutionPath, root) = CreateSampleSolutionCopyWithRoot();
        try
        {
            var scratch = Path.Combine(root, "scratch-packages");
            var (status, runner, executor, manager) = await LoadAsync(solutionPath);
            using (manager)
            using (executor)
            {
                foreach (var project in status.Projects)
                {
                    WriteAssetsWithPackagesPath(project.FilePath, scratch);
                }

                await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, ValidationOptions, manager, status with { RestoreRequired = true }, autoRestore: true, NullLogger.Instance, CancellationToken.None);

                Assert.HasCount(1, runner.Invocations, "One distinct recorded path must produce a single solution-level restore.");
                CollectionAssert.AreEqual(
                    new[] { "restore", solutionPath, "--nologo", "--packages", scratch },
                    runner.Invocations[0].ToArray());
            }
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    public async Task AutoRestore_NoAssetsFile_OmitsPackagesFlag()
    {
        var (solutionPath, root) = CreateSampleSolutionCopyWithRoot();
        try
        {
            var (status, runner, executor, manager) = await LoadAsync(solutionPath);
            using (manager)
            using (executor)
            {
                foreach (var objDir in Directory.EnumerateDirectories(root, "obj", SearchOption.AllDirectories).ToArray())
                {
                    TestFixtureFileSystem.DeleteDirectoryIfExists(objDir);
                }

                await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, ValidationOptions, manager, status with { RestoreRequired = true }, autoRestore: true, NullLogger.Instance, CancellationToken.None);

                Assert.HasCount(1, runner.Invocations);
                CollectionAssert.AreEqual(
                    new[] { "restore", solutionPath, "--nologo" },
                    runner.Invocations[0].ToArray(),
                    "First restore (no assets file) must keep the plain argument list.");
            }
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    public async Task AutoRestore_ProjectsRecordDifferentPackagesPaths_RestoresEachProjectWithItsOwnPath()
    {
        var (solutionPath, root) = CreateSampleSolutionCopyWithRoot();
        try
        {
            var (status, runner, executor, manager) = await LoadAsync(solutionPath);
            using (manager)
            using (executor)
            {
                Assert.IsGreaterThanOrEqualTo(2, status.Projects.Count, "Sample solution must contain at least two projects.");
                var pathA = Path.Combine(root, "packages-a");
                var pathB = Path.Combine(root, "packages-b");
                WriteAssetsWithPackagesPath(status.Projects[0].FilePath, pathA);
                WriteAssetsWithPackagesPath(status.Projects[1].FilePath, pathB);
                foreach (var project in status.Projects.Skip(2))
                {
                    var assets = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "obj", "project.assets.json");
                    if (File.Exists(assets))
                    {
                        File.Delete(assets);
                    }
                }

                await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, ValidationOptions, manager, status with { RestoreRequired = true }, autoRestore: true, NullLogger.Instance, CancellationToken.None);

                Assert.HasCount(status.Projects.Count, runner.Invocations, "Mixed package folders must restore each project separately.");
                CollectionAssert.AreEqual(
                    new[] { "restore", status.Projects[0].FilePath, "--nologo", "--packages", pathA, "--no-dependencies" },
                    runner.Invocations.Single(a => a[1] == status.Projects[0].FilePath).Take(6).ToArray());
                CollectionAssert.AreEqual(
                    new[] { "restore", status.Projects[1].FilePath, "--nologo", "--packages", pathB, "--no-dependencies" },
                    runner.Invocations.Single(a => a[1] == status.Projects[1].FilePath).Take(6).ToArray());
                foreach (var tail in runner.Invocations.Where(a => !status.Projects.Take(2).Any(p => p.FilePath == a[1])))
                {
                    CollectionAssert.DoesNotContain(tail.ToList(), "--packages", "A project with no recorded path keeps the default folder.");
                }
            }
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    public void TryReadRestorePackagesPath_MissingOrMalformedAssets_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "restore-packages-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            var projectPath = Path.Combine(root, "App.csproj");
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath, RoslynMcp.Roslyn.Helpers.MsBuildMetadataHelper.DefaultGlobalProperties), "No assets file.");

            var assetsPath = Path.Combine(root, "obj", "project.assets.json");
            File.WriteAllText(assetsPath, "{ not json");
            Assert.AreEqual(assetsPath, RoslynMcp.Roslyn.Helpers.ProjectMetadataParser.GetExistingAssetsPath(
                projectPath, RoslynMcp.Roslyn.Helpers.MsBuildMetadataHelper.DefaultGlobalProperties));
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath, RoslynMcp.Roslyn.Helpers.MsBuildMetadataHelper.DefaultGlobalProperties), "Malformed assets file.");

            File.WriteAllText(assetsPath, "{\"project\":{\"restore\":{}}}");
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath, RoslynMcp.Roslyn.Helpers.MsBuildMetadataHelper.DefaultGlobalProperties), "Property absent.");

            File.WriteAllText(assetsPath, "{\"project\":{\"restore\":{\"packagesPath\":\"  \"}}}");
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath, RoslynMcp.Roslyn.Helpers.MsBuildMetadataHelper.DefaultGlobalProperties), "Blank value.");

            File.WriteAllText(assetsPath, "{\"project\":{\"restore\":{\"packagesPath\":\"D:/scratch\"}}}");
            Assert.AreEqual("D:/scratch", RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath, RoslynMcp.Roslyn.Helpers.MsBuildMetadataHelper.DefaultGlobalProperties));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static (string SolutionPath, string Root) CreateSampleSolutionCopyWithRoot()
    {
        var solutionPath = CreateSampleSolutionCopy();
        return (solutionPath, Path.GetDirectoryName(solutionPath)!);
    }

    private static async Task<(WorkspaceStatusDto Status, RecordingRestoreRunner Runner, GatedCommandExecutor Executor, WorkspaceManager Manager)> LoadAsync(
        string solutionPath)
    {
        var manager = new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4, RestoreRaceWaitMs = 0 });
        var status = await manager.LoadAsync(solutionPath, CancellationToken.None);
        var runner = new RecordingRestoreRunner();
        var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
        return (status, runner, executor, manager);
    }

    private static void WriteAssetsWithPackagesPath(string projectFilePath, string packagesPath)
    {
        var objDirectory = Path.Combine(Path.GetDirectoryName(projectFilePath)!, "obj");
        Directory.CreateDirectory(objDirectory);
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            project = new { restore = new { packagesPath } },
        });
        File.WriteAllText(Path.Combine(objDirectory, "project.assets.json"), json);
    }

    private sealed class RecordingRestoreRunner : IDotnetCommandRunner
    {
        private readonly List<IReadOnlyList<string>> _invocations = [];

        public IReadOnlyList<IReadOnlyList<string>> Invocations
        {
            get
            {
                lock (_invocations)
                {
                    return _invocations.ToArray();
                }
            }
        }

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            lock (_invocations)
            {
                _invocations.Add(arguments.ToArray());
            }

            return Task.FromResult(new CommandExecutionDto(
                "dotnet", arguments, workingDirectory, targetPath, 0, true, 0, string.Empty, string.Empty));
        }
    }

    internal sealed class RestoreFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(TestTempRoot.Current, "restore-inventory-" + Guid.NewGuid().ToString("N"));
        public string[] Projects { get; }
        public string TargetPath => Projects[0];
        public RealRestoreRunner Runner { get; } = new();
        public Dictionary<string, string> CacheOverrides { get; } = [];

        public RestoreFixture(bool nonCompilationChain)
        {
            Directory.CreateDirectory(Root);
            var feed = Path.Combine(Root, "feed");
            Directory.CreateDirectory(feed);
            using (var archive = ZipFile.Open(Path.Combine(feed, "Inventory.Probe.1.0.0.nupkg"), ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Inventory.Probe.nuspec").Open()))
                {
                    writer.Write("<package><metadata><id>Inventory.Probe</id><version>1.0.0</version><authors>Tests</authors><description>Local restore fixture</description></metadata></package>");
                }
                using var payload = new StreamWriter(archive.CreateEntry("content/probe.txt").Open());
                payload.Write("local package payload");
            }
            File.WriteAllText(Path.Combine(Root, "NuGet.Config"),
                "<configuration><packageSources><clear/><add key=\"local\" value=\"feed\"/></packageSources></configuration>");
            Projects = nonCompilationChain
                ? [Path.Combine(Root, "A", "A.csproj"), Path.Combine(Root, "B", "B.proj"), Path.Combine(Root, "C", "C.proj")]
                : [Path.Combine(Root, "A", "A.csproj"), Path.Combine(Root, "B", "B.csproj")];
            for (var i = 0; i < Projects.Length; i++)
            {
                var directory = Path.GetDirectoryName(Projects[i])!;
                Directory.CreateDirectory(directory);
                var reference = i + 1 < Projects.Length
                    ? $"<ItemGroup><ProjectReference Include=\"{Path.GetRelativePath(directory, Projects[i + 1])}\" ReferenceOutputAssembly=\"false\" /></ItemGroup>"
                    : string.Empty;
                File.WriteAllText(Path.Combine(directory, "references.targets"),
                    $"<Project><PropertyGroup><InventoryReferences>true</InventoryReferences></PropertyGroup><Choose><When Condition=\"'$(InventoryReferences)' == 'true'\">{reference}</When></Choose></Project>");
                File.WriteAllText(Projects[i],
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit><RestoreProjectStyle>PackageReference</RestoreProjectStyle></PropertyGroup><ItemGroup><PackageReference Include=\"Inventory.Probe\" Version=\"1.0.0\"/></ItemGroup><Import Project=\"references.targets\"/></Project>");
            }
        }

        public WorkspaceManager CreateManager() => new(NullLogger<WorkspaceManager>.Instance, new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4, RestoreRaceWaitMs = 0 });

        public string CacheFor(string project) => CacheOverrides.TryGetValue(project, out var cache)
            ? cache : Path.Combine(Root, "packages-" + Path.GetFileNameWithoutExtension(project));

        public string ReadPackageRoot(string project)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json")));
            return document.RootElement.GetProperty("project").GetProperty("restore").GetProperty("packagesPath").GetString()!;
        }

        public async Task RestoreInitialAsync()
        {
            foreach (var project in Projects)
            {
                var result = await Runner.RunAsync(Path.GetDirectoryName(project)!, project,
                    ["restore", project, "--nologo", "--packages", CacheFor(project), "--no-dependencies"], CancellationToken.None);
                Assert.IsTrue(result.Succeeded, result.StdOut + result.StdErr);
                Assert.AreEqual(CacheFor(project), ReadPackageRoot(project));
            }
            Runner.Invocations.Clear();
        }

        public void Dispose() => TestFixtureFileSystem.DeleteDirectoryIfExists(Root);
    }

    internal sealed class RealRestoreRunner : IDotnetCommandRunner
    {
        public List<IReadOnlyList<string>> Invocations { get; } = [];
        private readonly DotnetCommandRunner _inner = new();
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Invocations.Add(arguments.ToArray());
            return _inner.RunAsync(workingDirectory, targetPath, arguments, ct);
        }
    }
}
