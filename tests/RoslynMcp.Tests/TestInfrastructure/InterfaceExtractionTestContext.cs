using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests.TestInfrastructure;

internal static class InterfaceExtractionTestContext
{
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
}
