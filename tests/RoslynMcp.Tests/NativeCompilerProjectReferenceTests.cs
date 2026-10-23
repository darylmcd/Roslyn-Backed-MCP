using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeCompilerProjectReferenceTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize] public static void Initialize(TestContext _) => InitializeServices();
    [TestMethod]
    [DataRow("normal")]
    [DataRow("disabled")]
    [DataRow("build-only")]
    [DataRow("aliases")]
    [DataRow("interop")]
    [DataRow("remove")]
    [DataRow("policy-transition")]
    [DataRow("membership")]
    [DataRow("forged-alias")]
    [DataRow("forged-interop")]
    [DataRow("forged-context")]
    [DataRow("initial-proof")]
    [DataRow("initial-proof-authored")]
    public async Task ReviewedNewDependency_RealizesOnlyCertifiedNativeCompilerReferences(string mode)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeCompilerProjectReferenceTests), Guid.NewGuid().ToString("N"));
        var solutionPath = Path.Combine(root, "Graph.slnx");
        var source = CreateWorkspaceScope(root, solutionPath, WorkspaceManager);
        WorkspaceForkApplyResultDto? fork = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"App/App.csproj\" /><Project Path=\"Library/Library.csproj\" /><Project Path=\"Contract/Contract.csproj\" /></Solution>");
            foreach (var name in new[] { "App", "Library", "Contract" })
            {
                var directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                var xml = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("NuGetAudit", "false")));
                if (name == "App")
                {
                    if (mode == "disabled") xml.Element("PropertyGroup")!.Add(new XElement("DisableTransitiveProjectReferences", "true"));
                    if (mode is "aliases" or "interop")
                        xml.Add(new XElement("ItemDefinitionGroup", new XElement("ProjectReference",
                            new XElement("Aliases", "global,reviewed"), new XElement("EmbedInteropTypes", mode == "interop" ? "true" : "false"))));
                    xml.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../Library/Library.csproj"))));
                    if (mode == "initial-proof-authored")
                        xml.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../Contract/Contract.csproj"))));
                    xml.Add(new XElement("ItemGroup", new XElement("AdditionalFiles", new XAttribute("Include", "App.csproj"))));
                }
                if (name == "Library")
                {
                    xml.Add(new XElement("ItemGroup", new XElement("AdditionalFiles", new XAttribute("Include", "Library.csproj"))));
                    if (mode is "remove" or "policy-transition" or "membership" || mode.StartsWith("forged-", StringComparison.Ordinal)
                        || mode.StartsWith("initial-proof", StringComparison.Ordinal))
                        xml.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../Contract/Contract.csproj"))));
                }
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".csproj"), xml.ToString());
                var code = mode == "interop" && name != "App" ? $$"""
                    using System.Runtime.InteropServices;
                    [assembly: Guid("{{(name == "Library" ? "527E4788-D94D-40CC-88CA-CA4F740E32A1" : "A87E82ED-2093-44D2-89C9-3DB4DFE9DA59")}}")]
                    [assembly: ImportedFromTypeLib("{{name}}")]
                    namespace {{name}};
                    [ComImport, Guid("5783904E-959A-4098-A539-765A8F1D3645"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                    public interface I{{name}} { void Invoke(); }
                    """ : "namespace " + name + "; public class Entry { }";
                await File.WriteAllTextAsync(Path.Combine(directory, "Entry.cs"), code);
            }
            await RestoreWorkspaceAsync(source);
            await source.LoadAsync();
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var library = original.Projects.Single(project => project.Name == "Library");
            var contract = original.Projects.Single(project => project.Name == "Contract");
            if (mode.StartsWith("initial-proof", StringComparison.Ordinal))
            {
                var proofOwner = original.Projects.Single(project => project.Name == "App");
                var proofEdge = proofOwner.ProjectReferences.Single(reference => reference.ProjectId == contract.Id);
                var proof = await RestoredCompilerInputAuthority.CaptureAsync(source.WorkspaceId, original,
                    static path => path, new OwnedTestWorkspacePathPolicy(), RestoreArtifactPhase.BeforeRestore, CancellationToken.None);
                var appAssets = new HashSet<string>(FileSystemPath.Comparer) { Path.Combine(root, "App", "obj", "project.assets.json") };
                var retained = proof.WithoutAbsentAssetsImplicitReferences(original, new HashSet<string>(FileSystemPath.Comparer), proof);
                CollectionAssert.AreEquivalent(proofOwner.ProjectReferences.ToArray(), retained.GetProject(proofOwner.Id)!.ProjectReferences.ToArray());
                var unrelated = proof.WithoutAbsentAssetsImplicitReferences(original,
                    new HashSet<string>(FileSystemPath.Comparer) { Path.Combine(root, "Library", "obj", "project.assets.json") }, proof);
                CollectionAssert.AreEquivalent(proofOwner.ProjectReferences.ToArray(), unrelated.GetProject(proofOwner.Id)!.ProjectReferences.ToArray());
                var initial = proof.WithoutAbsentAssetsImplicitReferences(original, appAssets, proof);
                Assert.AreEqual(mode == "initial-proof-authored", initial.GetProject(proofOwner.Id)!.ProjectReferences.Contains(proofEdge),
                    "Only an assets-certified implicit edge without an authored declaration may disappear.");
                Assert.IsTrue(initial.GetProject(proofOwner.Id)!.ProjectReferences.Any(reference => reference.ProjectId == library.Id));
                CollectionAssert.AreEquivalent(library.ProjectReferences.ToArray(), initial.GetProject(library.Id)!.ProjectReferences.ToArray());
                foreach (var changed in new[] { new ProjectReference(contract.Id, ["unreviewed"], proofEdge.EmbedInteropTypes),
                    new ProjectReference(contract.Id, proofEdge.Aliases, !proofEdge.EmbedInteropTypes) })
                {
                    var forged = original.WithProjectReferences(proofOwner.Id, proofOwner.ProjectReferences.Where(reference => reference != proofEdge).Append(changed));
                    Assert.IsTrue(proof.WithoutAbsentAssetsImplicitReferences(forged, appAssets, proof).GetProject(proofOwner.Id)!.ProjectReferences.Contains(changed),
                        "Uncertified metadata must remain visible to the strict graph comparison.");
                }
                var changedContext = original.WithProjectOutputFilePath(contract.Id, Path.Combine(root, "unreviewed", "Contract.dll"));
                Assert.ThrowsExactly<PhysicalGraphMismatchException>(() => proof.WithoutAbsentAssetsImplicitReferences(changedContext, appAssets, proof));
                return;
            }
            if (mode.StartsWith("forged-", StringComparison.Ordinal))
            {
                var appOwner = original.Projects.Single(project => project.Name == "App");
                var implicitEdge = appOwner.ProjectReferences.Single(reference => reference.ProjectId == contract.Id);
                var assetsPath = Path.Combine(root, "App", "obj", "project.assets.json");
                var assetsBefore = await File.ReadAllBytesAsync(assetsPath);
                var projectBefore = await File.ReadAllBytesAsync(appOwner.FilePath!);
                var receipt = await RestoredCompilerInputAuthority.CaptureAsync(source.WorkspaceId, original,
                    static path => path, new OwnedTestWorkspacePathPolicy(), RestoreArtifactPhase.BeforeRestore, CancellationToken.None);
                var referenceProof = receipt.ReferenceAuthority();
                referenceProof.AssertCurrent(appOwner);
                var forged = mode == "forged-context"
                    ? original.WithProjectOutputFilePath(contract.Id, Path.Combine(root, "unreviewed", "Contract.dll"))
                    : original.WithProjectReferences(appOwner.Id, appOwner.ProjectReferences.Where(reference => reference != implicitEdge)
                        .Append(new ProjectReference(contract.Id, mode == "forged-alias" ? ["unreviewed"] : implicitEdge.Aliases,
                            mode == "forged-interop" || implicitEdge.EmbedInteropTypes)));
                var forgedOwner = forged.GetProject(appOwner.Id);
                Assert.IsNotNull(forgedOwner);
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => referenceProof.AssertCurrent(forgedOwner));
                CollectionAssert.AreEqual(assetsBefore, await File.ReadAllBytesAsync(assetsPath));
                CollectionAssert.AreEqual(projectBefore, await File.ReadAllBytesAsync(appOwner.FilePath!));
                return;
            }
            Solution modified;
            if (mode == "membership")
            {
                var appOwner = original.Projects.Single(project => project.Name == "App");
                modified = original.AddAdditionalDocument(DocumentId.CreateNewId(appOwner.Id), "Review.txt",
                    SourceText.From("reviewed membership\n"), filePath: Path.Combine(root, "App", "Review.txt"));
            }
            else
            {
                var owner = mode == "policy-transition" ? original.Projects.Single(project => project.Name == "App") : library;
                var declaration = owner.AdditionalDocuments.Single(document => document.FilePath == owner.FilePath);
                var xmlChange = XDocument.Parse((await declaration.GetTextAsync()).ToString());
                if (mode == "remove") xmlChange.Descendants("ProjectReference").Single().Remove();
                else if (mode == "policy-transition") xmlChange.Root!.Element("PropertyGroup")!.Add(new XElement("DisableTransitiveProjectReferences", "true"));
                else
                {
                    var item = new XElement("ProjectReference", new XAttribute("Include", "../Contract/Contract.csproj"));
                    if (mode == "build-only") item.Add(new XElement("ReferenceOutputAssembly", "false"));
                    xmlChange.Root!.Add(new XElement("ItemGroup", item));
                }
                modified = original.WithAdditionalDocumentText(declaration.Id, SourceText.From(xmlChange.ToString()));
                if (mode == "remove") modified = modified.RemoveProjectReference(library.Id, library.ProjectReferences.Single(reference => reference.ProjectId == contract.Id));
                else if (mode is not "build-only" and not "policy-transition") modified = modified.AddProjectReference(library.Id, new ProjectReference(contract.Id));
            }
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified,
                SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
            var token = PreviewStore.Store(source.WorkspaceId, original, modified, WorkspaceManager.GetCurrentVersion(source.WorkspaceId),
                "reviewed compiler graph", changes, PreviewKind.Unspecified);
            var protectedPaths = new[] { solutionPath, library.FilePath!, Path.Combine(root, "App", "App.csproj"),
                Path.Combine(root, "App", "obj", "project.assets.json") };
            var sourceBytes = new Dictionary<string, byte[]>();
            foreach (var path in protectedPaths) sourceBytes.Add(path, await File.ReadAllBytesAsync(path));
            var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager, WorkspaceExecutionGate, PreviewStore,
                new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService, TestRunnerService, WorkspaceManager, ChangeTracker),
                TestRunnerService, new DotnetCommandRunner(), NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
            fork = await service.ApplyAsync(source.WorkspaceId, token, "keep", false, null, "compiler-graph", CancellationToken.None);
            Assert.IsTrue(fork.Success);
            var actual = WorkspaceManager.GetCurrentSolution(fork.ForkWorkspaceId!);
            var app = actual.Projects.Single(project => project.Name == "App");
            if (mode == "membership")
            {
                Assert.AreEqual("reviewed membership\n", (await app.AdditionalDocuments.Single(document => document.Name == "Review.txt").GetTextAsync()).ToString());
                Assert.IsFalse(File.Exists(Path.Combine(root, "App", "Review.txt")));
            }
            var edge = app.ProjectReferences.SingleOrDefault(reference => actual.GetProject(reference.ProjectId)?.Name == "Contract");
            if (mode is "disabled" or "build-only" or "remove" or "policy-transition") Assert.IsNull(edge);
            else
            {
                Assert.IsNotNull(edge);
                CollectionAssert.AreEqual(mode is "aliases" or "interop" ? new[] { "global", "reviewed" } : Array.Empty<string>(), edge.Aliases.ToArray());
                Assert.AreEqual(mode == "interop", edge.EmbedInteropTypes);
            }
            foreach (var (path, bytes) in sourceBytes) CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
            Assert.IsNotNull(PreviewStore.Retrieve(token));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native compiler graph fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (fork?.ForkWorkspaceId is { } id) WorkspaceManager.Close(id); }),
                CleanupFailureCollector.FromAction(() => { if (fork is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(fork.ForkPath); }),
                () => source.DisposeAsync());
        }
    }
}
