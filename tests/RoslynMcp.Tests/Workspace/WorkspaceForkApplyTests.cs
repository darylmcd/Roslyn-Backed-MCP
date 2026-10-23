using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

// donotparallelize-audit-wave-35: [DoNotParallelize] removed. Each fork-apply test loads its own
// GUID-unique IsolatedWorkspaceScope copy, stores a token-keyed preview for that workspace id only,
// and copies/replays workspace_fork_apply under the per-workspace write gate. The fork is created under the
// shared service parent with a GUID-unique owned child; the static ForkApplyLocks entry is keyed by the unique
// source root, and the retained fork workspace is closed and deleted in finally. The fork's real
// `dotnet restore` targets only the fork directory — the same shape parallel-enabled
// CrossProjectRefactoringIntegrationTests and ScaffoldingFirstTestFileTests already run
// concurrently. Validation receives explicit appliedFiles, so the shared ChangeTracker is not read.
// The remaining tests are pure (ShouldRetainFork) or run on per-test fake command runners. No
// shared-workspace reload or close, no static or environment mutation. Validated by a bounded
// repeated (3x) concurrent run alongside its wave-35 siblings and parallel-enabled
// workspace-loading classes, green every time.
[TestClass]
public sealed class WorkspaceForkApplyTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    [DataRow("recursive-item")]
    [DataRow("native-exists")]
    public async Task WorkspaceForkApply_PrivateNamespacePreservesActualNativeSourceObservers(string shape)
    {
        IsolatedWorkspaceScope? workspace = null;
        Exception? primary = null;
        try
        {
            workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
            var project = workspace.GetPath("SampleLib", "SampleLib.csproj");
            await NativeNamespaceObservationFixture.AddAsync(project, workspace.RootPath, shape, "forks");
            await workspace.ReloadAsync(CancellationToken.None);
            var source = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
            var globals = EvaluatedProjectGraph.Properties(source);
            var before = NativeNamespaceObservationFixture.AssertBaseline(project, workspace.RootPath, shape, "forks", globals);
            var token = await StoreDogPreviewAsync(workspace.WorkspaceId, text => text.Replace("Woof", "NativeObserverWoof", StringComparison.Ordinal));
            string? forkPath = null;
            string? forkWorkspace = null;
            try
            {
                using var response = JsonDocument.Parse(await ValidationBundleTools.WorkspaceForkApply(
                    CreateForkApplyService(CreateValidationService()), workspace.WorkspaceId, token, "keep", false, null,
                    "native-observer", CancellationToken.None));
                var result = response.RootElement;
                Assert.IsTrue(result.GetProperty("success").GetBoolean(), result.GetRawText());
                forkPath = result.GetProperty("forkPath").GetString();
                forkWorkspace = result.GetProperty("forkWorkspaceId").GetString();
                Assert.IsTrue(result.GetProperty("retained").GetBoolean());
                Assert.IsNotNull(forkPath);
                Assert.IsNotNull(forkWorkspace);
                NativeNamespaceObservationFixture.AssertUnchanged(before, project, globals);
                var fork = WorkspaceManager.GetCurrentSolution(forkWorkspace).Projects.Single(item => item.Name == "SampleLib");
                if (shape == "recursive-item")
                    Assert.AreSequenceEqual(before.AdditionalFiles.Select(path => Path.Combine(forkPath, Path.GetRelativePath(workspace.RootPath, path)))
                            .Order(FileSystemPath.Comparer).ToArray(),
                        fork.AdditionalDocuments.Select(document => document.FilePath!).Order(FileSystemPath.Comparer).ToArray());
                StringAssert.Contains(await File.ReadAllTextAsync(Path.Combine(forkPath, "SampleLib", "Dog.cs")), "NativeObserverWoof");
                Assert.IsFalse((await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "Dog.cs")))
                    .Contains("NativeObserverWoof", StringComparison.Ordinal));
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Native observer fork cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => { if (forkWorkspace is not null) WorkspaceManager.Close(forkWorkspace); }),
                    CleanupFailureCollector.FromAction(() => { if (forkPath is not null) DeleteDirectoryIfExists(forkPath); }));
            }
            NativeNamespaceObservationFixture.AssertUnchanged(before, project, globals);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native observer source workspace cleanup failed.", primary,
                async () => { if (workspace is not null) await workspace.DisposeAsync(); });
        }
    }

    [TestMethod]
    public async Task WorkspaceForkApply_RestoreFailureRetainsCleanupFailureAlongsidePrimary()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows sharing semantics are required to obstruct fork deletion.");
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, text => text.Replace("Woof", "CleanupWoof", StringComparison.Ordinal));
        using var runner = new LockedFailingRestoreRunner();
        var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager,
            WorkspaceExecutionGate, PreviewStore, CreateValidationService(), TestRunnerService, runner,
            NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyAsync(workspace.WorkspaceId,
            token, "drop-always", false, null, "cleanup-failure", CancellationToken.None));
        StringAssert.Contains(failure.Message, "dotnet restore");
        var outcomes = failure.Data["CleanupOutcomes"] as string[];
        Assert.IsNotNull(outcomes);
        Assert.IsTrue(outcomes.Any(outcome => outcome.Contains("delete fork directory", StringComparison.Ordinal)));
        Assert.IsFalse(outcomes.Any(outcome => outcome.Contains(workspace.RootPath, StringComparison.Ordinal)),
            "Cleanup outcomes must contain bounded role/type labels, never physical paths.");
        Assert.IsNotNull(PreviewStore.Retrieve(token), "Cleanup failure must preserve source preview authority.");
    }

    [TestMethod]
    public async Task WorkspaceForkApply_InvalidRetentionPublishesChoicesWithoutCallerText()
    {
        const string sentinel = "PRIVATE-FORK-RETENTION";
        var service = CreateForkApplyService(CreateValidationService());
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyAsync(
            sentinel, sentinel, sentinel, false, null, null, CancellationToken.None));
        Assert.IsInstanceOfType<IPublicMessageException>(exception);
        using var envelope = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(exception, "workspace_fork_apply"));
        Assert.AreEqual("InvalidArgument", envelope.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("ArgumentException", envelope.RootElement.GetProperty("exceptionType").GetString());
        StringAssert.Contains(envelope.RootElement.GetProperty("message").GetString()!,
            "drop-on-success, drop-on-failure, drop-always, keep");
        Assert.IsFalse(envelope.RootElement.GetRawText().Contains(sentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task WorkspaceForkApply_CrossWorkspaceTokenPublishesCorrectionWithoutIdentifiers()
    {
        await using var owner = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        await using var other = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var token = await StoreDogPreviewAsync(owner.WorkspaceId, source => source.Replace("Woof", "ForkWoof", StringComparison.Ordinal));
        var service = CreateForkApplyService(CreateValidationService());
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyAsync(
            other.WorkspaceId, token, "keep", false, null, null, CancellationToken.None));
        Assert.IsInstanceOfType<IPublicMessageException>(exception);
        using var envelope = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(exception, "workspace_fork_apply"));
        var json = envelope.RootElement.GetRawText();
        Assert.AreEqual("InvalidArgument", envelope.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("ArgumentException", envelope.RootElement.GetProperty("exceptionType").GetString());
        StringAssert.Contains(envelope.RootElement.GetProperty("message").GetString()!, "previewToken");
        foreach (var identifier in new[] { owner.WorkspaceId, other.WorkspaceId, token })
            Assert.IsFalse(json.Contains(identifier, StringComparison.Ordinal), json);
        Assert.IsNotNull(PreviewStore.Retrieve(token), "Refusal must preserve the original preview.");
    }

    [TestMethod]
    public async Task WorkspaceForkApply_KeepSuccess_MutatesOnlyFork()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogPath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalSource = await File.ReadAllTextAsync(dogPath, CancellationToken.None);
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, source =>
            source.Replace("Woof", "ForkWoof", StringComparison.Ordinal));
        var validationService = CreateValidationService();

        var json = await ValidationBundleTools.WorkspaceForkApply(
            CreateForkApplyService(validationService),
            workspace.WorkspaceId,
            token,
            retention: "keep",
            runTests: false,
            testFilter: null,
            forkName: "success",
            ct: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.IsTrue(root.GetProperty("success").GetBoolean());
        Assert.IsTrue(root.GetProperty("retained").GetBoolean());

        var forkPath = root.GetProperty("forkPath").GetString()
            ?? throw new AssertFailedException("forkPath should be returned.");
        var forkWorkspaceId = root.GetProperty("forkWorkspaceId").GetString()
            ?? throw new AssertFailedException("forkWorkspaceId should be returned for retained forks.");

        try
        {
            var forkDogPath = Path.Combine(forkPath, "SampleLib", "Dog.cs");
            StringAssert.Contains(await File.ReadAllTextAsync(forkDogPath, CancellationToken.None), "ForkWoof");
            Assert.AreEqual(originalSource, await File.ReadAllTextAsync(dogPath, CancellationToken.None),
                "workspace_fork_apply must not mutate the source workspace file.");
        }
        finally
        {
            WorkspaceManager.Close(forkWorkspaceId);
            DeleteDirectoryIfExists(forkPath);
        }
    }

    [TestMethod]
    public async Task WorkspaceForkApply_DefaultRetention_KeepsFailedForkLoadable()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, _ => "namespace SampleLib; public class Dog {");
        var validationService = CreateValidationService();

        var json = await ValidationBundleTools.WorkspaceForkApply(
            CreateForkApplyService(validationService),
            workspace.WorkspaceId,
            token,
            retention: "drop-on-success",
            runTests: false,
            testFilter: null,
            forkName: "failure",
            ct: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.IsFalse(root.GetProperty("success").GetBoolean());
        Assert.IsTrue(root.GetProperty("retained").GetBoolean(),
            "drop-on-success should retain failed forks for inspection.");

        var forkPath = root.GetProperty("forkPath").GetString()
            ?? throw new AssertFailedException("forkPath should be returned.");
        var forkWorkspaceId = root.GetProperty("forkWorkspaceId").GetString()
            ?? throw new AssertFailedException("forkWorkspaceId should be returned for retained failed forks.");

        try
        {
            Assert.IsTrue(Directory.Exists(forkPath), "Failed retained fork should remain on disk.");
            Assert.IsTrue(WorkspaceManager.ContainsWorkspace(forkWorkspaceId),
                "Retained failed fork should remain loaded for follow-up inspection.");
        }
        finally
        {
            WorkspaceManager.Close(forkWorkspaceId);
            DeleteDirectoryIfExists(forkPath);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WorkspaceForkApply_TestsDoNotBlockSourceWrite(bool explicitFilter)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, source =>
            source.Replace("Woof", "ForkWoof", StringComparison.Ordinal));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new BlockingTestRunner(entered, release);
        var validation = new WorkspaceValidationService(
            CompileCheckService, DiagnosticService, new FixedDiscovery(), runner, WorkspaceManager);
        var service = new WorkspaceForkApplyService(PathPolicy,
            WorkspaceManager, WorkspaceExecutionGate, PreviewStore, validation, runner, new DotnetCommandRunner(),
            NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var forkTask = ValidationBundleTools.WorkspaceForkApply(
            service, workspace.WorkspaceId, token,
            retention: "drop-always", runTests: true,
            testFilter: explicitFilter ? "FullyQualifiedName=ForkProbe" : null,
            ct: cancellation.Token);
        Task<bool>? writeTask = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(45));
            writeTask = WorkspaceExecutionGate.RunWriteAsync(workspace.WorkspaceId, async ct =>
            {
                await File.AppendAllTextAsync(workspace.GetPath("SampleLib", "Dog.cs"),
                    "\n// source write during fork tests\n", ct);
                return true;
            }, cancellation.Token, applyStalenessPolicy: false);
            Assert.IsTrue(await writeTask.WaitAsync(TimeSpan.FromSeconds(2)),
                "Source writes must complete while fork tests are still blocked.");
            Assert.IsFalse(forkTask.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await forkTask;
            if (writeTask is not null)
                await writeTask;
        }
    }

    [TestMethod]
    public async Task WorkspaceForkApply_CallerCancellation_ClosesLoadedFork()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, source =>
            source.Replace("Woof", "ForkWoof", StringComparison.Ordinal));
        using var cancellation = new CancellationTokenSource();
        var runner = new CancellingTestRunner(cancellation);
        var service = new WorkspaceForkApplyService(PathPolicy,
            WorkspaceManager, WorkspaceExecutionGate, PreviewStore, CreateValidationService(), runner,
            new DotnetCommandRunner(), NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);

        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => ValidationBundleTools.WorkspaceForkApply(
            service, workspace.WorkspaceId, token, runTests: true,
            testFilter: "FullyQualifiedName=ForkProbe", ct: cancellation.Token));
        Assert.IsNotNull(runner.WorkspaceId);
        Assert.IsFalse(WorkspaceManager.ContainsWorkspace(runner.WorkspaceId),
            "Cancellation after fork loading must close the fork workspace before deleting its files.");
        Assert.IsNotNull(runner.ForkPath);
        if (OperatingSystem.IsWindows())
            Assert.IsFalse(Directory.Exists(runner.ForkPath), "Windows cancellation cleanup removes the owned fork namespace.");
        else
        {
            Assert.IsTrue(Directory.Exists(runner.ForkPath), "POSIX cancellation cleanup retains directory names.");
            Assert.IsFalse(Directory.EnumerateFiles(runner.ForkPath, "*", SearchOption.AllDirectories).Any(), "Cancellation must still reclaim original file contents.");
            var outcomes = Assert.IsInstanceOfType<string[]>(failure.Data[OwnedResourceCleanup.OutcomeDataKey]);
            Assert.IsTrue(outcomes.Any(outcome => outcome.Contains("directory names were retained", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    [DataRow("drop-on-success", true, false)]
    [DataRow("drop-on-success", false, true)]
    [DataRow("drop-on-failure", true, true)]
    [DataRow("drop-on-failure", false, false)]
    [DataRow("drop-always", true, false)]
    [DataRow("drop-always", false, false)]
    [DataRow("keep", true, true)]
    [DataRow("keep", false, true)]
    public void ShouldRetainFork_AllPolicies_OwnTheirStateTransition(
        string retention,
        bool success,
        bool expected)
    {
        Assert.AreEqual(expected, WorkspaceForkApplyService.ShouldRetainFork(retention, success));
    }

    [TestMethod]
    public async Task RestoreForkAsync_Failure_RedactsCredentialBearingOutput()
    {
        const string secret = "super-secret-feed-password";
        var runner = new FailingRestoreRunner(
            $"NU1301: Unable to load https://build:{secret}@feed.example/v3/index.json" +
            $"?api_key={secret} Authorization: Bearer {secret}");
        var service = new WorkspaceForkApplyService(PathPolicy,
            workspaceManager: null!,
            workspaceExecutionGate: null!,
            previewStore: null!,
            validationService: null!,
            testRunnerService: null!,
            commandRunner: runner,
            logger: NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);

        var acceptedEpochs = 0;
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.RunRestorePhaseAsync(token => service.RestoreForkAsync(
                RestoreInvocationPlanner.Plan(Path.Combine(Path.GetTempPath(), "fork", "SampleSolution.slnx"), []),
                static _ => Task.CompletedTask,
                (_, _) => { acceptedEpochs++; return Task.CompletedTask; },
                static failure => failure,
                token), CancellationToken.None));

        Assert.AreEqual(0, acceptedEpochs, "A failed restore must never mint an artifact epoch.");
        StringAssert.Contains(exception.Message, "exit code 1");
        StringAssert.Contains(exception.Message, "[redacted]");
        Assert.IsFalse(
            exception.Message.Contains(secret, StringComparison.Ordinal),
            "Client-facing restore failures must not expose credential-shaped output.");
        Assert.IsTrue(
            exception.Message.Length < 700,
            "Client-facing restore failures must stay bounded.");
    }

    [TestMethod]
    public async Task RestoreForkAsync_NonTimeoutCancellation_PropagatesWithoutTimeoutReclassification()
    {
        var expected = new OperationCanceledException("runner-owned cancellation");
        var service = new WorkspaceForkApplyService(PathPolicy,
            workspaceManager: null!,
            workspaceExecutionGate: null!,
            previewStore: null!,
            validationService: null!,
            testRunnerService: null!,
            commandRunner: new CancelingRestoreRunner(expected),
            logger: NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);

        var actual = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.RunRestorePhaseAsync(token => service.RestoreForkAsync(
                RestoreInvocationPlanner.Plan(Path.Combine(Path.GetTempPath(), "fork", "SampleSolution.slnx"), []),
                static _ => Task.CompletedTask,
                static (_, _) => Task.CompletedTask,
                static failure => failure,
                token), CancellationToken.None));

        Assert.AreSame(expected, actual,
            "Only the service-owned timeout token may be reclassified as TimeoutException.");
    }


    [TestMethod]
    public async Task WorkspaceForkApply_RestoreCapturesSourcePackagesBeforeObjIsExcluded()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, source => source.Replace("Woof", "ForkWoof", StringComparison.Ordinal));
        var packages = Path.Combine(Path.GetDirectoryName(WorkspaceManager.GetStatus(workspace.WorkspaceId).LoadedPath!)!, "scratch-packages");
        foreach (var project in WorkspaceManager.GetStatus(workspace.WorkspaceId).Projects)
        {
            var obj = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "obj");
            Directory.CreateDirectory(obj);
            SetRecordedPackageRoot(Path.Combine(obj, "project.assets.json"), packages);
        }
        var runner = new CapturingRestoreRunner();
        var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager, WorkspaceExecutionGate, PreviewStore,
            CreateValidationService(), TestRunnerService, runner, NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyAsync(workspace.WorkspaceId, token,
            "drop-always", false, null, "packages", CancellationToken.None));
        var invocation = runner.Invocations.Single();
        var sourceNode = EvaluatedBuildGraph.Source(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), CancellationToken.None).Nodes
            .Single(node => !node.IsOuterBuild && string.Equals(Path.GetFileName(node.ProjectPath), Path.GetFileName(invocation[1]), StringComparison.Ordinal));
        var relative = Path.GetRelativePath(workspace.RootPath, sourceNode.ProjectPath);
        var forkRoot = Path.TrimEndingDirectorySeparator(invocation[1][..^relative.Length]);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(forkRoot, relative)), invocation[1]);
        var transported = sourceNode.Globals.ToDictionary(pair => pair.Key, pair => BuildPathRelocation.RelocateGlobal(pair.Key,
            pair.Value, path => FileSystemPath.Comparer.Equals(path, workspace.RootPath) || FileSystemPath.IsStrictDescendant(workspace.RootPath, path)
                ? Path.GetFullPath(Path.Combine(forkRoot, Path.GetRelativePath(workspace.RootPath, path))) : path,
            BuildPathRelocation.KnownPathProperties), StringComparer.OrdinalIgnoreCase);
        var expectedArguments = MsBuildCommandProperties.Append(
            ["restore", invocation[1], "--nologo", "--packages", packages, "--no-dependencies"], transported);
        CollectionAssert.AreEqual(expectedArguments.Take(6).ToArray(), invocation.Take(6).ToArray(), "The recorded root and isolated restore policy must be exact.");
        CollectionAssert.AreEquivalent(expectedArguments.Skip(6).ToArray(), invocation.Skip(6).ToArray(), "Every captured project-context flag must survive transport exactly once.");
        Assert.IsFalse(runner.AssetsPresent, "Fork assets must be absent before restore; roots must come from the source.");
    }


    [TestMethod]
    public async Task WorkspaceForkApply_MixedRecordedAndDefaultRoots_IsolatesForkProjects()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, source => source.Replace("Woof", "ForkWoof", StringComparison.Ordinal));
        var projects = WorkspaceManager.GetStatus(workspace.WorkspaceId).Projects;
        Assert.IsGreaterThanOrEqualTo(2, projects.Count);
        var packages = Path.Combine(Path.GetDirectoryName(projects[0].FilePath)!, "scratch-packages");
        for (var i = 0; i < projects.Count; i++)
        {
            var obj = Path.Combine(Path.GetDirectoryName(projects[i].FilePath)!, "obj");
            Directory.CreateDirectory(obj);
            SetRecordedPackageRoot(Path.Combine(obj, "project.assets.json"), i == 0 ? packages : null);
        }
        var runner = new CapturingRestoreRunner(projects.Count);
        var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager, WorkspaceExecutionGate, PreviewStore,
            CreateValidationService(), TestRunnerService, runner, NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyAsync(workspace.WorkspaceId, token,
            "drop-always", false, null, "mixed-packages", CancellationToken.None));
        Assert.HasCount(projects.Count, runner.Invocations);
        foreach (var invocation in runner.Invocations)
        {
            CollectionAssert.Contains(invocation.ToArray(), "--no-dependencies");
            Assert.IsTrue(FileSystemPath.IsStrictDescendant(PhysicalGraphNamespace.ForkParent, invocation[1]));
        }
        Assert.HasCount(1, runner.Invocations.Where(i => i.Contains("--packages")).ToArray());
        Assert.IsTrue(runner.Tokens.All(token => token == runner.Tokens[0]), "All restores must share one timeout/cancellation budget.");
    }

    [TestMethod]
    public async Task RestoreForkAsync_CallerCancellationBetweenProjects_DoesNotLaunchNextRestore()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new CancellingBetweenRestoresRunner(cancellation);
        var service = new WorkspaceForkApplyService(PathPolicy, null!, null!, null!, null!, null!, runner, NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
        var plan = RestoreInvocationPlanner.Plan(Path.GetFullPath("App.slnx"),
            [new(Path.GetFullPath("A.csproj"), "packages", null, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, null), new(Path.GetFullPath("B.csproj"), null, null, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, null)]);
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RunRestorePhaseAsync(
            token => service.RestoreForkAsync(plan, static _ => Task.CompletedTask,
                static (_, _) => Task.CompletedTask, static failure => failure, token), cancellation.Token));
        Assert.AreEqual(1, runner.InvocationCount);
    }

    private sealed class CancellingBetweenRestoresRunner(CancellationTokenSource cancellation) : RoslynMcp.Tests.Helpers.CannedCommandRunner
    {
        public int InvocationCount { get; private set; }
        public override Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            InvocationCount++;
            cancellation.Cancel();
            return Task.FromResult(new CommandExecutionDto("dotnet", arguments, workingDirectory, targetPath, 0, true, 0, "", ""));
        }
    }

    private sealed class CapturingRestoreRunner(int failOnInvocation = 1) : RoslynMcp.Tests.Helpers.CannedCommandRunner
    {
        public List<IReadOnlyList<string>> Invocations { get; } = [];
        public bool AssetsPresent { get; private set; }
        public List<CancellationToken> Tokens { get; } = [];
        public override Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Invocations.Add(arguments.ToArray());
            Tokens.Add(ct);
            AssetsPresent = Directory.EnumerateFiles(workingDirectory, "project.assets.json", SearchOption.AllDirectories).Any();
            var fails = Invocations.Count == failOnInvocation;
            return Task.FromResult(new CommandExecutionDto("dotnet", arguments, workingDirectory, targetPath, fails ? 1 : 0, !fails, 0, "", "test refusal"));
        }
    }

    private static WorkspaceValidationService CreateValidationService() =>
        new(
            CompileCheckService,
            DiagnosticService,
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker);

    private static void SetRecordedPackageRoot(string assetsPath, string? packagesPath)
    {
        var assets = JsonNode.Parse(File.ReadAllText(assetsPath))?.AsObject()
            ?? throw new AssertFailedException("The native fixture must supply valid restore assets.");
        var restore = assets["project"]?["restore"]?.AsObject()
            ?? throw new AssertFailedException("The native fixture must supply restore metadata.");
        if (packagesPath is null) restore.Remove("packagesPath");
        else restore["packagesPath"] = packagesPath;
        File.WriteAllText(assetsPath, assets.ToJsonString());
    }

    private static WorkspaceForkApplyService CreateForkApplyService(
        WorkspaceValidationService validationService) =>
        new(
            PathPolicy,
            WorkspaceManager,
            WorkspaceExecutionGate,
            PreviewStore,
            validationService,
            TestRunnerService,
            new DotnetCommandRunner(),
            NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);

    private static async Task<string> StoreDogPreviewAsync(string workspaceId, Func<string, string> mutate)
    {
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var dog = solution.Projects
            .SelectMany(project => project.Documents)
            .First(document => string.Equals(document.Name, "Dog.cs", StringComparison.Ordinal));

        var source = (await dog.GetTextAsync(CancellationToken.None).ConfigureAwait(false)).ToString();
        var modified = solution.WithDocumentText(dog.Id, SourceText.From(mutate(source)));

        return PreviewStore.Store(workspaceId, solution, modified, WorkspaceManager.GetCurrentVersion(workspaceId), "workspace_fork_apply test preview", await SolutionDiffHelper.ComputeChangesAsync(solution, modified, SolutionDiffReadContext.ForWorkspace(WorkspaceManager, workspaceId), CancellationToken.None), PreviewKind.Unspecified);
    }

    private sealed class LockedFailingRestoreRunner : IDotnetCommandRunner, IDisposable
    {
        private FileStream? _lock;
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _lock = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult(new CommandExecutionDto("dotnet", arguments, workingDirectory, targetPath, 1, false, 0,
                string.Empty, "Synthetic restore failure with an owned deletion obstruction."));
        }
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
            => RunAsync(workingDirectory, targetPath, arguments, ct);
        public void Dispose() => _lock?.Dispose();
    }

    private sealed class FailingRestoreRunner(string stderr) : RoslynMcp.Tests.Helpers.CannedCommandRunner
    {
        public override Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct) =>
            Task.FromResult(new CommandExecutionDto(
                Command: "dotnet",
                Arguments: arguments,
                WorkingDirectory: workingDirectory,
                TargetPath: targetPath,
                ExitCode: 1,
                Succeeded: false,
                DurationMs: 1,
                StdOut: string.Empty,
                StdErr: stderr));
    }

    private sealed class CancelingRestoreRunner(OperationCanceledException exception) : RoslynMcp.Tests.Helpers.CannedCommandRunner
    {
        public override Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct) =>
            Task.FromException<CommandExecutionDto>(exception);
    }

    private sealed class BlockingTestRunner(TaskCompletionSource entered, TaskCompletionSource release) : ITestRunnerService
    {
        public Task<TestRunPlan> PrepareTestRunAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct) =>
            throw new NotSupportedException("This composed-run fixture does not support prepared execution.");

        public Task<TestRunResultDto> RunTestCommandAsync(TestRunPlan plan, CancellationToken ct) =>
            throw new NotSupportedException("This composed-run fixture does not support prepared execution.");

        public Task<TestRunResultDto> CompleteTestRunAsync(TestRunPlan plan, TestRunResultDto result, CancellationToken ct) =>
            throw new NotSupportedException("This composed-run fixture does not support prepared execution.");

        public async Task<TestRunResultDto> RunTestsAsync(
            string workspaceId, string? projectName, string? filter, CancellationToken ct)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new TestRunResultDto(
                new CommandExecutionDto("dotnet", ["test"], string.Empty, string.Empty, 0, true, 0, string.Empty, string.Empty),
                Total: 1, Passed: 1, Failed: 0, Skipped: 0, Failures: []);
        }
    }

    private sealed class FixedDiscovery : ITestDiscoveryService
    {
        public Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(
            string workspaceId, IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct) =>
            Task.FromResult(new RelatedTestsForFilesDto([], "FullyQualifiedName=ForkProbe",
                new PaginationInfo(0, 0, false), new RelatedTestsDiagnosticsDto(1, ["fixed test fixture"], [])));

        public Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();

        public Task<RelatedTestsForSymbolDto> FindRelatedTestsAsync(string workspaceId, SymbolLocator locator,
            int maxResults, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class CancellingTestRunner(CancellationTokenSource cancellation) : ITestRunnerService
    {
        public Task<TestRunPlan> PrepareTestRunAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct) =>
            throw new NotSupportedException("This composed-run fixture does not support prepared execution.");

        public Task<TestRunResultDto> RunTestCommandAsync(TestRunPlan plan, CancellationToken ct) =>
            throw new NotSupportedException("This composed-run fixture does not support prepared execution.");

        public Task<TestRunResultDto> CompleteTestRunAsync(TestRunPlan plan, TestRunResultDto result, CancellationToken ct) =>
            throw new NotSupportedException("This composed-run fixture does not support prepared execution.");

        public string? WorkspaceId { get; private set; }
        public string? ForkPath { get; private set; }

        public Task<TestRunResultDto> RunTestsAsync(
            string workspaceId, string? projectName, string? filter, CancellationToken ct)
        {
            WorkspaceId = workspaceId;
            ForkPath = Path.GetDirectoryName(WorkspaceManager.GetStatus(workspaceId).LoadedPath);
            cancellation.Cancel();
            return Task.FromCanceled<TestRunResultDto>(ct);
        }
    }
}
