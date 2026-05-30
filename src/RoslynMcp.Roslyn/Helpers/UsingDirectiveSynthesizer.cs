using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Shared using-directive synthesis for refactors that emit a new file from an existing
/// symbol (interface extraction, cross-project type move). Merges the semantically-required
/// namespaces with the source file's using block: keeps source-file aliases, static usings,
/// and global usings (they cannot be re-synthesized from symbols), adds plain-<c>using</c>
/// directives for every required namespace, and drops plain source-file usings the semantic
/// walk determined are unnecessary (the previous text-grep heuristic kept too much or too
/// little). Previously duplicated verbatim in <c>InterfaceExtractionService</c> and
/// <c>CrossProjectRefactoringService</c>; the copies drifted independently, so the cluster
/// is hoisted here.
/// </summary>
internal static class UsingDirectiveSynthesizer
{
    public static SyntaxList<UsingDirectiveSyntax> BuildUsingDirectives(
        SyntaxList<UsingDirectiveSyntax> sourceUsings,
        IReadOnlyCollection<string> requiredNamespaces)
    {
        var result = new List<UsingDirectiveSyntax>();
        var alreadyAddedPlainNamespaces = new HashSet<string>(StringComparer.Ordinal);

        PreserveSpecialAndRequiredSourceUsings(
            sourceUsings,
            requiredNamespaces,
            result,
            alreadyAddedPlainNamespaces);
        AddMissingRequiredUsingDirectives(requiredNamespaces, alreadyAddedPlainNamespaces, result);
        return SortUsingDirectives(result);
    }

    private static void PreserveSpecialAndRequiredSourceUsings(
        SyntaxList<UsingDirectiveSyntax> sourceUsings,
        IReadOnlyCollection<string> requiredNamespaces,
        List<UsingDirectiveSyntax> result,
        ISet<string> alreadyAddedPlainNamespaces)
    {
        // Preserve aliases, static usings, and global usings — the semantic walker cannot
        // reproduce these, they may carry meaningful intent, and they never cause the
        // missing-using bug we're fixing.
        foreach (var source in sourceUsings)
        {
            if (IsSpecialUsingDirective(source))
            {
                result.Add(source);
                continue;
            }

            // Plain using: keep ONLY if the semantic walk identified this namespace as
            // required. This drops unrelated usings that pollute the generated file.
            var name = GetUsingNamespace(source);
            if (name is null || !requiredNamespaces.Contains(name))
            {
                continue;
            }

            result.Add(source);
            alreadyAddedPlainNamespaces.Add(name);
        }
    }

    private static void AddMissingRequiredUsingDirectives(
        IReadOnlyCollection<string> requiredNamespaces,
        ISet<string> alreadyAddedPlainNamespaces,
        List<UsingDirectiveSyntax> result)
    {
        foreach (var ns in requiredNamespaces)
        {
            if (!alreadyAddedPlainNamespaces.Contains(ns))
            {
                result.Add(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns)));
            }
        }
    }

    private static SyntaxList<UsingDirectiveSyntax> SortUsingDirectives(List<UsingDirectiveSyntax> usings)
    {
        // Sort: System.* first alphabetically, then other plain usings alphabetically,
        // then aliases/static/global at the end in their original order.
        var systemUsings = usings
            .Where(IsSystemUsingDirective)
            .OrderBy(u => u.Name!.ToString(), StringComparer.Ordinal);
        var otherPlain = usings
            .Where(u => !IsSpecialUsingDirective(u) && !IsSystemUsingDirective(u))
            .OrderBy(u => u.Name!.ToString(), StringComparer.Ordinal);
        var specials = usings.Where(IsSpecialUsingDirective);
        return SyntaxFactory.List(systemUsings.Concat(otherPlain).Concat(specials));
    }

    private static string? GetUsingNamespace(UsingDirectiveSyntax source)
    {
        var name = source.Name?.ToString();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static bool IsSystemUsingDirective(UsingDirectiveSyntax usingDirective)
    {
        return !IsSpecialUsingDirective(usingDirective)
            && (usingDirective.Name?.ToString().StartsWith("System", StringComparison.Ordinal) ?? false);
    }

    private static bool IsSpecialUsingDirective(UsingDirectiveSyntax usingDirective)
    {
        return usingDirective.Alias is not null
            || usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
            || usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword);
    }
}
