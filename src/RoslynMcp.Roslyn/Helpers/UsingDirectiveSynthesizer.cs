using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Shared using-directive synthesis for refactors that emit a new interface file from an
/// existing symbol. Consumers: <c>InterfaceExtractionService.BuildInterfaceFile</c> and
/// <c>CrossProjectRefactoringService.CreateInterfaceCompilationUnit</c>. Raw cross-project type
/// move does not use this helper (it copies source usings verbatim via
/// <c>MovedDeclarationFileWriter</c>). Merges the semantically-required namespaces with the
/// source file's using block: keeps source-file aliases, static usings, and global usings (they
/// cannot be re-synthesized from symbols), adds plain-<c>using</c> directives for every required
/// namespace, and drops plain source-file usings the semantic walk determined are unnecessary.
/// Output always lists global usings before non-global ones (CS8915).
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
        // CS8915: every global using precedes every non-global using. Order: global usings
        // (plain/alias/static, original order), System.* plain alphabetically, other plain
        // alphabetically, then non-global aliases/static in their original order.
        var globalUsings = usings.Where(IsGlobalUsingDirective);
        var systemUsings = usings
            .Where(IsSystemUsingDirective)
            .OrderBy(u => u.Name!.ToString(), StringComparer.Ordinal);
        var otherPlain = usings
            .Where(u => !IsSpecialUsingDirective(u) && !IsSystemUsingDirective(u))
            .OrderBy(u => u.Name!.ToString(), StringComparer.Ordinal);
        var localSpecials = usings.Where(u => IsSpecialUsingDirective(u) && !IsGlobalUsingDirective(u));
        return SyntaxFactory.List(globalUsings.Concat(systemUsings).Concat(otherPlain).Concat(localSpecials));
    }

    private static bool IsGlobalUsingDirective(UsingDirectiveSyntax usingDirective)
    {
        return usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword);
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
            || IsGlobalUsingDirective(usingDirective);
    }
}
