using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// find-type-consumers-file-granularity-rollup: produces the per-file rollup that backs the
/// <c>find_type_consumers</c> MCP tool. Reuses <see cref="SymbolFinder.FindReferencesAsync"/>
/// (the same reference index already used by <see cref="ReferenceService"/>) and groups the
/// resulting reference sites by file path, classifying each site's syntactic context into one
/// of the well-known kinds (<c>using</c>, <c>ctor</c>, <c>inherit</c>, <c>field</c>,
/// <c>local</c>) or <c>other</c> when the context does not match any known pattern.
/// </summary>
public sealed class TypeConsumersService : ITypeConsumersService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<TypeConsumersService> _logger;

    public TypeConsumersService(IWorkspaceManager workspace, ILogger<TypeConsumersService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TypeConsumerFileRollupDto>> FindTypeConsumersAsync(
        string workspaceId,
        string typeName,
        int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("typeName must be non-empty.", nameof(typeName));
        }
        if (limit < 1)
        {
            throw new ArgumentException("limit must be >= 1.", nameof(limit));
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await ResolveTypeAsync(solution, typeName, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"No type found matching '{typeName}'. Pass a fully qualified metadata name "
                + "(e.g. 'SampleLib.IAnimal') or a short type name; generic arity uses backtick "
                + "notation (e.g. 'List`1').");

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var refLocations = references.SelectMany(r => r.Locations).ToList();
        if (refLocations.Count == 0)
        {
            return [];
        }

        // Group by file path first so we only fetch each document's syntax root once per file
        // even when a file has dozens of reference sites. Order the groups deterministically.
        var byFile = refLocations
            .Where(loc => loc.Location.IsInSource && loc.Document is not null)
            .GroupBy(loc => loc.Location.GetLineSpan().Path ?? string.Empty, StringComparer.Ordinal)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToList();

        var rollups = new List<TypeConsumerFileRollupDto>(byFile.Count);
        foreach (var group in byFile)
        {
            ct.ThrowIfCancellationRequested();
            var sample = group.First();
            var root = await sample.Document!.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root is null) continue;

            var kindSet = new SortedSet<string>(StringComparer.Ordinal);
            var siteCount = 0;
            foreach (var loc in group)
            {
                siteCount++;
                var node = root.FindNode(loc.Location.SourceSpan, findInsideTrivia: true, getInnermostNodeForTie: true);
                kindSet.Add(ClassifyKind(node));
            }

            rollups.Add(new TypeConsumerFileRollupDto(
                FilePath: group.Key,
                Kinds: kindSet.ToList(),
                Count: siteCount));
        }

        return rollups
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.FilePath, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Resolves a type by either a fully qualified metadata name (fast path via
    /// <see cref="Compilation.GetTypeByMetadataName"/>) or a short/partial name (fallback via
    /// <see cref="SymbolFinder.FindDeclarationsAsync"/>). Returns the first match.
    /// </summary>
    private static async Task<INamedTypeSymbol?> ResolveTypeAsync(Solution solution, string typeName, CancellationToken ct)
    {
        // Fast path: try metadata name resolution against every project.
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;
            var typeSymbol = compilation.GetTypeByMetadataName(typeName);
            if (typeSymbol is not null)
            {
                return typeSymbol;
            }
        }

        // Fallback: short-name lookup. Strip generic arity for the search query (Roslyn's
        // FindDeclarationsAsync matches on the bare identifier), then filter back to types
        // whose metadata name matches the original input.
        var searchName = StripArity(typeName);
        foreach (var project in solution.Projects)
        {
            var declarations = await SymbolFinder.FindDeclarationsAsync(project, searchName, ignoreCase: false, SymbolFilter.Type, ct).ConfigureAwait(false);
            var match = declarations
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(t => MatchesTypeName(t, typeName));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string StripArity(string typeName)
    {
        // "List`1" -> "List"; "Namespace.Foo`2" -> "Foo"
        var lastDot = typeName.LastIndexOf('.');
        var simple = lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
        var backtick = simple.IndexOf('`');
        return backtick >= 0 ? simple[..backtick] : simple;
    }

    private static bool MatchesTypeName(INamedTypeSymbol type, string typeName)
    {
        // Accept either a fully qualified match (Namespace.Type or Namespace.Type`N) or a
        // bare-identifier match (Type / Type`N) so callers can pass a short name when the
        // short name is unambiguous.
        var metadataName = type.MetadataName; // includes `N for generics
        var fullyQualified = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);
        var fullyQualifiedMetadata = type.ContainingNamespace?.IsGlobalNamespace == false
            ? $"{type.ContainingNamespace.ToDisplayString()}.{metadataName}"
            : metadataName;

        return string.Equals(metadataName, typeName, StringComparison.Ordinal)
            || string.Equals(type.Name, typeName, StringComparison.Ordinal)
            || string.Equals(fullyQualifiedMetadata, typeName, StringComparison.Ordinal)
            || string.Equals(fullyQualified, typeName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Classifies a single reference site by walking the syntax-node ancestor chain. Returns
    /// one of the documented kinds (<c>using</c>, <c>ctor</c>, <c>inherit</c>, <c>field</c>,
    /// <c>local</c>) or <c>other</c> when no recognized syntactic context wraps the reference.
    /// </summary>
    private static string ClassifyKind(SyntaxNode? node)
    {
        if (node is null) return "other";

        // Walk through type-name wrappers (qualified names, nullable, array, generic name) so
        // the classifier sees the surrounding declaration context rather than the bare token.
        var current = node;
        while (current.Parent is QualifiedNameSyntax
               or AliasQualifiedNameSyntax
               or NullableTypeSyntax
               or ArrayTypeSyntax
               or GenericNameSyntax
               or TypeArgumentListSyntax)
        {
            current = current.Parent;
        }

        for (var ancestor = current; ancestor is not null; ancestor = ancestor.Parent)
        {
            switch (ancestor)
            {
                case UsingDirectiveSyntax:
                    return "using";
                case ObjectCreationExpressionSyntax:
                case ImplicitObjectCreationExpressionSyntax:
                    return "ctor";
                case BaseTypeSyntax:
                case BaseListSyntax:
                    return "inherit";
                case FieldDeclarationSyntax:
                case EventFieldDeclarationSyntax:
                    return "field";
                case LocalDeclarationStatementSyntax:
                    return "local";
                // Stop walking once we leave a member declaration boundary so a nested type
                // reference inside a method body does not bubble up to its containing field
                // initializer or class declaration accidentally.
                case MemberDeclarationSyntax when ancestor is not BaseTypeDeclarationSyntax:
                    return "other";
            }
        }

        return "other";
    }
}
