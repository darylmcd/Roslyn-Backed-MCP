using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class CodePatternAnalyzer : ICodePatternAnalyzer
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<CodePatternAnalyzer> _logger;

    public CodePatternAnalyzer(IWorkspaceManager workspace, ILogger<CodePatternAnalyzer> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReflectionUsageDto>> FindReflectionUsagesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<ReflectionUsageDto>();
        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested) break;
                if (PathFilter.IsGeneratedOrContentFile(tree.FilePath)) continue;

                try
                {
                    var semanticModel = compilation.GetSemanticModel(tree);
                    var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                    CollectReflectionInvocations(root, semanticModel, results, ct);
                    CollectTypeofUsages(root, semanticModel, results, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to analyze syntax tree {Path} for reflection usages, skipping",
                        tree.FilePath);
                }
            }
        }

        return results;
    }

    private static void CollectReflectionInvocations(
        SyntaxNode root, SemanticModel semanticModel, List<ReflectionUsageDto> results, CancellationToken ct)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
            if (symbolInfo.Symbol is not IMethodSymbol method) continue;

            var usageKind = ClassifyReflectionUsage(method);
            if (usageKind is null) continue;

            var lineSpan = invocation.GetLocation().GetLineSpan();
            var containingMethod = GetContainingMethodName(invocation, semanticModel, ct);
            var typeArg = method.TypeArguments.Length > 0
                ? method.TypeArguments[0].ToDisplayString()
                : null;

            results.Add(new ReflectionUsageDto(
                usageKind,
                method.ToDisplayString(),
                lineSpan.Path,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1,
                containingMethod,
                typeArg));
        }
    }

    private static string? ClassifyReflectionUsage(IMethodSymbol method)
    {
        var containingNs = method.ContainingType?.ContainingNamespace?.ToDisplayString() ?? "";
        var containingTypeName = method.ContainingType?.Name ?? "";

        return (containingNs, containingTypeName, method.Name) switch
        {
            ("System", "Type", "GetType") => "Type.GetType",
            ("System", "Activator", "CreateInstance") => "Activator.CreateInstance",
            ("System.Reflection", "Assembly", "GetType") => "Assembly.GetType",
            ("System.Reflection", "Assembly", "Load") => "Assembly.Load",
            ("System.Reflection", "Assembly", "LoadFrom") => "Assembly.LoadFrom",
            ("System.Reflection", _, "Invoke") => "Reflection.Invoke",
            ("System.Reflection", "PropertyInfo", "GetValue") => "PropertyInfo.GetValue",
            ("System.Reflection", "PropertyInfo", "SetValue") => "PropertyInfo.SetValue",
            ("System.Reflection", "FieldInfo", "GetValue") => "FieldInfo.GetValue",
            ("System.Reflection", "FieldInfo", "SetValue") => "FieldInfo.SetValue",
            (_, "Type", "GetMethod" or "GetProperty" or "GetField" or "GetMember" or "GetMethods"
                or "GetProperties" or "GetFields" or "GetMembers") => $"Type.{method.Name}",
            _ => null
        };
    }

    private static void CollectTypeofUsages(
        SyntaxNode root, SemanticModel semanticModel, List<ReflectionUsageDto> results, CancellationToken ct)
    {
        foreach (var typeofExpr in root.DescendantNodes().OfType<TypeOfExpressionSyntax>())
        {
            var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type, ct);
            var lineSpan = typeofExpr.GetLocation().GetLineSpan();
            var containingMethod = GetContainingMethodName(typeofExpr, semanticModel, ct);

            results.Add(new ReflectionUsageDto(
                "typeof",
                "typeof()",
                lineSpan.Path,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1,
                containingMethod,
                typeInfo.Type?.ToDisplayString()));
        }
    }

    public async Task<IReadOnlyList<SemanticSearchResultDto>> SemanticSearchAsync(
        string workspaceId, string query, string? projectFilter, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<SemanticSearchResultDto>();
        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        // Parse the query into predicates
        var predicates = ParseSemanticQuery(query);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || results.Count >= limit) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested || results.Count >= limit) break;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                var declarations = root.DescendantNodes().OfType<MemberDeclarationSyntax>();
                foreach (var decl in declarations)
                {
                    if (ct.IsCancellationRequested || results.Count >= limit) break;

                    var symbol = semanticModel.GetDeclaredSymbol(decl, ct);
                    if (symbol is null || symbol is INamespaceSymbol) continue;
                    if (symbol.IsImplicitlyDeclared) continue;

                    if (!predicates.All(p => p(symbol))) continue;

                    var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
                    if (location is null) continue;
                    var lineSpan = location.GetLineSpan();

                    results.Add(new SemanticSearchResultDto(
                        symbol.Name,
                        symbol.Kind.ToString(),
                        lineSpan.Path,
                        lineSpan.StartLinePosition.Line + 1,
                        symbol.ToDisplayString(),
                        symbol.ContainingType?.Name,
                        symbol.ContainingNamespace?.ToDisplayString(),
                        SymbolHandleSerializer.CreateHandle(symbol)));
                }
            }
        }

        return results;
    }

    private static string? GetContainingMethodName(SyntaxNode node, SemanticModel model, CancellationToken ct)
    {
        var ancestor = node.Ancestors().FirstOrDefault(a =>
            a is MethodDeclarationSyntax or PropertyDeclarationSyntax or ConstructorDeclarationSyntax);
        if (ancestor is null) return null;
        var symbol = model.GetDeclaredSymbol(ancestor, ct);
        return symbol?.ToDisplayString();
    }

    private static readonly Dictionary<string, Func<ISymbol, bool>> KeywordPredicates = new(StringComparer.Ordinal)
    {
        ["async"] = s => s is IMethodSymbol m && m.IsAsync,
        ["abstract"] = s => s.IsAbstract,
        ["virtual"] = s => s.IsVirtual,
        ["sealed"] = s => s.IsSealed,
        ["method"] = s => s is IMethodSymbol { MethodKind: MethodKind.Ordinary },
        ["interface"] = s => s is INamedTypeSymbol { TypeKind: TypeKind.Interface },
        ["propert"] = s => s is IPropertySymbol,
        ["field"] = s => s is IFieldSymbol,
        ["generic"] = s => s is INamedTypeSymbol { IsGenericType: true } or IMethodSymbol { IsGenericMethod: true },
    };

    private static readonly Dictionary<string, Accessibility> AccessibilityKeywords = new(StringComparer.Ordinal)
    {
        ["public"] = Accessibility.Public,
        ["private"] = Accessibility.Private,
        ["internal"] = Accessibility.Internal,
        ["protected"] = Accessibility.Protected,
    };

    private static List<Func<ISymbol, bool>> ParseSemanticQuery(string query)
    {
        var predicates = new List<Func<ISymbol, bool>>();
        var q = query.ToLowerInvariant().Trim();

        // Simple keyword predicates from the lookup table
        foreach (var (keyword, predicate) in KeywordPredicates)
        {
            if (q.Contains(keyword))
                predicates.Add(predicate);
        }

        // "static" with guard against "non-static"
        if (q.Contains("static") && !q.Contains("non-static"))
            predicates.Add(s => s.IsStatic);

        // "classes" with guard against "classes implementing"
        if (q.Contains("class") && !q.Contains("classes implementing"))
            predicates.Add(s => s is INamedTypeSymbol { TypeKind: TypeKind.Class });

        // Accessibility keywords (mutually exclusive) — capture to local to avoid closure issue
        foreach (var (keyword, accessibility) in AccessibilityKeywords)
        {
            var capturedAccessibility = accessibility;
            if (keyword == "public" ? q.Contains("public") && !q.Contains("non-public") : q.Contains(keyword))
            {
                predicates.Add(s => s.DeclaredAccessibility == capturedAccessibility);
                break;
            }
        }

        // "returning/returns <type>"
        AddReturnTypePredicate(predicates, q);

        // "implementing <interface>"
        AddImplementingPredicate(predicates, q);

        // "more than N parameters"
        var paramMatch = System.Text.RegularExpressions.Regex.Match(q, @"(?:more than|>)\s*(\d+)\s*param");
        if (paramMatch.Success && int.TryParse(paramMatch.Groups[1].Value, out var minParams))
            predicates.Add(s => s is IMethodSymbol m && m.Parameters.Length > minParams);

        // Fallback: name-based search
        if (predicates.Count == 0)
            predicates.Add(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        return predicates;
    }

    private static void AddReturnTypePredicate(List<Func<ISymbol, bool>> predicates, string q)
    {
        if (!q.Contains("returning ") && !q.Contains("returns "))
            return;

        var returnTypeStart = q.IndexOf("returning ", StringComparison.Ordinal);
        if (returnTypeStart < 0) returnTypeStart = q.IndexOf("returns ", StringComparison.Ordinal);
        if (returnTypeStart < 0) return;

        var afterKeyword = q[(q.IndexOf(' ', returnTypeStart) + 1)..].Trim();
        var returnType = afterKeyword.Split(' ', ',')[0].Trim();
        predicates.Add(s => s is IMethodSymbol m &&
            m.ReturnType.ToDisplayString().Contains(returnType, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddImplementingPredicate(List<Func<ISymbol, bool>> predicates, string q)
    {
        if (!q.Contains("implementing "))
            return;

        var ifaceName = q[(q.IndexOf("implementing ", StringComparison.Ordinal) + 13)..].Trim().Split(' ', ',')[0];
        predicates.Add(s => s is INamedTypeSymbol t &&
            t.AllInterfaces.Any(i => i.Name.Equals(ifaceName, StringComparison.OrdinalIgnoreCase) ||
                i.ToDisplayString().Contains(ifaceName, StringComparison.OrdinalIgnoreCase)));
    }
}
