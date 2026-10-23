using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeReferenceDiagnosticTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    public async Task RepeatedPhysicalDocuments_RequireExactMultiplicity(int actualCount)
    {
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            workspace = new AdhocWorkspace();
            var root = Path.Combine(TestTempRoot.Current, nameof(NativeReferenceDiagnosticTests), "multiplicity");
            var owner = ProjectId.CreateNewId();
            var empty = workspace.CurrentSolution.AddProject(owner, "Owner", "Owner", LanguageNames.CSharp)
                .WithProjectFilePath(owner, Path.Combine(root, "Owner.csproj"));
            var expected = AddRepeated(empty, 2, "class Input { }");
            await SolutionFileMutationPlanner.AssertEquivalentAsync(expected, expected, static path => path, static path => path,
                true, new HashSet<string>(), CancellationToken.None);
            var actual = AddRepeated(empty, actualCount, "class Input { }");
            var failure = await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => SolutionFileMutationPlanner.AssertEquivalentAsync(
                expected, actual, static path => path, static path => path, true, new HashSet<string>(), CancellationToken.None));
            Assert.AreEqual("The evaluated typed document multiplicity differs from the reviewed graph.", failure.Message);

            Solution AddRepeated(Solution solution, int count, string text)
            {
                for (var index = 0; index < count; index++)
                    solution = solution.AddDocument(DocumentId.CreateNewId(owner), "Input.cs", SourceText.From(text),
                        filePath: Path.Combine(root, "Input.cs"));
                return solution;
            }
        }
        catch (Exception exception) { primary = exception; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native reference diagnostic fixture cleanup failed.", primary, workspace); }
    }

    [TestMethod]
    public async Task RepeatedMappedDocuments_PreserveEveryOccurrenceTextAndOrder()
    {
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            workspace = new AdhocWorkspace();
            var root = Path.Combine(TestTempRoot.Current, nameof(NativeReferenceDiagnosticTests), "occurrences");
            var privateRoot = Path.Combine(root, "private");
            var owner = ProjectId.CreateNewId();
            var first = DocumentId.CreateNewId(owner);
            var second = DocumentId.CreateNewId(owner);
            var expected = workspace.CurrentSolution.AddProject(owner, "Owner", "Owner", LanguageNames.CSharp)
                .WithProjectFilePath(owner, Path.Combine(root, "Owner.csproj"))
                .AddDocument(first, "Input.cs", SourceText.From("class First { }"), filePath: Path.Combine(root, "Input.cs"))
                .AddDocument(second, "Input.cs", SourceText.From("class Second { }"), filePath: Path.Combine(root, "Input.cs"));
            var actual = expected.WithProjectFilePath(owner, Path.Combine(privateRoot, "Owner.csproj"))
                .WithDocumentFilePath(first, Path.Combine(privateRoot, "Input.cs"))
                .WithDocumentFilePath(second, Path.Combine(privateRoot, "Input.cs"));
            string Map(string path) => Path.Combine(privateRoot, Path.GetRelativePath(root, path));
            await SolutionFileMutationPlanner.AssertEquivalentAsync(expected, actual, Map, static path => path,
                true, new HashSet<string>(), CancellationToken.None);
            var changedSecond = actual.WithDocumentText(second, SourceText.From("class Changed { }"));
            await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => SolutionFileMutationPlanner.AssertEquivalentAsync(
                expected, changedSecond, Map, static path => path, true, new HashSet<string>(), CancellationToken.None));
            var swappedTexts = actual.WithDocumentText(first, SourceText.From("class Second { }"))
                .WithDocumentText(second, SourceText.From("class First { }"));
            await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => SolutionFileMutationPlanner.AssertEquivalentAsync(
                expected, swappedTexts, Map, static path => path, true, new HashSet<string>(), CancellationToken.None));
        }
        catch (Exception exception) { primary = exception; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native reference diagnostic fixture cleanup failed.", primary, workspace); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MetadataMismatchDiagnostics_RetainWholeFileNamesWithoutParsingOpaqueKeys(bool aliasOnly)
    {
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            using var image = new MemoryStream();
            var compilation = CSharpCompilation.Create("ControlledLibrary", [CSharpSyntaxTree.ParseText("public class Controlled { }")],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.IsTrue(compilation.Emit(image).Success, "The controlled in-memory metadata image must be valid.");
            var root = Path.Combine(TestTempRoot.Current, nameof(NativeReferenceDiagnosticTests));
            var beforePath = Path.Combine(root, "Contract|Old.dll");
            var afterPath = aliasOnly ? beforePath : Path.Combine(root, "Contract|New.dll");
            var before = MetadataReference.CreateFromImage(image.ToArray(),
                new MetadataReferenceProperties(MetadataImageKind.Assembly, aliasOnly ? ["left"] : []), filePath: beforePath);
            var after = MetadataReference.CreateFromImage(image.ToArray(),
                new MetadataReferenceProperties(MetadataImageKind.Assembly, aliasOnly ? ["right"] : []), filePath: afterPath);
            workspace = new AdhocWorkspace();
            var owner = ProjectId.CreateNewId();
            var expected = workspace.CurrentSolution.AddProject(owner, "Owner", "Owner", LanguageNames.CSharp)
                .WithProjectFilePath(owner, Path.Combine(root, "Owner.csproj")).AddMetadataReference(owner, before);
            var actual = expected.WithProjectMetadataReferences(owner, [after]);
            var failure = await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => SolutionFileMutationPlanner.AssertEquivalentAsync(
                expected, actual, static path => path, static path => path, true, new HashSet<string>(), CancellationToken.None));
            Assert.AreEqual(Path.GetFileName(before.FilePath), failure.Data["MissingMetadataFileNames"]);
            Assert.AreEqual(Path.GetFileName(after.FilePath), failure.Data["ExtraMetadataFileNames"]);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native reference diagnostic fixture cleanup failed.", primary, workspace); }
    }

    [TestMethod]
    public async Task ProjectEdgeMismatchDiagnostics_DoNotPublishOpaqueContextIdentities()
    {
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            var root = Path.Combine(TestTempRoot.Current, nameof(NativeReferenceDiagnosticTests));
            workspace = new AdhocWorkspace();
            var owner = ProjectId.CreateNewId();
            var target = ProjectId.CreateNewId();
            var expected = workspace.CurrentSolution.AddProject(owner, "Owner", "Owner", LanguageNames.CSharp)
                .WithProjectFilePath(owner, Path.Combine(root, "Owner.csproj"))
                .AddProject(target, "Target", "Target", LanguageNames.CSharp)
                .WithProjectFilePath(target, Path.Combine(root, "Target.csproj"))
                .AddProjectReference(owner, new ProjectReference(target, ["left"]));
            var actual = expected.WithProjectReferences(owner, [new ProjectReference(target, ["right"])]);
            var failure = await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => SolutionFileMutationPlanner.AssertEquivalentAsync(
                expected, actual, static path => path, static path => path, false, new HashSet<string>(), CancellationToken.None));
            Assert.AreEqual("The evaluated physical project references differ from the reviewed graph.", failure.Message,
                "Encoded native identities can contain authored values and must remain private diagnostic internals.");
        }
        catch (Exception exception) { primary = exception; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native reference diagnostic fixture cleanup failed.", primary, workspace); }
    }

    [TestMethod]
    public async Task ActualIdentityMapping_RejectsMutableReferenceLeftOutsidePrivateGraph()
    {
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            var root = Path.Combine(TestTempRoot.Current, nameof(NativeReferenceDiagnosticTests), "mutable");
            var privateRoot = Path.Combine(root, "private");
            workspace = new AdhocWorkspace();
            var owner = ProjectId.CreateNewId();
            var image = await File.ReadAllBytesAsync(typeof(object).Assembly.Location);
            var sourceReference = MetadataReference.CreateFromImage(image, filePath: Path.Combine(root, "Mutable.dll"));
            var expected = workspace.CurrentSolution.AddProject(owner, "Owner", "Owner", LanguageNames.CSharp)
                .WithProjectFilePath(owner, Path.Combine(root, "Owner.csproj")).AddMetadataReference(owner, sourceReference);
            var actual = expected.WithProjectFilePath(owner, Path.Combine(privateRoot, "Owner.csproj"))
                .WithProjectMetadataReferences(owner, [MetadataReference.CreateFromImage(image, filePath: Path.Combine(privateRoot, "Mutable.dll"))]);
            string MapExpected(string path) => Path.Combine(privateRoot, Path.GetRelativePath(root, path));
            await SolutionFileMutationPlanner.AssertEquivalentAsync(expected, actual, MapExpected, static path => path,
                true, new HashSet<string>(), CancellationToken.None);
            actual = actual.WithProjectMetadataReferences(owner, [sourceReference]);
            await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => SolutionFileMutationPlanner.AssertEquivalentAsync(
                expected, actual, MapExpected, static path => path, true, new HashSet<string>(), CancellationToken.None));
        }
        catch (Exception exception) { primary = exception; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native reference diagnostic fixture cleanup failed.", primary, workspace); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AdmittedPathNormalization_PreservesAliasAndInteropDifferences(bool changeInterop)
    {
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            var root = Path.Combine(TestTempRoot.Current, nameof(NativeReferenceDiagnosticTests), "immutable");
            var originalPath = Path.Combine(root, "Package.dll");
            var privatePath = Path.Combine(root, "private", "Package.dll");
            workspace = new AdhocWorkspace();
            var owner = ProjectId.CreateNewId();
            var image = await File.ReadAllBytesAsync(typeof(object).Assembly.Location);
            var properties = new MetadataReferenceProperties(MetadataImageKind.Assembly, ["original"]);
            var original = MetadataReference.CreateFromImage(image, properties, filePath: originalPath);
            var expected = workspace.CurrentSolution.AddProject(owner, "Owner", "Owner", LanguageNames.CSharp)
                .WithProjectFilePath(owner, Path.Combine(root, "Owner.csproj")).AddMetadataReference(owner, original);
            var actual = expected.WithProjectMetadataReferences(owner, [MetadataReference.CreateFromImage(image, properties, filePath: privatePath)]);
            string MapAdmitted(string path) => path == originalPath ? privatePath : path;
            await SolutionFileMutationPlanner.AssertEquivalentAsync(expected, actual, MapAdmitted, MapAdmitted,
                true, new HashSet<string>(), CancellationToken.None);
            await SolutionFileMutationPlanner.AssertEquivalentAsync(expected, expected, MapAdmitted, MapAdmitted,
                true, new HashSet<string>(), CancellationToken.None);
            var changed = original.WithProperties(new MetadataReferenceProperties(MetadataImageKind.Assembly,
                changeInterop ? ["original"] : ["different"], embedInteropTypes: changeInterop));
            actual = actual.WithProjectMetadataReferences(owner, [changed]);
            var failure = await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => SolutionFileMutationPlanner.AssertEquivalentAsync(
                expected, actual, MapAdmitted, MapAdmitted, true, new HashSet<string>(), CancellationToken.None));
            Assert.AreEqual("The evaluated assembly or analyzer references differ from the reviewed graph.", failure.Message);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native reference diagnostic fixture cleanup failed.", primary, workspace); }
    }
}
