namespace RoslynMcp.Tests;

/// <summary>
/// msbuild-evaluation-uncached-perf regression guard. <see cref="RoslynMcp.Roslyn.Services.MsBuildEvaluationService"/>
/// previously constructed a fresh <see cref="Microsoft.Build.Evaluation.ProjectCollection"/> and re-ran
/// <c>LoadProject</c> — re-parsing the full MSBuild import graph from disk — on every
/// <c>EvaluatePropertyAsync</c>/<c>EvaluateItemsAsync</c>/<c>GetEvaluatedPropertiesAsync</c> call. The
/// service now caches the loaded project keyed by (workspace version, project file path) and invalidates
/// the whole cache on <c>WorkspaceReloaded</c>/<c>WorkspaceClosed</c>. These tests assert the cache is
/// (a) actually caching — an on-disk mutation without a reload is NOT observed — and (b) correctly
/// invalidated — a reload (version bump) surfaces the new value.
/// </summary>
[TestClass]
public sealed class MsBuildEvaluationCacheTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        // Registers MSBuild + builds the shared service graph (MsBuildEvaluationService, WorkspaceManager).
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    private const string ProbePropertyName = "CacheProbeProperty";

    private static string ProjectXml(string probeValue) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <{ProbePropertyName}>{probeValue}</{ProbePropertyName}>
          </PropertyGroup>
        </Project>
        """;

    [TestMethod]
    public async Task EvaluateProperty_MutatedOnDiskWithoutReload_ReturnsCachedValue()
    {
        using var fixture = CreateProjectFixture("CacheProbe.csproj", ProjectXml("before"));

        var loadStatus = await WorkspaceManager.LoadAsync(fixture.ProjectPath, CancellationToken.None);
        try
        {
            var projectName = (await WorkspaceManager.GetStatusAsync(loadStatus.WorkspaceId, CancellationToken.None))
                .Projects[0].Name;

            var first = await MsBuildEvaluationService.EvaluatePropertyAsync(
                loadStatus.WorkspaceId, projectName, ProbePropertyName, CancellationToken.None);
            Assert.AreEqual("before", first.EvaluatedValue,
                "First evaluation must read the property value present on disk at load time.");

            // Mutate the .csproj on disk WITHOUT reloading the workspace. The workspace version is
            // unchanged, so the cached MSBuild project must still surface the original value — proving
            // the evaluation is served from cache rather than a fresh LoadProject re-parse.
            File.WriteAllText(fixture.ProjectPath, ProjectXml("after"));

            var second = await MsBuildEvaluationService.EvaluatePropertyAsync(
                loadStatus.WorkspaceId, projectName, ProbePropertyName, CancellationToken.None);
            Assert.AreEqual("before", second.EvaluatedValue,
                "Without a workspace reload (no version bump), the cached project must be reused — the on-disk mutation must NOT be observed.");
        }
        finally
        {
            WorkspaceManager.Close(loadStatus.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task EvaluateProperty_AfterReload_InvalidatesCacheAndReturnsNewValue()
    {
        using var fixture = CreateProjectFixture("CacheProbeReload.csproj", ProjectXml("before"));

        var loadStatus = await WorkspaceManager.LoadAsync(fixture.ProjectPath, CancellationToken.None);
        try
        {
            var projectName = (await WorkspaceManager.GetStatusAsync(loadStatus.WorkspaceId, CancellationToken.None))
                .Projects[0].Name;

            var first = await MsBuildEvaluationService.EvaluatePropertyAsync(
                loadStatus.WorkspaceId, projectName, ProbePropertyName, CancellationToken.None);
            Assert.AreEqual("before", first.EvaluatedValue, "Baseline evaluation must read the on-disk value.");

            // Mutate on disk, then reload (bumps the workspace version and raises WorkspaceReloaded,
            // which the service subscribes to to drop the workspace's cached ProjectCollection(s)).
            File.WriteAllText(fixture.ProjectPath, ProjectXml("after"));
            await WorkspaceManager.ReloadAsync(loadStatus.WorkspaceId, CancellationToken.None);

            var afterReload = await MsBuildEvaluationService.EvaluatePropertyAsync(
                loadStatus.WorkspaceId, projectName, ProbePropertyName, CancellationToken.None);
            Assert.AreEqual("after", afterReload.EvaluatedValue,
                "A workspace reload must invalidate the cache (WorkspaceReloaded → InvalidateWorkspace); the next evaluation must re-parse and surface the new on-disk value.");
        }
        finally
        {
            WorkspaceManager.Close(loadStatus.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task EvaluateItems_RepeatedUnchangedCalls_ReturnIdenticalResults()
    {
        using var fixture = CreateProjectFixture(
            "CacheProbeItems.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);

        var loadStatus = await WorkspaceManager.LoadAsync(fixture.ProjectPath, CancellationToken.None);
        try
        {
            var projectName = (await WorkspaceManager.GetStatusAsync(loadStatus.WorkspaceId, CancellationToken.None))
                .Projects[0].Name;

            var first = await MsBuildEvaluationService.EvaluateItemsAsync(
                loadStatus.WorkspaceId, projectName, "PackageReference", CancellationToken.None);
            var second = await MsBuildEvaluationService.EvaluateItemsAsync(
                loadStatus.WorkspaceId, projectName, "PackageReference", CancellationToken.None);

            // Functional idempotence: caching the underlying project must not change the observed items.
            CollectionAssert.AreEqual(
                first.Items.Select(i => i.Include).ToList(),
                second.Items.Select(i => i.Include).ToList(),
                "Repeated EvaluateItemsAsync calls for an unchanged project must return identical item includes.");
            Assert.IsTrue(
                first.Items.Any(i => string.Equals(i.Include, "Newtonsoft.Json", StringComparison.OrdinalIgnoreCase)),
                "The declared PackageReference must be surfaced by evaluation.");
        }
        finally
        {
            WorkspaceManager.Close(loadStatus.WorkspaceId);
        }
    }

    private static ProjectFixture CreateProjectFixture(string fileName, string content)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RoslynMcpTests",
            "MsBuildEvaluationCacheTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var projectPath = Path.Combine(tempRoot, fileName);
        File.WriteAllText(projectPath, content);
        return new ProjectFixture(tempRoot, projectPath);
    }

    private sealed class ProjectFixture(string rootPath, string projectPath) : IDisposable
    {
        public string ProjectPath { get; } = projectPath;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; a locked temp dir is reclaimed by the OS sweep.
            }
        }
    }
}
