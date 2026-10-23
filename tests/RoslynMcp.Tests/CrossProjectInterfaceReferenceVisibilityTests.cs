using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CrossProjectInterfaceReferenceVisibilityTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();
    [TestMethod]
    [DataRow("Counter", false)]
    [DataRow("Counter<T>", false)]
    [DataRow("Counter(int seed)", false)]
    [DataRow("Counter", true)]
    public async Task Extraction_PreservesHeaderSeparatorAndRequiredReferenceVisibility(string header, bool aliasesOnly)
    {
        var scope = CreateIsolatedWorkspaceCopy();
        Exception? primary = null;
        try
        {
            AddProjectToCopiedSolution(scope.RootPath, "Contracts", "net10.0");
            var sourcePath = scope.GetPath("SampleLib", "Counter.cs");
            await File.WriteAllTextAsync(sourcePath,
                "namespace SampleLib; public class " + header + " { public int Count() => 1; }");
            var projectPath = scope.GetPath("SampleLib", "SampleLib.csproj");
            if (aliasesOnly)
            {
                var xml = XDocument.Load(projectPath);
                xml.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
                    new XAttribute("Include", "../Contracts/Contracts.csproj"),
                    new XElement("Aliases", "ContractAlias"), new XElement("EmbedInteropTypes", "false"))));
                await File.WriteAllTextAsync(projectPath, xml.ToString());
            }
            await RestoreCopiedSolutionAsync(scope.SolutionPath, new Dictionary<string, string>(), CancellationToken.None);
            await scope.LoadAsync();
            var original = WorkspaceManager.GetCurrentSolution(scope.WorkspaceId);
            var target = original.Projects.Single(project => project.Name == "Contracts");
            if (aliasesOnly)
            {
                var edge = original.Projects.Single(project => project.Name == "SampleLib").ProjectReferences
                    .Single(reference => reference.ProjectId == target.Id);
                CollectionAssert.AreEqual(new[] { "ContractAlias" }, edge.Aliases.ToArray());
            }
            var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(scope.WorkspaceId,
                sourcePath, "Counter", "ICounter", "Contracts", CancellationToken.None);
            var entry = PreviewStore.Retrieve(preview.PreviewToken)
                ?? throw new AssertFailedException("The extraction preview must be retained.");
            var library = entry.ModifiedSolution.Projects.Single(project => project.Name == "SampleLib");
            var text = await library.Documents.Single(document => document.FilePath == sourcePath).GetTextAsync();
            StringAssert.Contains(text.ToString(), "class " + header + " : global::Contracts.ICounter");
            var reference = library.ProjectReferences.Single(edge => edge.ProjectId == target.Id);
            if (aliasesOnly)
            {
                CollectionAssert.AreEqual(new[] { "ContractAlias", "global" }, reference.Aliases.ToArray());
                Assert.IsFalse(reference.EmbedInteropTypes);
                Assert.IsTrue(preview.Changes.Any(change => change.FilePath == projectPath),
                    "The visibility metadata update must be included in the reviewed project delta.");
            }
            var compilation = await library.GetCompilationAsync();
            var errors = compilation!.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, string.Join("; ", errors.Select(error => error.ToString())));
            var concrete = compilation.GetTypeByMetadataName(header.Contains('<') ? "SampleLib.Counter`1" : "SampleLib.Counter")!;
            var expected = compilation.GetTypeByMetadataName("Contracts.ICounter");
            Assert.IsNotNull(expected);
            Assert.IsTrue(SymbolEqualityComparer.Default.Equals(expected, concrete.Interfaces.Single()));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Header and reference visibility fixture cleanup failed.", primary,
                () => scope.DisposeAsync());
        }
    }
}
