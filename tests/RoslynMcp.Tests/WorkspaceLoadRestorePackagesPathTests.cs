using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

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
                var nodes = EvaluatedBuildGraph.Source(manager.GetCurrentSolution(status.WorkspaceId), CancellationToken.None).Nodes.Where(node => !node.IsOuterBuild).ToArray();

                await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, ValidationOptions, manager, status with { RestoreRequired = true }, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

                AssertNodeInvocations(nodes, runner.Invocations, _ => scratch);
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
                var nodes = EvaluatedBuildGraph.Source(manager.GetCurrentSolution(status.WorkspaceId), CancellationToken.None).Nodes.Where(node => !node.IsOuterBuild).ToArray();

                await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, ValidationOptions, manager, status with { RestoreRequired = true }, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

                AssertNodeInvocations(nodes, runner.Invocations, _ => null);
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
                var nodes = EvaluatedBuildGraph.Source(manager.GetCurrentSolution(status.WorkspaceId), CancellationToken.None).Nodes.Where(node => !node.IsOuterBuild).ToArray();

                await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, ValidationOptions, manager, status with { RestoreRequired = true }, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

                AssertNodeInvocations(nodes, runner.Invocations, node => node.ProjectPath == status.Projects[0].FilePath ? pathA
                    : node.ProjectPath == status.Projects[1].FilePath ? pathB : null);
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
            File.WriteAllText(projectPath, "<Project><PropertyGroup><ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup></Project>");

            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty), "No assets file.");

            var assetsPath = Path.Combine(root, "obj", "project.assets.json");
            File.WriteAllText(assetsPath, "{ not json");
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty), "Malformed assets file.");

            File.WriteAllText(assetsPath, "{\"project\":{\"restore\":{}}}");
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty), "Property absent.");

            File.WriteAllText(assetsPath, "{\"project\":{\"restore\":{\"packagesPath\":\"  \"}}}");
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty), "Blank value.");

            File.WriteAllText(assetsPath, "{\"project\":{\"restore\":{\"packagesPath\":\"D:/scratch\"}}}");
            Assert.AreEqual("D:/scratch", RestoreStalenessDetector.TryReadRestorePackagesPath(projectPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty));
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
            File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project><PropertyGroup><ProjectAssetsFile>obj/project.assets.json</ProjectAssetsFile></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "obj", "project.assets.json"), json);
            Assert.IsNull(RestoreStalenessDetector.TryReadRestorePackagesPath(Path.Combine(root, "App.csproj"), System.Collections.Immutable.ImmutableDictionary<string, string>.Empty));
        }
        finally { DeleteDirectoryIfExists(root); }
    }


    [TestMethod]
    public void Planner_NullAndRecordedRoots_IsolatesEveryProject()
    {
        var plan = RestoreInvocationPlanner.Plan("App.slnx",
            [new("A.csproj", "packages", null, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, null), new("B.csproj", null, null, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, null)]);
        Assert.HasCount(2, plan);
        CollectionAssert.Contains(plan[0].Arguments.ToArray(), "--no-dependencies");
        CollectionAssert.AreEqual(new[] { "restore", "B.csproj", "--nologo", "--no-dependencies" }, plan[1].Arguments.ToArray());
    }


    [TestMethod]
    public void Planner_SelectedProject_CapturesOnlyItsEvaluatedTransitiveContexts()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(WorkspaceLoadRestorePackagesPathTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
            var solution = workspace.CurrentSolution;
            var ids = new[] { "C", "B", "A", "Unrelated" }.ToDictionary(name => name, name => Microsoft.CodeAnalysis.ProjectId.CreateNewId(name), StringComparer.Ordinal);
            foreach (var (name, id) in ids)
            {
                var reference = name == "A" ? "B" : name == "B" ? "C" : null;
                File.WriteAllText(Path.Combine(root, name + ".csproj"), reference is null ? "<Project />"
                    : $"<Project><ItemGroup><ProjectReference Include=\"{reference}.csproj\" /></ItemGroup></Project>");
                solution = solution.AddProject(Microsoft.CodeAnalysis.ProjectInfo.Create(id, Microsoft.CodeAnalysis.VersionStamp.Create(),
                    name, name, Microsoft.CodeAnalysis.LanguageNames.CSharp, filePath: Path.Combine(root, name + ".csproj")));
            }
            solution = solution.AddProjectReference(ids["A"], new Microsoft.CodeAnalysis.ProjectReference(ids["B"]))
                .AddProjectReference(ids["B"], new Microsoft.CodeAnalysis.ProjectReference(ids["C"]));
            CollectionAssert.AreEqual(new[] { Path.Combine(root, "C.csproj"), Path.Combine(root, "B.csproj"), Path.Combine(root, "A.csproj") },
                RestoreInvocationPlanner.Capture(solution.GetProject(ids["A"])!, CancellationToken.None).Select(project => project.ProjectPath).ToArray());
        }
        finally { DeleteDirectoryIfExists(root); }
    }

    [TestMethod]
    public void Planner_DependencyWithoutFilePath_RefusesIncompleteGraph()
    {
        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var project = workspace.AddProject("InMemory", Microsoft.CodeAnalysis.LanguageNames.CSharp);
        Assert.ThrowsExactly<RoslynMcp.Core.Services.PublicInvalidOperationException>(() => RestoreInvocationPlanner.Capture(project, CancellationToken.None));
    }

    [TestMethod]
    public void Planner_SharedAssets_UsesPlatformPackageRootIdentity()
    {
        var assets = Path.GetFullPath("obj/project.assets.json");
        RestoreInvocationPlanner.ProjectRestore[] captured =
            [new("A.csproj", "packages", null, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, assets),
                new("A.csproj", "PACKAGES", null, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, assets)];
        if (OperatingSystem.IsWindows()) Assert.HasCount(1, RestoreInvocationPlanner.Plan("App.slnx", captured));
        else Assert.ThrowsExactly<RoslynMcp.Core.Services.PublicInvalidOperationException>(() => RestoreInvocationPlanner.Plan("App.slnx", captured));
    }

    [TestMethod]
    public void Planner_EqualLeafGlobalsAndRoots_DoNotAuthorizeSolutionEntryBatching()
    {
        var globals = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty
            .Add("Platform", "AnyCPU").Add("Configuration", "Debug").Add("BuildingSolutionFile", "true");
        var plan = RestoreInvocationPlanner.Plan("App.slnx",
            [new("A.csproj", "packages", null, globals, Path.GetFullPath("A/obj/project.assets.json")),
                new("B.csproj", "packages", null, globals, Path.GetFullPath("B/obj/project.assets.json"))]);
        CollectionAssert.AreEqual(new[] { "A.csproj", "B.csproj" }, plan.Select(invocation => invocation.TargetPath).ToArray());
        foreach (var invocation in plan)
            CollectionAssert.AreEqual(new[] { "restore", invocation.TargetPath, "--nologo", "--packages", "packages", "--no-dependencies" },
                invocation.Arguments.Take(6).ToArray());
        foreach (var invocation in plan)
        {
            CollectionAssert.AreEquivalent(new[] { "-p:Platform=AnyCPU", "-p:Configuration=Debug", "-p:BuildingSolutionFile=true" },
                invocation.Arguments.Skip(6).ToArray());
            Assert.IsFalse(invocation.Arguments.Contains("App.slnx"));
        }
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
                projects.Add(new(path, Path.Combine(root, "packages-" + name), null, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, Path.Combine(directory, "obj", "project.assets.json")));
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
                RestoreInvocationPlanner.Capture(projects.Select(p => p.ProjectPath), System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, CancellationToken.None));
            foreach (var invocation in plan)
            {
                var execution = await runner.RunAsync(root, invocation.TargetPath, invocation.Arguments, CancellationToken.None);
                Assert.IsTrue(execution.Succeeded, execution.StdErr + execution.StdOut);
            }
            foreach (var project in projects)
            {
                Assert.AreEqual(project.PackagesPath, RestoreStalenessDetector.TryReadRestorePackagesPath(project.ProjectPath, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty));
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
        var manager = new WorkspaceManager(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(new RoslynMcp.Tests.Helpers.OwnedTestWorkspacePathPolicy()),
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4, RestoreRaceWaitMs = 0 });
        var status = await manager.LoadAsync(solutionPath, RoslynMcp.Tests.Helpers.OwnedNativeLoadContext.Properties(solutionPath, new Dictionary<string, string>()), CancellationToken.None);
        var runner = new RecordingRestoreRunner();
        var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
        return (status, runner, executor, manager);
    }

    private static void AssertNodeInvocations(IReadOnlyList<EvaluatedBuildGraph.Node> nodes,
        IReadOnlyList<IReadOnlyList<string>> invocations, Func<EvaluatedBuildGraph.Node, string?> root)
    {
        Assert.HasCount(nodes.Count, invocations);
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var prefix = new List<string> { "restore", node.ProjectPath, "--nologo" };
            if (root(node) is { } packages) prefix.AddRange(["--packages", packages]);
            prefix.Add("--no-dependencies");
            CollectionAssert.AreEqual(MsBuildCommandProperties.Append(prefix, node.Globals), invocations[index].ToArray(),
                "Every dependency-first project command must retain its exact evaluated property context.");
        }
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

    private sealed class RecordingRestoreRunner : RoslynMcp.Tests.Helpers.CannedCommandRunner
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

        public override Task<CommandExecutionDto> RunAsync(
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
