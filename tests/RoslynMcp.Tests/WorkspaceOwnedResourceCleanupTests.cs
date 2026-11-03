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
        var primary = new InvalidOperationException("primary load failure");
        var loader = new WorkspaceSessionLoader
        {
            ResourceDisposerForTesting = disposer,
            AfterLeaseCreatedForTesting = (_, lease) =>
            {
                Assert.IsNotNull(lease);
                throw primary;
            }
        };
        var logger = new RecordingLogger();
        var path = SamplePath();

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => loader.CreateAndOpenAsync(
            "cleanup-test", path, null,
            new WorkspaceDiagnosticsSink(16), logger, CancellationToken.None));

        Assert.AreSame(primary, thrown);
        CollectionAssert.AreEqual(new[] { "workspace", "lease" }, disposer.Calls);
        var log = logger.Errors.Single();
        CollectionAssert.AreEqual(new[] { disposer.WorkspaceFailure, disposer.LeaseFailure },
            ((AggregateException)log).InnerExceptions);
    }

    [TestMethod]
    public async Task LoaderFailure_AdmissionDisposalThrows_PreservesPrimaryException()
    {
        var primary = new InvalidOperationException("primary load failure");
        var admissionFailure = new InvalidOperationException("admission cleanup failure");
        var logger = new RecordingLogger();
        var admission = new FakeAdmissionLifetime { DisposeFailure = admissionFailure };
        var loader = new WorkspaceSessionLoader
        {
            AdmissionFactoryForTesting = _ => ValueTask.FromResult<IWorkspaceAdmissionLifetime?>(admission),
            AfterLeaseCreatedForTesting = (_, lease) =>
            {
                Assert.IsNotNull(lease);
                throw primary;
            }
        };

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => loader.CreateAndOpenAsync(
            "cleanup-test", SamplePath(), null,
            new WorkspaceDiagnosticsSink(16), logger, CancellationToken.None));

        Assert.AreSame(primary, thrown);
        Assert.IsTrue(admission.Disposed);
        Assert.AreSame(admissionFailure, logger.Errors.Single());
    }

    [TestMethod]
    public async Task LoaderReturn_AdmissionDisposalThrows_AttemptsWorkspaceThenLease()
    {
        var path = SamplePath();
        var disposer = new ThrowingDisposer();
        var admissionFailure = new InvalidOperationException("admission cleanup failure");
        var logger = new RecordingLogger { ErrorFailure = new InvalidOperationException("logger failure") };
        var admission = new FakeAdmissionLifetime { DisposeFailure = admissionFailure };
        var loader = new WorkspaceSessionLoader
        {
            ResourceDisposerForTesting = disposer,
            AdmissionFactoryForTesting = _ => ValueTask.FromResult<IWorkspaceAdmissionLifetime?>(admission)
        };

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => loader.CreateAndOpenAsync(
            "cleanup-test", path, null, new WorkspaceDiagnosticsSink(16), logger, CancellationToken.None));

        Assert.AreSame(admissionFailure, thrown);
        Assert.IsTrue(admission.Disposed);
        CollectionAssert.AreEqual(new[] { "workspace", "lease" }, disposer.Calls);
        Assert.IsInstanceOfType<AggregateException>(thrown.Data["WorkspaceSessionLoader.CleanupFailures"]);
        Assert.IsInstanceOfType<List<Exception>>(thrown.Data["WorkspaceSessionLoader.ReportingFailures"]);
        var cleanup = logger.Errors.Single(e => e is AggregateException);
        CollectionAssert.AreEqual(new[] { disposer.WorkspaceFailure, disposer.LeaseFailure },
            ((AggregateException)cleanup).InnerExceptions);
    }

    [TestMethod]
    public async Task LoaderFailure_ErrorLoggerThrows_PreservesPrimaryAfterLiveLeaseCleanup()
    {
        var primary = new InvalidOperationException("primary load failure");
        var disposer = new ThrowingDisposer();
        var logger = new RecordingLogger { ErrorFailure = new InvalidOperationException("logger failure") };
        var loader = new WorkspaceSessionLoader
        {
            ResourceDisposerForTesting = disposer,
            AfterLeaseCreatedForTesting = (_, lease) =>
            {
                Assert.IsNotNull(lease);
                throw primary;
            }
        };

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => loader.CreateAndOpenAsync(
            "cleanup-test", SamplePath(), null, new WorkspaceDiagnosticsSink(16), logger, CancellationToken.None));

        Assert.AreSame(primary, thrown);
        CollectionAssert.AreEqual(new[] { "workspace", "lease" }, disposer.Calls);
        Assert.IsInstanceOfType<AggregateException>(thrown.Data["WorkspaceSessionLoader.CleanupFailures"]);
        Assert.IsInstanceOfType<List<Exception>>(thrown.Data["WorkspaceSessionLoader.ReportingFailures"]);
    }

    [TestMethod]
    public async Task LoaderWarning_ThrowingLogger_StillCleansLiveLease()
    {
        var warning = "simulated retained admission material";
        var admission = new FakeAdmissionLifetime { WarningOnDispose = warning };
        var disposer = new ThrowingDisposer();
        var logger = new RecordingLogger { WarningFailure = new InvalidOperationException("warning logger failure") };
        var loader = new WorkspaceSessionLoader
        {
            ResourceDisposerForTesting = disposer,
            AdmissionFactoryForTesting = _ => ValueTask.FromResult<IWorkspaceAdmissionLifetime?>(admission)
        };

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => loader.CreateAndOpenAsync(
            "cleanup-test", SamplePath(), null, new WorkspaceDiagnosticsSink(16), logger, CancellationToken.None));

        StringAssert.Contains(thrown.Message, warning);
        Assert.IsTrue(admission.Disposed);
        CollectionAssert.AreEqual(new[] { "workspace", "lease" }, disposer.Calls);
        Assert.IsInstanceOfType<AggregateException>(thrown.Data["WorkspaceSessionLoader.CleanupFailures"]);
        Assert.IsInstanceOfType<List<Exception>>(thrown.Data["WorkspaceSessionLoader.ReportingFailures"]);
    }

    private static string SamplePath() => TestFixtureFileSystem.FindFixturePath(
        TestFixtureFileSystem.FindRepositoryRoot(), "SampleSolution", "SampleSolution.slnx", "SampleSolution.sln");

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
            Assert.IsNotNull(lease, "The caller must release a live analyzer lease.");
            lease.Dispose();
            throw LeaseFailure;
        }

        public override void DisposeLoadLock(SemaphoreSlim loadLock)
        {
            Calls.Add("lock");
            loadLock.Dispose();
            throw LockFailure;
        }
    }

    private sealed class FakeAdmissionLifetime : IWorkspaceAdmissionLifetime
    {
        public Exception? DisposeFailure { get; set; }
        public string? WarningOnDispose { get; set; }
        public string? CleanupWarning { get; private set; }
        public bool Disposed { get; private set; }
        public Dictionary<string, string> Properties(IDictionary<string, string>? original) =>
            original is null ? [] : new Dictionary<string, string>(original);
        public void Complete() { }
        public void ThrowIfRefused() { }
        public void Dispose()
        {
            Disposed = true;
            CleanupWarning = WarningOnDispose;
            if (DisposeFailure is not null) throw DisposeFailure;
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<Exception> Errors { get; } = [];
        public Exception? ErrorFailure { get; set; }
        public Exception? WarningFailure { get; set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (level == LogLevel.Error)
            {
                if (exception is not null) Errors.Add(exception);
                if (ErrorFailure is not null) throw ErrorFailure;
            }
            if (level == LogLevel.Warning && WarningFailure is not null) throw WarningFailure;
        }
    }
}
