using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

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
