using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Produces the compiler snapshot used by diagnostic surfaces after executing every source
/// generator loaded for the project. MSBuildWorkspace can return a raw compilation without
/// generator output after reload; running the project references explicitly preserves build
/// parity without suppressing diagnostics such as CS8795.
/// </summary>
internal static class SourceGeneratorCompilation
{
    public static async Task<CompilationSnapshot?> CreateAsync(Project project, CancellationToken ct)
    {
        var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null)
        {
            return null;
        }

        if (project.Language != LanguageNames.CSharp)
        {
            return new CompilationSnapshot(compilation, ImmutableArray<Diagnostic>.Empty);
        }

        var generators = project.AnalyzerReferences
            .SelectMany(reference => reference.GetGenerators(project.Language))
            .ToImmutableArray();
        if (generators.IsDefaultOrEmpty)
        {
            return new CompilationSnapshot(compilation, ImmutableArray<Diagnostic>.Empty);
        }

        var materializedGeneratedTrees = await GetMaterializedGeneratedTreesAsync(project, ct)
            .ConfigureAwait(false);
        if (!materializedGeneratedTrees.IsDefaultOrEmpty)
        {
            compilation = compilation.RemoveSyntaxTrees(materializedGeneratedTrees);
        }

        var driver = CSharpGeneratorDriver.Create(
            generators,
            additionalTexts: project.AnalyzerOptions.AdditionalFiles,
            parseOptions: project.ParseOptions as CSharpParseOptions,
            optionsProvider: project.AnalyzerOptions.AnalyzerConfigOptionsProvider);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var generatedCompilation,
            out var generatorDiagnostics,
            ct);
        return new CompilationSnapshot(generatedCompilation, generatorDiagnostics);
    }

    private static async Task<ImmutableArray<SyntaxTree>> GetMaterializedGeneratedTreesAsync(
        Project project,
        CancellationToken ct)
    {
        var documents = (await project.GetSourceGeneratedDocumentsAsync(ct).ConfigureAwait(false))
            .ToImmutableArray();
        if (documents.IsDefaultOrEmpty)
        {
            return ImmutableArray<SyntaxTree>.Empty;
        }

        var trees = ImmutableArray.CreateBuilder<SyntaxTree>(documents.Length);
        foreach (var document in documents)
        {
            var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            if (tree is not null)
            {
                trees.Add(tree);
            }
        }

        return trees.ToImmutable();
    }
}
