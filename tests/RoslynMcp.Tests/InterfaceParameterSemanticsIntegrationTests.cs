using System.Globalization;
using System.Runtime.Loader;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed partial class InterfaceParameterSemanticsIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Indexer_Parameter_Type_Binds_Without_Source_Or_Implicit_Usings(bool crossProject)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var path = workspace.GetPath("SampleLib", "UriIndexer.cs");
        await File.WriteAllTextAsync(path, "namespace Contracts; public class UriIndexer { public int this[System.Uri key] { get => 1; set { } } }");
        await workspace.LoadAsync();
        await AssertLoadedNullableContextAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var preview = crossProject
            ? await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId, path, "UriIndexer", "IUriIndexer", "Contracts", CancellationToken.None)
            : await InterfaceExtractionService.PreviewExtractInterfaceAsync(workspace.WorkspaceId, path, "UriIndexer", "IUriIndexer", null, false, CancellationToken.None);
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var generated = pending.Projects.SelectMany(p => p.Documents).Single(d => d.Name == "IUriIndexer.cs");
        var sourceCompilation = (await before.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!;
        // Compile the generated file alone: the fixture's implicit global usings cannot
        // hide a missing namespace in this sole indexer-parameter type.
        var isolated = CSharpCompilation.Create("NoImplicitUsings", [(await generated.GetSyntaxTreeAsync())!], sourceCompilation.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.AreEqual(0, isolated.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error), string.Join(Environment.NewLine, isolated.GetDiagnostics()));
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "indexer_test", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Extraction_Preserves_Parameter_Contracts_Through_Preview_Apply_Reload(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var sourcePath = workspace.GetPath("SampleLib", "ParameterService.cs");
        await File.WriteAllTextAsync(sourcePath, Source);
        var unrelatedPath = workspace.GetPath("SampleLib", "Unrelated.cs");
        var unrelatedBytes = System.Text.Encoding.UTF8.GetBytes("// byte preservation\r\nnamespace Contracts;\r\npublic class Unrelated { public int Value => 42; }\r\n");
        await File.WriteAllBytesAsync(unrelatedPath, unrelatedBytes);
        await workspace.LoadAsync();
        await AssertLoadedNullableContextAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var sourceProject = before.Projects.Single(p => p.Name == "SampleLib");
        var originalType = (await sourceProject.GetCompilationAsync())!.GetTypeByMetadataName("Contracts.ParameterService")!;
        var references = (await sourceProject.GetCompilationAsync())!.References.OfType<PortableExecutableReference>().ToArray();
        var beforeBehavior = ExecuteContractCalls(Source, null, Calls.Replace("IParameterService", "ParameterService", StringComparison.Ordinal), references, false, null);
        Assert.AreEqual("2|1|2|2|Run|True|True|value|quote\"slash\\line\n|True", beforeBehavior);

        var culture = CultureInfo.CurrentCulture;
        RefactoringPreviewDto preview;
        var compositeStore = new CompositePreviewStore();
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            preview = route switch
            {
                "standalone" => await InterfaceExtractionService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                    sourcePath, "ParameterService", "IParameterService", null, false, CancellationToken.None),
                "dependency-inversion" => await CrossProjectRefactoringService.PreviewDependencyInversionAsync(
                    workspace.WorkspaceId, sourcePath, "ParameterService", "IParameterService", "Contracts", CancellationToken.None),
                "extract-and-wire" => await new ExtractAndWireOrchestrator(WorkspaceManager, compositeStore, PreviewStore,
                    CrossProjectRefactoringService, DiRegistrationService).PreviewExtractAndWireInterfaceAsync(
                        workspace.WorkspaceId, sourcePath, "ParameterService", "IParameterService", "Contracts", true, CancellationToken.None),
                _ => await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
                    sourcePath, "ParameterService", "IParameterService", "Contracts", CancellationToken.None),
            };
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        var pending = PreviewStore.Retrieve(preview.PreviewToken);
        var previewSolution = pending?.ModifiedSolution ?? before;
        if (route == "extract-and-wire")
        {
            foreach (var mutation in compositeStore.Retrieve(preview.PreviewToken)!.Value.Mutations)
            {
                if (!mutation.FilePath.EndsWith(".cs", StringComparison.Ordinal)) continue;
                var existing = previewSolution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => d.FilePath == mutation.FilePath);
                if (existing is not null)
                    previewSolution = previewSolution.WithDocumentText(existing.Id, SourceText.From(mutation.UpdatedContent!));
                else
                    previewSolution = previewSolution.AddDocument(DocumentId.CreateNewId(previewSolution.Projects.Single(p => p.Name == "Contracts").Id),
                        Path.GetFileName(mutation.FilePath), SourceText.From(mutation.UpdatedContent!), filePath: mutation.FilePath);
            }
            var sourceId = previewSolution.Projects.Single(p => p.Name == "SampleLib").Id;
            previewSolution = previewSolution.AddProjectReference(sourceId,
                new ProjectReference(previewSolution.Projects.Single(p => p.Name == "Contracts").Id));
        }
        await AssertContractsAsync(previewSolution, originalType, route, beforeBehavior, references);
        Assert.AreEqual(Source, await File.ReadAllTextAsync(sourcePath), "Preview must not mutate source bytes.");
        var result = route == "extract-and-wire"
            ? await new CompositeApplyOrchestrator(WorkspaceManager, compositeStore, undoService: UndoService)
                .ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None)
            : await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "parameter_test", CancellationToken.None);
        Assert.IsTrue(result.Success, result.Error);
        await workspace.ReloadAsync();
        await AssertContractsAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), originalType, route, beforeBehavior, references);
        CollectionAssert.AreEqual(unrelatedBytes, await File.ReadAllBytesAsync(unrelatedPath));
    }

    private static async Task AssertContractsAsync(Solution solution, INamedTypeSymbol originalType, string route,
        string beforeBehavior, IReadOnlyList<MetadataReference> references)
    {
        await AssertCompilesAsync(solution);
        var project = solution.Projects.Single(p => p.Name == (route == "standalone" ? "SampleLib" : "Contracts"));
        var compilation = (await project.GetCompilationAsync())!;
        var contract = compilation.GetTypeByMetadataName("Contracts.IParameterService")!;
        Assert.IsNotNull(contract);
        foreach (var method in originalType.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
        {
            var projected = contract.GetMembers(method.Name).OfType<IMethodSymbol>().Single();
            CollectionAssert.AreEqual(method.Parameters.Select(ParameterContract).ToArray(), projected.Parameters.Select(ParameterContract).ToArray(), method.Name);
        }
        foreach (var property in originalType.GetMembers().OfType<IPropertySymbol>())
        {
            var projected = contract.GetMembers().OfType<IPropertySymbol>().Single(p => p.MetadataName == property.MetadataName);
            Assert.AreEqual(property.IsIndexer, projected.IsIndexer);
            CollectionAssert.AreEqual(property.Parameters.Select(ParameterContract).ToArray(), projected.Parameters.Select(ParameterContract).ToArray());
            Assert.AreEqual(property.GetMethod?.DeclaredAccessibility == Accessibility.Public, projected.GetMethod is not null, property.Name);
            Assert.AreEqual(property.SetMethod?.DeclaredAccessibility == Accessibility.Public, projected.SetMethod is not null, property.Name);
            if (projected.SetMethod is not null) Assert.AreEqual(property.SetMethod!.IsInitOnly, projected.SetMethod.IsInitOnly);
        }
        var sourceProject = solution.Projects.Single(p => p.Name == "SampleLib");
        var callDocument = DocumentId.CreateNewId(sourceProject.Id);
        var withCalls = solution.AddDocument(callDocument, "ContractCalls.cs", SourceText.From(Calls));
        await AssertCompilesAsync(withCalls);
        var source = await sourceProject.Documents.Single(d => d.Name == "ParameterService.cs").GetTextAsync();
        var generated = await project.Documents.Single(d => d.Name == "IParameterService.cs").GetTextAsync();
        Assert.AreEqual(beforeBehavior, ExecuteContractCalls(source.ToString(), generated.ToString(), Calls, references, false, null));
    }

    private static string ExecuteContractCalls(string source, string? contract, string calls, IReadOnlyList<MetadataReference> references, bool allowUnsafe, string? sharedSource)
    {
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(calls, path: "ContractCalls.cs") };
        if (contract is not null) trees.Add(CSharpSyntaxTree.ParseText(contract));
        if (sharedSource is not null) trees.Add(CSharpSyntaxTree.ParseText(sharedSource));
        var compilation = CSharpCompilation.Create("ParameterBehavior" + Guid.NewGuid().ToString("N"), trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: allowUnsafe));
        using var bytes = new MemoryStream();
        var emit = compilation.Emit(bytes);
        Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        bytes.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(bytes);
            return (string)assembly.GetType("Contracts.ContractCalls")!.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }

    private static string ParameterContract(IParameterSymbol parameter) => string.Join("|",
        parameter.Name, parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers)),
        parameter.RefKind, parameter.ScopedKind, parameter.IsParams, parameter.IsOptional, parameter.HasExplicitDefaultValue,
        parameter.HasExplicitDefaultValue ? Convert.ToString(parameter.ExplicitDefaultValue, CultureInfo.InvariantCulture) : "-",
        string.Join(";", parameter.GetAttributes().Select(a => a.ToString()).Order(StringComparer.Ordinal)));

    private static async Task AssertCompilesAsync(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = (await project.GetCompilationAsync())!;
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, $"{project.Name}: {string.Join(Environment.NewLine, errors.Select(e => e.ToString()))}" +
                (errors.Length == 0 ? string.Empty : Environment.NewLine + CompilationContext(project, compilation, errors)));
        }
    }

    private static async Task AssertLoadedNullableContextAsync(Solution solution)
    {
        foreach (var project in solution.Projects.Where(p => p.Name is "SampleLib" or "Contracts"))
        {
            var compilation = (await project.GetCompilationAsync())!;
            Assert.AreEqual(NullableContextOptions.Enable, compilation.Options.NullableContextOptions,
                "The fixture configures Nullable=enable. " + CompilationContext(project, compilation, []));
        }
    }

    private static string CompilationContext(Project project, Compilation compilation, IReadOnlyList<Diagnostic> diagnostics)
    {
        var options = compilation.Options;
        var provider = options.SyntaxTreeOptionsProvider;
        var specific = options.SpecificDiagnosticOptions.TryGetValue("CS8632", out var policy) ? policy.ToString() : "unset";
        var global = provider?.TryGetGlobalDiagnosticValue("CS8632", CancellationToken.None, out var globalPolicy) == true
            ? globalPolicy.ToString() : "unset";
        var configured = "not-a-known-fixture";
        if (project.Name is "SampleLib" or "Contracts" && project.FilePath is not null)
        {
            var values = XDocument.Load(project.FilePath).Descendants("Nullable").Select(e => e.Value.Trim()).ToArray();
            configured = string.Join(",", values.Take(4).Select(v => v is "enable" or "disable" or "warnings" or "annotations" ? v : "unrecognized"));
        }

        var lines = new List<string>
        {
            $"CompilationContext: nullable={options.NullableContextOptions}; language={(project.ParseOptions as CSharpParseOptions)?.LanguageVersion}; " +
                $"general={options.GeneralDiagnosticOption}; specificCS8632={specific}; globalCS8632={global}; " +
                $"treeProvider={provider?.GetType().FullName ?? "none"}; fixtureXmlNullable={configured}",
            AssemblyIdentity(typeof(Compilation)),
            AssemblyIdentity(typeof(CSharpCompilation)),
            AssemblyIdentity(typeof(Microsoft.CodeAnalysis.Workspace)),
        };
        var trees = diagnostics.Where(d => d.Location.SourceTree is not null).Select(d => d.Location.SourceTree!)
            .Distinct().Take(8).ToArray();
        if (trees.Length == 0) trees = compilation.SyntaxTrees.Take(2).ToArray();
        foreach (var tree in trees)
        {
            var treePolicy = provider?.TryGetDiagnosticValue(tree, "CS8632", CancellationToken.None, out var report) == true
                ? report.ToString() : "unset";
            var model = compilation.GetSemanticModel(tree);
            var positions = diagnostics.Where(d => d.Location.SourceTree == tree).Select(d => d.Location.SourceSpan.Start)
                .Distinct().Take(8).DefaultIfEmpty(0);
            var contexts = string.Join(",", positions.Select(p => $"{p}:{model.GetNullableContext(p)}"));
            lines.Add($"TreeContext: file={Path.GetFileName(tree.FilePath)}; kind={tree.Options.Kind}; " +
                $"language={(tree.Options as CSharpParseOptions)?.LanguageVersion}; generated={provider?.IsGenerated(tree, CancellationToken.None)}; " +
                $"CS8632={treePolicy}; nullableAtDiagnostic={contexts}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string AssemblyIdentity(Type type)
    {
        var assembly = type.Assembly;
        return $"RoslynAssembly: name={assembly.GetName().Name}; version={assembly.GetName().Version}; mvid={assembly.ManifestModule.ModuleVersionId}";
    }

    private const string Source = """
        namespace Contracts;
        public class ParameterService
        {
            private const string Local = "quote\"slash\\line\n";
            public void Modify(ref int @event, out int result, in int input) { result = input; @event++; }
            public void Read(ref readonly int input) { }
            public void Scoped(scoped System.Span<int> values, scoped ref int input) { }
            public int Sum(params int[] values) => values.Length;
            public int Collection(params System.ReadOnlySpan<int> values) => values.Length;
            public void Nullable(System.Collections.Generic.List<string?>? @class) { }
            public string Defaults(string text = Local, char character = '\'', double number = 1.25, float single = 1.5F,
                decimal amount = 2.75M, ulong large = ulong.MaxValue, System.DayOfWeek day = System.DayOfWeek.Friday,
                System.DateTime date = default, object? value = null) => text;
            public string Caller([System.Runtime.CompilerServices.CallerMemberName] string member = "",
                [System.Runtime.CompilerServices.CallerLineNumber] int line = 0,
                [System.Runtime.CompilerServices.CallerFilePath] string file = "") => $"{member}|{line > 0}|{file.EndsWith("ContractCalls.cs", System.StringComparison.Ordinal)}";
            public string Expression(int value, [System.Runtime.CompilerServices.CallerArgumentExpression("value")] string expression = "") => expression;
            public bool Optional([System.Runtime.InteropServices.Optional] object value) => object.ReferenceEquals(value, System.Reflection.Missing.Value);
            public void MetadataDefaults([System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(123)] System.DateTime date,
                [System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DecimalConstant(2, 0, 0, 0, 125)] decimal amount,
                [System.Runtime.InteropServices.Optional, System.Runtime.InteropServices.DefaultParameterValue(13)] int number) { }
            public void NumericDefaults(double positive = double.PositiveInfinity, double negative = double.NegativeInfinity,
                double nan = double.NaN, float single = float.NaN, long minimum = long.MinValue, uint maximum = uint.MaxValue) { }
            public int this[System.Uri @event] { get => 1; set { } }
            public int VisibleGet { get; private set; }
            public int VisibleSet { private get; set; }
            public int Init { get; init; }
        }
        """;

    private const string Calls = """
        namespace Contracts;
        public static class ContractCalls
        {
            public static string Run()
            {
                IParameterService service = new ParameterService { Init = 7 };
                int value = 1;
                service.Modify(ref value, out int result, in value);
                service.Read(in value);
                service.Scoped(System.Span<int>.Empty, ref value);
                service.MetadataDefaults();
                service.NumericDefaults();
                service.Nullable(null);
                service[new System.Uri("https://example.com")] = result;
                service.VisibleSet = 1;
                return $"{value}|{result}|{service.Sum(1, 2)}|{service.Collection(3, 4)}|{service.Caller()}|{service.Expression(value)}|{service.Defaults()}|{service.Optional()}";
            }
        }
        """;
}
