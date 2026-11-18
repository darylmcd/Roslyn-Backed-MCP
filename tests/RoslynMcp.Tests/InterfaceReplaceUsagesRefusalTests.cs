using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

/// <summary>
/// <c>replaceUsages</c> publishes a preview only when the requested rewrite was actually performed: an unavailable
/// post-generation compilation or an unrebindable concrete type refuses before any token is stored, while
/// source-generated documents (which no preview can edit) are skipped by name, not by accident.
/// </summary>
[TestClass]
public sealed class InterfaceReplaceUsagesRefusalTests : InterfaceRouteTestBase
{
    private const string Source =
        "namespace SampleLib; public class EchoService { public int Echo(int value) => value + 1; }";

    private const string Holder = """
        namespace SampleLib;

        public class EchoHolder
        {
            private EchoService _service = default!;

            public void Use(EchoService service) { _service = service; }
        }
        """;

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("compilation", "replaceUsages could not compile project 'SampleLib'")]
    [DataRow("rebind", "replaceUsages could not find type 'EchoService'")]
    public async Task Unavailable_Replacement_Compilation_Or_Symbol_Refuses_Before_Publishing(string failure, string expectedText)
    {
        await using var workspace = await LoadAsync();
        var store = new TrackingPreviewStore(new PreviewStore());
        using var cache = new CompilationCache(WorkspaceManager);
        var service = new InterfaceExtractionService(WorkspaceManager, store, cache, NullLogger<InterfaceExtractionService>.Instance)
        {
            ReplacementCompilationAsync = failure == "compilation"
                ? static (_, _) => Task.FromResult<Compilation?>(null)
                : static (_, _) => Task.FromResult<Compilation?>(CSharpCompilation.Create("Unrelated")),
        };
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var files = Snapshot(workspace.RootPath);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewExtractInterfaceAsync(workspace.WorkspaceId, workspace.GetPath("SampleLib", "EchoService.cs"),
                "EchoService", "IEchoService", null, replaceUsages: true, CancellationToken.None));

        StringAssert.Contains(refusal.Message, expectedText);
        StringAssert.Contains(refusal.Message, "no preview was produced");
        Assert.AreEqual(0, store.GetRedeemableTokens(workspace.WorkspaceId).Count);
        Assert.AreSame(solution, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        AssertSnapshot(files);
        Assert.IsFalse(File.Exists(workspace.GetPath("SampleLib", "IEchoService.cs")));
    }

    [TestMethod]
    public async Task Replacement_Seam_Is_Not_Consulted_When_Usages_Are_Not_Replaced()
    {
        await using var workspace = await LoadAsync();
        var store = new PreviewStore();
        using var cache = new CompilationCache(WorkspaceManager);
        var service = new InterfaceExtractionService(WorkspaceManager, store, cache, NullLogger<InterfaceExtractionService>.Instance)
        {
            ReplacementCompilationAsync = static (_, _) => throw new AssertFailedException("replaceUsages=false must not read the replacement compilation."),
        };

        var preview = await service.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
            workspace.GetPath("SampleLib", "EchoService.cs"), "EchoService", "IEchoService", null, replaceUsages: false, CancellationToken.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PreviewToken));
    }

    [TestMethod]
    public async Task Available_Compilation_Still_Replaces_Field_And_Parameter()
    {
        await using var workspace = await LoadAsync();
        var preview = await InterfaceExtractionService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
            workspace.GetPath("SampleLib", "EchoService.cs"), "EchoService", "IEchoService", null, replaceUsages: true, CancellationToken.None);
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;

        await AssertCompilesAsync(pending);
        var compilation = (await pending.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!;
        var holder = compilation.GetTypeByMetadataName("SampleLib.EchoHolder")!;
        Assert.AreEqual("IEchoService", holder.GetMembers("_service").OfType<IFieldSymbol>().Single().Type.Name);
        Assert.AreEqual("IEchoService", holder.GetMembers("Use").OfType<IMethodSymbol>().Single().Parameters.Single().Type.Name);
    }

    [TestMethod]
    public async Task Standalone_Replace_Skips_Source_Generated_Reference_And_Rewrites_The_Rest()
    {
        await using var workspace = await LoadAsync();
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var generatorProject = solution.Projects.Single(p => p.Name == "SampleLib")
            .AddAnalyzerReference(GeneratorReference(workspace.RootPath));
        await AssertGeneratedReferenceAsync(generatorProject, "SampleLib.GeneratedHolder");
        var manager = new FixedSolutionWorkspace(WorkspaceManager, generatorProject.Solution);
        var store = new PreviewStore();
        using var cache = new CompilationCache(manager);
        var service = new InterfaceExtractionService(manager, store, cache, NullLogger<InterfaceExtractionService>.Instance);

        var preview = await service.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
            workspace.GetPath("SampleLib", "EchoService.cs"), "EchoService", "IEchoService", null, replaceUsages: true, CancellationToken.None);
        var pending = store.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;

        var pendingProject = pending.Projects.Single(p => p.Name == "SampleLib");
        var compilation = (await pendingProject.GetCompilationAsync())!;
        var holder = compilation.GetTypeByMetadataName("SampleLib.EchoHolder")!;
        Assert.AreEqual("IEchoService", holder.GetMembers("_service").OfType<IFieldSymbol>().Single().Type.Name);
        var generated = compilation.GetTypeByMetadataName("SampleLib.GeneratedHolder")!;
        Assert.AreEqual("EchoService", generated.GetMembers("Held").OfType<IFieldSymbol>().Single().Type.Name,
            "generator output is not editable and stays as the generator emits it");
        Assert.AreEqual(0, compilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    public async Task Dependency_Inversion_Skips_Source_Generated_Constructor_And_Rewrites_The_Rest()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs",
            "namespace SampleLib; public sealed class RefService { public int Get() => 1; }",
            "User.cs", "namespace Consumer; public sealed class User { public User(SampleLib.RefService service) { } }");
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var consumer = solution.Projects.Single(p => p.Name == "Consumer")
            .AddAnalyzerReference(GeneratorReference(workspace.RootPath));
        await AssertGeneratedReferenceAsync(consumer, "Consumer.GeneratedUser");
        var manager = new FixedSolutionWorkspace(WorkspaceManager, consumer.Solution);
        var store = new PreviewStore();
        using var cache = new CompilationCache(manager);
        var service = new CrossProjectRefactoringService(manager, store, cache);

        var preview = await service.PreviewDependencyInversionAsync(workspace.WorkspaceId, sourcePath, "RefService",
            "IRefService", "Contracts", CancellationToken.None);
        var pending = store.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;

        var compilation = (await pending.Projects.Single(p => p.Name == "Consumer").GetCompilationAsync())!;
        var user = compilation.GetTypeByMetadataName("Consumer.User")!.Constructors.Single(c => !c.IsImplicitlyDeclared);
        Assert.AreEqual("IRefService", user.Parameters.Single().Type.Name);
        var generated = compilation.GetTypeByMetadataName("Consumer.GeneratedUser")!.Constructors.Single(c => !c.IsImplicitlyDeclared);
        Assert.AreEqual("RefService", generated.Parameters.Single().Type.Name,
            "generator output is not editable and stays as the generator emits it");
    }

    private static async Task AssertGeneratedReferenceAsync(Project project, string generatedTypeName)
    {
        var compilation = (await project.GetCompilationAsync())!;
        var type = compilation.GetTypeByMetadataName(generatedTypeName);
        Assert.IsNotNull(type, "the generator must emit " + generatedTypeName);
        Assert.IsInstanceOfType<SourceGeneratedDocument>(project.Solution.GetDocument(type.DeclaringSyntaxReferences.Single().SyntaxTree),
            "the reference under test must live in a source-generated document");
    }

    private async Task<IsolatedWorkspaceScope> LoadAsync()
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "EchoService.cs"), Source);
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "EchoHolder.cs"), Holder);
        await workspace.LoadAsync(CancellationToken.None);
        return workspace;
    }

    private sealed class FixedSolutionWorkspace(IWorkspaceManager inner, Solution solution) : DelegatingWorkspaceManager(inner)
    {
        public override Solution GetCurrentSolution(string workspaceId) => solution;
    }

    private const string GeneratorSource = """
        using Microsoft.CodeAnalysis;

        [Generator(LanguageNames.CSharp)]
        public sealed class ReferencingGenerator : IIncrementalGenerator
        {
            public void Initialize(IncrementalGeneratorInitializationContext context) =>
                context.RegisterSourceOutput(context.CompilationProvider, static (output, compilation) =>
                {
                    var source = compilation.AssemblyName switch
                    {
                        "SampleLib" => "namespace SampleLib; public class GeneratedHolder { public EchoService Held = default!; }",
                        "Consumer" => "namespace Consumer; public sealed class GeneratedUser { public GeneratedUser(SampleLib.RefService service) { } }",
                        _ => null,
                    };
                    if (source is not null) output.AddSource("Referencing.g.cs", source);
                });
        }
        """;

    /// <summary>
    /// A real analyzer-assembly reference: find-references checksums every analyzer reference and only a file reference
    /// survives that. The generator is compiled from <see cref="GeneratorSource"/> into the workspace copy under a unique
    /// assembly name (the default load context cannot hold two same-named assemblies) and emits only into the two named projects.
    /// </summary>
    private static AnalyzerReference GeneratorReference(string directory)
    {
        var name = "ReferencingGenerator" + Guid.NewGuid().ToString("N");
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(GeneratorSource)],
            trusted.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(directory, name + ".dll");
        using (var image = File.Create(path))
        {
            var result = compilation.Emit(image);
            Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        return new AnalyzerFileReference(path, new DefaultLoadContextAnalyzerLoader());
    }

    private sealed class DefaultLoadContextAnalyzerLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public System.Reflection.Assembly LoadFromPath(string fullPath) =>
            System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(File.ReadAllBytes(fullPath)));
    }
}
