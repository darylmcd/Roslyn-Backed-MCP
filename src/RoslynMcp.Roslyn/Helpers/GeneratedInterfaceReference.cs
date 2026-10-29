using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Spells a reference to a generated interface so it binds to that exact symbol at the use site:
/// the bare name when it already binds there, otherwise the namespace-qualified name, otherwise the
/// <c>global::</c>-qualified name (an alias or type shadows the first namespace segment).
/// </summary>
internal static class GeneratedInterfaceReference
{
    public static TypeSyntax Create(INamedTypeSymbol generated, SemanticModel model, int position)
    {
        var bare = Name(generated.Name);
        if (Binds(model, position, bare, generated)) return bare;
        if (generated.ContainingNamespace is { IsGlobalNamespace: false })
        {
            var qualified = Qualify(generated, global: false);
            if (Binds(model, position, qualified, generated)) return qualified;
        }

        return Qualify(generated, global: true);
    }

    /// <summary>Identity across compilations: a referenced project's symbol is not guaranteed to be the same instance.</summary>
    public static bool IsSame(ISymbol? candidate, INamedTypeSymbol expected) =>
        candidate is INamedTypeSymbol type &&
        (SymbolEqualityComparer.Default.Equals(type, expected) || IsSameAcrossCompilations(type, expected));

    // Same metadata name, same chain of containing types (a nested `Outer.IFoo` is not `IFoo`), and for the
    // outermost type the same namespace and assembly.
    private static bool IsSameAcrossCompilations(INamedTypeSymbol type, INamedTypeSymbol expected)
    {
        if (type.MetadataName != expected.MetadataName) return false;
        if (type.ContainingType is null || expected.ContainingType is null)
        {
            return type.ContainingType is null && expected.ContainingType is null &&
                type.ContainingNamespace.ToDisplayString() == expected.ContainingNamespace.ToDisplayString() &&
                type.ContainingAssembly?.Identity.Name == expected.ContainingAssembly?.Identity.Name;
        }

        return IsSameAcrossCompilations(type.ContainingType, expected.ContainingType);
    }

    private static bool Binds(SemanticModel model, int position, TypeSyntax candidate, INamedTypeSymbol generated) =>
        IsSame(model.GetSpeculativeSymbolInfo(position, candidate, SpeculativeBindingOption.BindAsTypeOrNamespace).Symbol, generated);

    private static TypeSyntax Qualify(INamedTypeSymbol generated, bool global)
    {
        var segments = new Stack<string>();
        for (var ns = generated.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
            segments.Push(ns.Name);
        var leaf = Name(generated.Name);
        var globalAlias = SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword));
        if (segments.Count == 0) return SyntaxFactory.AliasQualifiedName(globalAlias, leaf);

        NameSyntax name = global
            ? SyntaxFactory.AliasQualifiedName(globalAlias, Name(segments.Pop()))
            : Name(segments.Pop());
        while (segments.Count > 0)
            name = SyntaxFactory.QualifiedName(name, Name(segments.Pop()));
        return SyntaxFactory.QualifiedName(name, leaf);
    }

    /// <summary>
    /// An identifier whose text is escaped (<c>@namespace</c>) and whose value is not: a node built from
    /// the escaped text alone would bind to a name that literally contains the '@'.
    /// </summary>
    private static IdentifierNameSyntax Name(string identifier)
    {
        var escaped = SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;
        return SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(default, SyntaxKind.IdentifierToken, escaped, identifier, default));
    }

    /// <summary>The interface declared by <paramref name="interfaceDocumentId"/> in <paramref name="solution"/>.</summary>
    public static async Task<INamedTypeSymbol> ResolveGeneratedAsync(Solution solution, DocumentId interfaceDocumentId, CancellationToken ct)
    {
        var document = solution.GetDocument(interfaceDocumentId)
            ?? throw InvalidOperationErrors.Internal("Generated interface document is unavailable.");
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("Generated interface syntax is unavailable.");
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("Generated interface semantic model is unavailable.");
        return model.GetDeclaredSymbol(root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single(), ct)
            ?? throw InvalidOperationErrors.Internal("Generated interface symbol is unavailable.");
    }

    /// <summary>
    /// Adds the generated interface to the base list of the type declared at <paramref name="typeSpan"/> of
    /// <paramref name="sourceDocumentId"/>, spelled so it binds at that declaration. A type that already
    /// implements the generated symbol is left alone.
    /// </summary>
    public static async Task<Solution> AddToBaseListAsync(
        Solution solution, DocumentId sourceDocumentId, TextSpan typeSpan, DocumentId interfaceDocumentId,
        bool multilineContinuation, CancellationToken ct)
    {
        var generated = await ResolveGeneratedAsync(solution, interfaceDocumentId, ct).ConfigureAwait(false);
        var document = solution.GetDocument(sourceDocumentId)
            ?? throw InvalidOperationErrors.Internal("Source document is unavailable.");
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("Source syntax is unavailable.");
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("Source semantic model is unavailable.");
        var declaration = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Single(n => n.Span == typeSpan);
        if (model.GetDeclaredSymbol(declaration, ct) is { } symbol && symbol.Interfaces.Any(i => IsSame(i, generated)))
            return solution;

        var reference = Create(generated, model, declaration.SpanStart);
        var updated = InterfaceBaseTypeWriter.AddBaseType(declaration, reference, multilineContinuation);
        return solution.WithDocumentSyntaxRoot(sourceDocumentId, root.ReplaceNode(declaration, updated));
    }
}
