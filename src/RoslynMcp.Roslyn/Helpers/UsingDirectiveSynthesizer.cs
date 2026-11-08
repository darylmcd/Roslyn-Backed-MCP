using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Shared using-directive synthesis for refactors that emit a new interface file from an
/// existing symbol. Consumers: <c>InterfaceExtractionService.BuildInterfaceFile</c> and
/// <c>CrossProjectRefactoringService.CreateInterfaceCompilationUnit</c>. Raw cross-project type
/// move does not use this helper (it copies source usings verbatim via
/// <c>MovedDeclarationFileWriter</c>). Merges the semantically-required namespaces with the
/// source file's using block: keeps source-file aliases and static usings, and keeps global
/// usings only when the receiving project differs (same-project globals are already in scope).
/// Adds plain-<c>using</c> directives for required namespaces that are not already supplied by
/// a source or destination-project global using, and drops unrelated plain source-file usings.
/// Copied globals always precede non-global usings (CS8915).
/// </summary>
internal static class UsingDirectiveSynthesizer
{
    public static SyntaxList<UsingDirectiveSyntax> BuildUsingDirectives(
        IEnumerable<UsingDirectiveSyntax> sourceUsings,
        IReadOnlyCollection<string> requiredNamespaces,
        bool preserveGlobalUsings,
        IReadOnlyCollection<string> destinationGlobalPlainNamespaces,
        SemanticModel? sourceSemanticModel,
        CancellationToken ct)
    {
        var result = new List<UsingDirectiveSyntax>();
        var alreadyAddedPlainNamespaces = new HashSet<string>(destinationGlobalPlainNamespaces, StringComparer.Ordinal);

        PreserveSpecialAndRequiredSourceUsings(
            sourceUsings,
            requiredNamespaces,
            preserveGlobalUsings,
            sourceSemanticModel,
            ct,
            result,
            alreadyAddedPlainNamespaces);
        AddMissingRequiredUsingDirectives(requiredNamespaces, alreadyAddedPlainNamespaces, result);
        return SortUsingDirectives(result);
    }

    /// <summary>Lift only aliases needed by projected signatures from the source type's visible namespace scopes.</summary>
    public static (IReadOnlyList<UsingDirectiveSyntax> SourceUsings, IReadOnlyList<UsingDirectiveSyntax> QualifiedNamespaceUsings)
        SelectVisibleAliasUsings(CompilationUnitSyntax sourceRoot, TypeDeclarationSyntax typeDeclaration,
            IReadOnlyCollection<string> referencedAliasNames, IReadOnlyCollection<ITypeSymbol> referencedUnqualifiedTypes,
            SemanticModel sourceSemanticModel, CancellationToken ct)
    {
        var namespaceUsings = typeDeclaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .SelectMany(scope => scope.Usings).ToArray(); // Nearest scope first.
        var selectedAliases = namespaceUsings
            .Where(directive => directive.Alias is { } alias && referencedAliasNames.Contains(alias.Name.Identifier.ValueText))
            .DistinctBy(directive => directive.Alias!.Name.Identifier.ValueText, StringComparer.Ordinal)
            .ToArray();
        var shadowedRootAliases = selectedAliases.Select(directive => directive.Alias!.Name.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        var sourceUsings = sourceRoot.Usings
            .Where(directive => directive.Alias is not { } alias || !shadowedRootAliases.Contains(alias.Name.Identifier.ValueText))
            .Concat(selectedAliases).ToArray();

        var qualifiedNamespaceUsings = namespaceUsings.Concat(sourceRoot.Usings)
            .Where(directive => directive.Alias is null && directive.Name is { } name
                && name.DescendantNodesAndSelf().OfType<AliasQualifiedNameSyntax>().Any()
                && (directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                    ? directive.Parent is BaseNamespaceDeclarationSyntax
                        && sourceSemanticModel.GetSymbolInfo(name, ct).Symbol is INamedTypeSymbol importedType
                        && referencedUnqualifiedTypes.OfType<INamedTypeSymbol>()
                            .Any(type => SymbolEqualityComparer.Default.Equals(type.ContainingType, importedType))
                    : sourceSemanticModel.GetSymbolInfo(name, ct).Symbol is INamespaceSymbol importedNamespace
                        && referencedUnqualifiedTypes.Any(type => SymbolEqualityComparer.Default.Equals(type.ContainingNamespace, importedNamespace))))
            .DistinctBy(directive => (directive.StaticKeyword.RawKind, Name: directive.Name!.ToString()));
        return (sourceUsings, qualifiedNamespaceUsings.ToArray());
    }

    /// <summary>Copy the nearest visible extern declaration for each alias required by emitted syntax.</summary>
    public static SyntaxList<ExternAliasDirectiveSyntax> BuildRequiredExternAliases(
        CompilationUnitSyntax sourceRoot, TypeDeclarationSyntax typeDeclaration,
        IEnumerable<MemberDeclarationSyntax> emittedMembers, IEnumerable<UsingDirectiveSyntax> emittedUsings)
    {
        var required = emittedMembers.SelectMany(member => member.DescendantNodesAndSelf().OfType<AliasQualifiedNameSyntax>())
            .Concat(emittedUsings.SelectMany(directive => directive.DescendantNodesAndSelf().OfType<AliasQualifiedNameSyntax>()))
            .Select(name => name.Alias.Identifier.ValueText)
            .Where(name => name != "global")
            .Distinct(StringComparer.Ordinal);
        var visible = typeDeclaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .SelectMany(scope => scope.Externs).Concat(sourceRoot.Externs).ToArray(); // Nearest scope first.
        return SyntaxFactory.List(required
            .Select(name => visible.FirstOrDefault(directive => directive.Identifier.ValueText == name))
            .Where(directive => directive is not null)
            .Select(directive => directive!.WithoutTrivia()));
    }

    public static async Task<IReadOnlyCollection<string>> GetProjectGlobalPlainNamespacesAsync(
        Project project, CancellationToken ct)
    {
        var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("Destination project compilation is unavailable.");
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (await tree.GetRootAsync(ct).ConfigureAwait(false) is not CompilationUnitSyntax root)
            {
                continue;
            }

            SemanticModel? model = null;
            foreach (var usingDirective in root.Usings)
            {
                if (!IsGlobalUsingDirective(usingDirective) || usingDirective.Alias is not null
                    || usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                    || usingDirective.Name is null)
                {
                    continue;
                }

                model ??= compilation.GetSemanticModel(tree);
                if (model.GetSymbolInfo(usingDirective.Name, ct).Symbol is INamespaceSymbol namespaceSymbol)
                {
                    namespaces.Add(namespaceSymbol.ToDisplayString());
                }
            }
        }

        return namespaces;
    }

