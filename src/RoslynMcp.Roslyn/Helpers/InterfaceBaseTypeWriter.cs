using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Preserves header token boundaries and trivia when adding an extracted interface.</summary>
internal static class InterfaceBaseTypeWriter
{
    public static TypeDeclarationSyntax AddBaseType(TypeDeclarationSyntax declaration, TypeSyntax interfaceName, bool multilineContinuation)
    {
        if (declaration.BaseList?.Types.Any(baseType => baseType.Type.WithoutTrivia().IsEquivalentTo(interfaceName.WithoutTrivia())) == true)
            return declaration;

        // Header trivia belongs after the inserted base type, whether its old
        // owner is a base type, primary constructor, generic list or type name.
        var anchor = declaration.BaseList?.Types.Last().GetLastToken()
            ?? declaration.ParameterList?.CloseParenToken
            ?? declaration.TypeParameterList?.GreaterThanToken
            ?? declaration.Identifier;
        var trailing = anchor.TrailingTrivia;
        declaration = declaration.ReplaceToken(anchor, anchor.WithTrailingTrivia(default(SyntaxTriviaList)));
        if (trailing.Count == 0 && declaration.ConstraintClauses.Count > 0)
            trailing = SyntaxFactory.TriviaList(SyntaxFactory.Space);
        var interfaceType = SyntaxFactory.SimpleBaseType(interfaceName.WithoutTrivia()).WithTrailingTrivia(trailing);
        if (declaration.BaseList is { } existing)
        {
            var leading = multilineContinuation
                ? SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(Environment.NewLine), SyntaxFactory.Whitespace("    "))
                : SyntaxFactory.TriviaList(SyntaxFactory.Space);
            declaration = declaration.WithBaseList(existing.AddTypes(interfaceType.WithLeadingTrivia(leading)));
        }
        else
        {
            declaration = declaration.WithBaseList(SyntaxFactory.BaseList(
                SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(interfaceType.WithLeadingTrivia(SyntaxFactory.Space)))
                .WithLeadingTrivia(SyntaxFactory.Space));
        }

        var brace = declaration.OpenBraceToken;
        if (!brace.IsMissing && !brace.LeadingTrivia.Any(SyntaxKind.EndOfLineTrivia) &&
            !brace.GetPreviousToken().TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia))
            declaration = declaration.WithOpenBraceToken(brace.WithLeadingTrivia(
                SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(Environment.NewLine)).AddRange(brace.LeadingTrivia)));
        return declaration;
    }
}
