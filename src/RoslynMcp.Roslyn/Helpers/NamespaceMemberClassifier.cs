using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Single definition of what occupies a C# namespace and of when a source file still has content.
/// "Type-like" means <see cref="BaseTypeDeclarationSyntax"/> (class, struct, interface, record,
/// enum) or <see cref="DelegateDeclarationSyntax"/>: both introduce a named type into their
/// enclosing namespace, but only the former derive from <see cref="TypeDeclarationSyntax"/>, so
/// <c>OfType&lt;TypeDeclarationSyntax&gt;()</c> silently drops enums and delegates.
/// </summary>
/// <remarks>
/// <c>NamespaceDependencyService</c> counts nested type-like declarations as well; it has
/// different semantics and keeps its own walk.
/// </remarks>
internal static class NamespaceMemberClassifier
{
    /// <summary>True for a class, struct, interface, record, enum or delegate declaration.</summary>
    internal static bool IsTypeLike(SyntaxNode node) =>
        node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax;

    /// <summary>True for a type-like declaration whose parent is the compilation unit or a namespace.</summary>
    internal static bool IsDirectNamespaceMember(SyntaxNode node) =>
        IsTypeLike(node) && node.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax;

    /// <summary>The declared identifier of a type-like node, or <see langword="null"/> for any other node.</summary>
    internal static string? GetTypeLikeName(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        DelegateDeclarationSyntax @delegate => @delegate.Identifier.ValueText,
        _ => null,
    };

    /// <summary>
    /// Fully-qualified source spelling of the namespace containing <paramref name="node"/>,
    /// or the empty string for the global namespace. Active tokens preserve identifier escapes
    /// without copying trivia such as conditional directives into the generated namespace.
    /// </summary>
    internal static string GetContainingNamespaceName(SyntaxNode node) =>
        GetContainingNamespaceName(node, static token => token.Text);

    /// <summary>
    /// True when <paramref name="root"/> still declares a type-like member directly inside the
    /// namespace named <paramref name="namespaceName"/>, in any namespace block of the file.
    /// Compares identifier values so source spelling differences do not hide a sibling type.
    /// </summary>
    internal static bool DeclaresTypeLikeMemberIn(SyntaxNode root, string namespaceName)
    {
        var namespaceValue = GetNameText(SyntaxFactory.ParseName(namespaceName), static token => token.ValueText);
        return root.DescendantNodes().Any(n => IsDirectNamespaceMember(n) &&
            string.Equals(GetContainingNamespaceName(n, static token => token.ValueText), namespaceValue, StringComparison.Ordinal));
    }

    private static string GetContainingNamespaceName(SyntaxNode node, Func<SyntaxToken, string> getTokenText) =>
        string.Join(".", node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse()
            .Select(n => GetNameText(n.Name, getTokenText)));

    private static string GetNameText(NameSyntax name, Func<SyntaxToken, string> getTokenText) =>
        string.Concat(name.DescendantTokens().Select(getTokenText));

    /// <summary>
    /// True when a source file whose declaration was just removed must still be kept, i.e. it holds
    /// anything beyond namespace shells, <c>using</c> directives and ordinary trivia: any
    /// non-namespace member (type, enum, delegate, global statement, ...), an assembly or module
    /// attribute list, or text disabled by conditional compilation, which parses as trivia and is
    /// therefore invisible to a member walk.
    /// </summary>
    internal static bool HasRemainingContent(SyntaxNode root)
    {
        if (root is CompilationUnitSyntax { AttributeLists.Count: > 0 })
            return true;
        if (root.DescendantNodes().OfType<MemberDeclarationSyntax>().Any(m => m is not BaseNamespaceDeclarationSyntax))
            return true;
        return root.DescendantTrivia().Any(t => t.IsKind(SyntaxKind.DisabledTextTrivia));
    }
}
