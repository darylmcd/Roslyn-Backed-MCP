using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeCompilerLoadContextTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize] public static void Initialize(TestContext _) => InitializeServices();
    [TestMethod]
    [DataRow("none", false, false)]
    [DataRow("none", true, true)]
    [DataRow("attribute", false, true)]
    [DataRow("element", false, true)]
    [DataRow("matching", false, false)]
    [DataRow("leaf", false, true)]
    [DataRow("analyzer", false, true)]
    [DataRow("missing", false, false)]
    [DataRow("multi", false, false)]
    [DataRow("multi-divergent", false, true)]
    [DataRow("multi-test", false, false)]
    [DataRow("multi-output", false, true)]
    public async Task NativeSelectedContext_DivergenceCannotIssueCompilerAuthority(string referenceMetadata, bool solutionContext, bool mismatch)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeCompilerLoadContextTests), Guid.NewGuid().ToString("N"));
        var solutionPath = Path.Combine(root, "OwnerContext.slnx");
        var source = CreateWorkspaceScope(root, solutionPath, WorkspaceManager);
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), referenceMetadata == "matching"
                ? "<Project><PropertyGroup><OwnerFlavor>selected</OwnerFlavor></PropertyGroup></Project>" : "<Project />");
            await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"App/App.csproj\" /></Solution>");
            foreach (var name in new[] { "App", "Library", "Leaf" })
            {
                var directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                var xml = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("NuGetAudit", "false")));
                if (name == "App")
                {
                    xml.Add(new XElement("PropertyGroup", new XAttribute("Condition", "'$(SolutionDir)' != '' And '$(SolutionDir)' != '*Undefined*'"),
                        new XElement("DefineConstants", "$(DefineConstants);NATIVE_SOLUTION_DIRECTORY")));
                    if (solutionContext) xml.Add(new XElement("PropertyGroup", new XAttribute("Condition", "'$(SolutionName)' == 'OwnerContext'"),
                        new XElement("DefineConstants", "$(DefineConstants);SOLUTION_CONTEXT")));
                    var reference = new XElement("ProjectReference", new XAttribute("Include", "../Library/Library.csproj"));
                    if (referenceMetadata is "attribute" or "analyzer") reference.Add(new XAttribute("AdditionalProperties", "OwnerFlavor=selected"));
                    else if (referenceMetadata is "element" or "matching") reference.Add(new XElement("AdditionalProperties", "OwnerFlavor=selected"));
                    xml.Add(new XElement("ItemGroup", reference));
                    if (referenceMetadata.StartsWith("multi", StringComparison.Ordinal))
                        xml.Add(new XElement("ItemGroup", new XElement("ProjectReference",
                            new XAttribute("Include", "../Leaf/Leaf.csproj"),
                            new XElement("AdditionalProperties", "Route=direct"))));
                }
                if (name == "Library")
                {
                    if (referenceMetadata != "analyzer")
                        xml.Add(new XElement("PropertyGroup", new XAttribute("Condition", "'$(OwnerFlavor)' == 'selected'"),
                            new XElement("DefineConstants", "$(DefineConstants);REFERENCE_CONTEXT")));
                    if (referenceMetadata == "analyzer")
                        xml.Add(new XElement("Target", new XAttribute("Name", "SelectContextAnalyzers"),
                            new XAttribute("BeforeTargets", "CoreCompile"), new XAttribute("Condition", "'$(OwnerFlavor)' == 'selected'"),
                            new XElement("ItemGroup", new XElement("Analyzer", new XAttribute("Remove", "@(Analyzer)")))));
                    if (referenceMetadata == "missing")
                        xml.Add(new XElement("ItemGroup", new XElement("Analyzer", new XAttribute("Include", "MissingAnalyzer.dll"))));
                    var leafReference = new XElement("ProjectReference", new XAttribute("Include", "../Leaf/Leaf.csproj"));
                    if (referenceMetadata == "leaf") leafReference.Add(new XElement("AdditionalProperties", "LeafFlavor=selected"));
                    if (referenceMetadata.StartsWith("multi", StringComparison.Ordinal))
                        leafReference.Add(new XElement("AdditionalProperties", referenceMetadata == "multi-divergent"
                            ? "Route=indirect;LeafFlavor=selected" : "Route=indirect"));
                    xml.Add(new XElement("ItemGroup", leafReference));
                }
                if (name == "Leaf")
                    xml.Add(new XElement("PropertyGroup", new XAttribute("Condition", "'$(LeafFlavor)' == 'selected'"),
                        new XElement("DefineConstants", "$(DefineConstants);LEAF_CONTEXT")));
                if (name == "Leaf" && referenceMetadata is "multi-test" or "multi-output")
                    xml.Add(new XElement("PropertyGroup", new XAttribute("Condition", "'$(Route)' == 'indirect'"),
                        new XElement(referenceMetadata == "multi-test" ? "IsTestProject" : "OutputType", referenceMetadata == "multi-test" ? "true" : "Exe")));
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".csproj"), xml.ToString());
                await File.WriteAllTextAsync(Path.Combine(directory, "Entry.cs"), "namespace " + name + "; public class Entry { public static void Main() { } }");
            }
            await RestoreWorkspaceAsync(source);
            await source.LoadAsync();
            var solution = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            Assert.IsTrue(NativeCompilerContextCertificate.TryGet(solution, out var certificate));
            Assert.IsNotNull(certificate);
            var app = solution.Projects.Single(project => project.Name == "App");
            var library = solution.Projects.Single(project => project.Name == "Library");
            CollectionAssert.Contains(((CSharpParseOptions)app.ParseOptions!).PreprocessorSymbolNames.ToArray(), "NATIVE_SOLUTION_DIRECTORY");
            Assert.IsFalse(((CSharpParseOptions)app.ParseOptions!).PreprocessorSymbolNames.Contains("SOLUTION_CONTEXT"),
                "Static graph solution properties must not replace the actual Roslyn loader context.");
            Assert.AreEqual(referenceMetadata == "matching", ((CSharpParseOptions)library.ParseOptions!).PreprocessorSymbolNames.Contains("REFERENCE_CONTEXT"),
                "Preserve the independently loaded semantic context even when native edge globals differ.");
            var graph = EvaluatedBuildGraph.Source(solution, CancellationToken.None);
            var leaf = solution.Projects.Single(project => project.Name == "Leaf");
            if (referenceMetadata.StartsWith("multi", StringComparison.Ordinal))
            {
                var leafStatus = WorkspaceManager.GetStatus(source.WorkspaceId).Projects.Single(project => project.Name == "Leaf");
                CollectionAssert.Contains(leafStatus.TargetFrameworks.ToArray(), "net10.0");
                Assert.AreEqual(referenceMetadata == "multi-test", leafStatus.IsTestProject);
                Assert.AreEqual(leafStatus.IsTestProject, ProjectMetadataParser.IsTestProject(leaf, CancellationToken.None),
                    "Semantic classifiers must aggregate the same contexts without demanding producer selection.");
                Assert.AreEqual("Library", leafStatus.OutputType);
                var ambiguous = Assert.ThrowsExactly<PublicInvalidOperationException>(() => EvaluatedBuildGraph.Resolve(leaf, graph));
                StringAssert.Contains(ambiguous.Message, "one evaluated build context");
                if (referenceMetadata == "multi-test")
                {
                    var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => TestRunnerService.PrepareTestRunAsync(
                        source.WorkspaceId, null, null, CancellationToken.None));
                    StringAssert.Contains(refusal.Message, "one evaluated build context");
                }
            }
            Assert.IsFalse(((CSharpParseOptions)leaf.ParseOptions!).PreprocessorSymbolNames.Contains("LEAF_CONTEXT"));
            foreach (var project in new[] { app, library, leaf })
            {
                var contexts = graph.Nodes.Where(node => !node.IsOuterBuild && FileSystemPath.Comparer.Equals(node.ProjectPath, project.FilePath)).ToArray();
                Assert.AreEqual(project == leaf && referenceMetadata.StartsWith("multi", StringComparison.Ordinal) ? 2 : 1, contexts.Length,
                    "Every distinct native edge context must be exercised, including contexts with the same output path.");
                if (contexts.Length == 2)
                {
                    Assert.AreEqual(contexts[0].TargetPath, contexts[1].TargetPath);
                    CollectionAssert.AreEquivalent(new[] { "direct", "indirect" }, contexts.Select(node => node.Globals["Route"]).ToArray());
                }
                foreach (var node in contexts)
                {
                    var (native, _) = await NativeCompilerTaskInputs.ReadRawAsync(node.ProjectPath, node.Globals, node.TargetFramework,
                        node.TaskRegistrations.ToolsVersion, node.CompilerReferences.NativeCompileTargets, NativeCompilerTaskInputs.CapturePurpose.FinalCompiler, CancellationToken.None);
                    var actual = CSharpCommandLineParser.Default.Parse(native.Arguments, Path.GetDirectoryName(node.ProjectPath)!, sdkDirectory: null);
                    if (project == library && referenceMetadata == "analyzer")
                    {
                        Assert.IsNotEmpty(project.AnalyzerReferences, "The independently loaded SDK context must retain its actual analyzers.");
                        Assert.IsEmpty(actual.AnalyzerReferences, "The real edge-selected compiler target must remove every analyzer argument.");
                    }
                    if (project == library && referenceMetadata == "missing")
                    {
                        Assert.IsTrue(certificate.NativeSolution.GetProject(project.Id)!.AnalyzerReferences.Any(reference =>
                            reference is AnalyzerFileReference or UnresolvedAnalyzerReference
                            && Path.GetFileName(reference.FullPath) == "MissingAnalyzer.dll"));
                        Assert.IsTrue(actual.AnalyzerReferences.Any(reference => Path.GetFileName(reference.FilePath) == "MissingAnalyzer.dll"));
                    }
                    var expectedSymbol = project == app ? "SOLUTION_CONTEXT" : project == library ? "REFERENCE_CONTEXT" : "LEAF_CONTEXT";
                    var expectedValue = project == app ? solutionContext : project == library
                        ? referenceMetadata is "attribute" or "element" or "matching" : referenceMetadata == "leaf" || referenceMetadata == "multi-divergent" && node.Globals["Route"] == "indirect";
                    Assert.AreEqual(expectedValue,
                        actual.ParseOptions.PreprocessorSymbolNames.Contains(expectedSymbol),
                        "The real native compiler context must prove the fixture premise.");
                }
            }
            Assert.IsTrue(app.ProjectReferences.Any(reference => reference.ProjectId == library.Id));
            Assert.IsTrue(library.ProjectReferences.Any(reference => solution.GetProject(reference.ProjectId)?.Name == "Leaf"));
            if (mismatch)
            {
                Assert.AreEqual(NativeCompilerContextAvailability.SemanticContextMismatch, certificate.Availability,
                    "A graph receipt and a replay against shared workspace globals cannot certify different native semantics.");
                Assert.AreEqual(3, solution.Projects.Count());
                foreach (var original in certificate.NativeSolution.Projects)
                {
                    var preserved = solution.GetProject(original.Id);
                    Assert.IsNotNull(preserved);
                    CollectionAssert.AreEqual(original.DocumentIds.ToArray(), preserved.DocumentIds.ToArray(),
                        "Semantic-only publication must preserve every native authored and SDK-generated document identity.");
                    CollectionAssert.AreEqual(original.Documents.Select(document => document.FilePath).ToArray(),
                        preserved.Documents.Select(document => document.FilePath).ToArray());
                }
                Assert.AreEqual(3, solution.Projects.SelectMany(project => project.Documents)
                    .Count(document => Path.GetFileName(document.FilePath) == "Entry.cs"));
                var compilation = await app.GetCompilationAsync(CancellationToken.None);
                Assert.IsNotNull(compilation);
                Assert.IsFalse(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
                var refusal = Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(solution));
                StringAssert.Contains(refusal.Message, "semantic snapshot differs");
                return;
            }
            Assert.AreEqual(NativeCompilerContextAvailability.Certified, certificate.Availability);
            await certificate.AssertCurrentAsync(solution, source.WorkspaceId, PathPolicy, CancellationToken.None);
            await File.AppendAllTextAsync(Path.Combine(root, "App", "Entry.cs"), "\n// changed after native context publication\n");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                certificate.AssertCurrentAsync(solution, source.WorkspaceId, PathPolicy, CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Frozen compiler owner fixture cleanup failed.", primary,
                () => source.DisposeAsync());
        }
    }
}
