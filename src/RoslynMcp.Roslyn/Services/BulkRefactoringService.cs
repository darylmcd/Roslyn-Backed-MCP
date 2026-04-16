using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class BulkRefactoringService : IBulkRefactoringService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<BulkRefactoringService> _logger;

    public BulkRefactoringService(IWorkspaceManager workspace, IPreviewStore previewStore, ILogger<BulkRefactoringService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
    }

    public async Task<RefactoringPreviewDto> PreviewBulkReplaceTypeAsync(
        string workspaceId, string oldTypeName, string newTypeName, string? scope, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);

        // Resolve old type
        var oldTypeSymbol = await ResolveTypeByNameAsync(solution, oldTypeName, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Type '{oldTypeName}' not found in the solution.");

        // Resolve new type (must exist)
        var newTypeSymbol = await ResolveTypeByNameAsync(solution, newTypeName, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Replacement type '{newTypeName}' not found in the solution.");

        var normalizedScope = (scope ?? "all").ToLowerInvariant();
        var validScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "parameters", "fields", "all" };
        if (!validScopes.Contains(normalizedScope))
            throw new ArgumentException($"Invalid scope '{scope}'. Valid values: parameters, fields, all.");

        var references = await SymbolFinder.FindReferencesAsync(oldTypeSymbol, solution, ct).ConfigureAwait(false);
        var newSolution = solution;
        var replacementCount = 0;

        // Process replacements document by document to avoid stale syntax trees
        var refsByDocument = references
            .SelectMany(r => r.Locations)
            .GroupBy(loc => loc.Document.Id);

        foreach (var docGroup in refsByDocument)
        {
            if (ct.IsCancellationRequested) break;

            var (updatedSolution, count) = await ReplaceReferencesInDocumentAsync(
                newSolution, docGroup.Key, docGroup, newTypeName, newTypeSymbol, normalizedScope, ct)
                .ConfigureAwait(false);
            newSolution = updatedSolution;
            replacementCount += count;
        }

        if (replacementCount == 0)
        {
            throw new InvalidOperationException(
                $"No replaceable references found for '{oldTypeName}' with scope '{normalizedScope}'.");
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Replace {replacementCount} reference(s) of '{oldTypeName}' with '{newTypeName}' (scope: {normalizedScope})";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description, changes);

        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static async Task<(Solution Solution, int Count)> ReplaceReferencesInDocumentAsync(
        Solution solution, DocumentId docId, IEnumerable<ReferenceLocation> locations,
        string newTypeName, INamedTypeSymbol newTypeSymbol, string scope, CancellationToken ct)
    {
        var doc = solution.GetDocument(docId);
        if (doc is null) return (solution, 0);

        var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return (solution, 0);

        var nodesToReplace = new Dictionary<SyntaxNode, SyntaxNode>();

        foreach (var refLocation in locations)
        {
            var node = root.FindNode(refLocation.Location.SourceSpan);
            if (node is not (IdentifierNameSyntax or GenericNameSyntax or QualifiedNameSyntax)) continue;
            if (!ShouldReplace(node, scope)) continue;

            var newNode = SyntaxFactory.IdentifierName(GetSimpleName(newTypeName))
                .WithTriviaFrom(node);
            nodesToReplace[node] = newNode;
        }

        if (nodesToReplace.Count == 0) return (solution, 0);

        root = root.ReplaceNodes(nodesToReplace.Keys, (original, _) =>
            nodesToReplace.TryGetValue(original, out var replacement) ? replacement : original);

        root = EnsureUsingDirective(root, newTypeSymbol.ContainingNamespace?.ToDisplayString());

        return (solution.WithDocumentSyntaxRoot(docId, root), nodesToReplace.Count);
    }

    private static SyntaxNode EnsureUsingDirective(SyntaxNode root, string? namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName) || root is not CompilationUnitSyntax compilationUnit)
            return root;

        if (compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName))
            return root;

        var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .NormalizeWhitespace()
            .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);
        return compilationUnit.AddUsings(newUsing);
    }

    private static bool ShouldReplace(SyntaxNode node, string scope)
    {
        var contextNode = node;
        while (contextNode.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NullableTypeSyntax or GenericNameSyntax or TypeArgumentListSyntax)
        {
            contextNode = contextNode.Parent;
        }

        var parent = contextNode.Parent;
        if (parent is null) return false;

        return scope switch
        {
            "parameters" => parent is ParameterSyntax,
            "fields" => parent is VariableDeclarationSyntax vd && vd.Parent is FieldDeclarationSyntax,
            "all" => parent is ParameterSyntax
                  || (parent is VariableDeclarationSyntax vd2 && (vd2.Parent is FieldDeclarationSyntax || vd2.Parent is LocalDeclarationStatementSyntax))
                  || parent is PropertyDeclarationSyntax
                  || parent is MethodDeclarationSyntax
                  || parent is SimpleBaseTypeSyntax,
            _ => false
        };
    }

    private static string GetSimpleName(string typeName)
    {
        var lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
    }

    private static async Task<INamedTypeSymbol?> ResolveTypeByNameAsync(Solution solution, string typeName, CancellationToken ct)
    {
        // Try fully qualified name first
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var symbol = compilation.GetTypeByMetadataName(typeName);
            if (symbol is not null) return symbol;
        }

        // Try simple name search
        var symbols = await SymbolFinder.FindSourceDeclarationsWithPatternAsync(
            solution, typeName, SymbolFilter.Type, ct).ConfigureAwait(false);

        return symbols.OfType<INamedTypeSymbol>()
            .FirstOrDefault(s => string.Equals(s.Name, typeName, StringComparison.Ordinal) ||
                                 string.Equals(s.ToDisplayString(), typeName, StringComparison.Ordinal));
    }
}
