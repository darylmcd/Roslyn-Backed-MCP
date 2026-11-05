using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Rebinds references from retained source members to the extracted member's new owner.
/// References are matched by symbol identity, so overloads, static access, and method groups
/// cannot be redirected by a same-spelled unrelated symbol. Extracted declarations themselves
/// are skipped and retain their original intra-helper references. Also records the names of the
/// extracted members the retained code still references (each rebound to `_field.Member` /
/// `NewType.Member`), because only those must be reachable from the source type.
/// </summary>
internal sealed class TypeExtractionSameFileConsumerRewriter : CSharpSyntaxRewriter
{
    private readonly SemanticModel _semanticModel;
    private readonly IReadOnlySet<ISymbol> _extractedSymbols;
    private readonly string _newTypeName;
    private readonly string _fieldName;
    private readonly string _sourceTypeName;
    private readonly CancellationToken _ct;
    private readonly HashSet<string> _referencedExtractedNames = new(StringComparer.Ordinal);

    private TypeExtractionSameFileConsumerRewriter(
        SemanticModel semanticModel,
        IReadOnlySet<ISymbol> extractedSymbols,
        string newTypeName,
        string fieldName,
        string sourceTypeName,
        CancellationToken ct)
    {
        _semanticModel = semanticModel;
        _extractedSymbols = extractedSymbols;
        _newTypeName = newTypeName;
        _fieldName = fieldName;
        _sourceTypeName = sourceTypeName;
        _ct = ct;
    }

    /// <summary>
    /// Rewrites every retained reference to a member declared by <paramref name="analysisNodes"/> and returns the
    /// rewritten type plus the names of the extracted members the retained code still references.
    /// </summary>
    internal static (TypeDeclarationSyntax Rewritten, IReadOnlySet<string> ReferencedExtractedNames) Rewrite(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        IReadOnlyList<SyntaxNode> analysisNodes,
        string newTypeName,
        string fieldName,
        string sourceTypeName,
        CancellationToken ct)
    {
        var extractedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var node in analysisNodes)
        {
            if (!IsDeclarationNode(node)) continue;

            var symbol = semanticModel.GetDeclaredSymbol(node, ct);
            if (symbol is not null)
            {
                extractedSymbols.Add(symbol.OriginalDefinition);
            }
        }

        var rewriter = new TypeExtractionSameFileConsumerRewriter(
            semanticModel,
            extractedSymbols,
            newTypeName,
            fieldName,
            sourceTypeName,
            ct);
        var rewritten = (TypeDeclarationSyntax)rewriter.Visit(typeDecl)!;
        return (rewritten, rewriter._referencedExtractedNames);
    }

    /// <summary>
    /// True for the node kinds the semantic passes may hand to
    /// <c>SemanticModel.GetDeclaredSymbol</c>. The analysis list also carries non-declaration
    /// context nodes (a field's declared type and attribute lists) purely so their descendants get
    /// walked for dangling references.
    /// </summary>
    internal static bool IsDeclarationNode(SyntaxNode node)
    {
        return node is MemberDeclarationSyntax or VariableDeclaratorSyntax;
    }

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        _ct.ThrowIfCancellationRequested();
        if (node is not null && IsDeclarationNode(node))
        {
            var declared = _semanticModel.GetDeclaredSymbol(node, _ct);
            if (declared is not null && IsExtracted(declared))
            {
                return node;
            }
        }

        return base.Visit(node);
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (!TryGetExtractedSymbol(node.Name, out var symbol))
        {
            return base.VisitMemberAccessExpression(node);
        }

        EnsureInstanceReferenceCanBeRewritten(node, symbol);
        var memberName = (SimpleNameSyntax)base.Visit(node.Name)!;
        ExpressionSyntax owner;
        if (symbol.IsStatic)
        {
            owner = SyntaxFactory.IdentifierName(_newTypeName);
        }
        else
        {
            var receiver = (ExpressionSyntax)base.Visit(node.Expression)!;
            owner = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver.WithoutTrivia(),
                SyntaxFactory.IdentifierName(_fieldName));
        }

        return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                owner,
                memberName.WithoutTrivia())
            .WithTriviaFrom(node);
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        => RewriteUnqualifiedName(node) ?? base.VisitIdentifierName(node);

    public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
        => RewriteUnqualifiedName(node) ?? base.VisitGenericName(node);

    private ExpressionSyntax? RewriteUnqualifiedName(SimpleNameSyntax node)
    {
        if (node.Parent is MemberAccessExpressionSyntax { Name: var name } && name == node)
        {
            return null;
        }

        if (!TryGetExtractedSymbol(node, out var symbol))
        {
            return null;
        }

        if (node.Parent is MemberBindingExpressionSyntax)
        {
            throw CannotRewrite(symbol, "conditional-access receiver");
        }

        EnsureInstanceReferenceCanBeRewritten(node, symbol);
        var owner = symbol.IsStatic ? _newTypeName : _fieldName;
        return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(owner),
                node.WithoutTrivia())
            .WithTriviaFrom(node);
    }

    private bool TryGetExtractedSymbol(SimpleNameSyntax node, out ISymbol symbol)
    {
        var resolved = _semanticModel.GetSymbolInfo(node, _ct).Symbol;
        if (resolved is not null && IsExtracted(resolved))
        {
            _referencedExtractedNames.Add(resolved.Name);
            symbol = resolved;
            return true;
        }

        symbol = null!;
        return false;
    }

    private bool IsExtracted(ISymbol symbol)
        => _extractedSymbols.Contains(symbol.OriginalDefinition);

    private void EnsureInstanceReferenceCanBeRewritten(SyntaxNode node, ISymbol symbol)
    {
        if (symbol.IsStatic)
        {
            return;
        }

        if (node.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().Any())
        {
            throw CannotRewrite(symbol, "constructor body before the injected composition field is assigned");
        }
    }

    private InvalidOperationException CannotRewrite(ISymbol symbol, string reason)
        => new(
            $"Refusing to extract type '{_newTypeName}' from '{_sourceTypeName}': retained same-file code " +
            $"references extracted member '{symbol.Name}' through a {reason}, which cannot be rebound without " +
            "changing behavior. Refactor that reference first, then retry the extraction.");
}
