using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class SymbolSearchService : ISymbolSearchService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<SymbolSearchService> _logger;

    public SymbolSearchService(IWorkspaceManager workspace, ILogger<SymbolSearchService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    // symbol-search-partial-match-gap: `FindSourceDeclarationsWithPatternAsync` matches against
    // simple names and handles camelCase/substring at that level, but cannot hit queries that
    // mention any containing namespace (e.g. "Conversation.Manager" won't reach
    // `ITChatBot.Conversation.ConversationManager` because Roslyn's pattern matcher runs on the
    // simple-name level). We augment the primary pattern search with a fully-qualified-name
    // substring pass that walks the solution's named types so namespace-qualified queries
    // surface the declaration.
    private static readonly SymbolDisplayFormat QualifiedNameOnlyFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None);

    public async Task<IReadOnlyList<SymbolDto>> SearchSymbolsAsync(
        string workspaceId, string query, string? projectFilter, string? kindFilter, string? namespaceFilter, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<SymbolDto>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        var (allowedProjectPaths, earlyEmpty) = ResolveAllowedProjectPaths(solution, projectFilter);
        if (earlyEmpty)
            return [];

        await RunPrimaryPatternSearchAsync(
            solution, query, limit, kindFilter, namespaceFilter, allowedProjectPaths, results, seenKeys, ct).ConfigureAwait(false);

        // symbol-search-partial-match-gap: FQN-substring pass. Only runs when we still have
        // budget under `limit` — most queries are single-word simple-name lookups and the
        // primary pattern search already nails those.
        if (results.Count < limit && !ct.IsCancellationRequested)
        {
            await RunFqnSubstringFallbackAsync(
                solution, query, limit, kindFilter, namespaceFilter, allowedProjectPaths, results, seenKeys, ct).ConfigureAwait(false);
        }

        return results;
    }

    private static (HashSet<string>? AllowedProjectPaths, bool EarlyEmpty) ResolveAllowedProjectPaths(
        Solution solution, string? projectFilter)
    {
        if (projectFilter is null)
            return (null, false);

        var allowedProjectPaths = solution.Projects
            .Where(project => string.Equals(project.Name, projectFilter, StringComparison.OrdinalIgnoreCase))
            .SelectMany(project => project.Documents)
            .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
            .Select(document => Path.GetFullPath(document.FilePath!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (allowedProjectPaths, allowedProjectPaths.Count == 0);
    }

    private static async Task RunPrimaryPatternSearchAsync(
        Solution solution,
        string query,
        int limit,
        string? kindFilter,
        string? namespaceFilter,
        HashSet<string>? allowedProjectPaths,
        List<SymbolDto> results,
        HashSet<string> seenKeys,
        CancellationToken ct)
    {
        var symbols = await SymbolFinder.FindSourceDeclarationsWithPatternAsync(
            solution, query, SymbolFilter.All, ct).ConfigureAwait(false);

        foreach (var symbol in symbols)
        {
            if (!TryAddSymbol(symbol, limit, kindFilter, namespaceFilter, allowedProjectPaths, solution, results, seenKeys, ct))
                break;
        }
    }

    private static async Task RunFqnSubstringFallbackAsync(
        Solution solution,
        string query,
        int limit,
        string? kindFilter,
        string? namespaceFilter,
        HashSet<string>? allowedProjectPaths,
        List<SymbolDto> results,
        HashSet<string> seenKeys,
        CancellationToken ct)
    {
        foreach (var project in solution.Projects)
        {
            if (ct.IsCancellationRequested) break;
            if (results.Count >= limit) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            if (!CollectFqnMatchesForCompilation(
                compilation, query, limit, kindFilter, namespaceFilter, allowedProjectPaths, solution, results, seenKeys, ct))
            {
                break;
            }
        }
    }

    private static bool CollectFqnMatchesForCompilation(
        Compilation compilation,
        string query,
        int limit,
        string? kindFilter,
        string? namespaceFilter,
        HashSet<string>? allowedProjectPaths,
        Solution solution,
        List<SymbolDto> results,
        HashSet<string> seenKeys,
        CancellationToken ct)
    {
        foreach (var typeSymbol in EnumerateNamedTypes(compilation.GlobalNamespace))
        {
            if (ct.IsCancellationRequested) return false;
            if (results.Count >= limit) return false;

            var fqn = typeSymbol.ToDisplayString(QualifiedNameOnlyFormat);
            if (fqn.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (!TryAddSymbol(typeSymbol, limit, kindFilter, namespaceFilter, allowedProjectPaths, solution, results, seenKeys, ct))
                return false;

            // Also walk members whose FQN (Namespace.Type.Member) matches.
            foreach (var member in typeSymbol.GetMembers())
            {
                if (ct.IsCancellationRequested) return false;
                if (results.Count >= limit) return false;
                if (member.IsImplicitlyDeclared) continue;

                var memberFqn = member.ToDisplayString(QualifiedNameOnlyFormat);
                if (memberFqn.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (!TryAddSymbol(member, limit, kindFilter, namespaceFilter, allowedProjectPaths, solution, results, seenKeys, ct))
                    return false;
            }
        }

        return true;
    }

    private static bool TryAddSymbol(
        ISymbol symbol,
        int limit,
        string? kindFilter,
        string? namespaceFilter,
        HashSet<string>? allowedProjectPaths,
        Solution solution,
        List<SymbolDto> results,
        HashSet<string> seenKeys,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        if (results.Count >= limit) return false;

        if (kindFilter is not null && !MatchesKind(symbol, kindFilter))
            return true;

        if (namespaceFilter is not null && symbol.ContainingNamespace?.ToDisplayString() != namespaceFilter)
            return true;

        if (allowedProjectPaths is not null)
        {
            var inAllowedProject = symbol.Locations.Any(location =>
                location.IsInSource &&
                allowedProjectPaths.Contains(Path.GetFullPath(location.GetLineSpan().Path)));
            if (!inAllowedProject) return true;
        }

        // Dedupe by SymbolDisplayString — same symbol surfaces from both passes for simple names.
        var key = symbol.ToDisplayString();
        if (!seenKeys.Add(key)) return true;

        results.Add(SymbolMapper.ToDto(symbol, solution));
        return true;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol child)
            {
                foreach (var nested in EnumerateNamedTypes(child))
                    yield return nested;
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var nested in type.GetTypeMembers())
                    yield return nested;
            }
        }
    }

    public async Task<SymbolDto?> GetSymbolInfoAsync(string workspaceId, SymbolLocator locator, CancellationToken ct, bool allowAdjacent = false)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        // symbol-info-lenient-whitespace-resolution: the tool flag maps inversely — strict=true
        // when the caller has NOT opted into adjacent-token walking. Default false preserves
        // the stricter contract against silent whitespace drift.
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct, strict: !allowAdjacent).ConfigureAwait(false);
        return symbol is not null ? SymbolMapper.ToDto(symbol, solution) : null;
    }

    public async Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(string workspaceId, string filePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null)
            throw new FileNotFoundException($"Source file not found in the loaded workspace: {filePath}");

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return [];

        return CollectSymbols(root, ct);
    }

    private static bool MatchesKind(ISymbol symbol, string kindFilter)
    {
        var kinds = new[]
        {
            symbol.Kind.ToString(),
            symbol is INamedTypeSymbol namedType ? namedType.TypeKind.ToString() : null
        };

        return kinds.Any(kind => string.Equals(kind, kindFilter, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<DocumentSymbolDto> CollectSymbols(SyntaxNode node, CancellationToken ct)
    {
        var symbols = new List<DocumentSymbolDto>();

        foreach (var child in node.ChildNodes())
        {
            if (ct.IsCancellationRequested) break;

            switch (child)
            {
                case NamespaceDeclarationSyntax ns:
                    symbols.Add(new DocumentSymbolDto(
                        ns.Name.ToString(), "Namespace", null,
                        ns.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        ns.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        CollectSymbols(ns, ct)));
                    break;
                case FileScopedNamespaceDeclarationSyntax fns:
                    symbols.AddRange(CollectSymbols(fns, ct));
                    break;
                case TypeDeclarationSyntax typeDecl:
                    var kind = typeDecl switch
                    {
                        ClassDeclarationSyntax => "Class",
                        InterfaceDeclarationSyntax => "Interface",
                        StructDeclarationSyntax => "Struct",
                        RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "RecordStruct" : "Record",
                        _ => "Type"
                    };
                    symbols.Add(new DocumentSymbolDto(
                        typeDecl.Identifier.Text, kind,
                        typeDecl.Modifiers.Select(m => m.Text).ToList(),
                        typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        typeDecl.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        CollectMembers(typeDecl, ct)));
                    break;
                case EnumDeclarationSyntax enumDecl:
                    symbols.Add(new DocumentSymbolDto(
                        enumDecl.Identifier.Text, "Enum",
                        enumDecl.Modifiers.Select(m => m.Text).ToList(),
                        enumDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        enumDecl.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        enumDecl.Members.Select(m => new DocumentSymbolDto(
                            m.Identifier.Text, "EnumMember", null,
                            m.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            m.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                            null)).ToList()));
                    break;
                case DelegateDeclarationSyntax delegateDecl:
                    symbols.Add(new DocumentSymbolDto(
                        delegateDecl.Identifier.Text, "Delegate",
                        delegateDecl.Modifiers.Select(m => m.Text).ToList(),
                        delegateDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        delegateDecl.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        null));
                    break;
                case GlobalStatementSyntax:
                    break;
            }
        }

        return symbols;
    }

    private static IReadOnlyList<DocumentSymbolDto> CollectMembers(TypeDeclarationSyntax typeDecl, CancellationToken ct)
    {
        var members = new List<DocumentSymbolDto>();

        foreach (var member in typeDecl.Members)
        {
            if (ct.IsCancellationRequested) break;

            switch (member)
            {
                case MethodDeclarationSyntax method:
                    members.Add(new DocumentSymbolDto(
                        method.Identifier.Text, "Method",
                        method.Modifiers.Select(m => m.Text).ToList(),
                        method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        method.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        null));
                    break;
                case PropertyDeclarationSyntax prop:
                    members.Add(new DocumentSymbolDto(
                        prop.Identifier.Text, "Property",
                        prop.Modifiers.Select(m => m.Text).ToList(),
                        prop.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        prop.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        null));
                    break;
                case FieldDeclarationSyntax field:
                    foreach (var variable in field.Declaration.Variables)
                    {
                        members.Add(new DocumentSymbolDto(
                            variable.Identifier.Text, "Field",
                            field.Modifiers.Select(m => m.Text).ToList(),
                            field.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            field.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                            null));
                    }
                    break;
                case EventDeclarationSyntax evt:
                    members.Add(new DocumentSymbolDto(
                        evt.Identifier.Text, "Event",
                        evt.Modifiers.Select(m => m.Text).ToList(),
                        evt.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        evt.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        null));
                    break;
                case ConstructorDeclarationSyntax ctor:
                    members.Add(new DocumentSymbolDto(
                        ctor.Identifier.Text, "Constructor",
                        ctor.Modifiers.Select(m => m.Text).ToList(),
                        ctor.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        ctor.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        null));
                    break;
                case TypeDeclarationSyntax nestedType:
                    var kind = nestedType switch
                    {
                        ClassDeclarationSyntax => "Class",
                        InterfaceDeclarationSyntax => "Interface",
                        StructDeclarationSyntax => "Struct",
                        RecordDeclarationSyntax => "Record",
                        _ => "Type"
                    };
                    members.Add(new DocumentSymbolDto(
                        nestedType.Identifier.Text, kind,
                        nestedType.Modifiers.Select(m => m.Text).ToList(),
                        nestedType.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        nestedType.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        CollectMembers(nestedType, ct)));
                    break;
            }
        }

        return members;
    }
}
