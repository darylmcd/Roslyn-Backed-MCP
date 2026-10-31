using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceIdleDiagnosticTests
{
    [TestMethod]
    public async Task StatusRead_LogsPreviousIdleDuration_ThenRefreshesAccessTime()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        var logger = new ListLogger<WorkspaceManager>();
        using var manager = new WorkspaceManager(
            logger,
            new PreviewStore(),
            new FileWatcherService(new ListLogger<FileWatcherService>()),
            new WorkspaceManagerOptions(),
            cacheStore: null,
            sessionLoader: null,
            timeProvider: clock);
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var path = TestFixtureFileSystem.FindFixturePath(repositoryRoot, "SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");

        var loaded = await manager.LoadAsync(path, CancellationToken.None);
        var loadedAt = clock.GetUtcNow();
        logger.Entries.Clear();

        clock.Advance(TimeSpan.FromMinutes(61));
        manager.GetStatus(loaded.WorkspaceId);
        Assert.IsTrue(logger.Entries.Any(entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("idle for 61 minutes", StringComparison.Ordinal)),
            "A read after 61 idle minutes must report the previous idle duration.");
        Assert.AreEqual(loadedAt, loaded.LoadedAtUtc);

        logger.Entries.Clear();
        clock.Advance(TimeSpan.FromMinutes(1));
        manager.GetStatus(loaded.WorkspaceId);
        Assert.IsFalse(logger.Entries.Any(entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("has been idle", StringComparison.Ordinal)),
            "The prior read must refresh access time, so a recent read has no long-idle diagnostic.");

        clock.Advance(TimeSpan.FromMinutes(61));
        var reused = await manager.LoadAsync(path, CancellationToken.None);
        Assert.AreEqual(loaded.WorkspaceId, reused.WorkspaceId);
        logger.Entries.Clear();
        clock.Advance(TimeSpan.FromMinutes(1));
        manager.GetStatus(loaded.WorkspaceId);
        Assert.IsFalse(logger.Entries.Any(entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("has been idle", StringComparison.Ordinal)),
            "Reusing a loaded workspace must also refresh access time on the injected clock.");
    }
}
