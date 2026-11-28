using System.Xml.Linq;
using Microsoft.Extensions.Time.Testing;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ForkNativeOwnershipTests : ForkNativeAdmissionTestBase
{
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public void OwnedProjectImports_PreserveUtf16CommentAndMetadata_WhenInstalledAndStripped()
    {
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Unicode.custombuild");
        var guard = fixture.PathInFork("guard.targets");
        File.WriteAllText(guard, "<Project />");
        File.WriteAllText(project, "<Project><!-- Café Ω --><ItemGroup><None Include=\"input.txt\" Note=\"漢字\" /></ItemGroup></Project>",
            System.Text.Encoding.Unicode);
        var previous = new ForkAdmissionMaterial.Configuration(1, fixture.Source, fixture.Fork, guard, string.Empty);
        ForkProjectImports.Install(project, fixture.Fork, guard, () => { });
        Assert.AreEqual(System.Text.Encoding.Unicode.CodePage, SourceFileEncoding.FromBytes(File.ReadAllBytes(project)).CodePage);
        StringAssert.Contains(File.ReadAllText(project), "<!-- Café Ω -->");
        Assert.AreEqual("漢字", (string?)XDocument.Load(project).Descendants("None").Single().Attribute("Note"));
        ForkAdmissionMaterial.StripPreviousImports(fixture.Fork, previous, CancellationToken.None);
        Assert.AreEqual(System.Text.Encoding.Unicode.CodePage, SourceFileEncoding.FromBytes(File.ReadAllBytes(project)).CodePage);
        Assert.AreEqual(0, XDocument.Load(project).Descendants("Import").Count());
        StringAssert.Contains(File.ReadAllText(project), "<!-- Café Ω -->");
        Assert.AreEqual("漢字", (string?)XDocument.Load(project).Descendants("None").Single().Attribute("Note"));
    }

    [TestMethod]
    public void AdmissionInstallation_PreservesUnreferencedProjectShapedData()
    {
        using var fixture = new NativeFixture(false);
        var entry = fixture.PathInFork("Entry.custombuild");
        fixture.WriteProject(entry, string.Empty);
        var data = fixture.PathInFork("fixtures/Unloaded.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(data)!);
        File.WriteAllText(data, "<Project><!-- Fixture bytes: Café Ω --><PropertyGroup><Example>漢字</Example></PropertyGroup></Project>", System.Text.Encoding.Unicode);
        var before = File.ReadAllBytes(data);
        ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [entry], CancellationToken.None);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(data), "Unloaded project-shaped test data must remain byte-identical.");
        Assert.AreEqual(1, XDocument.Load(entry).Root!.Elements("Import").Count(import => (string?)import.Attribute("Label") == ForkProjectImports.Label));
    }

    [TestMethod]
    public async Task ExpiredForkSweep_PreservesActiveAndUncertainExecution_ThenDeletesCompletedMaterial()
    {
        using var fixture = new NativeFixture(false);
        var forks = Path.Combine(fixture.Root, "forks");
        var fork = Path.Combine(forks, "20000101T000000000Z-native");
        Directory.CreateDirectory(fork);
        var project = Path.Combine(fork, "Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        var config = ForkAdmissionMaterial.Install(fixture.Source, fork, [project], CancellationToken.None);
        using var execution = await ForkAdmissionExecution.CreateAsync(config, null, TimeProvider.System, CancellationToken.None);
        WorkspaceForkApplyService.SweepExpiredForks(forks, 1, DateTimeOffset.UtcNow, WorkspaceManager.TryReserveForkStorageDeletion);
        Assert.IsTrue(Directory.Exists(execution.Root), "TTL cleanup must preserve admission material during an active invocation.");
        execution.Dispose();
        Assert.IsNotNull(execution.CleanupWarning);
        WorkspaceForkApplyService.SweepExpiredForks(forks, 1, DateTimeOffset.UtcNow, WorkspaceManager.TryReserveForkStorageDeletion);
        Assert.IsTrue(Directory.Exists(execution.Root), "Uncertain native completion retains its execution and blocks expiry.");
        // This test never starts native work, so it can remove the uncertain residue.
        Directory.Delete(execution.Root, recursive: true);
        using var completed = await ForkAdmissionExecution.CreateAsync(config, null, TimeProvider.System, CancellationToken.None);
        completed.Complete();
        completed.Dispose();
        WorkspaceForkApplyService.SweepExpiredForks(forks, 1, DateTimeOffset.UtcNow, WorkspaceManager.TryReserveForkStorageDeletion);
        Assert.IsFalse(Directory.Exists(fork), "Completed expired forks must still be collected.");
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("valid-long-root")]
    [DataRow("missing-guard")]
    [DataRow("malformed-owned")]
    public async Task RetainedFork_ProjectFilePreviewReplay_StripsOnlyPreviousOwnedImport(string material)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var fixture = new NativeFixture(false);
        var sourceRoot = fixture.Source;
        if (material == "valid-long-root")
        {
            sourceRoot = Path.Combine(sourceRoot, new string('p', Math.Max(1, 190 - sourceRoot.Length - 1)));
            Directory.CreateDirectory(sourceRoot);
        }
        var project = Path.Combine(sourceRoot, "Entry.csproj");
        fixture.WriteProject(project, "<Import Project=\"Authored.targets\" Label=\"RoslynMcpForkAdmission\" />");
        File.WriteAllText(Path.Combine(sourceRoot, "Authored.targets"), "<Project><!-- Café Ω authored import --></Project>");
        var solutionPath = Path.Combine(sourceRoot, "Entry.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"Entry.csproj\" /></Solution>");
        var restored = await DotnetCommandRunner.RunAsync(sourceRoot, project, ["restore", project, "--nologo", "-nr:false"], timeout.Token);
        Assert.IsTrue(restored.Succeeded, restored.Describe());
        var source = await WorkspaceManager.LoadAsync(solutionPath, timeout.Token);
        var store = new PreviewStore();
        var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
            TestRunnerService, WorkspaceManager, ChangeTracker);
        await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store, validation,
            TestRunnerService, DotnetCommandRunner, Helpers.ForkStorageFixtureBoundary.ValidateAsync, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
        RoslynMcp.Core.Models.WorkspaceForkApplyResultDto? first = null;
        RoslynMcp.Core.Models.WorkspaceForkApplyResultDto? second = null;
        RoslynMcp.Core.Models.WorkspaceForkApplyResultDto? third = null;
        try
        {
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var token = store.Store(source.WorkspaceId, original, original, WorkspaceManager.GetCurrentVersion(source.WorkspaceId),
                "first retained fork", false, RoslynMcp.Core.Models.PreviewKind.Unspecified);
            first = await service.ApplyAsync(source.WorkspaceId, token, "keep", false, null, "first", timeout.Token);
            Assert.IsTrue(first.Success);
            Assert.IsNotNull(first.ForkWorkspaceId);
            Assert.AreEqual(Path.Combine(sourceRoot, ".roslynmcp", "forks"), Path.GetDirectoryName(first.ForkPath));
            var firstProject = Path.Combine(first.ForkPath, "Entry.csproj");
            var oldConfig = ForkAdmissionMaterial.Find(firstProject)!;
            var guard = XDocument.Load(oldConfig.GuardTargetsPath);
            guard.Root!.Add(new XElement("Target", new XAttribute("Name", "PreviousGuardMustNeverRunInCopiedProject"),
                new XAttribute("BeforeTargets", "PrepareForBuild"), new XAttribute("Condition", "'$(MSBuildProjectDirectory)' != '" + first.ForkPath + "'"),
                new XElement("Error", new XAttribute("Text", "PREVIOUS_GUARD_EXECUTED_IN_NEW_FORK"))));
            guard.Save(oldConfig.GuardTargetsPath);
            var firstSolution = WorkspaceManager.GetCurrentSolution(first.ForkWorkspaceId);
            var firstVersion = WorkspaceManager.GetCurrentVersion(first.ForkWorkspaceId);
            var firstStatus = await WorkspaceManager.GetStatusAsync(first.ForkWorkspaceId);
            var projectText = File.ReadAllText(firstProject).Replace("</Project>", "<!-- Replayed Café 漢字 --></Project>", StringComparison.Ordinal);
            if (material == "malformed-owned")
            {
                var replay = XDocument.Parse(projectText, LoadOptions.PreserveWhitespace);
                replay.Root!.Elements("Import").Single(import => (string?)import.Attribute("Project") == oldConfig.GuardTargetsPath)
                    .Add(new XAttribute("Condition", "'1' == '1'"));
                projectText = replay.ToString(SaveOptions.DisableFormatting);
            }
            if (material == "missing-guard") File.Delete(oldConfig.GuardTargetsPath);
            var modified = firstSolution.AddDocument(Microsoft.CodeAnalysis.DocumentId.CreateNewId(firstSolution.ProjectIds.Single()),
                "project-file-replay", Microsoft.CodeAnalysis.Text.SourceText.From(projectText, System.Text.Encoding.Unicode), filePath: firstProject);
            var secondToken = store.Store(first.ForkWorkspaceId, firstSolution, modified, firstVersion,
                "project XML preview reintroduces previous owned absolute import", false, RoslynMcp.Core.Models.PreviewKind.Unspecified);
            var entry = WorkspaceForkGraphReplayTests.CapturePreviewEntry(store, secondToken);
            var bytes = Directory.GetFiles(first.ForkPath, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            if (material is not ("valid" or "valid-long-root"))
            {
                var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    service.ApplyAsync(first.ForkWorkspaceId, secondToken, "keep", false, null, "second", timeout.Token));
                StringAssert.Contains(refusal.Message, "Recreate the fork");
            }
            else
            {
                second = await service.ApplyAsync(first.ForkWorkspaceId, secondToken, "keep", false, null, "second", timeout.Token);
                Assert.IsTrue(second.Success);
                Assert.IsNotNull(second.ForkWorkspaceId);
                Assert.AreEqual(Path.GetDirectoryName(first.ForkPath), Path.GetDirectoryName(second.ForkPath));
                var secondProject = Path.Combine(second.ForkPath, "Entry.csproj");
                var nextConfig = ForkAdmissionMaterial.Find(secondProject)!;
                Assert.AreEqual(first.ForkPath, nextConfig.SourceRoot, "Admission must use the actual retained source/current fork pair.");
                var xml = XDocument.Load(secondProject);
                Assert.AreEqual(0, xml.Root!.Elements("Import").Count(import => (string?)import.Attribute("Project") == oldConfig.GuardTargetsPath));
                Assert.AreEqual(1, xml.Root.Elements("Import").Count(import => (string?)import.Attribute("Project") == nextConfig.GuardTargetsPath));
                Assert.AreEqual(1, xml.Root.Elements("Import").Count(import => (string?)import.Attribute("Project") == "Authored.targets"));
                StringAssert.Contains(File.ReadAllText(secondProject), "Replayed Café 漢字");
                using var reader = new StreamReader(secondProject, detectEncodingFromByteOrderMarks: true);
                reader.Peek();
                Assert.AreEqual(1200, reader.CurrentEncoding.CodePage);
                var native = await GatedCommandExecutor.ExecuteAsync(second.ForkWorkspaceId, secondProject,
                    ["msbuild", "Entry.csproj", "-getProperty:MSBuildStartupDirectory", "-nr:false", "-nologo"], TimeSpan.FromSeconds(30), timeout.Token);
                Assert.IsTrue(native.Succeeded, native.Describe());
                Assert.AreEqual(Path.TrimEndingDirectorySeparator(second.ForkPath), Path.TrimEndingDirectorySeparator(native.StdOut.Trim()),
                    "Relative SDK invocation must start in the actual returned fork.");
                var secondSolution = WorkspaceManager.GetCurrentSolution(second.ForkWorkspaceId);
                var thirdToken = store.Store(second.ForkWorkspaceId, secondSolution, secondSolution, WorkspaceManager.GetCurrentVersion(second.ForkWorkspaceId),
                    "third generation", false, RoslynMcp.Core.Models.PreviewKind.Unspecified);
                third = await service.ApplyAsync(second.ForkWorkspaceId, thirdToken, "keep", false, null, "third", timeout.Token);
                Assert.IsTrue(third.Success);
                Assert.AreEqual(Path.GetDirectoryName(first.ForkPath), Path.GetDirectoryName(third.ForkPath));
                Assert.AreEqual(second.ForkPath, ForkAdmissionMaterial.Find(Path.Combine(third.ForkPath, "Entry.csproj"))!.SourceRoot);
            }
            Assert.AreSame(firstSolution, WorkspaceManager.GetCurrentSolution(first.ForkWorkspaceId));
            Assert.AreEqual(firstVersion, WorkspaceManager.GetCurrentVersion(first.ForkWorkspaceId));
            Assert.AreEqual(firstStatus.SnapshotToken, (await WorkspaceManager.GetStatusAsync(first.ForkWorkspaceId)).SnapshotToken);
            Assert.AreEqual(entry, WorkspaceForkGraphReplayTests.CapturePreviewEntry(store, secondToken));
            foreach (var (path, expected) in bytes) CollectionAssert.AreEqual(expected, File.ReadAllBytes(path), path);
        }
        finally
        {
            if (third?.ForkWorkspaceId is { } thirdId) WorkspaceManager.Close(thirdId);
            if (second?.ForkWorkspaceId is { } secondId) WorkspaceManager.Close(secondId);
            if (first?.ForkWorkspaceId is { } firstId) WorkspaceManager.Close(firstId);
            WorkspaceManager.Close(source.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetainedInvocation_RealVstestAndNativeMtp_AcceptForwardedAdmissionProperties(bool nativeMtp)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Probe.csproj");
        var body = new XElement("PropertyGroup", new XElement("IsTestProject", "true"),
            new XElement("EnableMSTestRunner", nativeMtp ? "true" : "false"), new XElement("OutputType", nativeMtp ? "Exe" : "Library")) +
            new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "Probe.cs")),
                new XElement("PackageReference", new XAttribute("Include", "Microsoft.NET.Test.Sdk"), new XAttribute("Version", "18.11.0")),
                new XElement("PackageReference", new XAttribute("Include", "MSTest.TestAdapter"), new XAttribute("Version", "4.5.1")),
                new XElement("PackageReference", new XAttribute("Include", "MSTest.TestFramework"), new XAttribute("Version", "4.5.1"))).ToString();
        if (nativeMtp)
        {
            body += new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Microsoft.Testing.Extensions.TrxReport"), new XAttribute("Version", "2.5.1")));
            File.WriteAllText(fixture.PathInFork("global.json"), "{\"test\":{\"runner\":\"Microsoft.Testing.Platform\"}}");
        }
        fixture.WriteProject(project, body);
        File.WriteAllText(fixture.PathInFork("Probe.cs"), "using Microsoft.VisualStudio.TestTools.UnitTesting; [TestClass] public sealed class NativeProbe { [TestMethod] public void Probe() { Assert.AreEqual(2, 1 + 1); } }");
        var marker = Path.Combine(fixture.Evidence, "forwarded.txt");
        File.WriteAllText(fixture.PathInFork("Directory.Build.targets"), new XElement("Project",
            new XElement("Target", new XAttribute("Name", "ObserveActualInvocationProperties"), new XAttribute("BeforeTargets", "PrepareForBuild"),
                new XElement("WriteLinesToFile", new XAttribute("File", marker), new XAttribute("Lines", "$(RoslynMcpForkExecutionRoot)"), new XAttribute("Overwrite", "true")))).ToString());
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
        var loaded = await WorkspaceManager.LoadAsync(project, timeout.Token);
        try
        {
            File.Delete(marker);
            var results = Path.Combine(fixture.Evidence, "results");
            string[] arguments = nativeMtp
                ? ["test", "--project", project, "--report-trx", "--results-directory", results, "--no-restore"]
                : ["test", project, "--no-restore", "--nologo", "--logger", "trx", "--results-directory", results];
            var result = await GatedCommandExecutor.ExecuteAsync(loaded.WorkspaceId, project, arguments, TimeSpan.FromMinutes(2), timeout.Token);
            TestContext.WriteLine(result.StdOut + result.StdErr);
            Assert.IsTrue(result.Succeeded, result.Describe());
            var trx = XDocument.Load(Directory.GetFiles(results, "*.trx", SearchOption.AllDirectories).Single());
            var counters = trx.Descendants().Single(element => element.Name.LocalName == "Counters");
            Assert.AreEqual("1", (string?)counters.Attribute("total"));
            Assert.AreEqual("1", (string?)counters.Attribute("passed"));
            StringAssert.StartsWith(File.ReadAllText(marker).Trim(), Path.Combine(fixture.Fork, ForkAdmissionMaterial.DirectoryName, "executions") + Path.DirectorySeparatorChar);
            Assert.AreEqual(0, Directory.GetDirectories(Path.Combine(fixture.Fork, ForkAdmissionMaterial.DirectoryName, "executions")).Length);
        }
        finally { WorkspaceManager.Close(loaded.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("cancel")]
    [DataRow("deadline")]
    [DataRow("stale")]
    [DataRow("construction")]
    [DataRow("pre-cancel")]
    public async Task AdmissionExecution_OwnershipWaitAndConstruction_ReleaseReferencesWithoutResurrection(string mode)
    {
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        var config = ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], CancellationToken.None);
        if (mode is "cancel" or "deadline")
        {
            using var blocked = await WorkspaceForkApplyService.AcquireForkApplyLockAsync(fixture.Fork, CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            var clock = new FakeTimeProvider();
            var deadline = mode == "deadline" ? clock.GetUtcNow().AddMilliseconds(100) : (DateTimeOffset?)null;
            var pending = ForkAdmissionExecution.CreateAsync(config, deadline, clock, cancellation.Token).AsTask();
            Assert.IsFalse(pending.IsCompleted, "The same existing per-fork critical section must serialize invocation start and deletion.");
            Assert.IsNull(WorkspaceForkApplyService.TryAcquireForkApplyLock(fixture.Fork));
            if (mode == "cancel")
            {
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            }
            else
            {
                clock.Advance(TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsExactlyAsync<TimeoutException>(() => pending);
            }
        }
        else if (mode == "pre-cancel")
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => ForkAdmissionExecution.CreateAsync(config, DateTimeOffset.UtcNow.AddMinutes(-1), TimeProvider.System, cancellation.Token).AsTask());
        }
        else if (mode == "stale")
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixture.Fork);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ForkAdmissionExecution.CreateAsync(config, null, TimeProvider.System, CancellationToken.None).AsTask());
            Assert.IsFalse(Directory.Exists(fixture.Fork), "A Find snapshot must not resurrect material deleted before ownership acquisition.");
        }
        else
        {
            var executions = Path.Combine(fixture.Fork, ForkAdmissionMaterial.DirectoryName, "executions");
            File.WriteAllText(executions, "Block directory creation before any native work starts.");
            await Assert.ThrowsAsync<IOException>(() => ForkAdmissionExecution.CreateAsync(config, null, TimeProvider.System, CancellationToken.None).AsTask());
            Assert.IsFalse(Directory.Exists(executions));
        }
        Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(fixture.Fork), "All failed/wait-cancelled construction paths must release their ownership reference.");
    }

    [TestMethod]
    public async Task ForkCleanup_OwnershipResolutionFailure_RetainsMaterialAndPreservesPrimaryCancellation()
    {
        using var fixture = new NativeFixture(false);
        var cycle = Path.Combine(fixture.Root, "cycle");
        WorkspaceForkGraphReplayTests.CreateLink(cycle, cycle);
        try
        {
            Assert.ThrowsExactly<IOException>(() => PhysicalPathResolver.Resolve(cycle));
            await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, new PreviewStore(),
                null!, null!, DotnetCommandRunner, Helpers.ForkStorageFixtureBoundary.ValidateAsync, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
            var cleanup = typeof(WorkspaceForkApplyService).GetMethod("CleanupFork", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var warnings = new List<string>();
            var original = new OperationCanceledException("Primary cancellation must survive cleanup identity failure.");
            var observed = Assert.ThrowsExactly<OperationCanceledException>(() =>
            {
                try { throw original; }
                catch
                {
                    cleanup.Invoke(service, [null, Path.Combine(cycle, "fork"), warnings, CancellationToken.None]);
                    throw;
                }
            });
            Assert.AreSame(original, observed);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings.Single(), "completion could not be verified");
            Assert.IsNotNull(new DirectoryInfo(cycle).LinkTarget, "Cleanup must retain unverifiable material.");
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(cycle); }
    }

    [TestMethod]
    public async Task ForkCleanup_ActiveInvocationRetainsWorkspaceAndMaterial_CompletedInvocationDeletesBoth()
    {
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        await fixture.RestoreAsync(project, DotnetCommandRunner, CancellationToken.None);
        var config = ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], CancellationToken.None);
        var loaded = await WorkspaceManager.LoadAsync(project, CancellationToken.None);
        try
        {
            var original = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
            using var execution = await ForkAdmissionExecution.CreateAsync(config, null, TimeProvider.System, CancellationToken.None);
            await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, new PreviewStore(),
                null!, null!, DotnetCommandRunner, Helpers.ForkStorageFixtureBoundary.ValidateAsync, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
            var cleanup = typeof(WorkspaceForkApplyService).GetMethod("CleanupFork", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var warnings = new List<string>();
            cleanup.Invoke(service, [loaded.WorkspaceId, fixture.Fork, warnings, CancellationToken.None]);
            Assert.AreEqual(1, warnings.Count);
            Assert.IsTrue(Directory.Exists(execution.Root));
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId), "A refused cleanup must not close the active invocation's workspace.");
            execution.Complete();
            execution.Dispose();
            warnings.Clear();
            cleanup.Invoke(service, [loaded.WorkspaceId, fixture.Fork, warnings, CancellationToken.None]);
            Assert.AreEqual(0, warnings.Count);
            Assert.IsFalse(Directory.Exists(fixture.Fork));
        }
        finally { WorkspaceManager.Close(loaded.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("marker")]
    [DataRow("cleanup")]
    public async Task AdmissionExecution_CancellationMarkerAndCleanupFailures_PreservePrimaryCancellationAndReleaseOwnership(string failure)
    {
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        var config = ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        using var execution = await ForkAdmissionExecution.CreateAsync(config, null, TimeProvider.System, cancellation.Token);
        FileStream? held = null;
        try
        {
            if (failure == "marker") Directory.CreateDirectory(Path.Combine(execution.Root, "cancelled"));
            else
            {
                if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file sharing supplies this deterministic cleanup refusal.");
                held = new FileStream(Path.Combine(execution.Root, "held"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            }
            cancellation.Cancel();
            Assert.ThrowsExactly<OperationCanceledException>(() => cancellation.Token.ThrowIfCancellationRequested());
            execution.Complete();
            execution.Dispose();
            Assert.IsNotNull(execution.CleanupWarning);
            Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(fixture.Fork));
            if (failure == "marker") StringAssert.Contains(execution.CleanupWarning, "cancellation marker");
            else
            {
                StringAssert.Contains(execution.CleanupWarning, "could not be removed");
                Assert.IsTrue(Directory.Exists(execution.Root));
            }
        }
        finally { held?.Dispose(); }
    }

    [TestMethod]
    public async Task ForkOwnership_PhysicalAliasSharesCriticalSectionForSweepAndCleanup()
    {
        using var fixture = new NativeFixture(false);
        var aliases = Path.Combine(fixture.Root, "aliases");
        Directory.CreateDirectory(aliases);
        var alias = Path.Combine(aliases, "20000101T000000000Z-alias");
        WorkspaceForkGraphReplayTests.CreateLink(alias, fixture.Fork);
        using var held = await WorkspaceForkApplyService.AcquireForkApplyLockAsync(fixture.Fork, CancellationToken.None);
        using var aliasOwnership = WorkspaceForkApplyService.TryAcquireForkApplyLock(alias);
        Assert.IsNull(aliasOwnership, "Physical aliases must use the same existing fork ownership key.");
        WorkspaceForkApplyService.SweepExpiredForks(aliases, 1, DateTimeOffset.UtcNow, WorkspaceManager.TryReserveForkStorageDeletion);
        Assert.IsTrue(Directory.Exists(alias), "Expiry must not delete an alias while its physical fork is owned.");
        await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, new PreviewStore(), null!, null!, DotnetCommandRunner,
            Helpers.ForkStorageFixtureBoundary.ValidateAsync, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
        var cleanup = typeof(WorkspaceForkApplyService).GetMethod("CleanupFork", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var warnings = new List<string>();
        cleanup.Invoke(service, [null, alias, warnings, CancellationToken.None]);
        Assert.AreEqual(1, warnings.Count);
        Assert.IsTrue(Directory.Exists(alias));
        held.Dispose();
        WorkspaceForkApplyService.SweepExpiredForks(aliases, 1, DateTimeOffset.UtcNow, WorkspaceManager.TryReserveForkStorageDeletion);
        Assert.IsTrue(Directory.Exists(alias), "Expiry must not follow a reparse-point victim, even after native ownership is released.");
        Assert.IsTrue(Directory.Exists(fixture.Fork));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetainedFork_MissingConfiguration_RefusesPublicReopen(bool owned)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        if (owned)
        {
            ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
            File.Delete(fixture.PathInFork(ForkAdmissionMaterial.DirectoryName + "/config.json"));
        }
        else
        {
            var inert = fixture.PathInFork(ForkAdmissionMaterial.DirectoryName);
            Directory.CreateDirectory(inert);
            File.WriteAllText(Path.Combine(inert, "guard.targets"), "<Project><!-- Ordinary user data, not owned admission material. --></Project>");
        }
        var before = File.ReadAllBytes(project);
        if (owned)
        {
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => WorkspaceManager.LoadAsync(project, timeout.Token));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(project));
            Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(fixture.Fork));
        }
        else
        {
            var loaded = await WorkspaceManager.LoadAsync(project, timeout.Token);
            WorkspaceManager.Close(loaded.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void ProjectImportInstallation_CancellationPreservesOriginalAndRemovesTemporaryFile(int boundary)
    {
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.custombuild");
        fixture.WriteProject(project, "<!-- Preserve original bytes -->");
        var guard = fixture.PathInFork("guard.targets");
        File.WriteAllText(guard, "<Project />");
        var before = File.ReadAllBytes(project);
        using var cancellation = new CancellationTokenSource();
        var checks = 0;
        Assert.ThrowsExactly<OperationCanceledException>(() => ForkProjectImports.Install(project, fixture.Fork, guard, () =>
        {
            if (++checks == boundary) cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        }));
        Assert.AreEqual(boundary, checks);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(project));
        Assert.AreEqual(0, Directory.GetFiles(fixture.Fork, "*.tmp").Length);
        Assert.IsFalse(File.Exists(fixture.ProtectedOutput));
    }

    [TestMethod]
    public async Task RetainedFork_ActualNativeCommandCancellation_StopsObservedChildAndReleasesOwnership()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var fixture = new NativeFixture(false);
        var project = fixture.PathInFork("Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        await fixture.RestoreAsync(project, DotnetCommandRunner, timeout.Token);
        ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [project], timeout.Token);
        var loaded = await WorkspaceManager.LoadAsync(project, timeout.Token);
        var original = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
        var version = WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId);
        var before = File.ReadAllBytes(project);
        var observer = Path.Combine(fixture.Evidence, "native-child.ps1");
        var observed = Path.Combine(fixture.Evidence, "child.pid");
        File.WriteAllText(observer, "param([string]$OutputPath)\n[IO.File]::WriteAllText($OutputPath, [string]$PID)\nStart-Sleep -Seconds 30\n");
        var command = "pwsh -NoProfile -File \"" + observer + "\" -OutputPath \"" + observed + "\"";
        File.WriteAllText(fixture.PathInFork("Directory.Build.targets"), new XElement("Project",
            new XElement("Target", new XAttribute("Name", "CancellationProbe"),
                new XElement("Exec", new XAttribute("Command", command)))).ToString());
        var running = GatedCommandExecutor.ExecuteAsync(loaded.WorkspaceId, project,
            ["msbuild", project, "-t:CancellationProbe", "-nr:false", "-nologo"], TimeSpan.FromSeconds(45), cancellation.Token);
        System.Diagnostics.Process? child = null;
        try
        {
            // The child's WriteAllText creates the file before it writes the PID: wait for the content.
            static bool Published(string path) => File.Exists(path) && new FileInfo(path).Length > 0;
            while (!Published(observed) && !running.IsCompleted) await Task.Delay(10, timeout.Token);
            Assert.IsTrue(Published(observed), "The native command must actually reach its owned child before cancellation.");
            child = System.Diagnostics.Process.GetProcessById(int.Parse(File.ReadAllText(observed), System.Globalization.CultureInfo.InvariantCulture));
            Assert.IsFalse(child.HasExited);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => running);
            await child.WaitForExitAsync(timeout.Token);
            Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(fixture.Fork));
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(project));
            Assert.IsFalse(File.Exists(fixture.ProtectedOutput));
        }
        finally
        {
            cancellation.Cancel();
            try { await running; } catch (OperationCanceledException) { }
            if (child is { HasExited: false })
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None);
            }
            child?.Dispose();
            WorkspaceManager.Close(loaded.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task LegacyNestedFork_ActualPausedCommand_ProtectsOuterOwnerFromExpiry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var fixture = NativeFixture.CreateNestedLegacy();
        var pool = Path.Combine(fixture.Source, ".roslynmcp", "forks");
        var outer = Path.Combine(pool, "20000101T000000000Z-outer");
        var inner = Path.Combine(outer, ".roslynmcp", "forks", "20000101T000000000Z-inner");
        var outerProject = Path.Combine(outer, "Entry.csproj");
        var project = Path.Combine(inner, "Entry.csproj");
        Assert.IsTrue(!OperatingSystem.IsWindows() || inner.Length <= 258, "The positive legacy fixture must support an actual native working directory.");
        fixture.WriteProject(outerProject, string.Empty);
        ForkAdmissionMaterial.Install(fixture.Source, outer, [outerProject], timeout.Token);
        fixture.WriteProject(project, string.Empty);
        var restored = await DotnetCommandRunner.RunAsync(inner, project, ["restore", project, "-nr:false", "--nologo"], timeout.Token);
        Assert.IsTrue(restored.Succeeded, restored.Describe());
        ForkAdmissionMaterial.Install(outer, inner, [project], timeout.Token);
        var loaded = await WorkspaceManager.LoadAsync(project, timeout.Token);
        var entered = Path.Combine(fixture.Evidence, "entered");
        var release = Path.Combine(fixture.Evidence, "release");
        var script = Path.Combine(fixture.Evidence, "pause.ps1");
        File.WriteAllText(script, "param([string]$Entered,[string]$Release)\n[IO.File]::WriteAllText($Entered,'entered')\nwhile (!(Test-Path -LiteralPath $Release)) { Start-Sleep -Milliseconds 20 }\n");
        File.WriteAllText(Path.Combine(inner, "Directory.Build.targets"), new XElement("Project",
            new XElement("Target", new XAttribute("Name", "Pause"), new XElement("Exec", new XAttribute("Command",
                "pwsh -NoProfile -File \"" + script + "\" -Entered \"" + entered + "\" -Release \"" + release + "\"")))).ToString());
        var running = GatedCommandExecutor.ExecuteAsync(loaded.WorkspaceId, project,
            ["msbuild", "Entry.csproj", "-t:Pause", "-nr:false", "-nologo"], TimeSpan.FromSeconds(45), timeout.Token);
        Exception? primaryFailure = null;
        try
        {
            while (!File.Exists(entered) && !running.IsCompleted) await Task.Delay(10, timeout.Token);
            Assert.IsTrue(File.Exists(entered), "The actual owned CLI must enter the paused target.");
            WorkspaceForkApplyService.SweepExpiredForks(pool, 1, DateTimeOffset.UtcNow, WorkspaceManager.TryReserveForkStorageDeletion);
            Assert.IsTrue(Directory.Exists(outer), "The physical enclosing owner must remain during the actual native invocation.");
            Assert.IsTrue(File.Exists(project), "A paused legacy nested invocation must protect its physical enclosing owner from outer-pool TTL deletion.");
        }
        catch (Exception ex) { primaryFailure = ex; throw; }
        finally
        {
            File.WriteAllText(release, "release");
            try
            {
                var result = await running;
                Assert.IsTrue(result.Succeeded, result.Describe());
            }
            catch (Exception completion) when (primaryFailure is not null)
            { throw new AggregateException("The active-owner assertion and native completion both failed.", primaryFailure, completion); }
            finally { WorkspaceManager.Close(loaded.WorkspaceId); }
        }
        WorkspaceForkApplyService.SweepExpiredForks(pool, 1, DateTimeOffset.UtcNow, WorkspaceManager.TryReserveForkStorageDeletion);
        Assert.IsFalse(Directory.Exists(outer), "An inactive completed legacy owner remains eligible for expiry.");
    }

    [TestMethod]
    public async Task FirstFork_UnrepresentableNativeCwd_RefusesBeforeAnyEffects()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var fixture = new NativeFixture(false);
        var sourceRoot = Path.Combine(fixture.Source, new string('p', 225 - fixture.Source.Length - 1));
        var project = Path.Combine(sourceRoot, "Entry.csproj");
        fixture.WriteProject(project, string.Empty);
        var restored = await DotnetCommandRunner.RunAsync(sourceRoot, project, ["restore", "Entry.csproj", "--nologo", "-nr:false"], timeout.Token);
        Assert.IsTrue(restored.Succeeded, restored.Describe());
        var loaded = await WorkspaceManager.LoadAsync(project, timeout.Token);
        var store = new PreviewStore();
        var solution = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
        var version = WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId);
        var status = await WorkspaceManager.GetStatusAsync(loaded.WorkspaceId);
        var token = store.Store(loaded.WorkspaceId, solution, solution, version, "native cwd refusal", false, RoslynMcp.Core.Models.PreviewKind.Unspecified);
        var entry = WorkspaceForkGraphReplayTests.CapturePreviewEntry(store, token);
        var bytes = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService, TestRunnerService, WorkspaceManager, ChangeTracker);
        await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store, validation, TestRunnerService, DotnetCommandRunner,
            Helpers.ForkStorageFixtureBoundary.ValidateAsync, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
        RoslynMcp.Core.Models.WorkspaceForkApplyResultDto? result = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.ApplyAsync(loaded.WorkspaceId, token, "keep", false, null, "first", timeout.Token));
                StringAssert.Contains(refusal.Message, "shorter authorized root");
                Assert.IsFalse(Directory.Exists(Path.Combine(sourceRoot, ".roslynmcp")), "Refusal must precede even pool creation or expiry.");
            }
            else
            {
                result = await service.ApplyAsync(loaded.WorkspaceId, token, "keep", false, null, "first", timeout.Token);
                Assert.IsTrue(result.Success, "The Windows native cwd preflight must not restrict Unix forks.");
                Assert.AreEqual(Path.Combine(sourceRoot, ".roslynmcp", "forks"), Path.GetDirectoryName(result.ForkPath));
            }
            Assert.AreSame(solution, WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId));
            Assert.AreEqual(status.SnapshotToken, (await WorkspaceManager.GetStatusAsync(loaded.WorkspaceId)).SnapshotToken);
            Assert.AreEqual(entry, WorkspaceForkGraphReplayTests.CapturePreviewEntry(store, token));
            foreach (var (path, expected) in bytes) CollectionAssert.AreEqual(expected, File.ReadAllBytes(path), path);
        }
        finally
        {
            if (result?.ForkWorkspaceId is { } id) WorkspaceManager.Close(id);
            WorkspaceManager.Close(loaded.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LegacyNestedAdmission_PartialOwnershipWait_ReleasesEveryAcquiredAncestor(bool deadline)
    {
        using var fixture = new NativeFixture(false);
        var outer = Path.Combine(fixture.Source, ".roslynmcp", "forks", "20000101T000000000Z-outer");
        var inner = Path.Combine(outer, ".roslynmcp", "forks", "20000101T000000000Z-inner");
        var outerProject = Path.Combine(outer, "Entry.csproj");
        var project = Path.Combine(inner, "Entry.csproj");
        fixture.WriteProject(outerProject, string.Empty);
        ForkAdmissionMaterial.Install(fixture.Source, outer, [outerProject], CancellationToken.None);
        fixture.WriteProject(project, string.Empty);
        var config = ForkAdmissionMaterial.Install(outer, inner, [project], CancellationToken.None);
        using var blocked = await WorkspaceForkApplyService.AcquireForkApplyLockAsync(inner, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        var waitDeadline = deadline ? clock.GetUtcNow().AddSeconds(1) : (DateTimeOffset?)null;
        var pending = ForkAdmissionExecution.CreateAsync(config, waitDeadline, clock, cancellation.Token).AsTask();
        Assert.IsFalse(pending.IsCompleted);
        Assert.IsTrue(WorkspaceForkApplyService.HasForkApplyLock(outer), "The outer owner must be acquired before waiting for the inner owner.");
        Assert.IsNull(WorkspaceForkApplyService.TryAcquireForkApplyLock(outer));
        if (deadline)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => pending);
        }
        else
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        }
        Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(outer));
        Assert.IsFalse(Directory.Exists(Path.Combine(inner, ForkAdmissionMaterial.DirectoryName, "executions")));
    }

    [TestMethod]
    public async Task LegacyNestedAdmission_UncertainDescendant_PreventsEnclosingExpiry()
    {
        using var fixture = new NativeFixture(false);
        var pool = Path.Combine(fixture.Source, ".roslynmcp", "forks");
        var outer = Path.Combine(pool, "20000101T000000000Z-outer");
        var inner = Path.Combine(outer, ".roslynmcp", "forks", "20000101T000000000Z-inner");
        var outerProject = Path.Combine(outer, "Entry.csproj");
        var project = Path.Combine(inner, "Entry.csproj");
        fixture.WriteProject(outerProject, string.Empty);
        ForkAdmissionMaterial.Install(fixture.Source, outer, [outerProject], CancellationToken.None);
        fixture.WriteProject(project, string.Empty);
        var config = ForkAdmissionMaterial.Install(outer, inner, [project], CancellationToken.None);
        using var uncertain = await ForkAdmissionExecution.CreateAsync(config, null, TimeProvider.System, CancellationToken.None);
        uncertain.Dispose();
        Assert.IsNotNull(uncertain.CleanupWarning);
        Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(outer));
        Assert.IsFalse(WorkspaceForkApplyService.HasForkApplyLock(inner));
        WorkspaceForkApplyService.SweepExpiredForks(pool, 1, DateTimeOffset.UtcNow, WorkspaceManager.TryReserveForkStorageDeletion);
        Assert.IsTrue(Directory.Exists(uncertain.Root), "Enclosing expiry must preserve uncertain nested admission material after ancestor locks are released.");
        // No native process starts in this control; the fixture owns the residue.
        Directory.Delete(uncertain.Root, recursive: true);
        WorkspaceForkApplyService.SweepExpiredForks(pool, 1, DateTimeOffset.UtcNow, WorkspaceManager.TryReserveForkStorageDeletion);
        Assert.IsFalse(Directory.Exists(outer));
    }

    [TestMethod]
    [DataRow("inert")]
    [DataRow("malformed")]
    [DataRow("missing-config")]
    [DataRow("subworkspace")]
    [DataRow("nonstandard")]
    [DataRow("linked")]
    public void EnclosingExpiry_DistinguishesVerifiedOrClaimedOwnedDescendantsFromInertNames(string shape)
    {
        using var fixture = new NativeFixture(false);
        var pool = Path.Combine(fixture.Source, ".roslynmcp", "forks");
        var outer = Path.Combine(pool, "20000101T000000000Z-outer");
        var outerProject = Path.Combine(outer, "Entry.csproj");
        fixture.WriteProject(outerProject, string.Empty);
        ForkAdmissionMaterial.Install(fixture.Source, outer, [outerProject], CancellationToken.None);
        var nested = shape == "nonstandard" ? Path.Combine(outer, "retained-data")
            : Path.Combine(outer, "loaded-subworkspace", ".roslynmcp", "forks", "20000101T000000000Z-inner");
        var project = Path.Combine(nested, "Entry.csproj");
        var ownedProject = shape == "linked" ? fixture.PathInFork("Entry.csproj") : project;
        fixture.WriteProject(ownedProject, string.Empty);
        var material = Path.Combine(nested, ForkAdmissionMaterial.DirectoryName);
        if (shape == "linked")
        {
            ForkAdmissionMaterial.Install(fixture.Source, fixture.Fork, [ownedProject], CancellationToken.None);
            Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
            WorkspaceForkGraphReplayTests.CreateLink(nested, fixture.Fork);
        }
        else if (shape != "inert") ForkAdmissionMaterial.Install(outer, nested, [project], CancellationToken.None);
        var residue = Path.Combine(material, "executions", "uncertain");
        Directory.CreateDirectory(residue);
        if (shape == "inert") File.WriteAllText(Path.Combine(material, "guard.targets"), "<Project><!-- authored fixture --></Project>");
        if (shape == "malformed") File.WriteAllText(Path.Combine(material, "config.json"), "malformed claimed ownership");
        if (shape == "missing-config") File.Delete(Path.Combine(material, "config.json"));
        var logger = new ListLogger<WorkspaceForkApplyService>();
        var targetBytes = File.ReadAllBytes(ownedProject);
        if (shape == "linked") File.SetAttributes(ownedProject, File.GetAttributes(ownedProject) | FileAttributes.ReadOnly);
        var targetAttributes = File.GetAttributes(ownedProject);
        WorkspaceForkApplyService.SweepExpiredForks(pool, 1, DateTimeOffset.UtcNow, WorkspaceManager.TryReserveForkStorageDeletion, logger);
        foreach (var entry in logger.Entries)
            TestContext.WriteLine($"{entry.Message}: {entry.Exception?.GetType().FullName} HResult={entry.Exception?.HResult} {entry.Exception}");
        if (shape == "linked")
        {
            Assert.IsTrue(File.Exists(ownedProject), "Expiry must not follow a link to independent owned material.");
            CollectionAssert.AreEqual(targetBytes, File.ReadAllBytes(ownedProject));
            Assert.AreEqual(targetAttributes, File.GetAttributes(ownedProject), "Expiry must preserve independent link-target attributes.");
        }
        Assert.AreEqual(shape is not ("inert" or "linked"), Directory.Exists(outer), "Only physically contained verified or unverifiable claimed owned material can retain an enclosing fork.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SharedCleanup_DeletesOnlyLinkedEntryAndOwnedReadonlyTree(bool linkedRoot)
    {
        using var fixture = new NativeFixture(false);
        var target = fixture.PathInFork("target.txt");
        File.WriteAllText(target, "Independent target bytes");
        File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
        var attributes = File.GetAttributes(target);
        var owned = Path.Combine(fixture.Source, "cleanup");
        if (linkedRoot) WorkspaceForkGraphReplayTests.CreateLink(owned, fixture.Fork);
        else
        {
            var deep = owned;
            for (var index = 0; index < 128; index++) deep = Path.Combine(deep, "d");
            Directory.CreateDirectory(deep);
            var file = Path.Combine(deep, "owned.txt");
            File.WriteAllText(file, "Owned bytes");
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            WorkspaceForkGraphReplayTests.CreateLink(Path.Combine(deep, "linked"), fixture.Fork);
        }
        WorkspaceForkApplyService.DeleteDirectoryIfExists(owned, CancellationToken.None);
        Assert.IsFalse(Directory.Exists(owned));
        Assert.AreEqual("Independent target bytes", File.ReadAllText(target));
        Assert.AreEqual(attributes, File.GetAttributes(target));
    }

    [TestMethod]
    public async Task LegacyNestedFork_ReopenAndFork_UsesOutermostPoolAndImmediateSource()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var fixture = NativeFixture.CreateNestedLegacy();
        var pool = Path.Combine(fixture.Source, ".roslynmcp", "forks");
        var outer = Path.Combine(pool, "20000101T000000000Z-outer");
        var inner = Path.Combine(outer, ".roslynmcp", "forks", "20000101T000000000Z-inner");
        var outerProject = Path.Combine(outer, "Entry.csproj");
        var project = Path.Combine(inner, "Entry.csproj");
        Assert.IsTrue(!OperatingSystem.IsWindows() || inner.Length <= 258, "The positive legacy fixture must support an actual native working directory.");
        fixture.WriteProject(outerProject, string.Empty);
        ForkAdmissionMaterial.Install(fixture.Source, outer, [outerProject], timeout.Token);
        fixture.WriteProject(project, string.Empty);
        var restored = await DotnetCommandRunner.RunAsync(inner, project, ["restore", "Entry.csproj", "-nr:false", "--nologo"], timeout.Token);
        Assert.IsTrue(restored.Succeeded, restored.Describe());
        ForkAdmissionMaterial.Install(outer, inner, [project], timeout.Token);
        var loaded = await WorkspaceManager.LoadAsync(project, timeout.Token);
        var store = new PreviewStore();
        var solution = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
        var token = store.Store(loaded.WorkspaceId, solution, solution, WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId), "legacy nested", false, RoslynMcp.Core.Models.PreviewKind.Unspecified);
        var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService, TestRunnerService, WorkspaceManager, ChangeTracker);
        await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store, validation, TestRunnerService, DotnetCommandRunner,
            Helpers.ForkStorageFixtureBoundary.ValidateAsync, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
        RoslynMcp.Core.Models.WorkspaceForkApplyResultDto? result = null;
        try
        {
            result = await service.ApplyAsync(loaded.WorkspaceId, token, "keep", false, null, "next", timeout.Token);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(pool, Path.GetDirectoryName(result.ForkPath));
            Assert.AreEqual(inner, ForkAdmissionMaterial.Find(Path.Combine(result.ForkPath, "Entry.csproj"))!.SourceRoot);
            Assert.IsTrue(File.Exists(project), "The source-owned ancestor must survive its own pool sweep.");
            Assert.AreSame(solution, WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId));
        }
        finally
        {
            if (result?.ForkWorkspaceId is { } id) WorkspaceManager.Close(id);
            WorkspaceManager.Close(loaded.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow(258)]
    [DataRow(259)]
    public void NativeRestoreDirectory_ExactWindowsBoundary(int length)
    {
        var root = Path.GetPathRoot(TestTempRoot.Current)!;
        var path = Path.Combine(root, new string('p', length - root.Length));
        Assert.AreEqual(length, path.Length);
        if (OperatingSystem.IsWindows() && length == 259)
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => WorkspaceForkApplyService.ValidateNativeRestoreDirectory(path));
        else WorkspaceForkApplyService.ValidateNativeRestoreDirectory(path);
    }
}
