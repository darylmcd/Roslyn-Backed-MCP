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

        var name = SyntaxFactory.ParseName(namespaceName);
        return receivingOptions.LanguageVersion < LanguageVersion.CSharp10
            ? SyntaxFactory.NamespaceDeclaration(name)
            : SyntaxFactory.FileScopedNamespaceDeclaration(name);
    }
}
