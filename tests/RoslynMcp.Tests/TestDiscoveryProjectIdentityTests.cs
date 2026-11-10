using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// test-related-project-qualified-identity: a discovered test's identity is (owning project,
/// fully-qualified name). Two test projects may legitimately declare the same
/// namespace/type/method, so related-test discovery must keep both, attributed to the right
/// project and file, in both the symbol route and the file route.
/// </summary>
[TestClass]
public sealed class TestDiscoveryProjectIdentityTests
{
    private const string SharedFqn = "Verification.First.Runs";
    private const string TestSource =
        "namespace Verification; class First { [TestMethod] void Runs() { _ = new Product.AlphaProbe(); } }";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FileRoute_KeepsSameFqnTestsFromBothProjects(bool forceInboundFallback)
    {
        using var fixture = new MultiProjectFixture(forceInboundFallback);
        var testA = fixture.AddTestFile("Tests.A", TestSource);
        var testB = fixture.AddTestFile("Tests.B", TestSource);

        var result = await fixture.Service.FindRelatedTestsForFilesAsync("fixture", [fixture.Source], 100, CancellationToken.None);

        Assert.AreEqual(2, result.Tests.Count);
        Assert.AreEqual(2, result.Pagination.Total);
        var byProject = result.Tests.ToDictionary(t => t.ProjectName, StringComparer.Ordinal);
        Assert.AreEqual(testA, byProject["Tests.A"].FilePath);
        Assert.AreEqual(testB, byProject["Tests.B"].FilePath);
        foreach (var test in result.Tests)
        {
            Assert.AreEqual(SharedFqn, test.FullyQualifiedName);
            CollectionAssert.AreEqual(new[] { fixture.Source }, test.TriggeredByFiles.ToArray());
        }
        Assert.IsTrue(result.Diagnostics.HeuristicsAttempted.Contains(forceInboundFallback ? "inbound-reference" : "direct-reference"));
        // `FullyQualifiedName~` filters are project-blind, so the shared FQN is correctly emitted once.
        Assert.AreEqual($"FullyQualifiedName~{SharedFqn}", result.DotnetTestFilter);
    }

    [TestMethod]
    public async Task FileRoute_MaxResultsTruncatesWithoutLosingTotal()
    {
        using var fixture = new MultiProjectFixture(forceInboundFallback: false);
        fixture.AddTestFile("Tests.A", TestSource);
        fixture.AddTestFile("Tests.B", TestSource);

        var result = await fixture.Service.FindRelatedTestsForFilesAsync("fixture", [fixture.Source], 1, CancellationToken.None);

        Assert.AreEqual(1, result.Tests.Count);
        Assert.AreEqual(2, result.Pagination.Total);
        Assert.IsTrue(result.Pagination.HasMore);
    }

    [TestMethod]
    public async Task FileRoute_OnlyReferencingProjectIsAttributed()
    {
        using var fixture = new MultiProjectFixture(forceInboundFallback: false);
        var testA = fixture.AddTestFile("Tests.A", TestSource);
        fixture.AddTestFile("Tests.B", "namespace Verification; class First { [TestMethod] void Runs() { } }");

        var result = await fixture.Service.FindRelatedTestsForFilesAsync("fixture", [fixture.Source], 100, CancellationToken.None);

        var only = result.Tests.Single();
        Assert.AreEqual("Tests.A", only.ProjectName);
        Assert.AreEqual(testA, only.FilePath);
    }

    [TestMethod]
    public async Task SymbolRoute_KeepsSameFqnTestsFromBothProjects()
    {
        using var fixture = new MultiProjectFixture(forceInboundFallback: false);
        var testA = fixture.AddTestFile("Tests.A", TestSource);
        var testB = fixture.AddTestFile("Tests.B", TestSource);

        var result = await fixture.Service.FindRelatedTestsAsync("fixture", SymbolLocator.ByMetadataName("Product.AlphaProbe"), 100, CancellationToken.None);

        Assert.AreEqual(2, result.Tests.Count);
        Assert.AreEqual(2, result.Pagination.Total);
        var byProject = result.Tests.ToDictionary(t => t.ProjectName, StringComparer.Ordinal);
        Assert.AreEqual(testA, byProject["Tests.A"].FilePath);
        Assert.AreEqual(testB, byProject["Tests.B"].FilePath);
        Assert.AreEqual($"FullyQualifiedName~{SharedFqn}", result.DotnetTestFilter);
    }

