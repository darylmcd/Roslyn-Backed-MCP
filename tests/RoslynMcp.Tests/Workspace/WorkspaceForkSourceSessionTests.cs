using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceForkSourceSessionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(1, true)]
    [DataRow(2, true)]
    public async Task WorkspaceForkApply_CapPressure_PreservesSourceAuthority(int capacity, bool overrideManager)
    {
        var repository = TestFixtureFileSystem.FindRepositoryRoot();
        var sample = TestFixtureFileSystem.FindFixturePath(repository,
            "SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");
        var sourcePath = TestFixtureFileSystem.CreateSampleSolutionCopy(repository, sample);
        string? otherPath = null;
        var runner = new PausedRestoreRunner();
        var managerLog = new ListLogger<WorkspaceManager>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new WorkspaceManagerOptions { MaxConcurrentWorkspaces = capacity });
        services.AddRoslynServices();
        services.AddSingleton<ILogger<WorkspaceManager>>(managerLog);
        services.AddSingleton<IDotnetCommandRunner>(runner);
        if (overrideManager)
            services.AddSingleton<IWorkspaceManager>(sp => WorkspaceManager.CreateProviderOwned(
                sp.GetRequiredService<ILogger<WorkspaceManager>>(), sp.GetRequiredService<IPreviewStore>(),
                sp.GetRequiredService<IFileWatcherService>(), sp.GetRequiredService<WorkspaceManagerOptions>(),
                cacheStore: null, sp.GetRequiredService<Lazy<IWorkspaceExecutionGate>>()));
        var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IWorkspaceManager>();
        var store = (PreviewStore)provider.GetRequiredService<IPreviewStore>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        Task<string>? forkTask = null;
        var applyOutcomeCaptured = false;
        Exception? primaryFailure = null;
        try
        {
            var managers = provider.GetServices<IWorkspaceManager>().ToArray();
            var originalManager = overrideManager ? managers[0] : null;
            var originalSession = originalManager is null ? null : await originalManager.LoadAsync(sourcePath, EvictPolicy.Strict, cancellation.Token);
            var originalManagerSolution = originalSession is null ? null : originalManager!.GetCurrentSolution(originalSession.WorkspaceId);
            var source = await manager.LoadAsync(sourcePath, EvictPolicy.Strict, cancellation.Token);
            string? otherId = null;
            if (capacity > 1)
            {
                otherPath = TestFixtureFileSystem.CreateSampleSolutionCopy(repository, sample);
                otherId = (await manager.LoadAsync(otherPath, EvictPolicy.Strict, cancellation.Token)).WorkspaceId;
            }

            var preview = await provider.GetRequiredService<IRefactoringService>().PreviewRenameAsync(
                source.WorkspaceId, SymbolLocator.ByMetadataName("SampleLib.Dog.Fetch"),
                "FetchInFork", cancellation.Token);
            Assert.IsTrue(preview.Changes.Count > 0, "Use a genuine, nonempty document-only rename preview.");
            var originalSolution = manager.GetCurrentSolution(source.WorkspaceId);
            var originalStatus = manager.GetStatus(source.WorkspaceId);
            var originalEntry = ReadFullEntry(store, preview.PreviewToken);
            Assert.IsNotNull(originalEntry);
            Assert.AreEqual(PreviewKind.SymbolRename, originalEntry.Kind);
            Assert.IsFalse(originalEntry.DiffTruncated);
            var changes = originalEntry.ModifiedSolution.GetChanges(originalEntry.OriginalSolution);
            Assert.AreEqual(0, changes.GetAddedProjects().Count());
            Assert.AreEqual(0, changes.GetRemovedProjects().Count());
            Assert.IsTrue(changes.GetProjectChanges().All(change =>
                !change.GetAddedDocuments().Any() && !change.GetRemovedDocuments().Any()
                && !change.GetAddedProjectReferences().Any() && !change.GetRemovedProjectReferences().Any()));
            var sourceBytes = originalSolution.Projects.SelectMany(project => project.Documents)
                .Select(document => document.FilePath).Append(sourcePath)
                .Concat(originalSolution.Projects.Select(project => project.FilePath))
                .OfType<string>().Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

            forkTask = ValidationBundleTools.WorkspaceForkApply(
                provider.GetRequiredService<IWorkspaceForkApplyService>(), source.WorkspaceId,
                preview.PreviewToken, retention: "keep", runTests: false,
                forkName: "source-authority", ct: cancellation.Token);
            var rendezvous = await Task.WhenAny(runner.Restored.Task, forkTask).WaitAsync(cancellation.Token);
            // Apply has already called source GetStatus and captured/replayed the preview.
            // Touch only the other idle workspace now: the source is the actual oldest
            // candidate when LoadAsync performs its LRU selection after restore resumes.
            if (rendezvous == runner.Restored.Task && otherId is not null) _ = manager.GetStatus(otherId);
            runner.Release.TrySetResult();

            string? result = null;
            Exception? applyFailure = null;
            try { result = await forkTask; }
            catch (Exception exception) { applyFailure = exception; }
            applyOutcomeCaptured = true;

            // Observe every source authority before asserting, and before fixture cleanup.
            var sourcePresent = manager.ContainsWorkspace(source.WorkspaceId);
            var entryAfter = ReadFullEntry(store, preview.PreviewToken);
            var statusAfter = sourcePresent ? manager.GetStatus(source.WorkspaceId) : null;
            var solutionAfter = sourcePresent ? manager.GetCurrentSolution(source.WorkspaceId) : null;
            var bytesPreserved = sourceBytes.All(pair => File.Exists(pair.Key)
                && pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)));
            var otherPresent = otherId is not null && manager.ContainsWorkspace(otherId);
            var evidence = new
            {
                capacity,
                overrideManager,
                source.WorkspaceId,
                otherId,
                sourcePresent,
                otherPresent,
                sourceSolutionSame = ReferenceEquals(originalSolution, solutionAfter),
                sourceWorkspaceSame = ReferenceEquals(originalSolution.Workspace, solutionAfter?.Workspace),
                versionBefore = originalStatus.WorkspaceVersion,
                versionAfter = statusAfter?.WorkspaceVersion,
                snapshotBefore = originalStatus.SnapshotToken,
                snapshotAfter = statusAfter?.SnapshotToken,
                previewEntrySame = ReferenceEquals(originalEntry, entryAfter),
                previewEntryEqual = originalEntry == entryAfter,
                bytesPreserved,
                previewBefore = EntryEvidence(originalEntry),
                previewAfter = EntryEvidence(entryAfter),
                result,
                exception = applyFailure?.ToString(),
                managerEvents = managerLog.Entries.Select(entry => entry.Message).ToArray()
            };
            var proof = JsonSerializer.Serialize(evidence);
            TestContext.WriteLine(proof);
            var defects = new List<string>();
            if (originalSession is not null && (!originalManager!.ContainsWorkspace(originalSession.WorkspaceId)
                || !ReferenceEquals(originalManagerSolution, originalManager.GetCurrentSolution(originalSession.WorkspaceId))
                || originalManager.ListWorkspaces().Count != 1))
                defects.Add("unused originally composed manager was changed");
            if (!sourcePresent) defects.Add("source session was evicted");
            if (!ReferenceEquals(originalSolution, solutionAfter)) defects.Add("source Solution identity lost");
            if (!ReferenceEquals(originalSolution.Workspace, solutionAfter?.Workspace)) defects.Add("source workspace identity lost");
            if (originalStatus.WorkspaceVersion != statusAfter?.WorkspaceVersion) defects.Add("source version authority lost");
            if (originalStatus.SnapshotToken != statusAfter?.SnapshotToken) defects.Add("source snapshot authority lost");
            if (!ReferenceEquals(originalEntry, entryAfter) || originalEntry != entryAfter) defects.Add("full stored preview authority lost");
            if (!bytesPreserved) defects.Add("source disk bytes changed");
            if (capacity == 1)
            {
                if (applyFailure is not InvalidOperationException or not IPublicMessageException)
                    defects.Add("source-only cap must return an actionable public capacity refusal");
                else if (!applyFailure.Message.Contains("workspace", StringComparison.OrdinalIgnoreCase))
                    defects.Add("capacity refusal must explain workspace recovery");
            }
            else
            {
                if (applyFailure is not null) defects.Add("eligible other idle session should permit fork load");
                if (otherPresent) defects.Add("other idle session must be evicted instead of source");
                if (result is null) defects.Add("fork result missing");
                else
                {
                    using var json = JsonDocument.Parse(result);
                    if (!json.RootElement.GetProperty("success").GetBoolean()) defects.Add("real fork validation did not succeed");
                    var forkId = json.RootElement.GetProperty("forkWorkspaceId").GetString();
                    if (forkId is null || !manager.ContainsWorkspace(forkId)) defects.Add("successful retained fork must be loaded");
                }
            }
            Assert.IsEmpty(defects, string.Join("; ", defects) + Environment.NewLine + proof);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            runner.Release.TrySetResult();
            await CleanupFailureCollector.RunAfterFailureAsync("Fork source qualification cleanup failed.", primaryFailure,
                CleanupFailureCollector.FromAction(cancellation.Cancel),
                async () =>
                {
                    if (forkTask is null) return;
                    try { await forkTask; }
                    catch (Exception) when (applyOutcomeCaptured) { /* The same outcome is already captured in the assertion evidence. */ }
                },
                async () => await provider.DisposeAsync(),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(sourcePath)!)),
                CleanupFailureCollector.FromAction(() =>
                {
                    if (otherPath is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(otherPath)!);
                }));
        }
    }

    // Read the real store's complete immutable entry without changing its TTL, policy,
    // contents or production API. Retrieve exposes only six of its nine authority fields.
    private static PreviewStore.PreviewEntry? ReadFullEntry(PreviewStore store, string token) =>
        (PreviewStore.PreviewEntry?)typeof(BoundedStore<PreviewStore.PreviewEntry>)
            .GetMethod("RetrieveEntry", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(store, [token]);

    private static object? EntryEvidence(PreviewStore.PreviewEntry? entry) => entry is null ? null : new
    {
        entry.WorkspaceId,
        entry.WorkspaceVersion,
        entry.MaxRedeemableVersion,
        entry.Description,
        entry.DiffTruncated,
        entry.Kind,
        entry.CreatedAt,
        originalSolutionId = entry.OriginalSolution.Id.ToString(),
        modifiedSolutionId = entry.ModifiedSolution.Id.ToString()
    };

    private sealed class PausedRestoreRunner : IDotnetCommandRunner
    {
        private readonly DotnetCommandRunner _inner = new();
        internal TaskCompletionSource Restored { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            var result = await _inner.RunAsync(workingDirectory, targetPath, arguments, ct);
            if (arguments[0] == "restore")
            {
                Assert.IsTrue(result.Succeeded, $"Native fork restore failed: {result.StdOut} {result.StdErr}");
                Restored.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }
}
