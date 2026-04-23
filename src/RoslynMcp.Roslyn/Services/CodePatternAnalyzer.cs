using System.Text.RegularExpressions;
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

    public async Task<SemanticSearchResponseDto> SemanticSearchAsync(
        string workspaceId, string query, string? projectFilter, int limit, CancellationToken ct)
    {
        // BUG fix (semantic-search-html-decode): AI clients commonly double-encode angle
        // brackets when constructing JSON queries, sending `Task&lt;bool&gt;` instead of
        // `Task<bool>`. The downstream parser strips `<>,` to extract type fragments, so
        // encoded entities silently broke matching. HTML-decode on ingress so callers
        // don't have to know our encoding discipline.
        var decodedQuery = System.Net.WebUtility.HtmlDecode(query ?? string.Empty);

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<SemanticSearchResultDto>();
        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        var parse = ParseSemanticQuery(decodedQuery);
        bool Combined(ISymbol s) => parse.Predicates.All(p => p(s));
        await CollectSemanticSearchMatchesAsync(solution, projects, Combined, limit, results, ct).ConfigureAwait(false);

        string? warning = null;
        var fallbackStrategy = results.Count > 0 ? "structured" : "none";

        // First fallback: whole-query name substring match (legacy).
        if (results.Count == 0 && decodedQuery.Trim().Length >= 2 && !parse.UsedImplicitNameOnly)
        {
            var term = decodedQuery.Trim();
            bool NameMatch(ISymbol s) => s.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            await CollectSemanticSearchMatchesAsync(solution, projects, NameMatch, limit, results, ct)
                .ConfigureAwait(false);
            if (results.Count > 0)
            {
                warning = "No structured query match; results use a name substring fallback.";
                fallbackStrategy = "name-substring";
            }
        }

        // Second fallback (semantic-search-zero-results-verbose-query): verbose natural-language
        // queries ("async methods returning Task<bool> inside the Firewall namespace") are too long
        // to match any symbol name directly. Split into stemmed keyword tokens and match symbols
        // whose name contains ANY token. Significantly widens the net for long queries while keeping
        // signal-to-noise reasonable because noise words are dropped via the stopword list.
        if (results.Count == 0 && parse.Tokens.Count > 0)
        {
            var tokens = parse.Tokens;
            bool TokenOrMatch(ISymbol s)
            {
                foreach (var token in tokens)
                {
                    if (s.Name.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            await CollectSemanticSearchMatchesAsync(solution, projects, TokenOrMatch, limit, results, ct)
                .ConfigureAwait(false);
            if (results.Count > 0)
            {
                warning = "No structured query match; results use a token-or name fallback over parsed keywords.";
                fallbackStrategy = "token-or-match";
            }
        }

        if (results.Count == 0)
        {
            warning ??= $"No symbols matched this semantic query: {decodedQuery.Trim()}";
        }

        var debug = new SemanticSearchDebugDto(
            ParsedTokens: parse.Tokens,
            AppliedPredicates: parse.PredicateLabels,
            FallbackStrategy: fallbackStrategy);

        return new SemanticSearchResponseDto(results, warning, debug);
    }

    private static async Task CollectSemanticSearchMatchesAsync(
        Solution solution,
        IEnumerable<Project> projects,
        Func<ISymbol, bool> symbolMatches,
        int limit,
        List<SemanticSearchResultDto> results,
        CancellationToken ct)
    {
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

                    if (!symbolMatches(symbol)) continue;

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

    private readonly record struct SemanticQueryParse(
        List<Func<ISymbol, bool>> Predicates,
        IReadOnlyList<string> PredicateLabels,
        IReadOnlyList<string> Tokens,
        bool UsedImplicitNameOnly);

    /// <summary>
    /// Natural-language stopwords dropped by <see cref="ExtractTokens"/> before tokens are
    /// used for the verbose-query fallback. Added for
    /// <c>semantic-search-zero-results-verbose-query</c> so queries like
    /// "async methods returning Task&lt;bool&gt; inside the Firewall namespace" decompose
    /// into useful tokens (<c>Task</c>, <c>bool</c>, <c>Firewall</c>) without noise.
    /// Note: <c>Task</c> is intentionally NOT a stopword — "returning Task&lt;bool&gt;" queries
    /// want the token-OR fallback to match symbols named <c>TaskRunner</c>, <c>TaskQueue</c>, etc.
    /// </summary>
    private static readonly HashSet<string> SemanticQueryStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "for", "with", "by", "to", "from", "on", "in", "inside", "into", "at", "and", "or",
        "that", "this", "these", "those", "is", "are", "be", "being", "been",
        "all", "any", "some", "more", "than", "over",
        "methods", "method", "classes", "class", "types", "type", "members", "member",
        "properties", "property", "fields", "field", "interfaces", "interface",
        "return", "returns", "returning", "receive", "receiving",
        "async", "static", "public", "private", "internal", "protected",
        "abstract", "virtual", "sealed", "generic",
        "namespace", "namespaces",
        "parameter", "parameters",
    };

    /// <returns>A structured parse result with predicates, their human-readable labels, and the stopword-filtered token list.</returns>
    private static SemanticQueryParse ParseSemanticQuery(string query)
    {
        var predicates = new List<Func<ISymbol, bool>>();
        var predicateLabels = new List<string>();
        var normalizedQuery = query.ToLowerInvariant().Trim();

        AddKeywordPredicates(predicates, predicateLabels, normalizedQuery);
        AddStaticPredicate(predicates, predicateLabels, normalizedQuery);
        AddClassPredicate(predicates, predicateLabels, normalizedQuery);
        AddAccessibilityPredicate(predicates, predicateLabels, normalizedQuery);
        AddReturnTypePredicateWithLabel(predicates, predicateLabels, normalizedQuery);
        AddImplementingPredicateWithLabel(predicates, predicateLabels, normalizedQuery);
        AddParameterCountPredicate(predicates, predicateLabels, normalizedQuery);

        return CreateSemanticQueryParse(query, predicates, predicateLabels);
    }

    private static void AddKeywordPredicates(
        List<Func<ISymbol, bool>> predicates,
        List<string> predicateLabels,
        string query)
    {
        // Simple keyword predicates from the lookup table.
        // Use a word boundary for "interface" so "IDisposable" does not trigger the interface symbol filter.
        // BUG-N8: "asynchronous" must not satisfy the "async" keyword (substring match would).
        foreach (var (keyword, predicate) in KeywordPredicates)
        {
            if (!MatchesKeyword(query, keyword))
            {
                continue;
            }

            predicates.Add(predicate);
            predicateLabels.Add($"keyword:{keyword}");
        }
    }

    private static bool MatchesKeyword(string query, string keyword)
    {
        return keyword switch
        {
            "interface" => Regex.IsMatch(query, @"\binterface\b"),
            "async" => Regex.IsMatch(query, @"\basync\b"),
            _ => query.Contains(keyword, StringComparison.Ordinal)
        };
    }

    private static void AddStaticPredicate(
        List<Func<ISymbol, bool>> predicates,
        List<string> predicateLabels,
        string query)
    {
        if (!query.Contains("static", StringComparison.Ordinal) || query.Contains("non-static", StringComparison.Ordinal))
        {
            return;
        }

        predicates.Add(symbol => symbol.IsStatic);
        predicateLabels.Add("keyword:static");
    }

    private static void AddClassPredicate(
        List<Func<ISymbol, bool>> predicates,
        List<string> predicateLabels,
        string query)
    {
        if (!query.Contains("class", StringComparison.Ordinal) || query.Contains("classes implementing", StringComparison.Ordinal))
        {
            return;
        }

        predicates.Add(symbol => symbol is INamedTypeSymbol { TypeKind: TypeKind.Class });
        predicateLabels.Add("keyword:class");
    }

    private static void AddAccessibilityPredicate(
        List<Func<ISymbol, bool>> predicates,
        List<string> predicateLabels,
        string query)
    {
        // Accessibility keywords are mutually exclusive. Capture to local to avoid closure issues.
        foreach (var (keyword, accessibility) in AccessibilityKeywords)
        {
            if (!MatchesAccessibilityKeyword(query, keyword))
            {
                continue;
            }

            var capturedAccessibility = accessibility;
            predicates.Add(symbol => symbol.DeclaredAccessibility == capturedAccessibility);
            predicateLabels.Add($"accessibility:{keyword}");
            return;
        }
    }

    private static bool MatchesAccessibilityKeyword(string query, string keyword)
    {
        return keyword == "public"
            ? query.Contains("public", StringComparison.Ordinal) && !query.Contains("non-public", StringComparison.Ordinal)
            : query.Contains(keyword, StringComparison.Ordinal);
    }

    private static void AddReturnTypePredicateWithLabel(
        List<Func<ISymbol, bool>> predicates,
        List<string> predicateLabels,
        string query)
    {
        var beforeCount = predicates.Count;
        AddReturnTypePredicate(predicates, query);
        if (predicates.Count > beforeCount)
        {
            predicateLabels.Add("returning-type");
        }
    }

    private static void AddImplementingPredicateWithLabel(
        List<Func<ISymbol, bool>> predicates,
        List<string> predicateLabels,
        string query)
    {
        var beforeCount = predicates.Count;
        AddImplementingPredicate(predicates, query);
        if (predicates.Count > beforeCount)
        {
            predicateLabels.Add("implementing-interface");
        }
    }

    private static void AddParameterCountPredicate(
        List<Func<ISymbol, bool>> predicates,
        List<string> predicateLabels,
        string query)
    {
        var paramMatch = System.Text.RegularExpressions.Regex.Match(query, @"(?:more than|>)\s*(\d+)\s*param");
        if (!paramMatch.Success || !int.TryParse(paramMatch.Groups[1].Value, out var minParams))
        {
            return;
        }

        predicates.Add(symbol => symbol is IMethodSymbol method && method.Parameters.Length > minParams);
        predicateLabels.Add($"min-parameters:{minParams}");
    }

    private static SemanticQueryParse CreateSemanticQueryParse(
        string originalQuery,
        List<Func<ISymbol, bool>> predicates,
        List<string> predicateLabels)
    {
        var tokens = ExtractTokens(originalQuery);

        // Fallback: name-based search.
        if (predicates.Count == 0)
        {
            predicates.Add(symbol => symbol.Name.Contains(originalQuery, StringComparison.OrdinalIgnoreCase));
            predicateLabels.Add("name-contains");
            return new SemanticQueryParse(predicates, predicateLabels, tokens, UsedImplicitNameOnly: true);
        }

        return new SemanticQueryParse(predicates, predicateLabels, tokens, UsedImplicitNameOnly: false);
    }

    /// <summary>
    /// Splits a natural-language query into significant tokens for the verbose-query fallback.
    /// Drops stopwords, retains fragments that look like type names (<c>Task</c>, <c>IDisposable</c>),
    /// and normalizes by lowercasing the comparison — the returned tokens preserve original case
    /// so the debug payload reads naturally for the caller.
    /// </summary>
    private static IReadOnlyList<string> ExtractTokens(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();

        // Split on whitespace + common punctuation, but keep `<`/`>`/`,` removed so
        // "Task<bool>" → "Task" + "bool".
        var splits = Regex.Split(query, @"[\s<>,\(\)\[\]\{\}\""'\.\?!;:]+");
        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in splits)
        {
            var token = raw.Trim();
            if (token.Length < 2) continue;
            if (SemanticQueryStopwords.Contains(token)) continue;
            if (!seen.Add(token)) continue;
            tokens.Add(token);
        }
        return tokens;
    }

    private static void AddReturnTypePredicate(List<Func<ISymbol, bool>> predicates, string q)
    {
        if (!q.Contains("returning ") && !q.Contains("returns "))
            return;

        var returnTypeStart = q.IndexOf("returning ", StringComparison.Ordinal);
        var keywordLen = "returning ".Length;
        if (returnTypeStart < 0)
        {
            returnTypeStart = q.IndexOf("returns ", StringComparison.Ordinal);
            keywordLen = "returns ".Length;
        }

        if (returnTypeStart < 0) return;

        var slice = q[(returnTypeStart + keywordLen)..].Trim();
        foreach (var stop in new[] { " methods", " method", " async", " with ", " that ", " in " })
        {
            var idx = slice.IndexOf(stop, StringComparison.Ordinal);
            if (idx > 0)
                slice = slice[..idx].Trim();
        }

        var typeFragment = slice.Trim();
        if (string.IsNullOrEmpty(typeFragment)) return;

        var normalizedSpaced = Regex.Replace(typeFragment, @"[\<\>\,]", " ").Trim();
        var compactFragment = new string(typeFragment.Where(c => !char.IsWhiteSpace(c)).ToArray());

        predicates.Add(s =>
        {
            if (s is not IMethodSymbol m) return false;
            var ret = m.ReturnType.ToDisplayString();
            var retCompact = new string(ret.Where(c => !char.IsWhiteSpace(c)).ToArray());
            return ret.Contains(normalizedSpaced, StringComparison.OrdinalIgnoreCase) ||
                   ret.Contains(typeFragment, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrEmpty(compactFragment) &&
                    retCompact.Contains(compactFragment, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static void AddImplementingPredicate(List<Func<ISymbol, bool>> predicates, string q)
    {
        if (!q.Contains("implementing "))
            return;

        var ifaceName = q[(q.IndexOf("implementing ", StringComparison.Ordinal) + 13)..].Trim().Split(' ', ',')[0];
        if (string.IsNullOrEmpty(ifaceName)) return;

        var requireClass = Regex.IsMatch(q, @"\bclasses?\b");
        // dr-semantic-search-idisposable-predicate-accuracy: Use exact match on
        // ToDisplayString() instead of Contains() to prevent false positives (e.g.,
        // "IDisposable" matching "IAsyncDisposable" via substring).
        predicates.Add(s => s is INamedTypeSymbol t &&
            (!requireClass || t.TypeKind == TypeKind.Class) &&
            t.AllInterfaces.Any(i => i.Name.Equals(ifaceName, StringComparison.OrdinalIgnoreCase) ||
                i.ToDisplayString().Equals(ifaceName, StringComparison.OrdinalIgnoreCase)));
    }
}
