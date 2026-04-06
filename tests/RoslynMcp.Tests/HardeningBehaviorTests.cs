using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class HardeningBehaviorTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task WorkspaceLoad_InvalidPath_DoesNotLeakSession()
    {
        var beforeCount = WorkspaceManager.ListWorkspaces().Count;
        var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.slnx");

        await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
            WorkspaceManager.LoadAsync(missingPath, CancellationToken.None));

        Assert.AreEqual(beforeCount, WorkspaceManager.ListWorkspaces().Count);
    }

    [TestMethod]
    public async Task WorkspaceManager_RejectsLoadsPastConfiguredLimit()
    {
        using var manager = new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 1 });

        var firstPath = CreateSampleSolutionCopy();
        var secondPath = CreateSampleSolutionCopy();

        try
        {
            await manager.LoadAsync(firstPath, CancellationToken.None);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                manager.LoadAsync(secondPath, CancellationToken.None));
        }
        finally
        {
            DeleteDirectoryIfExists(Path.GetDirectoryName(firstPath)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(secondPath)!);
        }
    }

    [TestMethod]
    public async Task FindRelatedTestsForFiles_RejectsTooManyFilePaths()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
        var excessivePaths = Enumerable.Range(0, 26)
            .Select(index => Path.Combine(Path.GetTempPath(), $"File{index}.cs"))
            .ToArray();

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            TestDiscoveryService.FindRelatedTestsForFilesAsync(workspaceId, excessivePaths, 100, CancellationToken.None));
    }

    [TestMethod]
    public async Task ValidationService_TimesOutLongRunningCommands()
    {
        var fakeWorkspaceManager = new FakeWorkspaceManager();
        var executor = new GatedCommandExecutor(
            fakeWorkspaceManager,
            new HangingDotnetCommandRunner(),
            NullLogger<GatedCommandExecutor>.Instance);
        var service = new BuildService(
            fakeWorkspaceManager,
            executor,
            NullLogger<BuildService>.Instance,
            new ValidationServiceOptions
            {
                BuildTimeout = TimeSpan.FromMilliseconds(50),
                TestTimeout = TimeSpan.FromMilliseconds(50),
                MaxRelatedFiles = 25
            });

        await Assert.ThrowsExceptionAsync<TimeoutException>(() =>
            service.BuildWorkspaceAsync("workspace-1", CancellationToken.None));
    }

    private sealed class HangingDotnetCommandRunner : IDotnetCommandRunner
    {
        public async Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("The delay should have been cancelled.");
        }
    }

    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => !string.IsNullOrWhiteSpace(workspaceId);
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];
        public WorkspaceStatusDto GetStatus(string workspaceId) =>
            new(
                WorkspaceId: workspaceId,
                LoadedPath: "C:\\repo\\Sample.slnx",
                WorkspaceVersion: 1,
                SnapshotToken: "snapshot",
                LoadedAtUtc: DateTimeOffset.UtcNow,
                ProjectCount: 0,
                DocumentCount: 0,
                Projects: [],
                IsLoaded: true,
                IsStale: false,
                WorkspaceDiagnostics: []);
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus(workspaceId));
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
    }
}
