using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests.TestInfrastructure;

internal static class InterfaceExtractionTestContext
{
    /// <summary>Number of previews held by a composite preview store (the store exposes no count).</summary>
    internal static int StoreCount(object store)
    {
        var type = store.GetType().BaseType!;
        var entries = (System.Collections.IDictionary)type.GetField("_entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
        return entries.Count;
    }

    internal static Dictionary<string, byte[]> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(p => p.EndsWith(".cs", StringComparison.Ordinal) || p.EndsWith(".csproj", StringComparison.Ordinal))
        .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        .ToDictionary(p => p, File.ReadAllBytes);

    internal static void AssertSnapshot(Dictionary<string, byte[]> files)
    {
        foreach (var (file, bytes) in files) CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file), file);
    }

    internal static Solution SerializedCompositePreview(Solution solution, IReadOnlyList<CompositeFileMutation> mutations)
    {
        foreach (var mutation in mutations)
        {
            if (!mutation.FilePath.EndsWith(".cs", StringComparison.Ordinal)) continue;
            var existing = solution.Projects.SelectMany(p => p.Documents).SingleOrDefault(d => d.FilePath == mutation.FilePath);
            solution = existing is not null
                ? solution.WithDocumentText(existing.Id, SourceText.From(mutation.UpdatedContent!))
                : solution.AddDocument(DocumentId.CreateNewId(solution.Projects.Single(p => p.Name == "Contracts").Id),
                    Path.GetFileName(mutation.FilePath), SourceText.From(mutation.UpdatedContent!), filePath: mutation.FilePath);
        }
        return solution.AddProjectReference(solution.Projects.Single(p => p.Name == "SampleLib").Id,
            new ProjectReference(solution.Projects.Single(p => p.Name == "Contracts").Id));
    }

    internal static async Task<Solution> ReparseSerializedDocumentsAsync(Solution solution)
    {
        foreach (var document in solution.Projects.SelectMany(p => p.Documents).ToArray())
        {
            var text = (await document.GetTextAsync()).ToString();
            var tree = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions?)document.Project.ParseOptions, document.FilePath ?? document.Name);
            solution = solution.WithDocumentSyntaxRoot(document.Id, await tree.GetRootAsync());
        }
        return solution;
    }

    internal static async Task AssertNativeContextAsync(IWorkspaceManager workspaceManager, string workspaceId)
    {
        var errors = workspaceManager.GetStatus(workspaceId).WorkspaceDiagnostics
            .Where(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToArray();
        var facts = new List<string> { "Native errors: " + string.Join(Environment.NewLine, errors.Take(8).Select(e => e.Message)) };
        foreach (var project in workspaceManager.GetCurrentSolution(workspaceId).Projects)
        {
            var compilation = (await project.GetCompilationAsync())!;
            facts.Add($"{project.Name}: Nullable={compilation.Options.NullableContextOptions}; diagnostics=" +
                string.Join(",", compilation.GetDiagnostics().Select(d => d.Id).Distinct().Order(StringComparer.Ordinal).Take(20)));
        }
        var context = string.Join(Environment.NewLine, facts);
        Console.WriteLine(context);
        Assert.AreEqual(0, errors.Length, context);
        foreach (var project in workspaceManager.GetCurrentSolution(workspaceId).Projects)
            Assert.AreEqual(NullableContextOptions.Enable, (await project.GetCompilationAsync())!.Options.NullableContextOptions, project.Name);
    }

    internal static async Task AssertCompilesAsync(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var errors = (await project.GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.AreEqual(0, errors.Length, project.Name + ": " + string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        }
    }

    internal static async Task AssertGeneratedReferenceAsync(Project project, string generatedTypeName)
    {
        var compilation = (await project.GetCompilationAsync())!;
        var type = compilation.GetTypeByMetadataName(generatedTypeName);
        Assert.IsNotNull(type, "the generator must emit " + generatedTypeName);
        Assert.IsInstanceOfType<SourceGeneratedDocument>(project.Solution.GetDocument(type.DeclaringSyntaxReferences.Single().SyntaxTree),
            "the reference under test must live in a source-generated document");
    }

    internal sealed class FixedSolutionWorkspace(IWorkspaceManager inner, Solution solution) : DelegatingWorkspaceManager(inner)
    {
        public override Solution GetCurrentSolution(string workspaceId) => solution;
    }

    /// <summary>The default generator outputs: one generated type per named project, referencing a concrete type by its bare name.</summary>
    internal static readonly IReadOnlyDictionary<string, string> DefaultGeneratorOutputs = new Dictionary<string, string>
    {
        ["SampleLib"] = "namespace SampleLib; public class GeneratedHolder { public EchoService Held = default!; }",
        ["Consumer"] = "namespace Consumer; public sealed class GeneratedUser { public GeneratedUser(SampleLib.RefService service) { } }",
    };

    /// <summary>
    /// A real analyzer-assembly reference: find-references checksums every analyzer reference and only a file reference
    /// survives that. The generator is compiled into the workspace copy under a unique assembly name (the default load
    /// context cannot hold two same-named assemblies) and emits <c>Referencing.g.cs</c> only into the projects named by
    /// <paramref name="outputsByAssembly"/> (assembly name to generated source; <see cref="DefaultGeneratorOutputs"/> when null).
    /// A key <c>Assembly|Type.MetadataName</c> emits only while the compilation contains that type, for a generator whose
    /// output depends on a type the preview itself adds.
    /// </summary>
    internal static AnalyzerReference GeneratorReference(string directory, IReadOnlyDictionary<string, string>? outputsByAssembly = null)
    {
        var entries = string.Join(Environment.NewLine, (outputsByAssembly ?? DefaultGeneratorOutputs).Select(output =>
        {
            var parts = output.Key.Split('|');
            var required = parts.Length > 1 ? SyntaxFactory.Literal(parts[1]).ToString() : "null";
            return $"        ({SyntaxFactory.Literal(parts[0])}, {required}, {SyntaxFactory.Literal(output.Value)}),";
        }));
        var generatorSource = $$"""
            using Microsoft.CodeAnalysis;

            [Generator(LanguageNames.CSharp)]
            public sealed class ReferencingGenerator : IIncrementalGenerator
            {
                private static readonly (string Assembly, string? RequiredType, string Source)[] Outputs =
                [
            {{entries}}
                ];

                public void Initialize(IncrementalGeneratorInitializationContext context) =>
                    context.RegisterSourceOutput(context.CompilationProvider, static (output, compilation) =>
                    {
                        for (var i = 0; i < Outputs.Length; i++)
                        {
                            var (assembly, requiredType, source) = Outputs[i];
                            if (compilation.AssemblyName == assembly && (requiredType is null || compilation.GetTypeByMetadataName(requiredType) is not null))
                                output.AddSource("Referencing" + i + ".g.cs", source);
                        }
                    });
            }
            """;
        var name = "ReferencingGenerator" + Guid.NewGuid().ToString("N");
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(generatorSource)],
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
