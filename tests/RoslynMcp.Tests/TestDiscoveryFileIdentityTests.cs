using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestDiscoveryFileIdentityTests
{
    [TestMethod]
    public async Task SymbolReferences_DoNotSelectCaseDistinctUnreferencedTestFile()
    {
        using var fixture = new DiscoveryFixture();
        fixture.RequireCaseSensitiveFiles();
        fixture.AddFile("Library.cs", "namespace Product; public class AlphaProbe { }");
        var upper = fixture.AddFile("Checks.cs", "namespace Verification; class First { [TestMethod] void Runs() { _ = new Product.AlphaProbe(); } }");
        fixture.AddFile("checks.cs", "namespace Verification; class Second { [TestMethod] void Idle() { } }");
        var result = await fixture.Service.FindRelatedTestsAsync("fixture", SymbolLocator.ByMetadataName("Product.AlphaProbe"), 100, CancellationToken.None);
        Assert.AreEqual(1, result.Tests.Count, "Only the file containing a real reference is related.");
        Assert.AreEqual(upper, result.Tests.Single().FilePath);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FileReferences_DoNotSelectCaseDistinctUnreferencedTestFile(bool forceInboundFallback)
    {
        using var fixture = new DiscoveryFixture();
        fixture.RequireCaseSensitiveFiles();
        // The direct sweep's 32-symbol cap leaves AlphaProbe to the inbound fallback.
        var padding = forceInboundFallback
            ? string.Join(" ", Enumerable.Range(0, 32).Select(i => $"class Padding{i} {{ }}"))
            : string.Empty;
        var source = fixture.AddFile("Library.cs", $"namespace Product; {padding} public class AlphaProbe {{ }}");
        var upper = fixture.AddFile("Checks.cs", "namespace Verification; class First { [TestMethod] void Runs() { _ = new Product.AlphaProbe(); } }");
        fixture.AddFile("checks.cs", "namespace Verification; class Second { [TestMethod] void Idle() { } }");
        var result = await fixture.Service.FindRelatedTestsForFilesAsync("fixture", [source], 100, CancellationToken.None);
        Assert.AreEqual(1, result.Tests.Count, "Reference-file identity must exclude the unreferenced casing sibling.");
        Assert.AreEqual(upper, result.Tests.Single().FilePath);
        CollectionAssert.AreEqual(new[] { source }, result.Tests.Single().TriggeredByFiles.ToArray());
        Assert.IsTrue(result.Diagnostics.HeuristicsAttempted.Contains(forceInboundFallback ? "inbound-reference" : "direct-reference"));
    }

    [TestMethod]
    public async Task FileReferences_NonTestCasingSiblingDoesNotSelectTestFile()
    {
        using var fixture = new DiscoveryFixture();
        fixture.RequireCaseSensitiveFiles();
        var source = fixture.AddFile("Library.cs", "namespace Product; public class AlphaProbe { }");
        fixture.AddFile("Checks.cs", "namespace Verification; class First { [TestMethod] void Idle() { } }");
        fixture.AddFile("checks.cs", "namespace Consumer; class Other { Product.AlphaProbe value = new(); }");
        var result = await fixture.Service.FindRelatedTestsForFilesAsync("fixture", [source], 100, CancellationToken.None);
        Assert.AreEqual(0, result.Tests.Count, "A non-test reference must not match its casing sibling's tests.");
    }

    [TestMethod]
    public async Task FileTriggers_PreserveBothCaseDistinctSourceFiles()
    {
        using var fixture = new DiscoveryFixture();
        fixture.RequireCaseSensitiveFiles();
        var upper = fixture.AddFile("Library.cs", "namespace Product; public class AlphaProbe { }");
        var lower = fixture.AddFile("library.cs", "namespace Product; public class BetaProbe { }");
        fixture.AddFile("Checks.cs", "namespace Verification; class First { [TestMethod] void Runs() { _ = new Product.AlphaProbe(); _ = new Product.BetaProbe(); } }");
        var result = await fixture.Service.FindRelatedTestsForFilesAsync("fixture", [upper, lower], 100, CancellationToken.None);
        Assert.AreEqual(1, result.Tests.Count);
        CollectionAssert.AreEqual(new[] { upper, lower }, result.Tests.Single().TriggeredByFiles.ToArray());
    }

    [TestMethod]
    public async Task WindowsAliases_SelectOnePhysicalFileAndOneTrigger()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows path casing aliases require Windows.");
        using var fixture = new DiscoveryFixture();
        var source = fixture.AddFile("Library.cs", "namespace Product; public class AlphaProbe { }");
        var test = fixture.AddFile("Checks.cs", "namespace Verification; class First { [TestMethod] void Runs() { _ = new Product.AlphaProbe(); } }");
        var alias = Path.Combine(Path.GetDirectoryName(source)!, "library.cs");
        Assert.AreEqual(File.ReadAllText(source), File.ReadAllText(alias), "Both paths identify the same physical file.");
        var result = await fixture.Service.FindRelatedTestsForFilesAsync("fixture", [source, alias], 100, CancellationToken.None);
        Assert.AreEqual(test, result.Tests.Single().FilePath);
        CollectionAssert.AreEqual(new[] { source }, result.Tests.Single().TriggeredByFiles.ToArray());
        var symbolResult = await fixture.Service.FindRelatedTestsAsync("fixture", SymbolLocator.ByMetadataName("Product.AlphaProbe"), 100, CancellationToken.None);
        Assert.AreEqual(test, symbolResult.Tests.Single().FilePath);
    }

    [TestMethod]
    public async Task NameAffinity_RemainsCaseInsensitive()
    {
        using var fixture = new DiscoveryFixture();
        var source = fixture.AddFile("Library.cs", "namespace Product; public class AlphaProbe { }");
        fixture.AddFile("Checks.cs", "namespace Verification; class First { [TestMethod] void ALPHAPROBE() { } }");
        var result = await fixture.Service.FindRelatedTestsForFilesAsync("fixture", [source], 100, CancellationToken.None);
        Assert.AreEqual("ALPHAPROBE", result.Tests.Single().DisplayName);
        var symbolResult = await fixture.Service.FindRelatedTestsAsync("fixture", SymbolLocator.ByMetadataName("Product.AlphaProbe"), 100, CancellationToken.None);
        Assert.AreEqual("ALPHAPROBE", symbolResult.Tests.Single().DisplayName);
    }

    private sealed class DiscoveryFixture : IDisposable
    {
        private readonly AdhocWorkspace _workspace = new();
        private readonly ProjectId _projectId = ProjectId.CreateNewId();
        private readonly string _root = Path.Combine(TestTempRoot.Current, "discovery-" + Guid.NewGuid().ToString("N"));
        public TestDiscoveryService Service => new(new FixtureWorkspaceManager(_workspace.CurrentSolution), NullLogger<TestDiscoveryService>.Instance);

        public DiscoveryFixture()
        {
            Directory.CreateDirectory(_root);
            var project = ProjectInfo.Create(_projectId, VersionStamp.Create(), "Verification", "Verification", LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            Assert.IsTrue(_workspace.TryApplyChanges(_workspace.CurrentSolution.AddProject(project)));
            AddFile("Attributes.cs", "class TestMethodAttribute : System.Attribute { }");
        }

        public void RequireCaseSensitiveFiles()
        {
            if (OperatingSystem.IsWindows()) Assert.Inconclusive("The platform file-identity contract is case-insensitive on Windows.");
            var upper = Path.Combine(_root, "Case.txt");
            var lower = Path.Combine(_root, "case.txt");
            File.WriteAllText(upper, "upper");
            File.WriteAllText(lower, "lower");
            if (File.ReadAllText(upper) != "upper") Assert.Inconclusive("The fixture filesystem does not preserve case-distinct files.");
            Assert.AreEqual("lower", File.ReadAllText(lower));
        }

        public string AddFile(string name, string source)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, source);
            var solution = _workspace.CurrentSolution.AddDocument(DocumentId.CreateNewId(_projectId), name, SourceText.From(File.ReadAllText(path)), filePath: path);
            Assert.IsTrue(_workspace.TryApplyChanges(solution));
            return path;
        }

        public void Dispose()
        {
            _workspace.Dispose();
            TestFixtureFileSystem.DeleteDirectoryIfExists(_root);
        }
    }

    private sealed class FixtureWorkspaceManager(Solution solution) : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }
        public Solution GetCurrentSolution(string workspaceId) => solution;
        public int GetCurrentVersion(string workspaceId) => 1;
        public WorkspaceStatusDto GetStatus(string workspaceId) => new(workspaceId, null, 1, "fixture:1", DateTimeOffset.UtcNow, 1,
            solution.Projects.Single().DocumentIds.Count, [new("Verification", "", solution.Projects.Single().DocumentIds.Count, [], [], true, "Verification", "Library")], true, false, []);
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(GetStatus(workspaceId));
        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => workspaceId == "fixture";
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [GetStatus("fixture")];
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => solution.Projects.SingleOrDefault(p => p.Name == projectNameOrPath);
    }
}
