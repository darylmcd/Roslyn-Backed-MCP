using System.Collections.Concurrent;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkInvocationPathPolicyTests
{
    [TestMethod]
    public async Task OutsideScope_UsesNormalWorkspaceIdentity()
    {
        var normal = new RecordingPolicy();
        var policy = new ForkInvocationPathPolicy(normal);
        await policy.ValidateAsync("ordinary", ["input"], CancellationToken.None);
        Assert.AreEqual("ordinary", normal.Calls.Single().Workspace);
    }

    [TestMethod]
    public async Task SourceAndBoundFork_UseLiveOriginalScope()
    {
        var normal = new RecordingPolicy();
        var policy = new ForkInvocationPathPolicy(normal);
        var authority = new TestAuthority(policy);
        using var scope = policy.Enter(authority);
        ForkInvocationPathPolicy.RequireActive(authority);
        ForkInvocationPathPolicy.AssertPolicyReceiver(policy);
        await policy.ValidateAsync("source", ["authored"], CancellationToken.None);
        await policy.ValidateAsync("fork", ["cache"], CancellationToken.None);
        Assert.AreEqual(2, normal.Calls.Count);
        Assert.IsTrue(normal.Calls.All(call => call.Workspace == "source"));
    }

    [TestMethod]
    public async Task ThirdWorkspaceAndWrongPolicy_RefuseBeforeNormalCallback()
    {
        var normal = new RecordingPolicy();
        var policy = new ForkInvocationPathPolicy(normal);
        var other = new ForkInvocationPathPolicy(normal);
        var authority = new TestAuthority(policy);
        using var scope = policy.Enter(authority);
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => policy.ValidateAsync("third", ["input"], CancellationToken.None));
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => other.ValidateAsync("fork", ["input"], CancellationToken.None));
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
        {
            ForkInvocationPathPolicy.AssertPolicyReceiver(normal);
            return Task.CompletedTask;
        });
        Assert.AreEqual(0, normal.Calls.Count);
    }

    [TestMethod]
    public async Task WrongAuthorityAndNestedEntry_RefuseWithoutReplacingActiveScope()
    {
        var normal = new RecordingPolicy();
        var policy = new ForkInvocationPathPolicy(normal);
        var other = new ForkInvocationPathPolicy(normal);
        var authority = new TestAuthority(policy);
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
        {
            using var invalid = other.Enter(authority);
            return Task.CompletedTask;
        });
        using var scope = policy.Enter(authority);
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
        {
            using var invalid = policy.Enter(new TestAuthority(policy));
            return Task.CompletedTask;
        });
        ForkInvocationPathPolicy.RequireActive(authority);
        await policy.ValidateAsync("fork", ["input"], CancellationToken.None);
        Assert.AreEqual(1, normal.Calls.Count);
    }

    [TestMethod]
    public async Task EscapedContinuation_AfterDisposeRefusesRatherThanFallingBack()
    {
        var normal = new RecordingPolicy();
        var policy = new ForkInvocationPathPolicy(normal);
        var authority = new TestAuthority(policy);
        var ready = Signal();
        var release = Signal();
        var scope = policy.Enter(authority);
        var escaped = Task.Run(async () =>
        {
            ready.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => policy.ValidateAsync("fork", ["input"], CancellationToken.None));
        });
        Exception? primary = null;
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            scope.Dispose();
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            scope.Dispose();
            release.TrySetResult();
            await CleanupFailureCollector.RunAfterFailureAsync("Fork scope test and escaped worker failed.", primary,
                async () => await escaped.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => authority.AssertCurrentAsync(CancellationToken.None));
        Assert.AreEqual(0, normal.Calls.Count);
        await policy.ValidateAsync("ordinary", ["input"], CancellationToken.None);
        Assert.AreEqual("ordinary", normal.Calls.Single().Workspace);
    }

    [TestMethod]
    public async Task DisposalDuringAwaitedValidation_RefusesSuccessfulUnderlyingResult()
    {
        var entered = Signal();
        var release = Signal();
        var normal = new RecordingPolicy { OnValidate = async (_, _, _) => { entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); } };
        var policy = new ForkInvocationPathPolicy(normal);
        var authority = new TestAuthority(policy);
        var scope = policy.Enter(authority);
        var pending = policy.ValidateAsync("fork", ["input"], CancellationToken.None);
        var observed = Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => pending);
        Exception? primary = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            scope.Dispose();
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            scope.Dispose();
            release.TrySetResult();
            await CleanupFailureCollector.RunAfterFailureAsync("Fork scope test and awaited validation failed.", primary,
                async () => await observed.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.AreEqual(1, normal.Calls.Count);
    }

    [TestMethod]
    public async Task UnrelatedExecutionContext_UsesNormalPolicyWhileForkIsActive()
    {
        var normal = new RecordingPolicy();
        var policy = new ForkInvocationPathPolicy(normal);
        var ready = Signal();
        var release = Signal();
        var unrelated = Task.Run(async () =>
        {
            ready.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await policy.ValidateAsync("unrelated", ["input"], CancellationToken.None);
        });
        IDisposable? scope = null;
        Exception? primary = null;
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            scope = policy.Enter(new TestAuthority(policy));
            release.SetResult();
            await unrelated.WaitAsync(TimeSpan.FromSeconds(10));
            await policy.ValidateAsync("fork", ["input"], CancellationToken.None);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            scope?.Dispose();
            release.TrySetResult();
            await CleanupFailureCollector.RunAfterFailureAsync("Fork scope test and unrelated worker failed.", primary,
                async () => await unrelated.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        CollectionAssert.AreEquivalent(new[] { "unrelated", "source" }, normal.Calls.Select(call => call.Workspace).ToArray());
    }

    [TestMethod]
    public async Task ConcurrentValidation_FreezesRequestedPathsBeforeAwait()
    {
        var normal = new RecordingPolicy();
        var policy = new ForkInvocationPathPolicy(normal);
        var entered = Signal();
        var release = Signal();
        var authority = new TestAuthority(policy) { BeforeRead = async () => { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); } };
        using var scope = policy.Enter(authority);
        var paths = new[] { "original" };
        var calls = Enumerable.Range(0, 16).Select(_ => policy.ValidateAsync("fork", paths, CancellationToken.None)).ToArray();
        var all = Task.WhenAll(calls);
        Exception? primary = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            paths[0] = "replacement";
            release.SetResult();
            await all.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            release.TrySetResult();
            await CleanupFailureCollector.RunAfterFailureAsync("Fork scope test and concurrent validators failed.", primary,
                async () => await all.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.AreEqual(16, normal.Calls.Count);
        Assert.IsTrue(normal.Calls.All(call => call.Paths.Single() == "original"));
    }

    [TestMethod]
    public async Task CurrentGrantReauthorizationAndCancellation_KeepCurrentAuthority()
    {
        var normal = new RecordingPolicy();
        var policy = new ForkInvocationPathPolicy(normal);
        var authority = new TestAuthority(policy);
        using var scope = policy.Enter(authority);
        await policy.ValidateAsync("fork", ["cache"], CancellationToken.None);
        normal.Deny = true;
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => authority.AssertCurrentAsync(CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => policy.ValidateAsync("fork", ["cache"], cancelled.Token));
        Assert.AreEqual(cancelled.Token, failure.CancellationToken);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RecordingPolicy : IWorkspacePathPolicy
    {
        internal ConcurrentQueue<(string Workspace, string[] Paths)> Calls { get; } = new();
        internal Func<string, IReadOnlyList<string>, CancellationToken, Task>? OnValidate { get; init; }
        internal bool Deny { get; set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Enqueue((workspaceId, paths.ToArray()));
            if (Deny) throw new UnauthorizedAccessException("The original current scope refuses this input.");
            if (OnValidate is { } callback) await callback(workspaceId, paths, ct);
        }
    }

    private sealed class TestAuthority(ForkInvocationPathPolicy policy) : ForkInvocationAuthority(policy)
    {
        internal override string StageParentPath { get; } = PhysicalGraphNamespace.StagingParent;
        private readonly ConcurrentDictionary<string, byte> _approved = new(StringComparer.Ordinal);
        internal Func<Task>? BeforeRead { get; init; }
        protected override void AssertCurrentCore() { }
        protected override bool AllowsWorkspaceCore(string workspaceId) => workspaceId is "source" or "fork";
        protected override Task AssertCurrentCoreAsync(CancellationToken ct)
            => Policy.ValidateUnscopedAsync("source", _approved.Keys.ToArray(), ct);
        protected override async Task ValidateCurrentCoreAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (BeforeRead is { } before) await before();
            await Policy.ValidateUnscopedAsync("source", paths, ct);
            foreach (var path in paths) _approved.TryAdd(path, 0);
        }
    }
}
