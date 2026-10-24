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
            var entry = store.Retrieve(token);
            var runner = new BoundaryRunner(source.RootPath);
            var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store,
                null!, null!, runner, NullLogger<WorkspaceForkApplyService>.Instance);
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
            Assert.AreEqual(entry, store.Retrieve(token));
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
    public async Task UnchangedPreview_LinkBeforeParentOutput_RefusesBeforeRunner()
        => await AssertLinkedParentEscapeAsync();

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
        var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store,
            null!, null!, runner, NullLogger<WorkspaceForkApplyService>.Instance);
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
            var service = new WorkspaceForkApplyService(WorkspaceManager, WorkspaceExecutionGate, store,
                null!, null!, runner, NullLogger<WorkspaceForkApplyService>.Instance);
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
