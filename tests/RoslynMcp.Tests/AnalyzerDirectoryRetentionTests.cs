using Microsoft.Extensions.Logging;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerDirectoryRetentionTests
{
    [TestMethod]
    public async Task Retirement_ReportsTerminalOutcomeWithoutRepeatingCleanup()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "analyzer-retention-" + Guid.NewGuid().ToString("N"));
        var shadow = Path.Combine(fixture, "lease");
        var payload = Path.Combine(shadow, "payload.dll");
        PhysicalDirectoryScope.OwnedRoot? shadowOwner = null;
        AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? lease = null;
        var logger = new RecordingLogger();
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            shadowOwner = PhysicalDirectoryScope.CreateOwnedRoot(shadow);
            File.WriteAllText(payload, "owned payload");
            lease = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(shadowOwner, [], 0, logger);
            shadowOwner = null;
            var late = Path.Combine(shadow, "late-content.txt");
            await File.WriteAllTextAsync(late, "content admitted by the private namespace");
            lease.Dispose();
            Assert.IsFalse(File.Exists(late), "Late content belongs to the admitted private namespace and must be reclaimed.");
            Assert.AreEqual(OperatingSystem.IsWindows() ? PhysicalDirectoryCleanupOutcome.Removed : PhysicalDirectoryCleanupOutcome.Retained,
                lease.CleanupOutcome);
            Assert.IsFalse(File.Exists(payload));
            Assert.AreEqual(!OperatingSystem.IsWindows(), Directory.Exists(shadow));
            var messages = logger.Messages.Count;
            Directory.CreateDirectory(shadow);
            File.WriteAllText(payload, "later unrelated bytes");
            lease.Dispose();
            Assert.AreEqual("later unrelated bytes", File.ReadAllText(payload));
            Assert.AreEqual(messages, logger.Messages.Count, "Terminal retention must not schedule or log repeated removal retries.");
            Assert.IsFalse(AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease.IsLiveRoot(shadow));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Analyzer retention fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => lease?.Dispose()),
            CleanupFailureCollector.FromAction(() => shadowOwner?.Dispose()),
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    public async Task Sweep_EmptyRetainedSkeletonDoesNotClaimContentReclamationAgain()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "analyzer-skeleton-" + Guid.NewGuid().ToString("N"));
        var lease = Path.Combine(fixture, "workspace", "lease");
        var logger = new RecordingLogger();
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(Path.Combine(lease, "nested"));
            File.WriteAllText(Path.Combine(lease, "nested", "payload.dll"), "stale payload");
            Directory.SetCreationTimeUtc(lease, DateTime.UtcNow.AddDays(-3));
            Directory.SetLastWriteTimeUtc(lease, DateTime.UtcNow.AddDays(-3));
            AnalyzerReferenceIsolation.SweepAbandonedRoots(fixture, logger);
            Assert.IsFalse(File.Exists(Path.Combine(lease, "nested", "payload.dll")));
            Assert.AreEqual(!OperatingSystem.IsWindows(), Directory.Exists(lease));
            var reclaimed = logger.Messages.Count(IsReclaimed);
            Assert.AreEqual(1, reclaimed, "The first pass must actually reclaim the stale payload.");
            AnalyzerReferenceIsolation.SweepAbandonedRoots(fixture, logger);
            Assert.AreEqual(reclaimed, logger.Messages.Count(IsReclaimed), "Already-empty retained skeletons contain no payload to reclaim again.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Analyzer sweep retention fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    private static bool IsReclaimed(string message) => message is "Swept one abandoned analyzer shadow lease."
        or "Reclaimed abandoned analyzer shadow content; retained directory namespace.";
    private sealed class RecordingLogger : ILogger
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
