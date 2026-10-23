using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Simplification;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Preserves surviving original names when an extraction introduces names or reference edges.</summary>
internal sealed class OriginalNameBindings
{
    private sealed record Binding(DocumentId Document, SyntaxAnnotation Annotation, SyntaxNode Node,
        Document OriginalDocument, ISymbol Symbol, bool ExternScopedNamespace);
    private sealed record NameOfValue(DocumentId Document, SyntaxAnnotation Annotation, string Value);
    private readonly List<Binding> _bindings;
    private readonly List<NameOfValue> _nameOfValues;

    private OriginalNameBindings(Solution solution, List<Binding> bindings, List<NameOfValue> nameOfValues)
    {
        Solution = solution;
        _bindings = bindings;
        _nameOfValues = nameOfValues;
    }

    public Solution Solution { get; }

    public static async Task<OriginalNameBindings> CaptureAsync(
        Solution solution, CancellationToken ct)
    {
        var bindings = new List<Binding>();
        var nameOfValues = new List<NameOfValue>();
        var annotated = solution;
        foreach (var document in solution.Projects.SelectMany(project => project.Documents))
        {
            ct.ThrowIfCancellationRequested();
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root is null)
                continue;
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
                ?? throw new PublicInvalidOperationException("Original name bindings could not be resolved.");
            var candidates = root.DescendantNodes().Where(node =>
                node is TypeSyntax or MemberAccessExpressionSyntax &&
                !IsDeclarationName(node))
                .Select(node => (Node: node, Symbol: BoundSymbol(node, model, ct)))
                .Where(entry => entry.Symbol is INamespaceSymbol or ITypeSymbol { TypeKind: not TypeKind.Error } or IMethodSymbol { MethodKind: MethodKind.Constructor })
                .ToArray();
            // A qualified name (including generic arguments) is one semantic unit. Never replace
            // a SimpleName child with a QualifiedName where the C# grammar requires SimpleName.
            var candidateNodes = candidates.Select(entry => entry.Node).ToHashSet();
            var selected = candidates.Where(entry => !entry.Node.Ancestors().Any(candidateNodes.Contains)).ToArray();
            var annotations = new Dictionary<SyntaxNode, SyntaxAnnotation>();
            foreach (var entry in selected)
            {
                var annotation = new SyntaxAnnotation();
                annotations.Add(entry.Node, annotation);
                bindings.Add(new(document.Id, annotation, entry.Node, document, entry.Symbol!,
                    entry.Symbol is INamespaceSymbol && UsesExternAlias(entry.Node, model, new HashSet<ISymbol>(SymbolEqualityComparer.Default), ct)));
            }
            foreach (var invocation in selected.SelectMany(entry => entry.Node.Ancestors())
                .OfType<InvocationExpressionSyntax>().Distinct())
            {
                if (invocation.Expression is not IdentifierNameSyntax { Identifier.ValueText: "nameof" } ||
                    model.GetSymbolInfo(invocation, ct).Symbol is not null ||
                    model.GetConstantValue(invocation, ct) is not { HasValue: true, Value: string value })
                    continue;
                var annotation = new SyntaxAnnotation();
                annotations.Add(invocation, annotation);
                nameOfValues.Add(new(document.Id, annotation, value));
            }
            if (annotations.Count != 0)
                annotated = annotated.WithDocumentSyntaxRoot(document.Id,
                    root.ReplaceNodes(annotations.Keys, (original, rewritten) =>
                        rewritten.WithAdditionalAnnotations(annotations[original])));
        }
        return new(annotated, bindings, nameOfValues);
    }

    public async Task<Solution> RestoreAsync(Solution modifiedSolution, CancellationToken ct)
    {
        var displacedNameOf = new HashSet<SyntaxAnnotation>();
        foreach (var group in _bindings.GroupBy(binding => binding.Document))
        {
            var document = modifiedSolution.GetDocument(group.Key);
            if (document is null) continue;
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (root is null || model is null)
                throw new PublicInvalidOperationException("Original name bindings could not be verified.");
            var replacements = new Dictionary<SyntaxNode, SyntaxNode>();
            foreach (var binding in group)
            {
                var node = root.GetAnnotatedNodes(binding.Annotation).SingleOrDefault();
                // Removed/replaced nodes represent intentional edits (for example inversion parameters).
                if (node is null || IsSameSymbol(binding, node, BoundSymbol(node, model, ct), model.Compilation, ct))
                    continue;
                var enclosingNameOf = _nameOfValues.Where(value => value.Document == group.Key)
                    .FirstOrDefault(value => root.GetAnnotatedNodes(value.Annotation).Any(invocation => invocation.Span.Contains(node.Span)));
                if (enclosingNameOf is not null)
                {
                    displacedNameOf.Add(enclosingNameOf.Annotation);
                    continue;
                }
                var expanded = await Simplifier.ExpandAsync(binding.Node, binding.OriginalDocument,
                    null, false, ct).ConfigureAwait(false);
                replacements.Add(node, expanded.WithTriviaFrom(node).WithAdditionalAnnotations(binding.Annotation));
            }
            if (replacements.Count != 0)
                modifiedSolution = modifiedSolution.WithDocumentSyntaxRoot(group.Key,
                    root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]));
        }
        // Expanding an alias in nameof can change its string value even when its type is preserved.
        // Preserve that original compile-time value when either its original binding or value changed.
        foreach (var group in _nameOfValues.GroupBy(value => value.Document))
        {
            var document = modifiedSolution.GetDocument(group.Key);
            if (document is null) continue;
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (root is null || model is null)
                throw new PublicInvalidOperationException("Original nameof values could not be verified.");
            var replacements = new Dictionary<SyntaxNode, SyntaxNode>();
            foreach (var value in group)
            {
                var node = root.GetAnnotatedNodes(value.Annotation).SingleOrDefault();
                if (node is null) continue;
                var current = model.GetConstantValue(node, ct);
                if (!displacedNameOf.Contains(value.Annotation) && current.HasValue && Equals(current.Value, value.Value)) continue;
                replacements.Add(node, PreserveNameOfValue(node, value.Value));
            }
            if (replacements.Count != 0)
                modifiedSolution = modifiedSolution.WithDocumentSyntaxRoot(group.Key,
                    root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]));
        }
        foreach (var group in _bindings.GroupBy(binding => binding.Document))
        {
            var document = modifiedSolution.GetDocument(group.Key);
            if (document is null) continue;
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (root is null || model is null)
                throw new PublicInvalidOperationException("Original name bindings could not be verified.");
            foreach (var binding in group)
            {
                var node = root.GetAnnotatedNodes(binding.Annotation).SingleOrDefault();
                if (node is not null && !IsSameSymbol(binding, node, BoundSymbol(node, model, ct), model.Compilation, ct))
                    throw new PublicInvalidOperationException("The generated interface would change an existing name binding.");
            }
            // This semantic owner already materialized the syntax. Populate its native text cache
            // so physical diff planning can prove annotation-only no-ops without invoking loaders.
            await document.GetTextAsync(ct).ConfigureAwait(false);
        }
        return modifiedSolution;
    }

    private static bool IsSameSymbol(Binding binding, SyntaxNode node, ISymbol? current, Compilation compilation, CancellationToken ct)
    {
        // Declaration TypeInfo deliberately omits the authored outer reference-type '?' even
        // though the declaration symbol reports it. Preserve that syntax contract as well as
        // the recursive semantic annotations that Roslyn does expose inside compound types.
        if (binding.Symbol is ITypeSymbol { IsValueType: false } &&
            (binding.Node is NullableTypeSyntax) != (node is NullableTypeSyntax)) return false;
        if (binding.Symbol is INamespaceSymbol originalNamespace && !binding.ExternScopedNamespace)
        {
            if (current is not INamespaceSymbol currentNamespace) return false;
            // Ordinary namespace lookup is compilation-wide even when one constituent lets Roslyn
            // return a module namespace. Adding a reference may turn that same lookup into a merge.
            var names = new Stack<string>();
            for (var part = originalNamespace; !part.IsGlobalNamespace; part = part.ContainingNamespace)
                names.Push(part.Name);
            INamespaceSymbol? corresponding = compilation.GlobalNamespace;
            foreach (var name in names)
            {
                ct.ThrowIfCancellationRequested();
                var matches = corresponding.GetNamespaceMembers().Where(member => member.Name == name).ToArray();
                if (matches.Length != 1) return false;
                corresponding = matches[0];
            }
            return SymbolEqualityComparer.Default.Equals(corresponding, compilation.GetCompilationNamespace(currentNamespace));
        }
        return current is not null && SameOriginalShape(binding.Symbol, current) &&
            SymbolFinder.FindSimilarSymbols(binding.Symbol, compilation, ct)
                .Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, current));
    }

    private static ExpressionSyntax PreserveNameOfValue(SyntaxNode original, string value)
    {
        var interior = original.DescendantTrivia().Where(trivia => original.Span.Contains(trivia.Span)).ToArray();
        if (interior.Any(trivia => trivia.IsDirective || trivia.IsKind(SyntaxKind.DisabledTextTrivia) || trivia.IsKind(SyntaxKind.SkippedTokensTrivia)))
            throw new PublicInvalidOperationException("An existing nameof with directives cannot be preserved across the new name binding.");
        return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value))
            .WithLeadingTrivia(original.GetLeadingTrivia().AddRange(interior))
            .WithTrailingTrivia(original.GetTrailingTrivia());
    }

    private static ISymbol? BoundSymbol(SyntaxNode node, SemanticModel model, CancellationToken ct) =>
        node is TypeSyntax and not NameSyntax ? model.GetTypeInfo(node, ct).Type : model.GetSymbolInfo(node, ct).Symbol;

    // Symbol-key correspondence does not retain nested nullable annotations. Verify the original
    // semantic shape separately; the resolved candidate proves definition identity, not annotations.
    private static bool SameOriginalShape(ISymbol original, ISymbol current) => (original, current) switch
    {
        (ITypeSymbol left, ITypeSymbol right) => SameTypeShape(left, right),
        (IMethodSymbol left, IMethodSymbol right) => SameMethodShape(left, right),
        (INamespaceSymbol left, INamespaceSymbol right) => Equals(left.ContainingAssembly?.Identity, right.ContainingAssembly?.Identity),
        _ => false
    };

    private static bool SameTypeShape(ITypeSymbol left, ITypeSymbol right)
    {
        if (left.TypeKind != right.TypeKind || left.NullableAnnotation != right.NullableAnnotation ||
            !Equals(left.ContainingAssembly?.Identity, right.ContainingAssembly?.Identity)) return false;
        return (left, right) switch
        {
            (INamedTypeSymbol a, INamedTypeSymbol b) => a.IsNativeIntegerType == b.IsNativeIntegerType &&
                a.IsTupleType == b.IsTupleType &&
                (!a.IsTupleType || a.TupleElements.Select(element => element.Name).SequenceEqual(b.TupleElements.Select(element => element.Name))) &&
                SameOptionalType(a.ContainingType, b.ContainingType) && SameTypes(a.TypeArguments, b.TypeArguments),
            (IArrayTypeSymbol a, IArrayTypeSymbol b) => a.Rank == b.Rank && a.IsSZArray == b.IsSZArray && SameTypeShape(a.ElementType, b.ElementType),
            (IPointerTypeSymbol a, IPointerTypeSymbol b) => SameTypeShape(a.PointedAtType, b.PointedAtType),
            (IFunctionPointerTypeSymbol a, IFunctionPointerTypeSymbol b) => SameMethodShape(a.Signature, b.Signature),
            (ITypeParameterSymbol, ITypeParameterSymbol) => true,
            (IDynamicTypeSymbol, IDynamicTypeSymbol) => true,
            _ => false
        };
    }

    private static bool SameMethodShape(IMethodSymbol left, IMethodSymbol right) =>
        Equals(left.ContainingAssembly?.Identity, right.ContainingAssembly?.Identity) &&
        left.MethodKind == right.MethodKind && left.RefKind == right.RefKind && left.CallingConvention == right.CallingConvention &&
        SameOptionalType(left.ContainingType, right.ContainingType) && SameTypeShape(left.ReturnType, right.ReturnType) &&
        SameTypes(left.TypeArguments, right.TypeArguments) &&
        SameTypes(left.UnmanagedCallingConventionTypes, right.UnmanagedCallingConventionTypes) &&
        left.Parameters.Length == right.Parameters.Length && left.Parameters.Zip(right.Parameters)
            .All(pair => pair.First.RefKind == pair.Second.RefKind && SameTypeShape(pair.First.Type, pair.Second.Type));

    private static bool SameOptionalType(ITypeSymbol? left, ITypeSymbol? right) =>
        left is null ? right is null : right is not null && SameTypeShape(left, right);

    private static bool SameTypes<T>(System.Collections.Immutable.ImmutableArray<T> left,
        System.Collections.Immutable.ImmutableArray<T> right) where T : ITypeSymbol =>
        left.Length == right.Length && left.Zip(right).All(pair => SameTypeShape(pair.First, pair.Second));

    private static bool UsesExternAlias(SyntaxNode node, SemanticModel model, HashSet<ISymbol> visited, CancellationToken ct)
    {
        foreach (var identifier in node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            var alias = model.GetAliasInfo(identifier, ct);
            if (alias is null || !visited.Add(alias)) continue;
            if (alias.DeclaringSyntaxReferences.IsEmpty && alias.Name != "global")
                throw new PublicInvalidOperationException("Original namespace alias scope could not be resolved.");
            foreach (var reference in alias.DeclaringSyntaxReferences)
            {
                var declaration = reference.GetSyntax(ct);
                if (declaration is ExternAliasDirectiveSyntax) return true;
                if (declaration is not UsingDirectiveSyntax { Name: { } target })
                    throw new PublicInvalidOperationException("Original namespace alias scope could not be resolved.");
                if (UsesExternAlias(target, model.Compilation.GetSemanticModel(target.SyntaxTree), visited, ct))
                    return true;
            }
        }
        return false;
    }

    private static bool IsDeclarationName(SyntaxNode node) => node.AncestorsAndSelf().Any(ancestor =>
        ancestor.Parent is BaseNamespaceDeclarationSyntax declaration && ReferenceEquals(declaration.Name, ancestor) ||
        ancestor.Parent is NameEqualsSyntax);
}
