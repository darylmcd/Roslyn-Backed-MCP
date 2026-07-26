using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the four acceptance bullets for <see cref="RoslynSymbolTraversal.EnumerateNamedTypes"/>:
/// (a) a class buried beneath non-class parents is yielded under a Class filter (no pruning),
/// (b) sibling declaration order is preserved, (c) an unfiltered walk yields every named type at
/// every depth including grandchildren, and (d) a kind filter drops non-matching types without
/// pruning matching descendants beneath them.
/// </summary>
[TestClass]
public class SymbolTraversalTests
{
    // Named types by kind in this fixture:
    //   classes:    Alpha, Beta, Gamma, Buried, Parent, Child, Grandchild, MatchingLeaf
    //   structs:    S1, S2, S3, Wrapper
    //   interfaces: INested
    private const string Source = @"
namespace Sample
{
    public class Alpha { }
    public class Beta { }
    public class Gamma { }

    // Three struct wrappers bury a class three levels deep beneath non-class parents.
    public struct S1
    {
        public struct S2
        {
            public struct S3
            {
                public class Buried { }
            }
        }
    }

    // Arbitrary-depth class nesting for the grandchildren case.
    public class Parent
    {
        public class Child
        {
            public class Grandchild { }
        }
    }

    // Mixed-kind nesting under a non-class parent for the filter case.
    public struct Wrapper
    {
        public interface INested { }
        public class MatchingLeaf { }
    }
}
";

    [TestMethod]
    public async Task Filtered_Yields_Class_Buried_Beneath_NonClass_Parents()
    {
        var compilation = await CompileAsync(Source);

        var classes = RoslynSymbolTraversal
            .EnumerateNamedTypes(SampleNamespace(compilation), TypeKind.Class)
            .Select(t => t.Name)
            .ToList();

        // `Buried` is a class nested three levels under struct wrappers S1/S2/S3. The buggy
        // predecessors pruned its walk the moment a parent failed the Class filter — the fix
        // descends unconditionally, so it must surface.
        CollectionAssert.Contains(classes, "Buried",
            "A class buried beneath non-class (struct) parents must not be pruned by the Class filter.");
        // MatchingLeaf sits beneath the `Wrapper` struct — same non-matching-parent shape.
        CollectionAssert.Contains(classes, "MatchingLeaf");
    }

    [TestMethod]
    public async Task Preserves_Sibling_Declaration_Order()
    {
        var compilation = await CompileAsync(Source);

        var names = RoslynSymbolTraversal
            .EnumerateNamedTypes(SampleNamespace(compilation))
            .Select(t => t.Name)
            .ToList();

        var alpha = names.IndexOf("Alpha");
        var beta = names.IndexOf("Beta");
        var gamma = names.IndexOf("Gamma");

        Assert.IsTrue(alpha >= 0 && beta >= 0 && gamma >= 0, "All three top-level siblings must be yielded.");
        Assert.IsTrue(alpha < beta && beta < gamma,
            $"Sibling declaration order must be preserved (Alpha<Beta<Gamma); got {alpha},{beta},{gamma}.");
    }

    [TestMethod]
    public async Task Unfiltered_Yields_Every_Named_Type_At_Every_Depth()
    {
        var compilation = await CompileAsync(Source);

        var names = RoslynSymbolTraversal
            .EnumerateNamedTypes(SampleNamespace(compilation))
            .Select(t => t.Name)
            .ToList();

        // Rooted at the `Sample` namespace, the walk must yield EXACTLY every declared named type
        // at every depth — grandchildren (Grandchild) and types buried beneath non-class parents
        // (Buried) included — and nothing more, nothing twice.
        string[] expected =
        {
            "Alpha", "Beta", "Gamma",
            "S1", "S2", "S3", "Buried",
            "Parent", "Child", "Grandchild",
            "Wrapper", "INested", "MatchingLeaf",
        };
        CollectionAssert.AreEquivalent(expected, names,
            $"Unfiltered walk must yield every declared type exactly once. Got: [{string.Join(", ", names)}]");
    }

    [TestMethod]
    public async Task Filter_Drops_NonMatching_Without_Pruning_Matching_Descendants()
    {
        var compilation = await CompileAsync(Source);

        var classes = RoslynSymbolTraversal
            .EnumerateNamedTypes(SampleNamespace(compilation), TypeKind.Class)
            .Select(t => t.Name)
            .ToHashSet();

        // Non-matching kinds must be excluded from the yielded set...
        foreach (var excluded in new[] { "S1", "S2", "S3", "Wrapper", "INested" })
            Assert.IsFalse(classes.Contains(excluded), $"'{excluded}' is not a class and must be filtered out.");

        // ...yet every class, at every depth, must still be present.
        foreach (var included in new[] { "Alpha", "Beta", "Gamma", "Parent", "Child", "Grandchild", "Buried", "MatchingLeaf" })
            Assert.IsTrue(classes.Contains(included), $"Class '{included}' must survive the filter.");
    }

    [TestMethod]
    public async Task FindContainingType_ReturnsNearestNestedType()
    {
        const string source = """
            namespace Sample;

            public class Outer
            {
                public class Inner
                {
                    public void Target()
                    {
                        var value = 1;
                    }
                }
            }
            """;
        var compilation = await CompileAsync(source);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var targetNode = root.DescendantTokens().Single(token => token.ValueText == "value").Parent!;

        var containingType = RoslynSymbolTraversal.FindContainingType(
            targetNode,
            compilation.GetSemanticModel(tree),
            CancellationToken.None);

        Assert.IsNotNull(containingType);
        Assert.AreEqual("Inner", containingType.Name);
    }

    [TestMethod]
    public async Task FindContainingType_OutsideType_ReturnsNull()
    {
        const string source = """
            using System;

            Console.WriteLine("top level");
            """;
        var compilation = await CompileAsync(source);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var usingNode = root.DescendantNodes().OfType<UsingDirectiveSyntax>().Single();

        var containingType = RoslynSymbolTraversal.FindContainingType(
            usingNode,
            compilation.GetSemanticModel(tree),
            CancellationToken.None);

        Assert.IsNull(containingType);
    }

    [TestMethod]
    public async Task FindContainingType_PropagatesCancellation()
    {
        const string source = "namespace Sample; public class Target { }";
        var compilation = await CompileAsync(source);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var typeNode = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Single();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            RoslynSymbolTraversal.FindContainingType(
                typeNode,
                compilation.GetSemanticModel(tree),
                cts.Token));
    }

    // Root the walk at the source `Sample` namespace rather than the merged global namespace —
    // `compilation.GlobalNamespace` also carries every referenced BCL type, which would drown the
    // fixture assertions. The walker accepts any namespace root, so this exercises the same code.
    private static INamespaceSymbol SampleNamespace(Compilation compilation) =>
        compilation.GlobalNamespace.GetNamespaceMembers().Single(n => n.Name == "Sample");

    private static Task<Compilation> CompileAsync(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAsm",
            new[] { tree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            });
        return Task.FromResult<Compilation>(compilation);
    }
}
