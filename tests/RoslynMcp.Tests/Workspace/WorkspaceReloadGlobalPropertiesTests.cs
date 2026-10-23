using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests.Workspace;

[TestClass]
public sealed class WorkspaceReloadGlobalPropertiesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ConfiguredReload_PreservesCompilerOptionsAndConditionalMember()
    {
        using var fixture = new ReloadFixture();
        using var manager = CreateManager(new WorkspaceSessionLoader());
        var status = await manager.LoadAsync(fixture.ProjectPath, ConfiguredGlobals(), TestContext.CancellationToken);
        await AssertCompilerStateAsync(manager.GetCurrentSolution(status.WorkspaceId), configured: true);

        await manager.ReloadAsync(status.WorkspaceId, TestContext.CancellationToken);

        await AssertCompilerStateAsync(manager.GetCurrentSolution(status.WorkspaceId), configured: true);
    }

    [TestMethod]
    public async Task DefaultReload_PreservesDefaultCompilerOptionsAndConditionalMember()
    {
        using var fixture = new ReloadFixture();
        using var manager = CreateManager(new WorkspaceSessionLoader());
        var status = await manager.LoadAsync(fixture.ProjectPath, TestContext.CancellationToken);
        await AssertCompilerStateAsync(manager.GetCurrentSolution(status.WorkspaceId), configured: false);

        await manager.ReloadAsync(status.WorkspaceId, TestContext.CancellationToken);

        await AssertCompilerStateAsync(manager.GetCurrentSolution(status.WorkspaceId), configured: false);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task CancelledConfiguredReload_PreservesExactSolutionVersionAndCompilerState()
    {
        using var fixture = new ReloadFixture();
        var loader = new CancelReloadLoader();
        using var manager = CreateManager(loader);
        var status = await manager.LoadAsync(fixture.ProjectPath, ConfiguredGlobals(), TestContext.CancellationToken);
        var before = manager.GetCurrentSolution(status.WorkspaceId);
        var version = manager.GetCurrentVersion(status.WorkspaceId);
        await AssertCompilerStateAsync(before, configured: true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);

        var reload = manager.ReloadAsync(status.WorkspaceId, cancellation.Token);
        await loader.ReloadEntered.Task.WaitAsync(TestContext.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => reload);

        Assert.AreSame(before, manager.GetCurrentSolution(status.WorkspaceId));
        Assert.AreEqual(version, manager.GetCurrentVersion(status.WorkspaceId));
        await AssertCompilerStateAsync(manager.GetCurrentSolution(status.WorkspaceId), configured: true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConfiguredRestoreDrift_UsesLoadedConfigurationAssets(bool releaseStale)
    {
        using var fixture = new ReloadFixture();
        await PrepareConfiguredMetadataAsync(fixture, releaseStale);
        using var manager = CreateManager(new WorkspaceSessionLoader());
        var status = await manager.LoadAsync(fixture.ProjectPath, ConfiguredGlobals(), TestContext.CancellationToken);
        var solution = manager.GetCurrentSolution(status.WorkspaceId);
        await AssertCompilerStateAsync(solution, configured: true);
        Assert.IsEmpty(((MSBuildWorkspace)solution.Workspace).Diagnostics,
            "The drift comparison must not be masked by workspace-load diagnostics: " +
            string.Join(Environment.NewLine, ((MSBuildWorkspace)solution.Workspace).Diagnostics.Select(diagnostic => diagnostic.Message)));

        Assert.AreEqual(releaseStale, status.RestoreRequired,
            "Restore drift must describe loaded Release assets, independently of inactive Debug assets.");
    }

    [TestMethod]
    public async Task ConfiguredMetadata_TargetFrameworkMatchesLoadedProject()
    {
        using var fixture = new ReloadFixture();
        await PrepareConfiguredMetadataAsync(fixture, releaseStale: false);
        using var manager = CreateManager(new WorkspaceSessionLoader());
        var status = await manager.LoadAsync(fixture.ProjectPath, ConfiguredGlobals(), TestContext.CancellationToken);
        await AssertCompilerStateAsync(manager.GetCurrentSolution(status.WorkspaceId), configured: true);

        CollectionAssert.AreEqual(new[] { "net10.0" }, status.Projects.Single().TargetFrameworks.ToArray());
    }

    [TestMethod]
    public async Task ConfiguredMetadata_OutputTypeMatchesLoadedProject()
    {
        using var fixture = new ReloadFixture();
        await PrepareConfiguredMetadataAsync(fixture, releaseStale: false);
        using var manager = CreateManager(new WorkspaceSessionLoader());
        var status = await manager.LoadAsync(fixture.ProjectPath, ConfiguredGlobals(), TestContext.CancellationToken);
        var solution = manager.GetCurrentSolution(status.WorkspaceId);
        await AssertCompilerStateAsync(solution, configured: true);
        Assert.AreEqual(OutputKind.ConsoleApplication, solution.Projects.Single().CompilationOptions!.OutputKind);

        Assert.AreEqual("Exe", status.Projects.Single().OutputType);
    }

    private static async Task PrepareConfiguredMetadataAsync(ReloadFixture fixture, bool releaseStale)
    {
        var projectDirectory = Path.GetDirectoryName(fixture.ProjectPath)!;
        var root = Path.GetDirectoryName(projectDirectory)!;
        var propsPath = Path.Combine(root, "Directory.Build.props");
        var props = XDocument.Load(propsPath);
        props.Root!.AddFirst(new XElement("PropertyGroup",
            new XElement("Configuration", new XAttribute("Condition", "'$(Configuration)' == ''"), "Debug"),
            new XElement("MSBuildProjectExtensionsPath", "obj/$(Configuration)/")));
        props.Save(propsPath);

        var project = XDocument.Load(fixture.ProjectPath);
        project.Root!.Add(
            new XElement("PropertyGroup", new XAttribute("Condition", "'$(Configuration)' == 'Debug'"),
                new XElement("TargetFramework", "net9.0"), new XElement("OutputType", "Library")),
            new XElement("PropertyGroup", new XAttribute("Condition", "'$(Configuration)' == 'Release'"),
                new XElement("OutputType", "Exe")));
        project.Save(fixture.ProjectPath);
        File.WriteAllText(Path.Combine(projectDirectory, "Program.cs"),
            "namespace ReloadQualification; internal static class EntryPoint { private static void Main() { } }");

        var defaultAssets = Path.Combine(projectDirectory, "obj", "project.assets.json");
        Assert.IsTrue(File.Exists(defaultAssets), "The fixture must begin with real restored assets.");
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var configuredDirectory = Path.Combine(projectDirectory, "obj", configuration);
            Directory.CreateDirectory(configuredDirectory);
            var assetsPath = Path.Combine(configuredDirectory, "project.assets.json");
            File.Copy(defaultAssets, assetsPath);
            foreach (var import in Directory.EnumerateFiles(Path.GetDirectoryName(defaultAssets)!, "*.nuget.g.*"))
            {
                File.Copy(import, Path.Combine(configuredDirectory, Path.GetFileName(import)));
            }

            if ((configuration == "Release") == releaseStale)
            {
                var assets = JsonNode.Parse(File.ReadAllText(assetsPath))!;
                var central = assets["project"]!["frameworks"]!["net10.0"]!["centralPackageVersions"]!;
                Assert.IsNotNull(central["Microsoft.CodeAnalysis.BannedApiAnalyzers"]);
                central["Microsoft.CodeAnalysis.BannedApiAnalyzers"] = "999.0.0";
                File.WriteAllText(assetsPath, assets.ToJsonString());
            }
        }

        File.Delete(defaultAssets);
        Assert.IsFalse(File.Exists(defaultAssets), "A default assets file must not mask configuration selection.");
        await GitFixtureRunner.InitializeRepositoryAsync(root, "https://github.com/example/metadata-fixture.git");
        await GitFixtureRunner.StageAndCommitAllAsync(root);
    }

    private static Dictionary<string, string> ConfiguredGlobals() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Configuration"] = "Release",
        ["DefineConstants"] = "RELOAD_CUSTOM",
    };

    private static WorkspaceManager CreateManager(WorkspaceSessionLoader loader) => new(
        NullLogger<WorkspaceManager>.Instance,
        new PreviewStore(),
        new FileWatcherService(NullLogger<FileWatcherService>.Instance),
        new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 1 },
        cacheStore: null,
        sessionLoader: loader);

    private async Task AssertCompilerStateAsync(Solution solution, bool configured)
    {
        var project = solution.Projects.Single();
        var parseOptions = (CSharpParseOptions)project.ParseOptions!;
        Assert.AreEqual(configured, parseOptions.PreprocessorSymbolNames.Contains("RELOAD_CUSTOM"));
        Assert.AreEqual(configured ? OptimizationLevel.Release : OptimizationLevel.Debug,
            ((CSharpCompilationOptions)project.CompilationOptions!).OptimizationLevel);
        var compilation = await project.GetCompilationAsync(TestContext.CancellationToken);
        Assert.IsNotNull(compilation);
        var errors = compilation.GetDiagnostics(TestContext.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        var probe = compilation.GetTypeByMetadataName("ReloadQualification.ConfigurationProbe");
        Assert.IsNotNull(probe);
        var field = probe.GetMembers("Value").OfType<IFieldSymbol>().Single();
        Assert.AreEqual(configured ? 11 : 29, field.ConstantValue);
    }

    private sealed class CancelReloadLoader : WorkspaceSessionLoader
    {
        private int _calls;
        public TaskCompletionSource ReloadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease)> CreateAndOpenAsync(
            string workspaceId,
            string path,
            IDictionary<string, string>? globalProperties,
            WorkspaceDiagnosticsSink diagnostics,
            ILogger logger,
            CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) > 1)
            {
                ReloadEntered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            }

            return await base.CreateAndOpenAsync(workspaceId, path, globalProperties, diagnostics, logger, ct)
                .ConfigureAwait(false);
        }
    }

    private sealed class ReloadFixture : IDisposable
    {
        private readonly string _root;
        public string ProjectPath { get; }

        public ReloadFixture()
        {
            var repository = TestFixtureFileSystem.FindRepositoryRoot();
            var solution = TestFixtureFileSystem.FindFixturePath(repository, "SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");
            (_root, ProjectPath) = TestFixtureFileSystem.CreateSampleLibraryCopy(repository, solution);
            try
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(ProjectPath)!, "ConfigurationProbe.cs"), """
                    namespace ReloadQualification;
                    public static class ConfigurationProbe
                    {
                    #if RELOAD_CUSTOM
                        public const int Value = 11;
                    #else
                        public const int Value = 29;
                    #endif
                    }
                    """);
            }
            catch
            {
                TestFixtureFileSystem.DeleteDirectoryIfExists(_root);
                throw;
            }
        }

        public void Dispose() => TestFixtureFileSystem.DeleteDirectoryIfExists(_root);
    }
}
