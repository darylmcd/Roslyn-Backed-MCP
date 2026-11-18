using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

internal static class ReceivingNamespaceFactory
{
    internal static CSharpParseOptions RequireCSharpOptions(ParseOptions? receivingOptions) =>
        receivingOptions as CSharpParseOptions
        ?? throw InvalidOperationErrors.Internal("The receiving project must have C# parse options.");

    internal static BaseNamespaceDeclarationSyntax? Create(string namespaceName, CSharpParseOptions receivingOptions)
    {
        ArgumentNullException.ThrowIfNull(receivingOptions);
        if (string.IsNullOrWhiteSpace(namespaceName))
            return null;

        var name = CreateName(namespaceName);
        return receivingOptions.LanguageVersion < LanguageVersion.CSharp10
            ? SyntaxFactory.NamespaceDeclaration(name)
            : SyntaxFactory.FileScopedNamespaceDeclaration(name);
    }

    /// <summary>
    /// Parses a dotted namespace name, escaping each keyword segment with <c>@</c>. The single
    /// definition every namespace-declaration builder shares, so a keyword segment never parses
    /// into a missing identifier.
    /// </summary>
    internal static NameSyntax CreateName(string namespaceName) =>
        SyntaxFactory.ParseName(string.Join('.', namespaceName.Split('.').Select(EscapeKeywordSegment)));

    private static string EscapeKeywordSegment(string segment)
    {
        return segment.StartsWith('@') || SyntaxFacts.GetKeywordKind(segment) == SyntaxKind.None
            ? segment
            : '@' + segment;
    }
}
