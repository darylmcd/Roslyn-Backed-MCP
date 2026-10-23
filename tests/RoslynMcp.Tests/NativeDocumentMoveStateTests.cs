using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeDocumentMoveStateTests
{
    [TestMethod]
    [DataRow(".cs", false, false)]
    [DataRow(".cs", true, false)]
    [DataRow(".cs", true, true)]
    [DataRow(".csx", false, false)]
    public async Task NativeMovePreview_PreservesSourceKindEncodingAndRenderedPreamble(string extension, bool updateNamespace, bool fileScoped)
    {
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        FileWatcherService? watcher = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.DocumentState", "1.0.0",
                new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var sourcePath = Path.Combine(fixture.Root, "Reviewed" + extension);
            var targetPath = Path.Combine(fixture.Root, "Moved", "Reviewed" + extension);
            var xml = XDocument.Load(project);
            xml.Root!.Add(new XElement("PropertyGroup", new XElement("EnableDefaultCompileItems", "false"),
                new XElement("RootNamespace", "KindEncodingOwner")));
            xml.Root.Add(new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", Path.GetFileName(sourcePath)))));
            await File.WriteAllTextAsync(project, xml.ToString());
            var originalText = fileScoped ? "namespace Before; public class Reviewed { }"
                : "namespace Before { public class Reviewed { } }";
            await File.WriteAllTextAsync(sourcePath, originalText, Encoding.Unicode);
            var before = await File.ReadAllBytesAsync(sourcePath);
            CollectionAssert.AreEqual(Encoding.Unicode.GetPreamble(), before[..Encoding.Unicode.GetPreamble().Length]);
            var previews = new PreviewStore();
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy()),
                NullLogger<WorkspaceManager>.Instance, previews, watcher,
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
            var status = await manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None);
            var original = manager.GetCurrentSolution(status.WorkspaceId);
            var source = original.Projects.Single().Documents.Single(document => document.FilePath == sourcePath);
            var sourceText = await source.GetTextAsync();
            Console.WriteLine("NATIVE-DOCUMENT-KIND extension=" + extension + ";kind=" + source.SourceCodeKind);
            Console.WriteLine("NATIVE-DOCUMENT-AUTHORITY availability=" + NativeCompilerContextCertificate.Require(original).Availability);
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, NativeCompilerContextCertificate.Require(original).Availability);
            Assert.IsNotNull(sourceText.Encoding);
            Assert.AreEqual(Encoding.Unicode.CodePage, sourceText.Encoding.CodePage);
            Assert.AreEqual("KindEncodingOwner", source.Project.DefaultNamespace);
            if (extension == ".cs") Assert.AreEqual(SourceCodeKind.Regular, source.SourceCodeKind);
            var service = new FileOperationService(manager, previews, NullLogger<FileOperationService>.Instance);

            var preview = await service.PreviewMoveFileAsync(status.WorkspaceId,
                new MoveFileDto(sourcePath, targetPath, null, updateNamespace), CancellationToken.None);

            var entry = previews.Retrieve(preview.PreviewToken);
            Assert.IsNotNull(entry);
            var modified = entry.Value.ModifiedSolution;
            var moved = modified.Projects.Single().Documents.Single(document => document.FilePath == targetPath);
            var movedText = await moved.GetTextAsync();
            Assert.AreEqual(source.SourceCodeKind, moved.SourceCodeKind);
            Assert.IsNotNull(movedText.Encoding);
            Assert.AreEqual(sourceText.Encoding.CodePage, movedText.Encoding.CodePage);
            Assert.AreEqual(sourceText.ChecksumAlgorithm, movedText.ChecksumAlgorithm);
            if (updateNamespace) StringAssert.Contains(movedText.ToString(), "namespace KindEncodingOwner.Moved");
            else Assert.AreEqual(originalText, movedText.ToString());
            var mutations = await SolutionDiffHelper.CreateMutationPlanAsync(original, modified,
                SolutionDiffReadContext.ForWorkspace(manager, status.WorkspaceId), true, CancellationToken.None);
            var destination = mutations.Single(mutation => FileSystemPath.Comparer.Equals(mutation.Path, targetPath));
            Assert.IsNotNull(destination.After);
            CollectionAssert.AreEqual(Encoding.Unicode.GetPreamble(), destination.After[..Encoding.Unicode.GetPreamble().Length]);
            Assert.AreEqual(movedText.ToString(), Encoding.Unicode.GetString(destination.After.AsSpan(Encoding.Unicode.GetPreamble().Length)));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(sourcePath));
            Assert.IsFalse(File.Exists(targetPath), "The actual preview and physical mutation planner must not publish file writes.");
            Assert.AreSame(original, manager.GetCurrentSolution(status.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Failed to release native document move state fixture.", primary,
                manager, manager is null ? watcher : null, fixture);
        }
    }
}
