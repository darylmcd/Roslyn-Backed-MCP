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
/// folder is preserved instead of silently switching to the host's default folder. Fake runner behind
/// the real <see cref="GatedCommandExecutor"/>; no child <c>dotnet</c> process is spawned.
/// </summary>
[TestClass]
public sealed class WorkspaceLoadRestorePackagesPathTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

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
                    runner.Invocations[0].ToArray());
                CollectionAssert.AreEqual(
                    new[] { "restore", status.Projects[1].FilePath, "--nologo", "--packages", pathB, "--no-dependencies" },
                    runner.Invocations[1].ToArray());
                foreach (var tail in runner.Invocations.Skip(2))
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

            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath), "No assets file.");

            var assetsPath = Path.Combine(root, "obj", "project.assets.json");
            File.WriteAllText(assetsPath, "{ not json");
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath), "Malformed assets file.");

            File.WriteAllText(assetsPath, "{\"project\":{\"restore\":{}}}");
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath), "Property absent.");

            File.WriteAllText(assetsPath, "{\"project\":{\"restore\":{\"packagesPath\":\"  \"}}}");
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath), "Blank value.");

            File.WriteAllText(assetsPath, "{\"project\":{\"restore\":{\"packagesPath\":\"D:/scratch\"}}}");
            Assert.AreEqual("D:/scratch", RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }


    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("42")]
    [DataRow("""{"project":null}""")]
    [DataRow("""{"project":[]}""")]
    [DataRow("""{"project":{"restore":false}}""")]
    [DataRow("""{"project":{"restore":{"packagesPath":5}}}""")]
    public void TryReadRestorePackagesPath_ValidJsonWrongShape_ReturnsNull(string json)
    {
        var root = Path.Combine(TestTempRoot.Current, "restore-shape-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            File.WriteAllText(Path.Combine(root, "obj", "project.assets.json"), json);
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(Path.Combine(root, "App.csproj")));
        }
        finally { DeleteDirectoryIfExists(root); }
    }


    [TestMethod]
    public void Planner_NullAndRecordedRoots_IsolatesEveryProject()
    {
        var plan = RestoreInvocationPlanner.Plan("App.slnx",
            [new("A.csproj", "packages"), new("B.csproj", null)]);
        Assert.HasCount(2, plan);
        CollectionAssert.Contains(plan[0].Arguments.ToArray(), "--no-dependencies");
        CollectionAssert.AreEqual(new[] { "restore", "B.csproj", "--nologo", "--no-dependencies" }, plan[1].Arguments.ToArray());
    }


    [TestMethod]
    public void Planner_DependencyClosure_ContainsOnlySelectedProjectAndTransitiveDependencies()
    {
        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var ids = new[] { "C", "B", "A", "Unrelated" }.ToDictionary(n => n, n => Microsoft.CodeAnalysis.ProjectId.CreateNewId(n), StringComparer.Ordinal);
        foreach (var (name, id) in ids)
            solution = solution.AddProject(Microsoft.CodeAnalysis.ProjectInfo.Create(id, Microsoft.CodeAnalysis.VersionStamp.Create(),
                name, name, Microsoft.CodeAnalysis.LanguageNames.CSharp, filePath: Path.GetFullPath(name + ".csproj")));
        solution = solution.AddProjectReference(ids["A"], new Microsoft.CodeAnalysis.ProjectReference(ids["B"]))
            .AddProjectReference(ids["B"], new Microsoft.CodeAnalysis.ProjectReference(ids["C"]));
        CollectionAssert.AreEqual(new[] { Path.GetFullPath("C.csproj"), Path.GetFullPath("B.csproj"), Path.GetFullPath("A.csproj") },
            RestoreInvocationPlanner.DependencyClosure(solution.GetProject(ids["A"])!).ToArray());
    }


    [TestMethod]
    public void Planner_DependencyWithoutFilePath_RefusesIncompleteGraph()
    {
        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var project = workspace.AddProject("InMemory", Microsoft.CodeAnalysis.LanguageNames.CSharp);
        Assert.ThrowsExactly<InvalidOperationException>(() => RestoreInvocationPlanner.DependencyClosure(project));
    }

    [TestMethod]
    public void Planner_CaseVariants_UsesPlatformIdentity()
    {
        var plan = RestoreInvocationPlanner.Plan("App.slnx",
            [new("A.csproj", "packages"), new("B.csproj", "PACKAGES")]);
        Assert.HasCount(OperatingSystem.IsWindows() ? 1 : 2, plan);
    }

    [TestMethod]
    public async Task Planner_RealMixedRootRestore_PreservesReferencedProjectAssets()
    {
        var root = Path.Combine(TestTempRoot.Current, "mixed-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            var feed = Path.Combine(root, "feed");
            Directory.CreateDirectory(feed);
            using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(feed, "RestoreProbe.1.0.0.nupkg"),
                System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("RestoreProbe.nuspec").Open()))
                    writer.Write("""<?xml version="1.0"?><package><metadata><id>RestoreProbe</id><version>1.0.0</version><authors>Tests</authors><description>Local restore fixture</description></metadata></package>""");
                zip.CreateEntry("lib/net10.0/_._");
            }
            File.WriteAllText(Path.Combine(root, "NuGet.Config"),
                """<configuration><packageSources><clear/><add key="local" value="feed"/></packageSources></configuration>""");
            var projects = new List<RestoreInvocationPlanner.ProjectRestore>();
            foreach (var name in new[] { "B", "A" })
            {
                var directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, name + ".csproj");
                var reference = name == "A" ? """<ProjectReference Include="../B/B.csproj"/>""" : "";
                File.WriteAllText(path, $"""
                    <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>
                    <ItemGroup><PackageReference Include="RestoreProbe" Version="1.0.0"/>{reference}</ItemGroup></Project>
                    """);
                projects.Add(new(path, Path.Combine(root, "packages-" + name)));
            }
            var runner = new DotnetCommandRunner();
            // Establish real assets with independently recorded caches using isolated restores.
            foreach (var project in projects)
            {
                var execution = await runner.RunAsync(root, project.ProjectPath,
                    ["restore", project.ProjectPath, "--packages", project.PackagesPath!, "--no-dependencies"], CancellationToken.None);
                Assert.IsTrue(execution.Succeeded, execution.StdErr + execution.StdOut);
            }
            File.WriteAllText(Path.Combine(root, "Both.slnx"), """<Solution><Project Path="A/A.csproj"/><Project Path="B/B.csproj"/></Solution>""");
            var plan = RestoreInvocationPlanner.Plan(Path.Combine(root, "Both.slnx"),
                RestoreInvocationPlanner.Capture(projects.Select(p => p.ProjectPath)));
            foreach (var invocation in plan)
            {
                var execution = await runner.RunAsync(root, invocation.TargetPath, invocation.Arguments, CancellationToken.None);
                Assert.IsTrue(execution.Succeeded, execution.StdErr + execution.StdOut);
            }
            foreach (var project in projects)
            {
                Assert.AreEqual(project.PackagesPath, RestoreStalenessDetector.TryReadRestorePackagesPath(project.ProjectPath));
                Assert.IsTrue(Directory.Exists(Path.Combine(project.PackagesPath!, "restoreprobe", "1.0.0")));
            }
        }
        finally { DeleteDirectoryIfExists(root); }
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
}
