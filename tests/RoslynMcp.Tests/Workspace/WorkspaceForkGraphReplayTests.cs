using System.Reflection;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceForkGraphReplayTests : IsolatedWorkspaceTestBase
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task UnchangedPreview_NativePackagePreprocessing_PreservesSource(bool sourceOutput, bool designTimeOnly)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var fixture = Path.Combine(TestTempRoot.Current, "fork-native-content-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(fixture, "source");
        var feed = Path.Combine(fixture, "feed");
        var package = Path.Combine(fixture, "package");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(Path.Combine(package, "contentFiles", "any", "net10.0"));
        var solutionPath = Path.Combine(root, "Native.slnx");
        await using var source = new IsolatedWorkspaceScope(root, solutionPath, WorkspaceManager);
        WorkspaceForkApplyResultDto? result = null;
        string? reopenedWorkspaceId = null;
        try
        {
            File.WriteAllText(Path.Combine(package, "Public.Native.Content.nuspec"), """
                <?xml version="1.0"?><package><metadata><id>Public.Native.Content</id><version>1.0.0</version><authors>Test</authors><description>Native preprocessing fixture</description><contentFiles><files include="any/net10.0/probe.txt.pp" buildAction="None" copyToOutput="true" flatten="false" /></contentFiles></metadata></package>
                """);
            File.WriteAllText(Path.Combine(package, "contentFiles", "any", "net10.0", "probe.txt.pp"), "native token $rootnamespace$");
            System.IO.Compression.ZipFile.CreateFromDirectory(package, Path.Combine(feed, "Public.Native.Content.1.0.0.nupkg"));
            File.WriteAllText(solutionPath, "<Solution><Project Path=\"Native.csproj\" /></Solution>");
            var outputName = designTimeOnly ? "design-preprocessed" : "preprocessed";
            var output = Path.Combine(root, outputName);
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
                    new XElement("EnableDefaultItems", "false"), new XElement("RootNamespace", "NativeControl"),
                    new XElement("RestorePackagesPath", Path.Combine(fixture, "packages")), new XElement("RestoreSources", feed),
                    new XElement("NuGetAudit", "false"),
                    new XElement("ContentPreprocessorOutputDirectory", designTimeOnly
                        ? "cli-preprocessed" + Path.DirectorySeparatorChar
                        : (sourceOutput ? output : outputName) + Path.DirectorySeparatorChar),
                    new XElement("ContentPreprocessorOutputDirectory",
                        new XAttribute("Condition", designTimeOnly ? "'$(DesignTimeBuild)' == 'true'" : "false"),
                        (sourceOutput ? output : outputName) + Path.DirectorySeparatorChar)),
                new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "Public.Native.Content"),
                    new XAttribute("Version", "1.0.0"))))).Save(Path.Combine(root, "Native.csproj"));
            var restore = await DotnetCommandRunner.RunAsync(root, solutionPath,
                ["restore", solutionPath, "--nologo", "-nr:false"], timeout.Token);
            TestContext.WriteLine($"SOURCE_RESTORE={restore.Succeeded}\n{restore.StdOut}\n{restore.StdErr}");
            Assert.IsTrue(restore.Succeeded, "Real fixture restore must succeed.");
            await source.LoadAsync(timeout.Token);
            var initialOutput = Directory.GetFiles(output, "probe.txt", SearchOption.AllDirectories).Single();
            Assert.AreEqual("native token NativeControl", File.ReadAllText(initialOutput));
            TestContext.WriteLine("SOURCE_LOAD_NATIVE_OUTPUT=" + initialOutput);
            // Remove only the known owned SDK product. Fork load must produce its own output.
            File.Delete(initialOutput);
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            var status = await WorkspaceManager.GetStatusAsync(source.WorkspaceId);
            var bytes = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
            var store = new PreviewStore();
            var token = store.Store(source.WorkspaceId, original, original, version, "unchanged native package", false, PreviewKind.Unspecified);
            var entry = CapturePreviewEntry(store, token);
            var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
                TestRunnerService, WorkspaceManager, ChangeTracker);
            await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store, validation,
                TestRunnerService, DotnetCommandRunner, Helpers.ForkStorageFixtureBoundary.ValidateAsync, NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
            Exception? refusal = null;
            try
            {
                result = await service.ApplyAsync(source.WorkspaceId, token, "keep", false, null, "native-content", timeout.Token);
                TestContext.WriteLine("PUBLIC_RESULT=" + System.Text.Json.JsonSerializer.Serialize(result));
            }
            catch (InvalidOperationException ex) when (ex is IPublicMessageException)
            {
                refusal = ex;
                TestContext.WriteLine("PUBLIC_REFUSAL=" + ex.Message);
            }
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            Assert.AreEqual(status.SnapshotToken, (await WorkspaceManager.GetStatusAsync(source.WorkspaceId)).SnapshotToken);
            Assert.AreEqual(entry, CapturePreviewEntry(store, token));
            foreach (var (path, expected) in bytes) CollectionAssert.AreEqual(expected, File.ReadAllBytes(path), path);
            TestContext.WriteLine("SOURCE_PREVIEW_AUTHORITY=unchanged");
            var sourceProducts = Directory.GetFiles(output, "probe.txt", SearchOption.AllDirectories);
            TestContext.WriteLine("PUBLIC_SOURCE_NATIVE_PRODUCTS=" + System.Text.Json.JsonSerializer.Serialize(
                sourceProducts.Select(p => new { Path = p, Text = File.ReadAllText(p) })));
            if (sourceOutput)
            {
                Assert.AreEqual(0, sourceProducts.Length, "Public fork apply recreated the source's real native package preprocessing output.");
                Assert.IsInstanceOfType<IPublicMessageException>(refusal);
            }
            else
            {
                Assert.IsNull(refusal);
                Assert.IsNotNull(result);
                Assert.IsTrue(result.Success, System.Text.Json.JsonSerializer.Serialize(result.Validation));
                Assert.IsTrue(result.Retained);
                Assert.IsNotNull(result.ForkWorkspaceId);
                Assert.AreEqual(0, sourceProducts.Length);
                var forkProduct = Directory.GetFiles(Path.Combine(result.ForkPath, outputName), "probe.txt", SearchOption.AllDirectories).Single();
                Assert.AreEqual("native token NativeControl", File.ReadAllText(forkProduct));
                TestContext.WriteLine("PUBLIC_FORK_NATIVE_OUTPUT=" + forkProduct);
                WorkspaceManager.Close(result.ForkWorkspaceId);
                File.Delete(forkProduct);
                var reopened = await WorkspaceManager.LoadAsync(Path.Combine(result.ForkPath, "Native.slnx"), timeout.Token);
                reopenedWorkspaceId = reopened.WorkspaceId;
                Assert.AreEqual("native token NativeControl", File.ReadAllText(forkProduct), "Reopened retained fork must execute guarded native preprocessing again.");
                Assert.AreEqual(0, Directory.GetDirectories(Path.Combine(result.ForkPath, ".roslynmcp-fork-admission", "executions")).Length);
                var retainedProject = Path.Combine(result.ForkPath, "Native.csproj");
                Exception? laterRefusal = null;
                try
                {
                    await GatedCommandExecutor.ExecuteAsync(reopenedWorkspaceId, retainedProject,
                        ["msbuild", retainedProject, "-nologo", "-nr:false", "-t:ResolvePackageAssets;RunProduceContentAssets",
                            "-p:ContentPreprocessorOutputDirectory=" + output + Path.DirectorySeparatorChar],
                        TimeSpan.FromMinutes(1), timeout.Token);
                }
                catch (InvalidOperationException ex) when (ex is IPublicMessageException) { laterRefusal = ex; }
                Assert.IsInstanceOfType<IPublicMessageException>(laterRefusal, "A later retained-fork native invocation must preserve admission.");
                Assert.AreEqual(0, Directory.GetFiles(output, "probe.txt", SearchOption.AllDirectories).Length);
                Assert.AreEqual(0, Directory.GetDirectories(Path.Combine(result.ForkPath, ".roslynmcp-fork-admission", "executions")).Length);
                Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
                Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
                Assert.AreEqual(status.SnapshotToken, (await WorkspaceManager.GetStatusAsync(source.WorkspaceId)).SnapshotToken);
                Assert.AreEqual(entry, CapturePreviewEntry(store, token));
                foreach (var (path, expected) in bytes) CollectionAssert.AreEqual(expected, File.ReadAllBytes(path), path);
            }
        }
        catch
        {
            var archive = Path.Combine(TestContext.TestResultsDirectory!, "native-content-" + sourceOutput + "-" + designTimeOnly + "-" + Guid.NewGuid().ToString("N") + ".zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(fixture, archive);
            TestContext.AddResultFile(archive);
            throw;
        }
        finally
        {
            if (reopenedWorkspaceId is not null) WorkspaceManager.Close(reopenedWorkspaceId);
            if (result?.ForkWorkspaceId is { } forkId) WorkspaceManager.Close(forkId);
            if (result is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(result.ForkPath);
            source.Dispose();
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixture);
        }
    }

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnchangedPreview_HiddenEvaluatedOutput_RefusesBeforeRunner(bool externalAlias)
    {
        await using var source = CreateIsolatedWorkspaceCopy();
        var external = Path.Combine(TestTempRoot.Current, "fork-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(external);
        try
        {
            var output = Path.Combine(source.RootPath, "escaped", "not-created");
            if (externalAlias)
            {
                var alias = Path.Combine(external, "alias");
                CreateLink(alias, source.RootPath);
                output = Path.Combine(alias, "escaped", "not-created");
                Assert.IsFalse(FileSystemPath.IsStrictDescendant(source.RootPath, output));
                Assert.IsTrue(FileSystemPath.IsStrictDescendant(source.RootPath, PhysicalPathResolver.Resolve(output)));
            }
            var projectPath = source.GetPath("SampleLib", "SampleLib.csproj");
            var project = XDocument.Load(projectPath);
            project.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
                new XAttribute("Include", "Hidden.proj"), new XAttribute("ReferenceOutputAssembly", "false"))));
            project.Save(projectPath);
            File.WriteAllText(source.GetPath("SampleLib", "Hidden.proj"),
                "<Project><Import Project=\"Hidden.props\" /></Project>");
            new XDocument(new XElement("Project", new XElement("PropertyGroup",
                new XElement("IntermediateOutputPath", output)))).Save(source.GetPath("SampleLib", "Hidden.props"));
            await source.LoadAsync();
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            Assert.IsFalse(original.Projects.Any(p => p.FilePath?.EndsWith("Hidden.proj", StringComparison.Ordinal) == true));
            var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            var status = await WorkspaceManager.GetStatusAsync(source.WorkspaceId);
            var bytes = Directory.GetFiles(source.RootPath, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes);
            var store = new PreviewStore();
            var token = store.Store(source.WorkspaceId, original, original, version,
                "unchanged graph", false, PreviewKind.Unspecified);
            var entry = CapturePreviewEntry(store, token);
            var runner = new BoundaryRunner(source.RootPath);
            await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store,
                null!, null!, runner, Helpers.ForkStorageFixtureBoundary.ValidateAsync, NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
            Exception? refusal = null;
            try
            {
                await service.ApplyAsync(source.WorkspaceId, token, "drop-always", true, null, null, CancellationToken.None);
            }
            catch (InvalidOperationException ex)
            {
                refusal = ex;
            }
            Assert.AreEqual(0, runner.Calls,
                "The actual copied hidden project evaluated an output into source and reached the forbidden runner.");
            Assert.IsInstanceOfType<IPublicMessageException>(refusal);
            StringAssert.Contains(refusal!.Message, "source workspace");
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            Assert.AreEqual(status.SnapshotToken, (await WorkspaceManager.GetStatusAsync(source.WorkspaceId)).SnapshotToken);
            Assert.AreEqual(entry, CapturePreviewEntry(store, token));
            foreach (var (path, expected) in bytes)
                CollectionAssert.AreEqual(expected, File.ReadAllBytes(path), path);
            Assert.IsFalse(Directory.Exists(Path.Combine(source.RootPath, "escaped")));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(external);
        }
    }

    [TestMethod]
    public async Task UnchangedPreview_CompilerInput_RefusesBeforeRunner()
    {
        await using var source = CreateIsolatedWorkspaceCopy();
        var sourceConfig = source.GetPath("source.config");
        File.WriteAllText(sourceConfig, "<configuration />");
        var projectPath = source.GetPath("SampleLib", "SampleLib.csproj");
        var project = XDocument.Load(projectPath);
        project.Root!.Add(new XElement("PropertyGroup", new XElement("AppConfig", sourceConfig)));
        project.Save(projectPath);
        await source.LoadAsync();
        var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
        var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
        var status = await WorkspaceManager.GetStatusAsync(source.WorkspaceId);
        var bytes = Directory.GetFiles(source.RootPath, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        var store = new PreviewStore();
        var token = store.Store(source.WorkspaceId, original, original, version, "unchanged", false, PreviewKind.Unspecified);
        var entry = CapturePreviewEntry(store, token);
        var runner = new CompilerInputBoundaryRunner(source.RootPath);
        await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store,
            null!, null!, runner, Helpers.ForkStorageFixtureBoundary.ValidateAsync, NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
        Exception? refusal = null;
        try
        {
            await service.ApplyAsync(source.WorkspaceId, token, "drop-always", true, null, null, CancellationToken.None);
        }
        catch (InvalidOperationException ex) { refusal = ex; }
        Assert.AreEqual(0, runner.Calls, "An evaluated compiler input in source reached the runner.");
        Assert.IsInstanceOfType<IPublicMessageException>(refusal);
        StringAssert.Contains(refusal!.Message, "source workspace");
        Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
        Assert.AreEqual(status.SnapshotToken, (await WorkspaceManager.GetStatusAsync(source.WorkspaceId)).SnapshotToken);
        Assert.AreEqual(entry, CapturePreviewEntry(store, token));
        foreach (var (path, expected) in bytes) CollectionAssert.AreEqual(expected, File.ReadAllBytes(path), path);
    }

    [TestMethod]
    public async Task UnchangedPreview_LinkBeforeParentOutput_RefusesBeforeRunner()
        => await AssertLinkedParentEscapeAsync();

    [TestMethod]
    public async Task UnchangedPreview_ChildCopyConsumedByParent_RefusesBeforeNativeCopy()
    {
        await using var source = CreateIsolatedWorkspaceCopy();
        File.WriteAllText(source.SolutionPath, "<Solution><Project Path=\"Parent.csproj\" /></Solution>");
        File.WriteAllText(source.GetPath("Parent.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutDir>bin/</OutDir><EnableDefaultItems>false</EnableDefaultItems></PropertyGroup>
              <ItemGroup><ProjectReference Include="a/b/c/Child.csproj" /></ItemGroup>
            </Project>
            """);
        Directory.CreateDirectory(source.GetPath("a", "b", "c"));
        File.WriteAllText(source.GetPath("a", "b", "c", "Child.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutDir>bin/</OutDir><EnableDefaultItems>false</EnableDefaultItems></PropertyGroup>
              <ItemGroup><Content Include="input.txt" CopyToOutputDirectory="Always" TargetPath="../../../escape.txt" /></ItemGroup>
            </Project>
            """);
        File.WriteAllText(source.GetPath("a", "b", "c", "input.txt"), "child content");
        await source.LoadAsync();
        var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
        Assert.AreEqual(2, original.ProjectIds.Count);
        var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
        var status = await WorkspaceManager.GetStatusAsync(source.WorkspaceId);
        var bytes = Directory.GetFiles(source.RootPath, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var store = new PreviewStore();
        var token = store.Store(source.WorkspaceId, original, original, version, "unchanged child copy", false, PreviewKind.Unspecified);
        var entry = CapturePreviewEntry(store, token);
        var runner = new ChildConsumerBoundaryRunner(source.RootPath, DotnetCommandRunner);
        var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
            TestRunnerService, WorkspaceManager, ChangeTracker);
        await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store,
            validation, TestRunnerService, runner, Helpers.ForkStorageFixtureBoundary.ValidateAsync, NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
        Exception? refusal = null;
        WorkspaceForkApplyResultDto? fork = null;
        try
        {
            fork = await service.ApplyAsync(source.WorkspaceId, token, "keep", false, null, null, CancellationToken.None);
            Assert.IsTrue(fork.Success);
            Assert.IsNotNull(fork.ForkWorkspaceId);
            Assert.AreEqual(1, runner.Calls);
            Assert.IsTrue(runner.RestoreSucceeded, "Real restore must complete before native-copy admission.");
            var parent = Path.Combine(fork.ForkPath, "Parent.csproj");
            await GatedCommandExecutor.ExecuteAsync(fork.ForkWorkspaceId, parent,
                ["build", parent, "--no-restore", "--nologo", "-nr:false", "-p:UseSharedCompilation=false"],
                TimeSpan.FromMinutes(1), CancellationToken.None);
        }
        catch (InvalidOperationException ex) when (ex is IPublicMessageException) { refusal = ex; }
        finally
        {
            if (fork?.ForkWorkspaceId is { } forkId) WorkspaceManager.Close(forkId);
            if (fork is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(fork.ForkPath);
        }
        Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
        Assert.AreEqual(status.SnapshotToken, (await WorkspaceManager.GetStatusAsync(source.WorkspaceId)).SnapshotToken);
        Assert.AreEqual(entry, CapturePreviewEntry(store, token));
        foreach (var (path, expected) in bytes) CollectionAssert.AreEqual(expected, File.ReadAllBytes(path), path);
        Assert.IsNotNull(runner.ParentDestination, "The independent parent destination oracle must run before restore.");
        Assert.IsFalse(File.Exists(runner.ParentDestination), "The actual parent copy must be refused before writing source.");
        Assert.IsInstanceOfType<IPublicMessageException>(refusal);
    }

    [TestMethod]
    public async Task UnchangedPreview_OriginalExternalProject_RefusesBeforeRunner()
    {
        await using var source = CreateIsolatedWorkspaceCopy();
        await using var dependency = CreateIsolatedWorkspaceCopy();
        var externalProject = dependency.GetPath("SampleLib", "SampleLib.csproj");
        var projectPath = source.GetPath("SampleLib", "SampleLib.csproj");
        var project = XDocument.Load(projectPath);
        project.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", externalProject))));
        project.Save(projectPath);
        await source.LoadAsync();
        var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
        Assert.IsTrue(original.Projects.Any(p => FileSystemPath.Comparer.Equals(p.FilePath, externalProject)),
            "The external project must be part of the real original source workspace.");
        var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
        var store = new PreviewStore();
        var token = store.Store(source.WorkspaceId, original, original, version, "unchanged", false, PreviewKind.Unspecified);
        var runner = new ExternalProjectBoundaryRunner(externalProject);
        await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store,
            null!, null!, runner, Helpers.ForkStorageFixtureBoundary.ValidateAsync, NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
        Exception? refusal = null;
        try
        {
            await service.ApplyAsync(source.WorkspaceId, token, "drop-always", true, null, null, CancellationToken.None);
        }
        catch (InvalidOperationException ex) { refusal = ex; }
        Assert.AreEqual(0, runner.Calls, "The copied graph must not restore an original external source project.");
        Assert.IsInstanceOfType<IPublicMessageException>(refusal);
        StringAssert.Contains(refusal!.Message, "source workspace");
        Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
        Assert.IsNotNull(store.Retrieve(token));
    }

    private async Task AssertLinkedParentEscapeAsync()
    {
        await using var source = CreateIsolatedWorkspaceCopy();
        var external = Path.Combine(TestTempRoot.Current, "fork-parent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(external);
        Directory.CreateDirectory(source.GetPath("child"));
        try
        {
            var alias = Path.Combine(external, "alias");
            CreateLink(alias, source.GetPath("child"));
            var output = Path.Join(alias, "..", "escaped", "not-created");
            Assert.IsFalse(FileSystemPath.IsStrictDescendant(source.RootPath, Path.GetFullPath(output)));
            Assert.IsTrue(FileSystemPath.IsStrictDescendant(source.RootPath, PhysicalPathResolver.Resolve(output)));
            var projectPath = source.GetPath("SampleLib", "SampleLib.csproj");
            var project = XDocument.Load(projectPath);
            project.Root!.Add(new XElement("PropertyGroup", new XElement("IntermediateOutputPath", output)));
            project.Save(projectPath);
            await source.LoadAsync();
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            var store = new PreviewStore();
            var token = store.Store(source.WorkspaceId, original, original, version, "unchanged", false, PreviewKind.Unspecified);
            var runner = new ParentBoundaryRunner(source.RootPath);
            await using var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store,
                null!, null!, runner, Helpers.ForkStorageFixtureBoundary.ValidateAsync, NullLogger<WorkspaceForkApplyService>.Instance, WorkspaceManager.RunWithRetainedForkSourceAsync, WorkspaceManager.TryReserveForkStorageDeletion);
            try
            {
                await service.ApplyAsync(source.WorkspaceId, token, "drop-always", true, null, null, CancellationToken.None);
            }
            catch (InvalidOperationException) { }
            Assert.AreEqual(0, runner.Calls, "A link before parent traversal must be resolved before containment.");
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(external);
        }
    }

    internal static void CreateLink(string path, string target)
    {
        try
        {
            if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(path, target);
            else Directory.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Inconclusive("Directory link creation failed: " + ex.Message);
        }
    }

    internal static RoslynMcp.Roslyn.Services.PreviewStore.PreviewEntry CapturePreviewEntry(IPreviewStore store, string token)
    {
        while (store is Helpers.TrackingPreviewStore tracking) store = tracking.Inner;
        // The public tuple omits creation time, provenance and redemption ceiling.
        // Invoke the store's protected lookup to compare the complete authority without changing it.
        var lookup = typeof(BoundedStore<RoslynMcp.Roslyn.Services.PreviewStore.PreviewEntry>)
            .GetMethod("RetrieveEntry", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Preview entry lookup is missing.");
        return (RoslynMcp.Roslyn.Services.PreviewStore.PreviewEntry)(lookup.Invoke(store, [token])
            ?? throw new AssertFailedException("Preview authority was consumed."));
    }

    private sealed class ChildConsumerBoundaryRunner(string sourceRoot, IDotnetCommandRunner inner) : IDotnetCommandRunner
    {
        internal int Calls { get; private set; }
        internal bool RestoreSucceeded { get; private set; }
        internal string? ParentDestination { get; private set; }

        public async Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls++;
            using var collection = new ProjectCollection();
            var parent = new ProjectInstance(Path.Combine(workingDirectory, "Parent.csproj"), null, null, collection);
            var reference = parent.GetItems("ProjectReference").Single();
            var childPath = Path.GetFullPath(reference.EvaluatedInclude, workingDirectory);
            var child = new ProjectInstance(childPath, null, null, collection);
            Assert.AreEqual(Path.Combine(workingDirectory, "a", "b", "c", "Child.csproj"), child.FullPath);
            var item = child.GetItems("Content").Single();
            Assert.AreEqual("Always", item.GetMetadataValue("CopyToOutputDirectory"));
            Assert.AreEqual("../../../escape.txt", item.GetMetadataValue("TargetPath"));
            var childDestination = PhysicalPathResolver.Resolve(Path.Join(Path.GetDirectoryName(child.FullPath),
                child.GetPropertyValue("OutDir"), item.GetMetadataValue("TargetPath")));
            var parentDestination = PhysicalPathResolver.Resolve(Path.Join(workingDirectory,
                parent.GetPropertyValue("OutDir"), item.GetMetadataValue("TargetPath")));
            Assert.IsTrue(FileSystemPath.IsStrictDescendant(workingDirectory, childDestination));
            Assert.IsFalse(FileSystemPath.IsStrictDescendant(workingDirectory, parentDestination));
            Assert.IsTrue(FileSystemPath.IsStrictDescendant(sourceRoot, parentDestination));
            ParentDestination = parentDestination;
            var result = await inner.RunAsync(workingDirectory, targetPath, arguments, ct);
            Assert.AreEqual("restore", arguments[0]);
            RestoreSucceeded = result.Succeeded;
            return result;
        }
    }

    private sealed class CompilerInputBoundaryRunner(string sourceRoot) : IDotnetCommandRunner
    {
        internal int Calls { get; private set; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls++;
            using var collection = new ProjectCollection();
            var project = new ProjectInstance(Path.Combine(workingDirectory, "SampleLib", "SampleLib.csproj"), null, null, collection);
            Assert.IsTrue(FileSystemPath.IsStrictDescendant(sourceRoot, PhysicalPathResolver.Resolve(project.GetPropertyValue("AppConfig"))));
            throw new InvalidOperationException("Recording boundary stopped native execution.");
        }
    }

    private sealed class BoundaryRunner(string sourceRoot) : IDotnetCommandRunner
    {
        internal int Calls { get; private set; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls++;
            using var collection = new ProjectCollection();
            var hidden = new ProjectInstance(Path.Combine(workingDirectory, "SampleLib", "Hidden.proj"),
                null, null, collection);
            var output = PhysicalPathResolver.Resolve(hidden.GetPropertyValue("IntermediateOutputPath"));
            Assert.IsTrue(FileSystemPath.IsStrictDescendant(sourceRoot, output),
                "The real copied project must evaluate the source output at the old runner boundary.");
            throw new InvalidOperationException("Recording boundary stopped native execution.");
        }
    }

    private sealed class ParentBoundaryRunner(string sourceRoot) : IDotnetCommandRunner
    {
        internal int Calls { get; private set; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls++;
            using var collection = new ProjectCollection();
            var project = new ProjectInstance(Path.Combine(workingDirectory, "SampleLib", "SampleLib.csproj"), null, null, collection);
            Assert.IsTrue(FileSystemPath.IsStrictDescendant(sourceRoot,
                PhysicalPathResolver.Resolve(project.GetPropertyValue("IntermediateOutputPath"))));
            throw new InvalidOperationException("Recording boundary stopped native execution.");
        }
    }

    private sealed class ExternalProjectBoundaryRunner(string externalProject) : IDotnetCommandRunner
    {
        internal int Calls { get; private set; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls++;
            using var collection = new ProjectCollection();
            var project = new ProjectInstance(Path.Combine(workingDirectory, "SampleLib", "SampleLib.csproj"), null, null, collection);
            Assert.IsTrue(project.GetItems("ProjectReference").Any(i => FileSystemPath.Comparer.Equals(i.EvaluatedInclude, externalProject)));
            throw new InvalidOperationException("Recording boundary stopped native execution.");
        }
    }
}
