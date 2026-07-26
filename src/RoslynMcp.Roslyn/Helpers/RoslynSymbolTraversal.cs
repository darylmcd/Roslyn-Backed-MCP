using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Shared lazy walker for named-type trees. Consolidates three independently-maintained local
/// enumeration walks (<c>ImpactSweepService</c>, <c>CouplingAnalysisService</c>,
/// <c>SymbolSearchService</c>), two of which carried distinct pruning / depth-cap defects.
/// </summary>
internal static class RoslynSymbolTraversal
{
    /// <summary>
    /// Resolves the nearest type declaration containing <paramref name="node"/>.
    /// Returns <see langword="null"/> for namespace-level and top-level-statement nodes.
    /// </summary>
    public static INamedTypeSymbol? FindContainingType(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(semanticModel);

        var typeDeclaration = node
            .AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        return typeDeclaration is null
            ? null
            : semanticModel.GetDeclaredSymbol(typeDeclaration, ct) as INamedTypeSymbol;
    }

    /// <summary>
    /// Yields every <see cref="INamedTypeSymbol"/> reachable from <paramref name="root"/> —
    /// descending through child namespaces and into nested types to ARBITRARY depth — in
    /// pre-order depth-first order matching <see cref="INamespaceOrTypeSymbol.GetMembers"/> /
    /// <see cref="INamedTypeSymbol.GetTypeMembers"/> declaration order (each container's subtree is
    /// fully exhausted before control returns to a shallower container).
    /// </summary>
    /// <param name="root">The namespace to walk (typically a global namespace).</param>
    /// <param name="allowedKinds">
    /// When supplied, only types whose <see cref="INamedTypeSymbol.TypeKind"/> equals this value are
    /// yielded. The walk still descends into EVERY named type regardless of whether it matched the
    /// filter, so a matching type nested beneath a non-matching parent (e.g. a class nested inside a
    /// struct) is never pruned.
    /// </param>
    /// <remarks>
    /// Implemented with an explicit <see cref="Stack{T}"/> of enumerator frames — one frame per open
    /// ancestor container — so auxiliary storage is O(tree depth) and enumeration stays lazy.
    /// </remarks>
    public static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(INamespaceSymbol root, TypeKind? allowedKinds = null)
    {
        var frames = new Stack<IEnumerator<ISymbol>>();
        frames.Push(((IEnumerable<ISymbol>)root.GetMembers()).GetEnumerator());
        try
        {
            while (frames.Count > 0)
            {
                var frame = frames.Peek();
                if (!frame.MoveNext())
                {
                    frames.Pop().Dispose();
                    continue;
                }

                switch (frame.Current)
                {
                    case INamespaceSymbol childNamespace:
                        frames.Push(((IEnumerable<ISymbol>)childNamespace.GetMembers()).GetEnumerator());
                        break;

                    case INamedTypeSymbol type:
                        if (allowedKinds is null || type.TypeKind == allowedKinds.Value)
                            yield return type;

                        // Descend UNCONDITIONALLY — matching descendants must not be pruned when the
                        // parent type fails the kind filter, and nested types are walked to arbitrary
                        // depth (not capped at one level).
                        frames.Push(type.GetTypeMembers().Cast<ISymbol>().GetEnumerator());
                        break;
                }
            }
        }
        finally
        {
            while (frames.Count > 0)
                frames.Pop().Dispose();
        }
    }
}
