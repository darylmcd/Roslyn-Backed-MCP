using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Single definition of namespace identity. The key is the identifier values of the dotted name
/// joined by <c>.</c>: no <c>@</c> escapes and no trivia, so <c>@Old.Sub</c>, <c>Old . Sub</c> and
/// <c>Old.Sub</c> are one namespace. Syntax <c>Name.ToString()</c> keeps both and must never be
/// compared with a symbol-derived or caller-supplied string.
/// </summary>
internal static class NamespaceNameIdentity
{
    private const string GlobalAliasPrefix = "global::";

    /// <summary>
    /// Identity key of a syntax name; empty for <see langword="null"/>. A leading <c>global::</c>
    /// qualifier does not change which namespace is named, so it is dropped.
    /// </summary>
    internal static string Of(NameSyntax? name)
    {
        if (name is null)
            return string.Empty;
        var key = string.Concat(name.DescendantTokens().Select(static token => token.ValueText));
        return key.StartsWith(GlobalAliasPrefix, StringComparison.Ordinal) ? key[GlobalAliasPrefix.Length..] : key;
    }

    /// <summary>
    /// Compilable source spelling of a syntax name: token texts (so <c>@</c> escapes stay) without
    /// any trivia, including trivia between the segments. Empty for <see langword="null"/>.
    /// </summary>
    internal static string SourceSpelling(NameSyntax? name) =>
        name is null ? string.Empty : string.Concat(name.DescendantTokens().Select(static token => token.Text));

    /// <summary>
    /// Identity key of a dotted spelling such as <c>Target.@class</c> or <c>Target.class</c>:
    /// one leading <c>@</c> is dropped per segment; blank input is the global namespace.
    /// </summary>
    internal static string Of(string? dottedName)
    {
        if (string.IsNullOrWhiteSpace(dottedName))
            return string.Empty;
        var trimmedName = dottedName.Trim();
        if (trimmedName.StartsWith(GlobalAliasPrefix, StringComparison.Ordinal))
            trimmedName = trimmedName[GlobalAliasPrefix.Length..];
        return string.Join('.', trimmedName.Split('.').Select(static segment =>
        {
            var trimmed = segment.Trim();
            return trimmed.StartsWith('@') ? trimmed[1..] : trimmed;
        }));
    }

    /// <summary>Identity key of a namespace symbol; the global namespace is empty.</summary>
    internal static string Of(INamespaceSymbol? symbol)
    {
        if (symbol is null || symbol.IsGlobalNamespace)
            return string.Empty;
        var segments = new Stack<string>();
        for (var current = symbol; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
            segments.Push(current.Name);
        return string.Join('.', segments);
    }

    /// <summary>True when the two dotted spellings name the same namespace.</summary>
    internal static bool AreSame(string? left, string? right) =>
        string.Equals(Of(left), Of(right), StringComparison.Ordinal);

    /// <summary>True for <c>using N;</c>: not an alias, not <c>using static</c>.</summary>
    internal static bool IsPlainImport(UsingDirectiveSyntax directive) =>
        directive.Alias is null && directive.StaticKeyword.IsKind(SyntaxKind.None);

    /// <summary>True when <paramref name="directive"/> is a plain import of the namespace with the given key.</summary>
    internal static bool ImportsNamespace(UsingDirectiveSyntax directive, string namespaceKey) =>
        IsPlainImport(directive)
        && directive.Name is { } name
        && string.Equals(Of(name), namespaceKey, StringComparison.Ordinal);
}
