using System.Collections;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceNativeCleanupFailureTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeWindowsAnalyzerCleanupFailure_ReleasesLoadGateAndPreservesPublishedOwner(bool failBeforePublication)
    {
        await RunNativeFixtureAsync(failBeforePublication, 2, true, async fixture =>
        {
            var original = fixture.Manager.GetCurrentSolution(fixture.Status.WorkspaceId);
            Assert.IsNotNull(fixture.Loader.InitialLease);
            Assert.AreEqual(1, fixture.Loader.InitialLease.RetargetedReferenceCount,
                "The fault must belong to the real native load's privately owned analyzer lease.");
            if (!failBeforePublication) fixture.Loader.Fault = InjectFailure(fixture.Loader.InitialLease, fixture.HandlePath);
            Exception? failure = null;
            try { await fixture.Manager.ReloadAsync(fixture.Status.WorkspaceId, CancellationToken.None); }
            catch (Exception observed) { failure = observed; }
            Assert.IsNotNull(failure, "The injected native handle failure must remain observable.");
            Assert.IsNotNull(fixture.Loader.Fault);
            Assert.AreEqual(1, fixture.Loader.Fault.DisposalCount, "The owned native lease must attempt its actual failing handle exactly once.");
            var loadGate = GetLoadGate(fixture.Manager, fixture.Status.WorkspaceId);
            Assert.AreEqual(1, loadGate.CurrentCount, "A cleanup failure must not permanently retain the session's load semaphore.");
            if (failBeforePublication)
            {
                Assert.AreEqual(typeof(UnauthorizedAccessException), failure.GetType());
                Assert.AreSame(original, fixture.Manager.GetCurrentSolution(fixture.Status.WorkspaceId), "Failed preparation must retain the prior published graph.");
                Assert.IsTrue(failure.Data[OwnedResourceCleanup.OutcomeDataKey] is string[] { Length: > 0 },
                    "Cleanup failure must remain visible without replacing the primary authorization failure.");
            }
            else
            {
                Assert.AreEqual(typeof(AggregateException), failure.GetType());
                Assert.AreNotSame(original, fixture.Manager.GetCurrentSolution(fixture.Status.WorkspaceId), "Cleanup after publication must retain the newly published graph.");
                Assert.IsNotNull(fixture.Loader.Fault);
                Assert.IsTrue(((AggregateException)failure).Flatten().InnerExceptions.Any(item => ReferenceEquals(item, fixture.Loader.Fault.Failure)),
                    "The actual analyzer-handle failure must be reported.");
            }
            fixture.Policy.Deny = false;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var recovered = await fixture.Manager.ReloadAsync(fixture.Status.WorkspaceId, deadline.Token);
            Assert.IsTrue(recovered.IsLoaded, "A later real reload must remain usable after the cleanup failure.");
            Assert.HasCount(1, fixture.Manager.GetCurrentSolution(fixture.Status.WorkspaceId).ProjectIds);
        });
    }

    [TestMethod]
    public async Task NativeCloseCleanupFailure_ReturnsCapacityAndReleasesSessionResources()
    {
        await RunNativeFixtureAsync(false, 1, true, async fixture =>
        {
            var gate = GetLoadGate(fixture.Manager, fixture.Status.WorkspaceId);
            fixture.Loader.Fault = InjectFailure(fixture.Loader.InitialLease!, fixture.HandlePath);
            Exception? failure = null;
            try { fixture.Manager.Close(fixture.Status.WorkspaceId); }
            catch (Exception observed) { failure = observed; }
            Assert.IsNotNull(failure);
            Assert.AreEqual(1, fixture.Loader.Fault.DisposalCount);
            Assert.IsFalse(fixture.Manager.HasSession(fixture.Status.WorkspaceId));
            var recovered = await fixture.Manager.LoadAsync(fixture.Project, fixture.Properties, CancellationToken.None);
            Assert.IsTrue(recovered.IsLoaded, "Close must return the removed session's capacity even when its actual analyzer lease fails.");
            Assert.ThrowsExactly<ObjectDisposedException>(() => gate.Wait(0), "The removed session must release its load gate too.");
            Assert.IsTrue(failure is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Any(item => ReferenceEquals(item, fixture.Loader.Fault.Failure)));
        });
    }

    [TestMethod]
    public async Task NativeShutdownCleanupFailure_ReleasesEverySessionAndLifecycleResource()
    {
        await RunNativeFixtureAsync(false, 2, true, async fixture =>
        {
            var second = await fixture.Manager.LoadAsync(fixture.Project, fixture.Properties, CancellationToken.None);
            Assert.AreNotEqual(fixture.Status.WorkspaceId, second.WorkspaceId);
            var firstGate = GetLoadGate(fixture.Manager, fixture.Status.WorkspaceId);
            var secondGate = GetLoadGate(fixture.Manager, second.WorkspaceId);
            var closed = new List<string>();
            fixture.Manager.WorkspaceClosed += closed.Add;
            fixture.Loader.Fault = InjectFailure(fixture.Loader.InitialLease!, fixture.HandlePath);
            Exception? failure = null;
            try { fixture.Manager.Dispose(); }
            catch (Exception observed) { failure = observed; }
            Assert.IsNotNull(failure);
            Assert.AreEqual(1, fixture.Loader.Fault.DisposalCount);
            Assert.HasCount(0, fixture.Manager.ListWorkspaces(), "One session's cleanup failure must not abandon shutdown ownership.");
            Assert.ThrowsExactly<ObjectDisposedException>(() => firstGate.Wait(0));
            Assert.ThrowsExactly<ObjectDisposedException>(() => secondGate.Wait(0));
            CollectionAssert.AreEquivalent(new[] { fixture.Status.WorkspaceId, second.WorkspaceId }, closed);
            var slots = GetPrivateField<SemaphoreSlim>(fixture.Manager, "_workspaceSlots");
            Assert.ThrowsExactly<ObjectDisposedException>(() => slots.Wait(0));
            var lifetime = GetPrivateField<CancellationTokenSource>(fixture.Manager, "_lifetimeCancellation");
            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = lifetime.Token);
            Assert.IsTrue(failure is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Any(item => ReferenceEquals(item, fixture.Loader.Fault.Failure)));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeHostCloseCleanupFailure_RemovesExecutionGateAfterWriterRelease(bool drainProcesses)
    {
        await RunNativeFixtureAsync(false, 1, true, async fixture =>
        {
            fixture.Loader.Fault = InjectFailure(fixture.Loader.InitialLease!, fixture.HandlePath);
            var runner = new DrainRecorder();
            Exception? failure = null;
            try
            {
                await WorkspaceTools.CloseWorkspaceCore(fixture.Gate, fixture.Manager, runner,
                    fixture.Status.WorkspaceId, drainProcesses, loggerFactory: null, exceptionReporter: null,
                    getProcessesByName: _ => [], processDrainTimeout: TimeSpan.FromSeconds(30), ct: CancellationToken.None);
            }
            catch (Exception observed) { failure = observed; }
            Assert.IsNotNull(failure);
            Assert.AreEqual(1, fixture.Loader.Fault.DisposalCount);
            Assert.IsFalse(fixture.Manager.HasSession(fixture.Status.WorkspaceId));
            CollectionAssert.AreEqual(new[] { fixture.Status.WorkspaceId }, fixture.Gate.Removed.ToArray(),
                "An actual removed session must release its host execution-gate entry even when native cleanup fails.");
            Assert.IsFalse(fixture.Gate.RemovedInsideWriter, "The writer must be released before its registry entry is removed.");
            Assert.AreEqual(drainProcesses ? 1 : 0, runner.Calls,
                "A committed close must attempt its requested process drain despite native resource cleanup failure.");
            Assert.IsTrue(failure is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Any(item => ReferenceEquals(item, fixture.Loader.Fault.Failure)));
            var recovered = await fixture.Manager.LoadAsync(fixture.Project, fixture.Properties, CancellationToken.None);
            Assert.IsTrue(recovered.IsLoaded);
        });
    }

    [TestMethod]
    public async Task NativeMissingRootCleanupFailure_RemovesExecutionGateAfterRetirementCompletes()
    {
        await RunNativeFixtureAsync(false, 1, true, async fixture =>
        {
            var projectBytes = await File.ReadAllBytesAsync(fixture.Project);
            fixture.Loader.Fault = InjectFailure(fixture.Loader.InitialLease!, fixture.HandlePath);
            fixture.Gate.PauseNextWriter = true;
            Task? retirement = null;
            Exception? primary = null;
            try
            {
                File.Delete(fixture.Project);
                await fixture.Gate.WriterEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var sync = GetPrivateField<object>(fixture.Manager, "_rootRetirementSync");
                lock (sync)
                {
                    var tasks = GetPrivateField<Dictionary<string, Task>>(fixture.Manager, "_rootRetirementTasks");
                    Assert.IsTrue(tasks.TryGetValue(fixture.Status.WorkspaceId, out retirement!));
                }
                fixture.Gate.ReleaseWriter.TrySetResult();
                await (retirement ?? throw new AssertFailedException("The actual retirement task must be captured.")).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.AreEqual(1, fixture.Loader.Fault.DisposalCount);
                Assert.IsFalse(fixture.Manager.HasSession(fixture.Status.WorkspaceId));
                CollectionAssert.AreEqual(new[] { fixture.Status.WorkspaceId }, fixture.Gate.Removed.ToArray(),
                    "Completed retirement must retain the successful removal outcome across a native cleanup failure.");
                Assert.IsFalse(fixture.Gate.RemovedInsideWriter);
                Assert.IsTrue(fixture.Log.Exceptions.Any(exception => exception is AggregateException aggregate
                    && aggregate.Flatten().InnerExceptions.Any(item => ReferenceEquals(item, fixture.Loader.Fault.Failure))),
                    "The actual cleanup failure must remain operationally visible after retirement.");
                await File.WriteAllBytesAsync(fixture.Project, projectBytes);
                var recovered = await fixture.Manager.LoadAsync(fixture.Project, fixture.Properties, CancellationToken.None);
                Assert.IsTrue(recovered.IsLoaded);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                fixture.Gate.ReleaseWriter.TrySetResult();
                await CleanupFailureCollector.RunAfterFailureAsync("Native retirement fixture cleanup failed.", primary,
                    async () =>
                    {
                        var started = retirement ?? FindRetirementTask(fixture.Manager, fixture.Status.WorkspaceId);
                        if (started is not null) await started;
                    },
                    async () => { if (!File.Exists(fixture.Project)) await File.WriteAllBytesAsync(fixture.Project, projectBytes); });
            }
        });
    }

    [TestMethod]
    public async Task NativeHostGateCleanupFailure_StillAttemptsRequestedProcessDrain()
    {
        await RunNativeFixtureAsync(false, 1, true, async fixture =>
        {
            var gateFailure = new IOException("Controlled execution-gate cleanup failure.");
            fixture.Gate.RemovalFailure = gateFailure;
            var runner = new DrainRecorder();
            Exception? failure = null;
            try
            {
                await WorkspaceTools.CloseWorkspaceCore(fixture.Gate, fixture.Manager, runner,
                    fixture.Status.WorkspaceId, drainProcesses: true, loggerFactory: null, exceptionReporter: null,
                    getProcessesByName: _ => [], processDrainTimeout: TimeSpan.FromSeconds(30), ct: CancellationToken.None);
            }
            catch (Exception observed) { failure = observed; }
            Assert.AreSame(gateFailure, failure, "The actual post-close gate failure must remain observable.");
            Assert.IsFalse(fixture.Manager.HasSession(fixture.Status.WorkspaceId));
            CollectionAssert.AreEqual(new[] { fixture.Status.WorkspaceId }, fixture.Gate.Removed.ToArray());
            Assert.IsFalse(fixture.Gate.RemovedInsideWriter);
            Assert.AreEqual(1, runner.Calls, "A gate cleanup failure after committed native close must not skip its requested process drain.");
            var recovered = await fixture.Manager.LoadAsync(fixture.Project, fixture.Properties, CancellationToken.None);
            Assert.IsTrue(recovered.IsLoaded);
        });
    }

    [TestMethod]
    public async Task NativeMissingRootCleanupFailure_WithoutEvictionGateReportsCompletedRemoval()
    {
        await RunNativeFixtureAsync(false, 1, false, async fixture =>
        {
            var projectBytes = await File.ReadAllBytesAsync(fixture.Project);
            fixture.Loader.Fault = InjectFailure(fixture.Loader.InitialLease!, fixture.HandlePath);
            var sync = GetPrivateField<object>(fixture.Manager, "_rootRetirementSync");
            Task? retirement = null;
            Exception? primary = null;
            try
            {
                lock (sync)
                {
                    File.Delete(fixture.Project);
                    var notification = typeof(WorkspaceManager).GetMethod("OnWorkspaceRootMissing", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(notification);
                    notification.Invoke(fixture.Manager, [fixture.Status.WorkspaceId]);
                    var tasks = GetPrivateField<Dictionary<string, Task>>(fixture.Manager, "_rootRetirementTasks");
                    Assert.IsTrue(tasks.TryGetValue(fixture.Status.WorkspaceId, out retirement!));
                }
                Exception? failure = null;
                try { await (retirement ?? throw new AssertFailedException("The actual retirement task must be captured.")).WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception observed) { failure = observed; }
                Assert.AreEqual(1, fixture.Loader.Fault.DisposalCount);
                Assert.IsFalse(fixture.Manager.HasSession(fixture.Status.WorkspaceId));
                Assert.IsNull(failure, "A completed removal must report native cleanup failure without faulting its owned retirement task.");
                Assert.AreEqual(1, fixture.Log.Exceptions.Count(exception => exception is AggregateException aggregate
                    && aggregate.Flatten().InnerExceptions.Any(item => ReferenceEquals(item, fixture.Loader.Fault.Failure))),
                    "The supported no-gate retirement path must report its actual cleanup failure once.");
                await File.WriteAllBytesAsync(fixture.Project, projectBytes);
                var recovered = await fixture.Manager.LoadAsync(fixture.Project, fixture.Properties, CancellationToken.None);
                Assert.IsTrue(recovered.IsLoaded);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Ungated retirement fixture cleanup failed.", primary,
                    async () =>
                    {
                        var started = retirement ?? FindRetirementTask(fixture.Manager, fixture.Status.WorkspaceId);
                        if (started is not null) await started;
                    },
                    async () => { if (!File.Exists(fixture.Project)) await File.WriteAllBytesAsync(fixture.Project, projectBytes); });
            }
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeHostCombinedCleanupFailure_PreservesPrimaryAndReportsDrainOutcome(bool gateFailure)
    {
        await RunNativeFixtureAsync(false, 1, true, async fixture =>
        {
            var gateError = new IOException("Controlled execution-gate cleanup failure.");
            if (gateFailure) fixture.Gate.RemovalFailure = gateError;
            else fixture.Loader.Fault = InjectFailure(fixture.Loader.InitialLease!, fixture.HandlePath);
            var drainError = new IOException("Controlled private drain marker must not appear in cleanup outcomes.");
            var runner = new DrainRecorder { Failure = drainError };
            Exception? failure = null;
            try
            {
                await WorkspaceTools.CloseWorkspaceCore(fixture.Gate, fixture.Manager, runner,
                    fixture.Status.WorkspaceId, drainProcesses: true, loggerFactory: null, exceptionReporter: null,
                    getProcessesByName: _ => [], processDrainTimeout: TimeSpan.FromSeconds(30), ct: CancellationToken.None);
            }
            catch (Exception observed) { failure = observed; }
            Assert.IsNotNull(failure);
            Assert.IsFalse(fixture.Manager.HasSession(fixture.Status.WorkspaceId));
            Assert.AreEqual(1, runner.Calls, "The real committed close must reach the requested drain.");
            Assert.IsFalse(fixture.Gate.RemovedInsideWriter);
            if (gateFailure) Assert.AreSame(gateError, failure, "The original gate failure must retain its identity.");
            else
            {
                Assert.IsInstanceOfType<WorkspaceCloseCleanupException>(failure, "The original completed-removal receipt must remain primary.");
                Assert.IsNotNull(fixture.Loader.Fault);
                Assert.AreEqual(1, fixture.Loader.Fault.DisposalCount);
                Assert.IsTrue(((AggregateException)failure).Flatten().InnerExceptions.Any(item => ReferenceEquals(item, fixture.Loader.Fault.Failure)));
            }
            var outcomes = failure.Data[OwnedResourceCleanup.OutcomeDataKey] as string[];
            Assert.IsNotNull(outcomes, "Secondary process-drain failure must remain visible even when no reporter is injected.");
            CollectionAssert.Contains(outcomes, "Process drain failed (IOException).");
            Assert.IsTrue(outcomes.Length <= OwnedResourceCleanup.MaximumOutcomeCount);
            Assert.IsTrue(outcomes.All(outcome => outcome.Length <= OwnedResourceCleanup.MaximumOutcomeLength));
            Assert.IsFalse(string.Join(";", outcomes).Contains("Controlled private drain marker", StringComparison.Ordinal),
                "Only fixed role and exception type labels may cross the cleanup outcome boundary.");
            var recovered = await fixture.Manager.LoadAsync(fixture.Project, fixture.Properties, CancellationToken.None);
            Assert.IsTrue(recovered.IsLoaded);
        });
    }

    private static Task? FindRetirementTask(WorkspaceManager manager, string workspaceId)
    {
        var sync = GetPrivateField<object>(manager, "_rootRetirementSync");
        lock (sync)
        {
            var tasks = GetPrivateField<Dictionary<string, Task>>(manager, "_rootRetirementTasks");
            return tasks.GetValueOrDefault(workspaceId);
        }
    }

    private static T GetPrivateField<T>(object owner, string name) where T : class
    {
        var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        return field.GetValue(owner) as T ?? throw new AssertFailedException("The actual lifecycle-owned resource must be present.");
    }

    private sealed record NativeFixture(string Project, Dictionary<string, string> Properties, string HandlePath,
        WorkspaceManager Manager, ObservingLoader Loader, WorkspaceStatusDto Status, DenialPolicy Policy,
        LifecycleGate Gate, CleanupLogger Log);

    private static async Task RunNativeFixtureAsync(bool failBeforePublication, int maximum, bool wireEvictionGate, Func<NativeFixture, Task> exercise)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The native loader installs owned analyzer shadow leases only on Windows.");
            return;
        }
        var root = Path.Combine(TestTempRoot.Current, nameof(WorkspaceNativeCleanupFailureTests), Guid.NewGuid().ToString("N"));
        WorkspaceManager? manager = null;
        ObservingLoader? loader = null;
        FileWatcherService? watcher = null;
        LifecycleGate? gate = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Owner.csproj");
            var analyzer = Path.Combine(root, "OwnedAnalyzer.dll");
            FileStream? output = null;
            Exception? outputFailure = null;
            try
            {
                output = File.Create(analyzer);
                var compilation = CSharpCompilation.Create("OwnedCleanupAnalyzer",
                    [CSharpSyntaxTree.ParseText("public sealed class ControlledImage { }")],
                    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.IsTrue(compilation.Emit(output).Success, "The owned analyzer input must be a real valid PE image.");
            }
            catch (Exception failure) { outputFailure = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native analyzer PE stream cleanup failed.", outputFailure, output); }
            await File.WriteAllTextAsync(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit>
                    <EnableNETAnalyzers>false</EnableNETAnalyzers><UseSharedCompilation>false</UseSharedCompilation>
                  </PropertyGroup>
                  <ItemGroup><Analyzer Include="OwnedAnalyzer.dll" /></ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "Owner.cs"), "public class Owner { }");
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            var feed = Path.Combine(root, "feed");
            Directory.CreateDirectory(feed);
            var handlePath = Path.Combine(root, "controlled-handle.bin");
            await File.WriteAllTextAsync(handlePath, "owned cleanup handle");
            var properties = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var restore = await new DotnetCommandRunner().RunAsync(root, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo", "--source", feed], properties), CancellationToken.None);
            Assert.IsTrue(restore.Succeeded, "Actual SDK restore must succeed before exercising native reload cleanup.");
            var normal = new DenialPolicy();
            loader = new ObservingLoader(normal, handlePath, failBeforePublication);
            var log = new CleanupLogger();
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(new ForkInvocationPathPolicy(normal), log,
                new PreviewStore(), watcher,
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = maximum }, cacheStore: null, sessionLoader: loader,
                evictionGate: wireEvictionGate ? new Lazy<IWorkspaceExecutionGate>(() => gate ?? throw new AssertFailedException("The real lifecycle gate must be initialized before loading.")) : null);
            gate = new LifecycleGate(new WorkspaceExecutionGate(new ExecutionGateOptions(), manager));
            var status = await manager.LoadAsync(project, properties, CancellationToken.None);
            await exercise(new(project, properties, handlePath, manager, loader, status, normal, gate, log));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native cleanup regression and fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (manager is null) watcher?.Dispose(); }),
                CleanupFailureCollector.FromAction(() => gate?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class CleanupLogger : ILogger<WorkspaceManager>
    {
        internal System.Collections.Concurrent.ConcurrentQueue<Exception> Exceptions { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null) Exceptions.Enqueue(exception);
        }
    }

    private sealed class DrainRecorder : IDotnetCommandRunner
    {
        internal int Calls { get; private set; }
        internal Exception? Failure { get; set; }
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            CollectionAssert.AreEqual(new[] { "build-server", "shutdown" }, arguments.ToArray());
            Assert.AreEqual(string.Empty, targetPath);
            Assert.IsTrue(Directory.Exists(workingDirectory));
            Calls++;
            if (Failure is { } failure) throw failure;
            return Task.FromResult(new CommandExecutionDto("dotnet", arguments, workingDirectory, targetPath, 0, true, 0, string.Empty, string.Empty));
        }
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
            => throw new AssertFailedException("The host process-drain path must use its ordinary command surface.");
    }

    private sealed class LifecycleGate(WorkspaceExecutionGate inner) : IWorkspaceExecutionGate, IDisposable
    {
        private int _insideWriter;
        internal bool PauseNextWriter { get; set; }
        internal TaskCompletionSource WriterEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseWriter { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<string> Removed { get; } = [];
        internal bool RemovedInsideWriter { get; private set; }
        internal IOException? RemovalFailure { get; set; }
        public Task<T> RunReadAsync<T>(string id, Func<CancellationToken, Task<T>> action, CancellationToken ct) => inner.RunReadAsync(id, action, ct);
        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => inner.RunLoadGateAsync(action, ct);
        public Task<T> RunWriteAsync<T>(string id, Func<CancellationToken, Task<T>> action, CancellationToken ct, bool applyStalenessPolicy = true)
            => inner.RunWriteAsync(id, async token =>
            {
                Interlocked.Increment(ref _insideWriter);
                try
                {
                    if (PauseNextWriter)
                    {
                        PauseNextWriter = false;
                        WriterEntered.TrySetResult();
                        await ReleaseWriter.Task.WaitAsync(token);
                    }
                    return await action(token);
                }
                finally { Interlocked.Decrement(ref _insideWriter); }
            }, ct, applyStalenessPolicy);
        public void RemoveGate(string id)
        {
            RemovedInsideWriter |= Volatile.Read(ref _insideWriter) != 0;
            Removed.Add(id);
            inner.RemoveGate(id);
            if (RemovalFailure is { } failure) throw failure;
        }
        public void Dispose() => inner.Dispose();
    }

    private static SemaphoreSlim GetLoadGate(WorkspaceManager manager, string workspaceId)
    {
        var field = typeof(WorkspaceManager).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        var sessions = field.GetValue(manager) as IDictionary;
        Assert.IsNotNull(sessions);
        var session = sessions[workspaceId];
        Assert.IsNotNull(session);
        var property = session.GetType().GetProperty("LoadLock");
        Assert.IsNotNull(property);
        return property.GetValue(session) as SemaphoreSlim ?? throw new AssertFailedException("The actual owned session must retain its load semaphore.");
    }

    private static FailingFileStream InjectFailure(AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease lease, string path)
    {
        var field = lease.GetType().GetField("_loaders", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        var loaders = field.GetValue(lease) as IReadOnlyList<AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader>;
        Assert.IsNotNull(loaders);
        Assert.HasCount(1, loaders);
        var handlesField = loaders[0].GetType().GetField("_imageHandles", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(handlesField);
        var handles = handlesField.GetValue(loaders[0]) as List<FileStream>;
        Assert.IsNotNull(handles);
        Assert.IsTrue(handles.Count > 0, "The native analyzer loader must already own its authorized image handles.");
        var fault = new FailingFileStream(path);
        handles.Add(fault);
        return fault;
    }

    private sealed class FailingFileStream(string path) : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)
    {
        internal IOException Failure { get; } = new("Controlled analyzer handle release failure.");
        internal int DisposalCount { get; private set; }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && ++DisposalCount == 1) throw Failure;
        }
    }

    private sealed class DenialPolicy : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        internal bool Deny { get; set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
            => Deny ? throw new UnauthorizedAccessException("Controlled post-loader input refusal.") : _owned.ValidateAsync(workspaceId, paths, ct);
    }

    private sealed class ObservingLoader(DenialPolicy policy, string handlePath, bool failBeforePublication) : WorkspaceSessionLoader
    {
        private int _calls;
        internal AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? InitialLease { get; private set; }
        internal FailingFileStream? Fault { get; set; }
        public override async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease,
            NativeCompilerContextCertificate Context, NativeSourceAdmission.SourceAdmissionReceipt? SourceAdmission)> CreateAndOpenAsync(string workspaceId, string path,
            IDictionary<string, string>? globalProperties, IWorkspacePathPolicy pathPolicy, WorkspaceDiagnosticsSink diagnostics,
            ILogger logger, SupplementalPackageReadAuthority supplementalPackages, NativeSourceAdmission? sourceAdmission, CancellationToken ct)
        {
            var loaded = await base.CreateAndOpenAsync(workspaceId, path, globalProperties, pathPolicy, diagnostics, logger, supplementalPackages, sourceAdmission, ct);
            try
            {
                _calls++;
                if (_calls == 1) InitialLease = loaded.Lease;
                if (_calls == 2 && failBeforePublication)
                {
                    Fault = InjectFailure(loaded.Lease, handlePath);
                    policy.Deny = true;
                }
                return loaded;
            }
            catch (Exception failure)
            {
                OwnedResourceCleanup.DisposePreservingFailure("Cleanup fixture loader and resource release failed.", failure, loaded.Workspace, loaded.Lease);
                throw;
            }
        }
    }
}
