using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceOwnedResourceCleanupTests
{
    [TestMethod]
    public async Task SessionClose_AttemptsWorkspaceLeaseAndLoadLockInOrder()
    {
        var path = TestFixtureFileSystem.FindFixturePath(
            TestFixtureFileSystem.FindRepositoryRoot(), "SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");
        var disposer = new ThrowingDisposer();
        var logger = new ListLogger<WorkspaceManager>();
        using var manager = new WorkspaceManager(logger, new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 1 }, null,
            new WorkspaceSessionLoader(), ownsFileWatcher: true)
        { ResourceDisposerForTesting = disposer };
        var status = await manager.LoadAsync(path, CancellationToken.None);

        var error = Assert.ThrowsExactly<AggregateException>(() => manager.Close(status.WorkspaceId));

        CollectionAssert.AreEqual(new[] { "workspace", "lease", "lock" }, disposer.Calls);
        CollectionAssert.AreEqual(new[] { disposer.WorkspaceFailure, disposer.LeaseFailure, disposer.LockFailure },
            error.Flatten().InnerExceptions);
    }

    [TestMethod]
    public async Task LoaderFailure_AttemptsWorkspaceThenLeaseAndPreservesPrimaryException()
    {
        var disposer = new ThrowingDisposer();
        var loader = new WorkspaceSessionLoader { ResourceDisposerForTesting = disposer };
        var primary = new InvalidOperationException("primary load failure");
        var logger = new ThrowingLogger(primary);

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => loader.CreateAndOpenAsync(
            "cleanup-test", "invalid.csproj", new Dictionary<string, string> { ["Configuration"] = "Release" },
            new WorkspaceDiagnosticsSink(16), logger, CancellationToken.None));

        Assert.AreSame(primary, thrown);
        CollectionAssert.AreEqual(new[] { "workspace", "lease" }, disposer.Calls);
        var log = logger.CleanupErrors.Single();
        CollectionAssert.AreEqual(new[] { disposer.WorkspaceFailure, disposer.LeaseFailure },
            ((AggregateException)log).InnerExceptions);
    }

    [TestMethod]
    public async Task LoaderFailure_AdmissionDisposalThrows_PreservesPrimaryException()
    {
        var primary = new InvalidOperationException("primary load failure");
        var admissionFailure = new InvalidOperationException("admission cleanup failure");
        var logger = new ThrowingLogger(primary);
        var loader = new WorkspaceSessionLoader
        {
            AdmissionDisposalForTesting = _ => throw admissionFailure
        };

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => loader.CreateAndOpenAsync(
            "cleanup-test", "invalid.csproj", new Dictionary<string, string> { ["Configuration"] = "Release" },
            new WorkspaceDiagnosticsSink(16), logger, CancellationToken.None));

        Assert.AreSame(primary, thrown);
        Assert.AreSame(admissionFailure, logger.CleanupErrors.Single());
    }

    [TestMethod]
    public async Task LoaderReturn_AdmissionDisposalThrows_AttemptsWorkspaceThenLease()
    {
        var path = TestFixtureFileSystem.FindFixturePath(
            TestFixtureFileSystem.FindRepositoryRoot(), "SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");
        var disposer = new ThrowingDisposer();
        var admissionFailure = new InvalidOperationException("admission cleanup failure");
        var logger = new ListLogger<WorkspaceSessionLoader>();
        var loader = new WorkspaceSessionLoader
        {
            ResourceDisposerForTesting = disposer,
            AdmissionDisposalForTesting = _ => throw admissionFailure
        };

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => loader.CreateAndOpenAsync(
            "cleanup-test", path, null, new WorkspaceDiagnosticsSink(16), logger, CancellationToken.None));

        Assert.AreSame(admissionFailure, thrown);
        CollectionAssert.AreEqual(new[] { "workspace", "lease" }, disposer.Calls);
        var cleanup = logger.Entries.Single(e => e.Level == LogLevel.Error && e.Exception is AggregateException);
        CollectionAssert.AreEqual(new[] { disposer.WorkspaceFailure, disposer.LeaseFailure },
            ((AggregateException)cleanup.Exception!).InnerExceptions);
    }

    private sealed class ThrowingDisposer : WorkspaceResourceDisposer
    {
        public List<string> Calls { get; } = [];
        public Exception WorkspaceFailure { get; } = new InvalidOperationException("workspace cleanup");
        public Exception LeaseFailure { get; } = new InvalidOperationException("lease cleanup");
        public Exception LockFailure { get; } = new InvalidOperationException("lock cleanup");

        public override void DisposeWorkspace(MSBuildWorkspace? workspace)
        {
            Calls.Add("workspace");
            workspace?.Dispose();
            throw WorkspaceFailure;
        }

        public override void DisposeLease(AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? lease)
        {
            Calls.Add("lease");
            lease?.Dispose();
            throw LeaseFailure;
        }

        public override void DisposeLoadLock(SemaphoreSlim loadLock)
        {
            Calls.Add("lock");
            loadLock.Dispose();
            throw LockFailure;
        }
    }

    private sealed class ThrowingLogger(Exception primary) : ILogger
    {
        public List<Exception> CleanupErrors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (level == LogLevel.Information) throw primary;
            if (level == LogLevel.Error && exception is not null) CleanupErrors.Add(exception);
        }
    }
}
