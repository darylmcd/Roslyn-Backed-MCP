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

    public async Task<IReadOnlyList<SymbolDto>> SearchSymbolsAsync(
        string workspaceId, string query, string? projectFilter, string? kindFilter, string? namespaceFilter, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<SymbolDto>();
        HashSet<string>? allowedProjectPaths = null;

        if (projectFilter is not null)
        {
            allowedProjectPaths = solution.Projects
                .Where(project => string.Equals(project.Name, projectFilter, StringComparison.OrdinalIgnoreCase))
                .SelectMany(project => project.Documents)
                .Where(document => !string.IsNullOrWhiteSpace(document.FilePath))
                .Select(document => Path.GetFullPath(document.FilePath!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (allowedProjectPaths.Count == 0)
            {
                return [];
            }
        }

        var symbols = await SymbolFinder.FindSourceDeclarationsWithPatternAsync(
            solution, query, SymbolFilter.All, ct).ConfigureAwait(false);

        foreach (var symbol in symbols)
        {
            if (ct.IsCancellationRequested) break;
            if (results.Count >= limit) break;

            if (kindFilter is not null && !MatchesKind(symbol, kindFilter))
                continue;

            if (namespaceFilter is not null && symbol.ContainingNamespace?.ToDisplayString() != namespaceFilter)
                continue;

            if (allowedProjectPaths is not null)
            {
                var inAllowedProject = symbol.Locations.Any(location =>
                    location.IsInSource &&
                    allowedProjectPaths.Contains(Path.GetFullPath(location.GetLineSpan().Path)));
                if (!inAllowedProject)
                {
                    continue;
                }
            }

            var dto = SymbolMapper.ToDto(symbol, solution);
            results.Add(dto);
        }

        return results;
    }

    public async Task<SymbolDto?> GetSymbolInfoAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
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
