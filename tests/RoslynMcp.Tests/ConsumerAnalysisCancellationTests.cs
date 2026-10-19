using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ConsumerAnalysisCancellationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Classification_CancellationDuringEnumerationNeverReturnsPartialConsumers(bool cancelAfterLast)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, symbol) = await CreateSolutionAsync(workspace);
        var references = (await SymbolFinder.FindReferencesAsync(symbol, solution)).SelectMany(item => item.Locations).ToList();
        var materialized = await ReferenceLocationMaterializer.MaterializeAsync(references, CancellationToken.None);
        Assert.IsTrue(materialized.Count > 0);
        using var cancellation = new CancellationTokenSource();
        var consumed = 0;
        IEnumerable<MaterializedReference> Items()
        {
            foreach (var item in materialized.Take(1))
            {
                consumed++;
                yield return item;
                cancellation.Cancel();
                if (!cancelAfterLast) yield return item;
            }
        }
        var error = Assert.ThrowsExactly<OperationCanceledException>(() =>
            ConsumerAnalysisService.ClassifyConsumers(Items(), symbol, cancellation.Token));
        Assert.IsTrue(consumed > 0);
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExtensionReferences_CancellationDuringEnumerationNeverReturnsPartialReferences(bool cancelAfterLast)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, symbol) = await CreateSolutionAsync(workspace);
        var member = symbol.GetMembers("M").OfType<IMethodSymbol>().Single();
        using var cancellation = new CancellationTokenSource();
        IEnumerable<IMethodSymbol> Members()
        {
            yield return member;
            cancellation.Cancel();
            if (!cancelAfterLast) yield return member;
        }
        var locations = new List<ReferenceLocation>();
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            ConsumerAnalysisService.AddExtensionMemberReferencesAsync(Members(), solution, locations, cancellation.Token));
        Assert.IsTrue(locations.Count > 0, "First iteration must collect real references before cancellation.");
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
    }

    private static async Task<(Solution Solution, INamedTypeSymbol Symbol)> CreateSolutionAsync(AdhocWorkspace workspace)
    {
        var project = workspace.AddProject("CancellationProbe", LanguageNames.CSharp)
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var document = project.AddDocument("Probe.cs", SourceText.From(
            "public class Target { public static void M() { } } public class Consumer { Target field; void Use() { Target.M(); } }"));
        var compilation = await document.Project.GetCompilationAsync();
        var symbol = compilation?.GetTypeByMetadataName("Target");
        Assert.IsNotNull(symbol);
        return (document.Project.Solution, symbol);
    }
}
