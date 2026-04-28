using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcp.Roslyn.Services;

public sealed class ConsumerAnalysisService : IConsumerAnalysisService
{
    private readonly IWorkspaceManager _workspace;

    public ConsumerAnalysisService(IWorkspaceManager workspace)
    {
        _workspace = workspace;
    }

    public async Task<ConsumerAnalysisDto?> FindConsumersAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        // Throw on unresolved symbol so callers see a structured NotFound envelope
        // (matches find_references contract).
        var symbol = await SymbolResolver.ResolveOrThrowAsync(solution, locator, ct).ConfigureAwait(false);

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var refLocations = references.SelectMany(r => r.Locations).ToList();

        // The materializer fetches syntax roots + semantic models in parallel under a bounded
        // semaphore. This is the dominant cost for high-fanout types like IWorkspaceManager.
        var materialized = await ReferenceLocationMaterializer.MaterializeAsync(refLocations, ct).ConfigureAwait(false);

        // Group references by containing type and classify dependency kind
        var consumerMap = new Dictionary<INamedTypeSymbol, HashSet<string>>(SymbolEqualityComparer.Default);

        foreach (var item in materialized)
        {
            if (ct.IsCancellationRequested) break;
            if (item.SyntaxRoot is null || item.SemanticModel is null) continue;

            var node = item.SyntaxRoot.FindNode(item.Source.Location.SourceSpan);
            var containingType = FindContainingType(node, item.SemanticModel, ct);
            if (containingType is null) continue;

            // Don't include self-references
            if (SymbolEqualityComparer.Default.Equals(containingType, symbol)) continue;

            if (!consumerMap.TryGetValue(containingType, out var kinds))
            {
                kinds = new HashSet<string>(StringComparer.Ordinal);
                consumerMap[containingType] = kinds;
            }

            var kind = ClassifyDependencyKind(node);
            kinds.Add(kind);
        }

        var consumers = consumerMap.Select(kvp =>
        {
            var type = kvp.Key;
            var loc = type.Locations.FirstOrDefault(l => l.IsInSource);
            var lineSpan = loc?.GetLineSpan();
            var project = loc is not null
                ? solution.GetDocument(loc.SourceTree!)?.Project.Name ?? "unknown"
                : "unknown";

            return new TypeConsumerDto(
                TypeName: type.Name,
                FullyQualifiedName: type.ToDisplayString(),
                FilePath: lineSpan?.Path ?? "unknown",
                Line: (lineSpan?.StartLinePosition.Line ?? 0) + 1,
                ProjectName: project,
                DependencyKinds: kvp.Value.OrderBy(k => k).ToList());
        })
        .OrderBy(c => c.TypeName)
        .ToList();

        var summary = $"Type '{symbol.Name}' has {consumers.Count} consumer(s) across " +
                      $"{consumers.Select(c => c.ProjectName).Distinct().Count()} project(s).";

        return new ConsumerAnalysisDto(
            SymbolMapper.ToDto(symbol, solution),
            consumers,
            summary);
    }

    private static INamedTypeSymbol? FindContainingType(SyntaxNode node, SemanticModel semanticModel, CancellationToken ct)
    {
        var current = node;
        while (current is not null)
        {
            if (current is TypeDeclarationSyntax typeDecl)
            {
                return semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;
            }
            current = current.Parent;
        }
        return null;
    }

    private static string ClassifyDependencyKind(SyntaxNode refNode)
    {
        var parent = refNode.Parent;

        // Walk up past qualified/generic names
        while (parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or GenericNameSyntax or NullableTypeSyntax)
        {
            refNode = parent;
            parent = parent.Parent;
        }

        if (parent is null) return nameof(TypeUsageClassification.Other);

        // services.AddSingleton<IFoo, Foo>() — type appears inside generic argument list
        if (parent is TypeArgumentListSyntax typeArgs &&
            typeArgs.Parent is InvocationExpressionSyntax inv &&
            inv.Expression is MemberAccessExpressionSyntax ma &&
            ma.Name.Identifier.Text is "AddSingleton" or "AddScoped" or "AddTransient" or "AddHostedService"
                or "AddKeyedSingleton" or "AddKeyedScoped" or "AddKeyedTransient")
            return nameof(TypeUsageClassification.DIRegistration);

        // Constructor parameter
        if (parent is ParameterSyntax param)
        {
            var paramParent = param.Parent?.Parent;
            if (paramParent is ConstructorDeclarationSyntax)
                return nameof(TypeUsageClassification.Constructor);
            return nameof(TypeUsageClassification.MethodParameter);
        }

        // Field declaration
        if (parent is VariableDeclarationSyntax varDecl)
        {
            if (varDecl.Parent is FieldDeclarationSyntax)
                return nameof(TypeUsageClassification.FieldType);
            return nameof(TypeUsageClassification.LocalVariable);
        }

        // Base type list
        if (parent is SimpleBaseTypeSyntax or BaseListSyntax)
            return nameof(TypeUsageClassification.BaseType);

        // Property type
        if (parent is PropertyDeclarationSyntax)
            return nameof(TypeUsageClassification.PropertyType);

        // Method return type
        if (parent is MethodDeclarationSyntax)
            return nameof(TypeUsageClassification.MethodReturnType);

        // Type argument (e.g., List<T>)
        if (parent is TypeArgumentListSyntax)
            return nameof(TypeUsageClassification.GenericArgument);

        return nameof(TypeUsageClassification.Other);
    }
}
