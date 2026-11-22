using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Builds the structured refusal for an interface-extraction route that was pointed at a declaration
/// kind it cannot handle. Enums derive from <c>BaseTypeDeclarationSyntax</c> (not <c>TypeDeclarationSyntax</c>)
/// and delegates are plain <c>MemberDeclarationSyntax</c> nodes; both are addressable by name from
/// <c>symbol_search</c>, so naming the resolved kind keeps the refusal distinct from a genuine "not found".
/// Every type-moving tool accepts all type-like kinds, so only the interface routes use this.
/// </summary>
internal static class UnsupportedTypeKindRefusal
{
    /// <summary>Supported-kinds text for the interface-extraction routes (classes and structs only).</summary>
    internal const string ClassAndStruct = "class and struct declarations";

    /// <summary>
    /// Creates the public refusal naming <paramref name="kind"/>, the calling <paramref name="toolName"/>
    /// and what that tool accepts (<paramref name="supportedKinds"/>).
    /// </summary>
    internal static PublicInvalidOperationException Create(SyntaxKind kind, string toolName, string supportedKinds)
    {
        var typeKindName = kind switch
        {
            SyntaxKind.EnumDeclaration => "Enum",
            SyntaxKind.DelegateDeclaration => "Delegate",
            _ => kind.ToString(),
        };
        return new PublicInvalidOperationException(
            $"type-kind {typeKindName} not supported for this refactor. " +
            $"{toolName} currently supports {supportedKinds} only.");
    }
}
