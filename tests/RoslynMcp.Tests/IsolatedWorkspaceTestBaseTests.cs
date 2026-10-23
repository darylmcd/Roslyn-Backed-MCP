using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class IsolatedWorkspaceTestBaseTests
{
    [TestMethod]
    public async Task RestoreWorkspaceAsync_FailureRetainsExitCodeAndBothOutputStreams()
    {
        var scope = CreateScope(
            closeWorkspace: _ => true,
            deleteRoot: _ => { });
        var runner = new FixtureCommandRunner(
            new CommandExecutionDto(
                Command: "dotnet",
                Arguments: ["restore"],
                WorkingDirectory: "fixture-root",
                TargetPath: "fixture-root/SampleSolution.slnx",
                ExitCode: 17,
                Succeeded: false,
                DurationMs: 1,
                StdOut: "restore-stdout-sentinel",
                StdErr: "restore-stderr-sentinel"));

        var failure = await Assert.ThrowsExactlyAsync<AssertFailedException>(() =>
            IsolatedWorkspaceTestBase.RestoreWorkspaceAsync(
                scope,
                runner,
                CancellationToken.None));

        StringAssert.Contains(failure.Message, "ExitCode=17");
        StringAssert.Contains(failure.Message, "restore-stdout-sentinel");
        StringAssert.Contains(failure.Message, "restore-stderr-sentinel");
        CollectionAssert.AreEqual(
            new[] { "restore", "fixture-root/SampleSolution.slnx", "--nologo" },
            runner.Arguments.ToArray());
    }

    [TestMethod]
    public async Task InitializeWithCleanupAsync_WhenInitializationFails_DisposesAndPreservesFailure()
    {
        var initializationFailure = new FixtureException("initialization failed");
        var resource = new FixtureResource();

        var actual = await Assert.ThrowsExactlyAsync<FixtureException>(() =>
            IsolatedWorkspaceTestBase.InitializeWithCleanupAsync(
                resource,
                (_, _) => Task.FromException(initializationFailure)));

        Assert.AreSame(initializationFailure, actual);
        Assert.IsTrue(resource.IsDisposed);
    }

    [TestMethod]
    public async Task InitializeWithCleanupAsync_WhenInitializationAndCleanupFail_AggregatesBoth()
    {
        var initializationFailure = new FixtureException("initialization failed");
        var cleanupFailure = new FixtureException("cleanup failed");
        var resource = new FixtureResource(cleanupFailure);

        var actual = await Assert.ThrowsExactlyAsync<AggregateException>(() =>
            IsolatedWorkspaceTestBase.InitializeWithCleanupAsync(
                resource,
                (_, _) => Task.FromException(initializationFailure)));

        CollectionAssert.AreEqual(
            new Exception[] { initializationFailure, cleanupFailure },
            actual.InnerExceptions.ToArray());
        Assert.IsTrue(resource.IsDisposed);
    }

    [TestMethod]
    public void ScopeDispose_WhenCloseFails_StillDeletesAndPreservesCloseFailure()
    {
        var closeFailure = new FixtureException("close failed");
        var calls = new List<string>();
        var scope = CreateScope(
            closeWorkspace: _ =>
            {
                calls.Add("close");
                throw closeFailure;
            },
            deleteRoot: _ => calls.Add("delete"));

        var actual = Assert.ThrowsExactly<FixtureException>(scope.Dispose);

        Assert.AreSame(closeFailure, actual);
        CollectionAssert.AreEqual(new[] { "close", "delete" }, calls);
    }

    [TestMethod]
    public void ScopeDispose_WhenDeleteFails_PreservesDeleteFailure()
    {
        var deleteFailure = new FixtureException("delete failed");
        var calls = new List<string>();
        var scope = CreateScope(
            closeWorkspace: _ =>
            {
                calls.Add("close");
                return true;
            },
            deleteRoot: _ =>
            {
                calls.Add("delete");
                throw deleteFailure;
            });

        var actual = Assert.ThrowsExactly<FixtureException>(scope.Dispose);

        Assert.AreSame(deleteFailure, actual);
        CollectionAssert.AreEqual(new[] { "close", "delete" }, calls);
    }

    [TestMethod]
    public void ScopeDispose_WhenCloseAndDeleteFail_AggregatesBoth()
    {
        var closeFailure = new FixtureException("close failed");
        var deleteFailure = new FixtureException("delete failed");
        var scope = CreateScope(
            closeWorkspace: _ => throw closeFailure,
            deleteRoot: _ => throw deleteFailure);

        var actual = Assert.ThrowsExactly<AggregateException>(scope.Dispose);

        CollectionAssert.AreEqual(
            new Exception[] { closeFailure, deleteFailure },
            actual.InnerExceptions.ToArray());
    }

    [TestMethod]
    public async Task ScopeDispose_AfterFailedCleanup_IsIdempotentAcrossSyncAndAsyncCalls()
    {
        var closeCalls = 0;
        var deleteCalls = 0;
        var scope = CreateScope(
            closeWorkspace: _ =>
            {
                closeCalls++;
                throw new FixtureException("close failed");
            },
            deleteRoot: _ => deleteCalls++);

        _ = Assert.ThrowsExactly<FixtureException>(scope.Dispose);
        scope.Dispose();
        await scope.DisposeAsync();

        Assert.AreEqual(1, closeCalls);
        Assert.AreEqual(1, deleteCalls);
    }

    [TestMethod]
    public async Task ScopeLoad_UsesOwnedCallbackOnceAndClosesItsWorkspace()
    {
        var manager = new FailClosedWorkspaceManagerStub();
        using var cancellation = new CancellationTokenSource();
        var loads = 0;
        string? closed = null;
        await using (var scope = new IsolatedWorkspaceTestBase.IsolatedWorkspaceScope(
            rootPath: "fixture-root",
            solutionPath: "fixture-root/Owned.slnx",
            workspaceId: null,
            manager,
            (path, ct) =>
            {
                loads++;
                Assert.AreEqual("fixture-root/Owned.slnx", path);
                Assert.AreEqual(cancellation.Token, ct);
                return Task.FromResult(new WorkspaceStatusDto(
                    "owned-workspace", path, 1, "snapshot", DateTimeOffset.UtcNow,
                    0, 0, [], true, false, []));
            },
            workspaceId => { closed = workspaceId; return true; },
            _ => { }))
        {
            Assert.AreEqual("owned-workspace", await scope.LoadAsync(cancellation.Token));
            Assert.AreEqual("owned-workspace", await scope.LoadAsync(cancellation.Token));
            Assert.AreEqual(1, loads);
        }
        Assert.AreEqual("owned-workspace", closed);
    }

    private static IsolatedWorkspaceTestBase.IsolatedWorkspaceScope CreateScope(
        Func<string, bool> closeWorkspace,
        Action<string> deleteRoot)
    {
        var manager = new FailClosedWorkspaceManagerStub();
        return new(
            rootPath: "fixture-root",
            solutionPath: "fixture-root/SampleSolution.slnx",
            workspaceId: "workspace-1",
            manager,
            (path, ct) => manager.LoadAsync(path, EvictPolicy.Strict, ct),
            closeWorkspace,
            deleteRoot);
    }

    private sealed class FixtureResource(Exception? cleanupFailure = null) : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return cleanupFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(cleanupFailure);
        }
    }

    private sealed class FixtureException(string message) : Exception(message);

    private sealed class FixtureCommandRunner(CommandExecutionDto result) : RoslynMcp.Tests.Helpers.CannedCommandRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public override Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Arguments = arguments;
            return Task.FromResult(result);
        }
    }
}
