using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

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

    /// <summary>
    /// Asserts the named projects have no unresolved-name errors (CS0246/CS0234/CS0400/CS0104): every
    /// reference to the generated interface binds. Unlike <see cref="AssertCompilesAsync"/> it tolerates
    /// the member-mismatch errors (CS0535/CS0738) a fixture gets while its signature types still live in
    /// the source project and the generated interface cannot name them: the signature-dependency row.
    /// </summary>
    internal static async Task AssertNoUnresolvedNamesAsync(Solution solution, params string[] projectNames)
    {
        string[] unresolved = ["CS0246", "CS0234", "CS0400", "CS0104"];
        foreach (var name in projectNames)
        {
            var project = solution.Projects.Single(p => p.Name == name);
            var errors = (await project.GetCompilationAsync())!.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error && unresolved.Contains(d.Id)).ToArray();
            Assert.AreEqual(0, errors.Length, name + ": " + string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        }
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
