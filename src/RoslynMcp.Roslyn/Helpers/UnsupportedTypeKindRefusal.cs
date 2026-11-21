using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Builds the structured refusal for a type-moving tool that was pointed at a declaration kind it
/// cannot move. Enums derive from <c>BaseTypeDeclarationSyntax</c> (not <c>TypeDeclarationSyntax</c>)
/// and delegates are plain <c>MemberDeclarationSyntax</c> nodes; both are addressable by name from
/// <c>symbol_search</c>, so callers expect the tool to handle them. Naming the resolved kind keeps
/// the refusal distinct from a genuine "not found".
/// </summary>
internal static class UnsupportedTypeKindRefusal
{
    /// <summary>Creates the public refusal naming <paramref name="kind"/> and the calling <paramref name="toolName"/>.</summary>
    internal static PublicInvalidOperationException Create(SyntaxKind kind, string toolName)
    {
        var typeKindName = kind switch
        {
            SyntaxKind.EnumDeclaration => "Enum",
            SyntaxKind.DelegateDeclaration => "Delegate",
            _ => kind.ToString(),
        };
        return new PublicInvalidOperationException(
            $"type-kind {typeKindName} not supported for this refactor. " +
            $"{toolName} currently supports class, struct, record, and interface declarations only.");
    }
}
