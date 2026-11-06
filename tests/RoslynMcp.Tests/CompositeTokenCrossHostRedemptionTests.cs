using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// composite-token-portable-workspace-identity (ADR 0032): a composite token persisted under
/// <c>ROSLYNMCP_PREVIEW_PERSIST_DIR</c> is redeemable by an independent host that has the same
/// solution loaded. Every host here is a separate DI graph with its own workspace sessions (distinct
/// random session ids) over one shared persist directory, redeemed through the production
/// <see cref="OrchestrationTools.ApplyComposite"/> route. Every refusal must leave the one-time
/// payload on disk and write nothing.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class CompositeTokenCrossHostRedemptionTests
{
    private readonly List<Host> _hosts = [];
    private string _persistDirectory = null!;
    private string _projectRoot = null!;
    private string _projectPath = null!;
    private string _targetFile = null!;

    [TestInitialize]
    public void Setup()
    {
        var repo = TestFixtureFileSystem.FindRepositoryRoot();
        (_projectRoot, _projectPath) = TestFixtureFileSystem.CreateSampleLibraryCopy(
            repo, Path.Combine(repo, "samples", "SampleSolution", "SampleSolution.slnx"));
        _persistDirectory = Path.Combine(TestTempRoot.Current, "cross-host-persist-" + Guid.NewGuid().ToString("N"));
        _targetFile = Path.Combine(_projectRoot, "SampleLib", "CrossHostTarget.cs");
    }

    [TestCleanup]
    public void Teardown()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }

        TestFixtureFileSystem.DeleteDirectoryIfExists(_projectRoot);
        TestFixtureFileSystem.DeleteDirectoryIfExists(_persistDirectory);
    }

    [TestMethod]
    public async Task HostB_WithSameSolutionLoaded_RedeemsHostAToken_AndSecondRedemptionRefuses()
    {
        var hostA = NewHost();
        var hostB = NewHost();
        var sessionA = await hostA.LoadAsync(_projectPath);
        var sessionB = await hostB.LoadAsync(_projectPath);
        Assert.AreNotEqual(sessionA, sessionB, "Premise: two hosts, two random session ids.");
        // A version no session of host B could ever report: it must not take part in matching.
        var token = hostA.Preview(sessionA, 4141, "// from host A");

        var result = await hostB.ApplyAsync(token);

        StringAssert.Contains(result, "\"success\": true");
        Assert.AreEqual("// from host A", File.ReadAllText(_targetFile));
        Assert.IsEmpty(PayloadFiles(token), "A successful redemption consumes the one-time payload.");
        await Assert.ThrowsExactlyAsync<PreviewTokenStaleException>(() => hostB.ApplyAsync(token));
        await Assert.ThrowsExactlyAsync<PreviewTokenStaleException>(() => hostA.ApplyAsync(token));
    }

    [TestMethod]
    public async Task HostB_VersionMismatchInThePayload_IsNotUsedAsEquivalence()
    {
        var hostA = NewHost();
        var hostB = NewHost();
        var sessionA = await hostA.LoadAsync(_projectPath);
        await hostB.LoadAsync(_projectPath);
        var token = hostA.Preview(sessionA, 1, "// version independent");
        RewritePayload(token, payload => payload["WorkspaceVersion"] = 999_999);

        var result = await hostB.ApplyAsync(token);

        StringAssert.Contains(result, "\"success\": true");
        Assert.AreEqual("// version independent", File.ReadAllText(_targetFile));
    }

    [TestMethod]
    public async Task ReceivingHostNotLoaded_Refuses_ThenRedeemsAfterTheSolutionIsLoadedInARestartedHost()
    {
        var hostA = NewHost();
        var token = hostA.Preview(await hostA.LoadAsync(_projectPath), 1, "// not loaded first");
        var unloaded = NewHost();

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => unloaded.ApplyAsync(token));

        StringAssert.Contains(refusal.Message, "not loaded");
        AssertRefusedWithoutConsuming(token);

        var restarted = NewHost();
        await restarted.LoadAsync(_projectPath);
        StringAssert.Contains(await restarted.ApplyAsync(token), "\"success\": true");
        Assert.AreEqual("// not loaded first", File.ReadAllText(_targetFile));
    }

    [TestMethod]
    public async Task ReceivingHostWithTheWrongSolution_Refuses_WithoutConsumingTheToken()
    {
        var hostA = NewHost();
        var token = hostA.Preview(await hostA.LoadAsync(_projectPath), 1, "// wrong solution");
        var repo = TestFixtureFileSystem.FindRepositoryRoot();
        var (otherRoot, otherProject) = TestFixtureFileSystem.CreateSampleLibraryCopy(
            repo, Path.Combine(repo, "samples", "SampleSolution", "SampleSolution.slnx"));
        try
        {
            var hostB = NewHost();
            await hostB.LoadAsync(otherProject);

            var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => hostB.ApplyAsync(token));

            StringAssert.Contains(refusal.Message, "not loaded");
            AssertRefusedWithoutConsuming(token);
            Assert.IsFalse(File.Exists(Path.Combine(otherRoot, "SampleLib", "CrossHostTarget.cs")));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(otherRoot);
        }
    }

    [TestMethod]
    public async Task ReceivingHostWithAmbiguousSessions_Refuses_ThenRedeemsOnceOneSessionRemains()
    {
        var hostA = NewHost();
        var token = hostA.Preview(await hostA.LoadAsync(_projectPath), 1, "// ambiguous");
        var hostB = NewHost();
        var first = await hostB.LoadAsync(_projectPath);
        var second = await hostB.LoadAsync(
            _projectPath, new Dictionary<string, string> { ["Configuration"] = "Release" });
        Assert.AreNotEqual(first, second, "Premise: global properties force a second session on the same path.");

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => hostB.ApplyAsync(token));

        StringAssert.Contains(refusal.Message, "2 workspace sessions");
        AssertRefusedWithoutConsuming(token);

        Assert.IsTrue(hostB.WorkspaceManager.Close(second));
        StringAssert.Contains(await hostB.ApplyAsync(token), "\"success\": true");
        Assert.AreEqual("// ambiguous", File.ReadAllText(_targetFile));
    }

    [TestMethod]
    public async Task CreatorHost_WithASecondSessionOnTheSamePath_StillRedeemsItsOwnToken()
    {
        var hostA = NewHost();
        var sessionA = await hostA.LoadAsync(_projectPath);
        await hostA.LoadAsync(_projectPath, new Dictionary<string, string> { ["Configuration"] = "Release" });
        var token = hostA.Preview(sessionA, 1, "// creator");

        StringAssert.Contains(await hostA.ApplyAsync(token), "\"success\": true");
        Assert.AreEqual("// creator", File.ReadAllText(_targetFile));
    }

    [TestMethod]
    public async Task LegacyFormat1Record_WithANonLiveCreator_Refuses_AndWithALiveCreatorRedeems()
    {
        var hostA = NewHost();
        var sessionA = await hostA.LoadAsync(_projectPath);
        var nonLive = hostA.Preview(sessionA, 1, "// legacy foreign");
        MakeFormat1(nonLive);
        var hostB = NewHost();
        await hostB.LoadAsync(_projectPath);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => hostB.ApplyAsync(nonLive));

        StringAssert.Contains(refusal.Message, "older server");
        AssertRefusedWithoutConsuming(nonLive);

        var live = hostA.Preview(sessionA, 1, "// legacy creator");
        MakeFormat1(live);
        StringAssert.Contains(await hostA.ApplyAsync(live), "\"success\": true");
        Assert.AreEqual("// legacy creator", File.ReadAllText(_targetFile));
    }

    [TestMethod]
    public async Task RecordFromANewerFormat_Refuses_AndKeepsThePayload()
    {
        var hostA = NewHost();
        var token = hostA.Preview(await hostA.LoadAsync(_projectPath), 1, "// future");
        RewritePayload(token, payload => payload["FormatVersion"] = 3);
        var hostB = NewHost();
        await hostB.LoadAsync(_projectPath);

        await Assert.ThrowsExactlyAsync<PreviewTokenStaleException>(() => hostB.ApplyAsync(token));

        AssertRefusedWithoutConsuming(token);
    }

    /// <summary>
    /// The session set changes after the pre-claim peek resolved a session and the write gate was
    /// taken on it. The claim must re-resolve under its lock and refuse BEFORE moving the payload.
    /// </summary>
    [TestMethod]
    public async Task SessionChangeBetweenPeekAndClaim_Refuses_WithoutBurningTheToken()
    {
        var hostA = NewHost();
        var token = hostA.Preview(await hostA.LoadAsync(_projectPath), 1, "// race");
        var hostB = NewHost();
        var sessionB = await hostB.LoadAsync(_projectPath);
        var racing = new HookAfterPeekStore(hostB.Store, () =>
        {
            Assert.IsTrue(hostB.WorkspaceManager.Close(sessionB));
            hostB.LoadAsync(_projectPath).GetAwaiter().GetResult();
        });
        var orchestrator = new CompositeApplyOrchestrator(hostB.WorkspaceManager, racing);

        await Assert.ThrowsExactlyAsync<PreviewTokenStaleException>(() =>
            OrchestrationTools.ApplyComposite(new PassThroughGate(), orchestrator, racing, token, CancellationToken.None));

        Assert.IsTrue(racing.Raced, "Premise: the session changed between the peek and the claim.");
        AssertRefusedWithoutConsuming(token);
        StringAssert.Contains(await hostB.ApplyAsync(token), "\"success\": true");
    }

    private Host NewHost()
    {
        var host = Host.Create(_persistDirectory, _targetFile);
        _hosts.Add(host);
        return host;
    }

    private string[] PayloadFiles(string token) =>
        Directory.Exists(_persistDirectory)
            ? Directory.EnumerateFiles(_persistDirectory, token + ".json*", SearchOption.AllDirectories).ToArray()
            : [];

    private void AssertRefusedWithoutConsuming(string token)
    {
        Assert.IsFalse(File.Exists(_targetFile), "A refused redemption must write nothing.");
        Assert.AreEqual(1, PayloadFiles(token).Length, "Exactly the untouched payload remains: no claim, no lock.");
        Assert.IsTrue(PayloadFiles(token).Single().EndsWith(token + ".json", StringComparison.Ordinal));
    }

    private void RewritePayload(string token, Action<JsonNode> edit)
    {
        var path = PayloadFiles(token).Single();
        var payload = JsonNode.Parse(File.ReadAllText(path))!;
        edit(payload);
        File.WriteAllText(path, payload.ToJsonString());
    }

    private void MakeFormat1(string token) => RewritePayload(token, payload =>
    {
        var root = payload.AsObject();
        root.Remove("FormatVersion");
        root.Remove("WorkspaceIdentity");
    });

    /// <summary>One independent server: its own DI graph, workspace sessions and stores.</summary>
    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly string _targetFile;

        private Host(ServiceProvider provider, string targetFile)
        {
            _provider = provider;
            _targetFile = targetFile;
        }

        public IWorkspaceManager WorkspaceManager => _provider.GetRequiredService<IWorkspaceManager>();

        public ICompositePreviewStore Store => _provider.GetRequiredService<ICompositePreviewStore>();

        public static Host Create(string persistDirectory, string targetFile)
        {
            var services = new ServiceCollection();
            services.AddLogging(static logging => logging.ClearProviders());
            services.AddSingleton(new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 8 });
            services.AddSingleton(new ValidationServiceOptions());
            services.AddSingleton(new PreviewStoreOptions { PersistDirectory = persistDirectory });
            services.AddSingleton(new ExecutionGateOptions { RateLimitMaxRequests = int.MaxValue });
            services.AddSingleton(new ScriptingServiceOptions());
            services.AddSingleton<ForkStoragePathValidator>(ForkStorageFixtureBoundary.ValidateAsync);
            services.AddRoslynServices();
            services.RemoveAll<IFileWatcherService>();
            services.AddSingleton<IFileWatcherService>(sp =>
                new FileWatcherService(sp.GetRequiredService<ILogger<FileWatcherService>>()));
            return new Host(services.BuildServiceProvider(), targetFile);
        }

        public async Task<string> LoadAsync(string path, IDictionary<string, string>? globalProperties = null)
        {
            var status = await ((RoslynMcp.Roslyn.Services.WorkspaceManager)WorkspaceManager)
                .LoadAsync(path, EvictPolicy.Strict, globalProperties, CancellationToken.None);
            return status.WorkspaceId;
        }

        /// <summary>Stores what a composite preview of the target file stores, as the preview tools do.</summary>
        public string Preview(string workspaceId, int workspaceVersion, string content) =>
            Store.Store(workspaceId, workspaceVersion, "cross-host preview", [new CompositeFileMutation(_targetFile, content)]);

        public Task<string> ApplyAsync(string token) =>
            OrchestrationTools.ApplyComposite(
                _provider.GetRequiredService<IWorkspaceExecutionGate>(),
                _provider.GetRequiredService<ICompositeApplyOrchestrator>(),
                Store,
                token,
                CancellationToken.None);

        public void Dispose() => _provider.Dispose();
    }

    /// <summary>Runs <c>hook</c> once, right after the workspace peek resolved the gated session.</summary>
    private sealed class HookAfterPeekStore(ICompositePreviewStore inner, Action hook) : ICompositePreviewStore
    {
        public bool Raced { get; private set; }

        public string Store(string workspaceId, int workspaceVersion, string description, IReadOnlyList<CompositeFileMutation> mutations) =>
            inner.Store(workspaceId, workspaceVersion, description, mutations);

        public (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(string token, string? gatedWorkspaceId) =>
            inner.Retrieve(token, gatedWorkspaceId);

        public void Invalidate(string token) => inner.Invalidate(token);

        public IReadOnlyList<string>? PeekChangedPaths(string token) => inner.PeekChangedPaths(token);

        public string? PeekWorkspaceId(string token)
        {
            var peeked = inner.PeekWorkspaceId(token);
            if (!Raced)
            {
                Raced = true;
                hook();
            }

            return peeked;
        }
    }

    private sealed class PassThroughGate : IWorkspaceExecutionGate
    {
        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => action(ct);

        public Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true)
            => action(ct);

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => throw new NotSupportedException();

        public void RemoveGate(string workspaceId)
            => throw new NotSupportedException();
    }
}