    [TestMethod]
    public async Task SymbolRoute_MaxResultsTruncatesWithoutLosingTotal()
    {
        using var fixture = new MultiProjectFixture(forceInboundFallback: false);
        fixture.AddTestFile("Tests.A", TestSource);
        fixture.AddTestFile("Tests.B", TestSource);

        var result = await fixture.Service.FindRelatedTestsAsync("fixture", SymbolLocator.ByMetadataName("Product.AlphaProbe"), 1, CancellationToken.None);

        Assert.AreEqual(1, result.Tests.Count);
        Assert.AreEqual(2, result.Pagination.Total);
        Assert.IsTrue(result.Pagination.HasMore);
    }

    [TestMethod]
    public async Task SingleProjectControl_IsUnchanged()
    {
        using var fixture = new MultiProjectFixture(forceInboundFallback: false);
        var testA = fixture.AddTestFile("Tests.A", TestSource);

        var files = await fixture.Service.FindRelatedTestsForFilesAsync("fixture", [fixture.Source], 100, CancellationToken.None);
        var symbol = await fixture.Service.FindRelatedTestsAsync("fixture", SymbolLocator.ByMetadataName("Product.AlphaProbe"), 100, CancellationToken.None);

        foreach (var test in new[] { files.Tests.Single(), symbol.Tests.Single() })
        {
            Assert.AreEqual("Tests.A", test.ProjectName);
            Assert.AreEqual(testA, test.FilePath);
            Assert.AreEqual(SharedFqn, test.FullyQualifiedName);
        }
        Assert.AreEqual(1, files.Pagination.Total);
        Assert.AreEqual(1, symbol.Pagination.Total);
    }

    internal sealed class MultiProjectFixture : IDisposable
    {
        private const string AttributeSource = "class TestMethodAttribute : System.Attribute { }";

        private readonly AdhocWorkspace _workspace = new();
        private readonly Dictionary<string, ProjectId> _projectIds = new(StringComparer.Ordinal);
        private readonly string _root = Path.Combine(TestTempRoot.Current, "discovery-" + Guid.NewGuid().ToString("N"));

        public MultiProjectFixture(bool forceInboundFallback)
        {
            Directory.CreateDirectory(_root);
            AddProject("Product");
            // The direct sweep's 32-symbol cap leaves AlphaProbe to the inbound fallback.
            var padding = forceInboundFallback
                ? string.Join(" ", Enumerable.Range(0, 32).Select(i => $"class Padding{i} {{ }}"))
                : string.Empty;
            Source = AddDocument("Product", "Library.cs", $"namespace Product; {padding} public class AlphaProbe {{ }}");
        }

        public string Source { get; }

        public TestDiscoveryService Service => CreateService();

        public TestDiscoveryService CreateService(Microsoft.Extensions.Logging.ILogger<TestDiscoveryService>? logger = null) => new(new FailClosedWorkspaceManagerStub
        {
            GetCurrentSolutionHandler = _ => _workspace.CurrentSolution,
            GetCurrentVersionHandler = _ => 1,
            ContainsWorkspaceHandler = _ => true,
            GetStatusAsyncHandler = workspaceId => new(workspaceId, null, 1, "fixture:1", DateTimeOffset.UtcNow, 1,
                _workspace.CurrentSolution.Projects.Sum(p => p.DocumentIds.Count),
                _workspace.CurrentSolution.Projects
                    .Select(p => new ProjectStatusDto(p.Name, "", p.DocumentIds.Count, [], [], p.Name != "Product", p.Name, "Library"))
                    .ToList(),
                true, false, [])
        }, logger ?? NullLogger<TestDiscoveryService>.Instance);

        public string AddTestFile(string projectName, string source)
        {
            if (!_projectIds.ContainsKey(projectName))
            {
                AddProject(projectName, _projectIds["Product"]);
                AddDocument(projectName, "Attributes.cs", AttributeSource);
            }
            return AddDocument(projectName, "Checks.cs", source);
        }

        private void AddProject(string name, ProjectId? reference = null)
        {
            var id = ProjectId.CreateNewId();
            var project = ProjectInfo.Create(id, VersionStamp.Create(), name, name, LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                projectReferences: reference is null ? null : [new ProjectReference(reference)]);
            Assert.IsTrue(_workspace.TryApplyChanges(_workspace.CurrentSolution.AddProject(project)));
            _projectIds[name] = id;
        }

        private string AddDocument(string projectName, string name, string source)
        {
            var directory = Path.Combine(_root, projectName);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, source);
            var solution = _workspace.CurrentSolution.AddDocument(
                DocumentId.CreateNewId(_projectIds[projectName]), name, SourceText.From(source), filePath: path);
            Assert.IsTrue(_workspace.TryApplyChanges(solution));
            return path;
        }

        public void Dispose()
        {
            _workspace.Dispose();
            TestFixtureFileSystem.DeleteDirectoryIfExists(_root);
        }
    }
}
