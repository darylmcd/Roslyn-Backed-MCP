using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ExtractAndWireCompositionTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();
    [TestMethod]
    [DataRow("stable")]
    [DataRow("linked-conflict")]
    [DataRow("intermediate-stale")]
    [DataRow("final-stale")]
    public async Task Composition_PreservesLinkedEditsAndSnapshotAuthority(string scenario)
    {
        var scope = CreateIsolatedWorkspaceCopy();
        Exception? primary = null;
        try
        {
            AddProjectToCopiedSolution(scope.RootPath, "Contracts", "net10.0");
            var sourcePath = scope.GetPath("SampleLib", "Counter.cs");
            await File.WriteAllTextAsync(sourcePath,
                "namespace SampleLib; public class Counter { public int Count() => 1; }");
            var linkedPath = scope.GetPath("SampleLib", "Registrations.cs");
            var linkedText = "using SampleLib; namespace Wiring; internal static class Registrations { static void Wire(Services services) { services.AddSingleton<Counter>(); } } internal sealed class Services { public void AddSingleton<T>() {} public void AddSingleton<TService,TImplementation>() where TImplementation : TService {} }";
            await File.WriteAllTextAsync(linkedPath, linkedText);
            var appPath = scope.GetPath("SampleApp", "SampleApp.csproj");
            var appXml = XDocument.Load(appPath);
            appXml.Root!.Add(new XElement("ItemGroup", new XElement("Compile",
                new XAttribute("Include", "../SampleLib/Registrations.cs"), new XElement("Link", "Registrations.cs"))));
            await File.WriteAllTextAsync(appPath, appXml.ToString());
            await RestoreCopiedSolutionAsync(scope.SolutionPath, new Dictionary<string, string>(), CancellationToken.None);
            await scope.LoadAsync(CancellationToken.None);
            var original = WorkspaceManager.GetCurrentSolution(scope.WorkspaceId);
            Assert.AreEqual(2, original.GetDocumentIdsWithFilePath(linkedPath).Length);
            var version = WorkspaceManager.GetCurrentVersion(scope.WorkspaceId);
            var visible = original;
            var visibleVersion = version;
            var manager = new FailClosedWorkspaceManagerStub
            {
                GetCurrentSolutionHandler = _ => visible,
                GetCurrentVersionHandler = _ => visibleVersion,
                AuthorizePathsHandler = (id, paths, ct) =>
                    ((IWorkspacePathAuthority)WorkspaceManager).AuthorizePathsAsync(id, paths, ct)
            };
            var stores = new RecordingCompositeStore();
            string? intermediateToken = null;
            var extractor = new ExtractionCallback(async ct =>
            {
                var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
                    scope.WorkspaceId, sourcePath, "Counter", "ICounter", "Contracts", ct);
                if (scenario is "intermediate-stale" or "linked-conflict")
                {
                    var entry = PreviewStore.Retrieve(preview.PreviewToken)
                        ?? throw new AssertFailedException("Expected the extraction preview.");
                    PreviewStore.Invalidate(preview.PreviewToken);
                    var before = scenario == "intermediate-stale"
                        ? original.WithProjectName(original.ProjectIds[0], "ChangedSnapshot") : original;
                    var after = entry.ModifiedSolution;
                    if (scenario == "linked-conflict")
                    {
                        var linkedId = after.GetDocumentIdsWithFilePath(linkedPath)[1];
                        after = after.WithDocumentText(linkedId, SourceText.From(linkedText + " // conflicting owner"));
                    }
                    var token = PreviewStore.Store(scope.WorkspaceId, before, after, version,
                        preview.Description, preview.Changes, PreviewKind.CrossProjectExtractInterface);
                    preview = preview with { PreviewToken = token };
                }
                intermediateToken = preview.PreviewToken;
                return preview;
            });
            var registrations = new RegistrationCallback(() =>
            {
                if (scenario == "final-stale")
                {
                    visible = original.WithProjectName(original.ProjectIds[0], "AdvancedSnapshot");
                    visibleVersion++;
                }
            });
            var service = new ExtractAndWireOrchestrator(manager, stores, PreviewStore, extractor, registrations);
            if (scenario != "stable")
            {
                if (scenario == "linked-conflict")
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PreviewExtractAndWireInterfaceAsync(
                        scope.WorkspaceId, sourcePath, "Counter", "ICounter", "Contracts", true, CancellationToken.None));
                else
                    await Assert.ThrowsExactlyAsync<StaleWorkspaceTransitionException>(() => service.PreviewExtractAndWireInterfaceAsync(
                        scope.WorkspaceId, sourcePath, "Counter", "ICounter", "Contracts", true, CancellationToken.None));
                Assert.AreEqual(0, stores.Publications, "A refused composition must not publish a token.");
            }
            else
            {
                var preview = await service.PreviewExtractAndWireInterfaceAsync(scope.WorkspaceId, sourcePath,
                    "Counter", "ICounter", "Contracts", true, CancellationToken.None);
                var entry = stores.Retrieve(preview.PreviewToken)
                    ?? throw new AssertFailedException("Expected the composed preview.");
                Assert.AreEqual(1, entry.Mutations.Count(item => item.FilePath == linkedPath));
                StringAssert.Contains(entry.Mutations.Single(item => item.FilePath == linkedPath).UpdatedContent!,
                    "AddSingleton<global::Contracts.ICounter, Counter>");
                Assert.AreEqual(1, preview.Changes.Count(item => item.FilePath == linkedPath));
                foreach (var name in new[] { "SampleLib", "SampleApp" })
                {
                    var project = original.Projects.Single(item => item.Name == name);
                    var mutation = entry.Mutations.Single(item => item.FilePath == project.FilePath);
                    Assert.IsTrue(XDocument.Parse(mutation.UpdatedContent!).Descendants("ProjectReference")
                        .Any(item => ((string?)item.Attribute("Include"))?.Contains("Contracts.csproj", StringComparison.Ordinal) == true));
                }
                Assert.AreEqual(1, stores.Publications);
            }
            Assert.IsNotNull(intermediateToken);
            Assert.IsNull(PreviewStore.Retrieve(intermediateToken), "Intermediate authority must be consumed even on refusal.");
            Assert.AreEqual(linkedText, await File.ReadAllTextAsync(linkedPath), "Preview must not mutate disk.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Composition fixture cleanup failed.", primary,
                () => scope.DisposeAsync());
        }
    }

    private sealed class ExtractionCallback(Func<CancellationToken, Task<RefactoringPreviewDto>> action) : ICrossProjectRefactoringService
    {
        public Task<RefactoringPreviewDto> PreviewExtractInterfaceAsync(string workspaceId, string filePath,
            string typeName, string? interfaceName, string? targetProjectName, CancellationToken ct) => action(ct);
        public Task<RefactoringPreviewDto> PreviewMoveTypeToProjectAsync(string workspaceId, string sourceFilePath,
            string typeName, string targetProjectName, string? targetNamespace, CancellationToken ct, bool preserveNamespace = false) => throw new NotSupportedException();
        public Task<RefactoringPreviewDto> PreviewDependencyInversionAsync(string workspaceId, string filePath,
            string typeName, string? interfaceName, string interfaceProjectName, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RegistrationCallback(Action action) : IDiRegistrationService
    {
        public Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(string workspaceId, string? projectFilter, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            action();
            return Task.FromResult<IReadOnlyList<DiRegistrationDto>>([]);
        }
        public Task<DiRegistrationScanResult> GetDiRegistrationsWithOverridesAsync(string workspaceId,
            string? projectFilter, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingCompositeStore : ICompositePreviewStore
    {
        private readonly CompositePreviewStore _inner = new();
        internal int Publications { get; private set; }
        public string Store(string workspaceId, int workspaceVersion, string description, IReadOnlyList<CompositeFileMutation> mutations)
        {
            Publications++;
            return _inner.Store(workspaceId, workspaceVersion, description, mutations);
        }
        public (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(string token) => _inner.Retrieve(token);
        public void Invalidate(string token) => _inner.Invalidate(token);
        public string? PeekWorkspaceId(string token) => _inner.PeekWorkspaceId(token);
    }
}