    private static void PreserveSpecialAndRequiredSourceUsings(
        IEnumerable<UsingDirectiveSyntax> sourceUsings,
        IReadOnlyCollection<string> requiredNamespaces,
        bool preserveGlobalUsings,
        SemanticModel? sourceSemanticModel,
        CancellationToken ct,
        List<UsingDirectiveSyntax> result,
        ISet<string> alreadyAddedPlainNamespaces)
    {
        // Local aliases and static usings cannot be re-synthesized from symbols. Global
        // directives are already available throughout the source project; copying them into
        // another file of that project can duplicate an alias (CS1537).
        foreach (var source in sourceUsings)
        {
            var isGlobal = IsGlobalUsingDirective(source);
            if (isGlobal && source.Alias is null
                && !source.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                && GetGlobalUsingNamespace(source, sourceSemanticModel, ct) is { } globalNamespace
                && !alreadyAddedPlainNamespaces.Add(globalNamespace)
                && preserveGlobalUsings)
            {
                continue;
            }

            if (isGlobal && !preserveGlobalUsings)
            {
                continue;
            }

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

            if (alreadyAddedPlainNamespaces.Add(name))
            {
                result.Add(source);
            }
        }
    }

    private static void AddMissingRequiredUsingDirectives(
        IReadOnlyCollection<string> requiredNamespaces,
        ISet<string> alreadyAddedPlainNamespaces,
        List<UsingDirectiveSyntax> result)
    {
        foreach (var ns in requiredNamespaces)
        {
            if (alreadyAddedPlainNamespaces.Add(ns))
            {
                result.Add(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns)));
            }
        }
    }

    private static SyntaxList<UsingDirectiveSyntax> SortUsingDirectives(List<UsingDirectiveSyntax> usings)
    {
        // CS8915: every global using precedes every non-global using. Order: global usings
        // (plain/alias/static, original order), System and System.* plain alphabetically,
        // other plain alphabetically, then non-global aliases/static in their original order.
        var globalUsings = usings.Where(IsGlobalUsingDirective);
        var systemUsings = usings
            .Where(IsSystemUsingDirective)
            .OrderBy(u => GetUsingNamespace(u), StringComparer.Ordinal);
        var otherPlain = usings
            .Where(u => !IsSpecialUsingDirective(u) && !IsSystemUsingDirective(u))
            .OrderBy(u => GetUsingNamespace(u), StringComparer.Ordinal);
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

    private static string? GetGlobalUsingNamespace(
        UsingDirectiveSyntax source, SemanticModel? sourceSemanticModel, CancellationToken ct)
    {
        if (sourceSemanticModel is null)
        {
            return GetUsingNamespace(source);
        }

        return source.Name is { } name
            && sourceSemanticModel.GetSymbolInfo(name, ct).Symbol is INamespaceSymbol namespaceSymbol
            ? namespaceSymbol.ToDisplayString()
            : null;
    }

    private static bool IsSystemUsingDirective(UsingDirectiveSyntax usingDirective)
    {
        var name = GetUsingNamespace(usingDirective);
        return !IsSpecialUsingDirective(usingDirective)
            && (name == "System" || (name?.StartsWith("System.", StringComparison.Ordinal) ?? false));
    }

    private static bool IsSpecialUsingDirective(UsingDirectiveSyntax usingDirective)
    {
        return usingDirective.Alias is not null
            || usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
            || IsGlobalUsingDirective(usingDirective);
    }
}
