using RoslynMcp.Tests.Helpers;
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
        var fixture = CreateProjectFixture("CacheProbe.csproj", ProjectXml("before"));
        await RunCacheFixtureAsync(
            async () => (await WorkspaceManager.LoadAsync(fixture.ProjectPath, CancellationToken.None)).WorkspaceId,
            async workspaceId =>
            {
                var projectName = (await WorkspaceManager.GetStatusAsync(workspaceId, CancellationToken.None))
                    .Projects[0].Name;

                var first = await MsBuildEvaluationService.EvaluatePropertyAsync(
                    workspaceId, projectName, ProbePropertyName, CancellationToken.None);
                Assert.AreEqual("before", first.EvaluatedValue,
                    "First evaluation must read the property value present on disk at load time.");

                // Mutate the .csproj on disk WITHOUT reloading the workspace. The workspace version is
                // unchanged, so the cached MSBuild project must still surface the original value — proving
                // the evaluation is served from cache rather than a fresh LoadProject re-parse.
                File.WriteAllText(fixture.ProjectPath, ProjectXml("after"));

                var second = await MsBuildEvaluationService.EvaluatePropertyAsync(
                    workspaceId, projectName, ProbePropertyName, CancellationToken.None);
                Assert.AreEqual("before", second.EvaluatedValue,
                    "Without a workspace reload (no version bump), the cached project must be reused — the on-disk mutation must NOT be observed.");
            }, id => WorkspaceManager.Close(id), fixture.Dispose);
    }

    [TestMethod]
    public async Task EvaluateProperty_AfterReload_InvalidatesCacheAndReturnsNewValue()
    {
        var fixture = CreateProjectFixture("CacheProbeReload.csproj", ProjectXml("before"));
        await RunCacheFixtureAsync(
            async () => (await WorkspaceManager.LoadAsync(fixture.ProjectPath, CancellationToken.None)).WorkspaceId,
            async workspaceId =>
            {
                var projectName = (await WorkspaceManager.GetStatusAsync(workspaceId, CancellationToken.None))
                    .Projects[0].Name;

                var first = await MsBuildEvaluationService.EvaluatePropertyAsync(
                    workspaceId, projectName, ProbePropertyName, CancellationToken.None);
                Assert.AreEqual("before", first.EvaluatedValue, "Baseline evaluation must read the on-disk value.");

                // Mutate on disk, then reload (bumps the workspace version and raises WorkspaceReloaded,
                // which the service subscribes to to drop the workspace's cached ProjectCollection(s)).
                File.WriteAllText(fixture.ProjectPath, ProjectXml("after"));
                await WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);

                var afterReload = await MsBuildEvaluationService.EvaluatePropertyAsync(
                    workspaceId, projectName, ProbePropertyName, CancellationToken.None);
                Assert.AreEqual("after", afterReload.EvaluatedValue,
                    "A workspace reload must invalidate the cache (WorkspaceReloaded → InvalidateWorkspace); the next evaluation must re-parse and surface the new on-disk value.");
            }, id => WorkspaceManager.Close(id), fixture.Dispose);
    }

    [TestMethod]
    public async Task EvaluateItems_RepeatedUnchangedCalls_ReturnIdenticalResults()
    {
        var fixture = CreateProjectFixture(
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
        await RunCacheFixtureAsync(
            async () => (await WorkspaceManager.LoadAsync(fixture.ProjectPath, CancellationToken.None)).WorkspaceId,
            async workspaceId =>
            {
                var projectName = (await WorkspaceManager.GetStatusAsync(workspaceId, CancellationToken.None))
                    .Projects[0].Name;

                var first = await MsBuildEvaluationService.EvaluateItemsAsync(
                    workspaceId, projectName, "PackageReference", CancellationToken.None);
                var second = await MsBuildEvaluationService.EvaluateItemsAsync(
                    workspaceId, projectName, "PackageReference", CancellationToken.None);

                // Functional idempotence: caching the underlying project must not change the observed items.
                CollectionAssert.AreEqual(
                    first.Items.Select(i => i.Include).ToList(),
                    second.Items.Select(i => i.Include).ToList(),
                    "Repeated EvaluateItemsAsync calls for an unchanged project must return identical item includes.");
                Assert.IsTrue(
                    first.Items.Any(i => string.Equals(i.Include, "Newtonsoft.Json", StringComparison.OrdinalIgnoreCase)),
                    "The declared PackageReference must be surfaced by evaluation.");
            }, id => WorkspaceManager.Close(id), fixture.Dispose);
    }

    private static Task RunCacheFixtureAsync(
        Func<Task<string>> loadWorkspace,
        Func<string, Task> body,
        Action<string> closeWorkspace,
        Action disposeFixture)
    {
        string? workspaceId = null;
        return FixtureCleanupScope.RunAsync(async () =>
        {
            workspaceId = await loadWorkspace();
            await body(workspaceId);
        },
        CleanupFailureCollector.FromAction(() =>
        {
            if (workspaceId is not null)
                closeWorkspace(workspaceId);
        }),
        CleanupFailureCollector.FromAction(disposeFixture));
    }

    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, true, true)]
    public async Task CacheFixtureCleanup_PreservesFailuresAcrossCombinations(
        bool failBody, bool failClose, bool failDelete)
    {
        var bodyFailure = new InvalidOperationException("body failed");
        var closeFailure = new InvalidOperationException("close failed");
        var deleteFailure = new IOException("delete failed");
        var closeCalls = 0;
        var deleteCalls = 0;

        Exception? failure = null;
        try
        {
            await RunCacheFixtureAsync(
                () => Task.FromResult("workspace"),
                _ => failBody ? Task.FromException(bodyFailure) : Task.CompletedTask,
                _ => { closeCalls++; if (failClose) throw closeFailure; },
                () => { deleteCalls++; if (failDelete) throw deleteFailure; });
        }
        catch (Exception observed)
        {
            failure = observed;
        }

        Assert.AreEqual(1, closeCalls);
        Assert.AreEqual(1, deleteCalls);
        var expected = new List<Exception>();
        if (failBody) expected.Add(bodyFailure);
        if (failClose) expected.Add(closeFailure);
        if (failDelete) expected.Add(deleteFailure);
        Assert.IsNotNull(failure);
        if (failBody && expected.Count == 1)
        {
            Assert.AreSame(expected[0], failure);
        }
        else
        {
            var aggregate = Assert.IsInstanceOfType<AggregateException>(failure);
            CollectionAssert.AreEqual(expected, aggregate.InnerExceptions.ToList());
        }
    }

    [TestMethod]
    public async Task CacheFixtureCleanup_WhenLoadFails_DeletesFixtureWithoutClosingWorkspace()
    {
        var loadFailure = new IOException("load failed");
        var closeCalls = 0;
        var deleteCalls = 0;
        var observed = await Assert.ThrowsExactlyAsync<IOException>(() =>
            RunCacheFixtureAsync(
                () => Task.FromException<string>(loadFailure),
                _ => throw new AssertFailedException("Body must not run without a workspace."),
                _ => closeCalls++,
                () => deleteCalls++));

        Assert.AreSame(loadFailure, observed);
        Assert.AreEqual(0, closeCalls);
        Assert.AreEqual(1, deleteCalls);
    }

    private static ProjectFixture CreateProjectFixture(string fileName, string content)
        => CreateProjectFixture(fileName, content, File.WriteAllText,
            TestFixtureFileSystem.DeleteDirectoryIfExists);

    private static ProjectFixture CreateProjectFixture(
        string fileName, string content, Action<string, string> writeFile, Action<string> deleteRoot)
    {
        var tempRoot = Path.Combine(
            TestTempRoot.Current,
            "MsBuildEvaluationCacheTests",
            Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(tempRoot, fileName);
        return FixtureConstructionScope.Create(tempRoot, () =>
        {
            writeFile(projectPath, content);
            return new ProjectFixture(tempRoot, projectPath);
        }, deleteRoot);
    }

    [TestMethod]
    public void ProjectFixtureConstruction_CompensatesWriteFailureAndPreservesCleanupFailure()
    {
        string? root = null;
        var writeFailure = new IOException("project write failed");
        var cleanupFailure = new IOException("root cleanup failed");
        var failure = Assert.ThrowsExactly<AggregateException>(() =>
            CreateProjectFixture("probe.csproj", "content",
                (path, _) => { root = Path.GetDirectoryName(path); throw writeFailure; },
                path => { TestFixtureFileSystem.DeleteDirectoryIfExists(path); throw cleanupFailure; }));

        Assert.AreSame(writeFailure, failure.InnerExceptions[0]);
        Assert.AreSame(cleanupFailure, failure.InnerExceptions[1]);
        Assert.IsNotNull(root);
        Assert.IsFalse(Directory.Exists(root));

        root = null;
        var observed = Assert.ThrowsExactly<IOException>(() =>
            CreateProjectFixture("probe.csproj", "content",
                (path, _) => { root = Path.GetDirectoryName(path); throw writeFailure; },
                TestFixtureFileSystem.DeleteDirectoryIfExists));
        Assert.AreSame(writeFailure, observed);
        Assert.IsNotNull(root);
        Assert.IsFalse(Directory.Exists(root));
    }

    private sealed class ProjectFixture(string rootPath, string projectPath) : IDisposable
    {
        public string ProjectPath { get; } = projectPath;

        public void Dispose()
            => TestFixtureFileSystem.DeleteDirectoryIfExists(rootPath);
    }

    [TestMethod]
    public void ProjectFixtureDispose_ReportsInvalidRoot_AndAcceptsMissingRoot()
    {
        var invalid = new ProjectFixture(Path.Combine(TestTempRoot.Current, "invalid\0project"), "unused");
        Assert.ThrowsExactly<ArgumentException>(invalid.Dispose);

        var missing = new ProjectFixture(Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N")), "unused");
        missing.Dispose();

        var root = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        new ProjectFixture(root, "unused").Dispose();
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public async Task ProjectFixtureDispose_PreservesAsyncBodyAndCleanupFailures()
    {
        var fixture = new ProjectFixture(Path.Combine(TestTempRoot.Current, "invalid\0project"), "unused");
        var failure = await Assert.ThrowsExactlyAsync<AggregateException>(async () =>
            await FixtureCleanupScope.RunAsync(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("body failure");
            }, fixture.Dispose));
        Assert.IsInstanceOfType<InvalidOperationException>(failure.InnerExceptions[0]);
        Assert.IsInstanceOfType<ArgumentException>(failure.InnerExceptions[1]);
    }
}
