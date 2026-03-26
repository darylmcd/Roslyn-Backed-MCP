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

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (var invocation in invocations)
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
                    if (symbolInfo.Symbol is not IMethodSymbol method) continue;

                    var containingNs = method.ContainingType?.ContainingNamespace?.ToDisplayString() ?? "";
                    var containingTypeName = method.ContainingType?.Name ?? "";

                    var usageKind = (containingNs, containingTypeName, method.Name) switch
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

                // Also check for typeof() expressions
                var typeofExprs = root.DescendantNodes().OfType<TypeOfExpressionSyntax>();
                foreach (var typeofExpr in typeofExprs)
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
        }

        return results;
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

    private static List<Func<ISymbol, bool>> ParseSemanticQuery(string query)
    {
        var predicates = new List<Func<ISymbol, bool>>();
        var q = query.ToLowerInvariant().Trim();

        // Pattern: "async methods" or "async"
        if (q.Contains("async"))
            predicates.Add(s => s is IMethodSymbol m && m.IsAsync);

        // Pattern: "returning Task<bool>" or "returns Task<"
        if (q.Contains("returning ") || q.Contains("returns "))
        {
            var returnTypeStart = q.IndexOf("returning ", StringComparison.Ordinal);
            if (returnTypeStart < 0) returnTypeStart = q.IndexOf("returns ", StringComparison.Ordinal);
            if (returnTypeStart >= 0)
            {
                var afterKeyword = q[(q.IndexOf(' ', returnTypeStart) + 1)..].Trim();
                // Take until next space or end
                var returnType = afterKeyword.Split(' ', ',')[0].Trim();
                predicates.Add(s => s is IMethodSymbol m &&
                    m.ReturnType.ToDisplayString().Contains(returnType, StringComparison.OrdinalIgnoreCase));
            }
        }

        // Pattern: "implementing IDisposable"
        if (q.Contains("implementing "))
        {
            var ifaceName = q[(q.IndexOf("implementing ", StringComparison.Ordinal) + 13)..].Trim().Split(' ', ',')[0];
            predicates.Add(s => s is INamedTypeSymbol t &&
                t.AllInterfaces.Any(i => i.Name.Equals(ifaceName, StringComparison.OrdinalIgnoreCase) ||
                    i.ToDisplayString().Contains(ifaceName, StringComparison.OrdinalIgnoreCase)));
        }

        // Pattern: "abstract methods" or "abstract classes"
        if (q.Contains("abstract"))
            predicates.Add(s => s.IsAbstract);

        // Pattern: "static methods" or "static"
        if (q.Contains("static") && !q.Contains("non-static"))
            predicates.Add(s => s.IsStatic);

        // Pattern: "virtual"
        if (q.Contains("virtual"))
            predicates.Add(s => s.IsVirtual);

        // Pattern: "methods" (filter to methods only)
        if (q.Contains("method"))
            predicates.Add(s => s is IMethodSymbol { MethodKind: MethodKind.Ordinary });

        // Pattern: "classes"
        if (q.Contains("class") && !q.Contains("classes implementing"))
            predicates.Add(s => s is INamedTypeSymbol { TypeKind: TypeKind.Class });

        // Pattern: "interfaces"
        if (q.Contains("interface"))
            predicates.Add(s => s is INamedTypeSymbol { TypeKind: TypeKind.Interface });

        // Pattern: "properties"
        if (q.Contains("propert"))
            predicates.Add(s => s is IPropertySymbol);

        // Pattern: "fields"
        if (q.Contains("field"))
            predicates.Add(s => s is IFieldSymbol);

        // Pattern: "more than N parameters" or "> N parameters"
        var paramMatch = System.Text.RegularExpressions.Regex.Match(q, @"(?:more than|>)\s*(\d+)\s*param");
        if (paramMatch.Success && int.TryParse(paramMatch.Groups[1].Value, out var minParams))
            predicates.Add(s => s is IMethodSymbol m && m.Parameters.Length > minParams);

        // Pattern: "public" / "private" / "internal" / "protected"
        if (q.Contains("public") && !q.Contains("non-public"))
            predicates.Add(s => s.DeclaredAccessibility == Accessibility.Public);
        else if (q.Contains("private"))
            predicates.Add(s => s.DeclaredAccessibility == Accessibility.Private);
        else if (q.Contains("internal"))
            predicates.Add(s => s.DeclaredAccessibility == Accessibility.Internal);
        else if (q.Contains("protected"))
            predicates.Add(s => s.DeclaredAccessibility == Accessibility.Protected);

        // Pattern: "sealed"
        if (q.Contains("sealed"))
            predicates.Add(s => s.IsSealed);

        // Pattern: "generic"
        if (q.Contains("generic"))
            predicates.Add(s => s is INamedTypeSymbol { IsGenericType: true } or IMethodSymbol { IsGenericMethod: true });

        // If no predicates matched, do a name-based search as fallback
        if (predicates.Count == 0)
            predicates.Add(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        return predicates;
    }
}
