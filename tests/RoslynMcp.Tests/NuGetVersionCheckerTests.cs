using System.Net;
using RoslynMcp.Host.Stdio.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `nuget-version-check-observability`: <see cref="NuGetVersionChecker"/>
/// must stay non-blocking and never-throwing, but a failed/timed-out background fetch must
/// now record an observable <see cref="VersionCheckStatus"/> instead of silently swallowing.
/// Drives the checker with a fake <see cref="HttpMessageHandler"/> so the background fetch
/// completes deterministically.
/// </summary>
[TestClass]
public sealed class NuGetVersionCheckerTests
{
    /// <summary>HttpMessageHandler whose send always throws — simulates a network failure.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("simulated network failure");
    }

    /// <summary>HttpMessageHandler returning a canned NuGet flat-container index.json body.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body),
            });
    }

    /// <summary>HttpMessageHandler that blocks until the test releases it.</summary>
    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RequestStarted => _requestStarted.Task;

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requestStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"versions":["1.0.0"]}"""),
            };
        }
    }

    /// <summary>HttpMessageHandler that succeeds once, then blocks the refresh request.</summary>
    private sealed class RefreshBlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _refreshStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseRefresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public Task RefreshStarted => _refreshStarted.Task;

        public void ReleaseRefresh() => _releaseRefresh.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestNumber = Interlocked.Increment(ref _requestCount);
            if (requestNumber == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"versions":["1.0.0"]}"""),
                };
            }

            _refreshStarted.TrySetResult();
            await _releaseRefresh.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"versions":["1.1.0"]}"""),
            };
        }
    }

    /// <summary>HttpMessageHandler that waits for the checker's bounded timeout.</summary>
    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        }
    }

    private static async Task WaitForCompletionAsync(NuGetVersionChecker checker)
    {
        // Await the in-flight background fetch directly via the internal test seam rather than
        // spin-looping on the observable status — deterministic and free of timing sensitivity
        // under heavy CI parallel load. The bounded WaitAsync is ONLY a true-hang guard, never a
        // normal-path timer: it MUST sit comfortably above NuGetVersionChecker.HttpTimeout (3 s,
        // the checker's internal CancellationTokenSource), because the timeout-path test
        // (GetLatestVersion_OnTimeout_...) relies on that 3 s timer firing AND its continuation
        // recording TimedOut before this wait expires. A tight bound (e.g. 5 s, ~2 s margin) loses
        // that race under self-hosted-runner CPU contention and throws TimeoutException spuriously
        // — the flake this guard's previous values caused twice (nuget-version-checker-test-wallclock-poll,
        // then nuget-version-checker-timeout-test-wallclock-race). 30 s gives ~10x headroom over the
        // 3 s timer; do NOT lower it toward HttpTimeout. If HttpTimeout ever grows, keep this >> it.
        var pending = checker.PendingCheckForTest;
        Assert.IsNotNull(pending, "Expected a background version check to be in flight.");
        await pending.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        Assert.AreNotEqual(VersionCheckStatus.Pending, checker.LastCheckStatus,
            "Background version check should have reached a terminal status.");
    }

    [TestMethod]
    public async Task GetLatestVersion_WhileFetchInFlight_RecordsPendingStatus()
    {
        using var handler = new BlockingHandler();
        var checker = new NuGetVersionChecker(new TestHttpClientFactory(handler));

        Assert.IsNull(checker.GetLatestVersion(), "First call returns null while the check runs.");

        await handler.RequestStarted.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.AreEqual(VersionCheckStatus.Pending, checker.LastCheckStatus);
        Assert.IsNull(checker.LastCheckedAt, "An in-flight check should not stamp completion time.");

        handler.Release();
        await WaitForCompletionAsync(checker);
    }

    [TestMethod]
    public async Task GetLatestVersion_RefreshInFlight_ClearsPreviousCompletionTime()
    {
        using var handler = new RefreshBlockingHandler();
        var checker = new NuGetVersionChecker(new TestHttpClientFactory(handler));

        Assert.IsNull(checker.GetLatestVersion(), "First call returns null while the check runs.");
        await WaitForCompletionAsync(checker);

        Assert.AreEqual(VersionCheckStatus.Succeeded, checker.LastCheckStatus);
        Assert.IsNotNull(checker.LastCheckedAt);
        Assert.AreEqual("1.0.0", checker.GetLatestVersion());

        ForceCacheExpired(checker);
        Assert.AreEqual("1.0.0", checker.GetLatestVersion(),
            "Refresh should return the stale cached version while the new check runs.");

        await handler.RefreshStarted.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.AreEqual(VersionCheckStatus.Pending, checker.LastCheckStatus);
        Assert.IsNull(checker.LastCheckedAt,
            "A pending refresh should not expose the previous completed check timestamp.");

        handler.ReleaseRefresh();
        await WaitForCompletionAsync(checker);
        Assert.AreEqual("1.1.0", checker.GetLatestVersion());
    }

    [TestMethod]
    public async Task GetLatestVersion_OnFetchFailure_StaysNonThrowingAndRecordsFailedStatus()
    {
        using var handler = new ThrowingHandler();
        var checker = new NuGetVersionChecker(new TestHttpClientFactory(handler));

        // (a) Non-blocking contract: returns null immediately, never throws.
        string? result = checker.GetLatestVersion();
        Assert.IsNull(result, "First call should return null while the check is in flight.");

        await WaitForCompletionAsync(checker);

        // (b) Failure is recorded, not silently swallowed.
        Assert.AreEqual(VersionCheckStatus.Failed, checker.LastCheckStatus);
        Assert.IsNotNull(checker.LastCheckedAt, "A completed failed check should stamp LastCheckedAt.");
        Assert.IsNull(checker.GetLatestVersion(), "Failed check must not produce a version.");
    }

    [TestMethod]
    public async Task GetLatestVersion_OnTimeout_StaysNonThrowingAndRecordsTimedOutStatus()
    {
        using var handler = new TimeoutHandler();
        var checker = new NuGetVersionChecker(new TestHttpClientFactory(handler));

        Assert.IsNull(checker.GetLatestVersion(), "First call should return null while the check is in flight.");

        await WaitForCompletionAsync(checker);

        Assert.AreEqual(VersionCheckStatus.TimedOut, checker.LastCheckStatus);
        Assert.IsNotNull(checker.LastCheckedAt, "A timed-out check should stamp LastCheckedAt.");
        Assert.IsNull(checker.GetLatestVersion(), "Timed-out check must not produce a version.");
    }

    [TestMethod]
    public async Task GetLatestVersion_OnSuccess_RecordsSucceededStatusAndLatestStableVersion()
    {
        const string body = """{"versions":["1.0.0","1.2.0","1.2.0-beta","1.1.0"]}""";
        using var handler = new StubHandler(body);
        var checker = new NuGetVersionChecker(new TestHttpClientFactory(handler));

        Assert.IsNull(checker.GetLatestVersion(), "First call returns null while the check runs.");

        await WaitForCompletionAsync(checker);

        Assert.AreEqual(VersionCheckStatus.Succeeded, checker.LastCheckStatus);
        Assert.IsNotNull(checker.LastCheckedAt);
        Assert.AreEqual("1.2.0", checker.GetLatestVersion(), "Should pick the latest stable (non-prerelease) version.");
    }

    [TestMethod]
    public async Task LegacyHttpClientConstructor_PreservesCallerOwnership()
    {
        using var handler = new StubHandler("""{"versions":["1.0.0"]}""");
        using var http = new HttpClient(handler, disposeHandler: false);
        var checker = new NuGetVersionChecker(http);

        Assert.IsNull(checker.GetLatestVersion());
        await WaitForCompletionAsync(checker);

        using var response = await http.GetAsync("https://example.invalid/");
        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            "The compatibility constructor must not dispose its caller-owned HttpClient.");
    }

    private static void ForceCacheExpired(NuGetVersionChecker checker)
    {
        var field = typeof(NuGetVersionChecker).GetField(
            "_checkedAtUtc",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Test expects NuGetVersionChecker to keep the cache timestamp in _checkedAtUtc.");
        field.SetValue(checker, DateTime.UtcNow - TimeSpan.FromHours(2));
    }
}
