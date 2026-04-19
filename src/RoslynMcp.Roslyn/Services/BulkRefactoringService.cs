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
        var crossedGenericBoundary = false;
        while (contextNode.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NullableTypeSyntax or GenericNameSyntax or TypeArgumentListSyntax)
        {
            if (contextNode.Parent is GenericNameSyntax or TypeArgumentListSyntax)
            {
                crossedGenericBoundary = true;
            }
            contextNode = contextNode.Parent;
        }

        var parent = contextNode.Parent;
        if (parent is null) return false;

        return scope switch
        {
            // scope=parameters covers method parameter declarations AND generic arguments
            // in implemented-interface / base-class declarations. The latter keeps the class's
            // interface-contract signatures in sync with the parameter rewrites — otherwise a
            // parameter-only rewrite produces an exact-match violation on interface members
            // whose signatures are parameterised by the old type (e.g. IValidateOptions<T>).
            "parameters" => parent is ParameterSyntax
                  || (crossedGenericBoundary && parent is SimpleBaseTypeSyntax),
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

    // ═══════════════════════════════════════════════════════════════════════════════════
    // replace-invocation-pattern-refactor: method-level call-site rewrite with argument
    // reorder. Parses FQ method signatures "Type.Method(P1,P2,P3)", resolves both methods
    // by overload match, builds a positional mapping (new[i] = old[oldIndexOf(new[i])]),
    // and rewrites every InvocationExpressionSyntax of the old method through SymbolFinder.
    // Named-argument callers keep their names (they already locate by name, so the reorder
    // is a no-op for them); positional or mixed callers are reordered by index.
    // ═══════════════════════════════════════════════════════════════════════════════════

    public async Task<RefactoringPreviewDto> PreviewReplaceInvocationAsync(
        string workspaceId, string oldMethod, string newMethod, string? scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(oldMethod))
            throw new ArgumentException("oldMethod must be a fully-qualified signature like 'Type.Method(P1,P2)'.", nameof(oldMethod));
        if (string.IsNullOrWhiteSpace(newMethod))
            throw new ArgumentException("newMethod must be a fully-qualified signature like 'Type.Method(P1,P2)'.", nameof(newMethod));

        // replace-invocation scope today is always 'all' — the parameter is reserved for
        // future file / project scoping. Reject unknown values eagerly so a stale caller
        // gets a clear error instead of silent expanded scope.
        var normalizedScope = (scope ?? "all").ToLowerInvariant();
        if (!string.Equals(normalizedScope, "all", StringComparison.Ordinal))
            throw new ArgumentException(
                $"Invalid scope '{scope}'. Only 'all' is supported for replace_invocation_preview today.", nameof(scope));

        var oldSig = ParseMethodSignature(oldMethod, nameof(oldMethod));
        var newSig = ParseMethodSignature(newMethod, nameof(newMethod));

        var solution = _workspace.GetCurrentSolution(workspaceId);

        var oldMethodSymbol = await ResolveMethodBySignatureAsync(solution, oldSig, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not resolve oldMethod '{oldMethod}'. Ensure the fully-qualified type name and parameter-type list match an existing method overload.");

        var newMethodSymbol = await ResolveMethodBySignatureAsync(solution, newSig, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not resolve newMethod '{newMethod}'. Ensure the fully-qualified type name and parameter-type list match an existing method overload.");

        // Build index mapping: newArgs[i] = oldArgs[indexMap[i]]. Match the new method's
        // parameter types against the old method's by type-display (normalised to the
        // symbol's display string) — the caller's literal type-list text may be a short
        // or alternate form, but the resolved symbols let us compare normalised types.
        var indexMap = BuildArgumentIndexMap(oldMethodSymbol, newMethodSymbol);

        var references = await SymbolFinder.FindReferencesAsync(oldMethodSymbol, solution, ct).ConfigureAwait(false);

        var refsByDocument = references
            .SelectMany(r => r.Locations)
            .Where(loc => loc.Location.IsInSource)
            .GroupBy(loc => loc.Document.Id);

        var newSolution = solution;
        var perFileCallsites = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var docGroup in refsByDocument)
        {
            ct.ThrowIfCancellationRequested();
            var (updatedSolution, count, filePath) = await RewriteInvocationsInDocumentAsync(
                newSolution, docGroup.Key, docGroup, oldMethodSymbol, newMethodSymbol, indexMap, ct)
                .ConfigureAwait(false);

            newSolution = updatedSolution;
            if (count > 0 && filePath is not null)
            {
                perFileCallsites[filePath] = perFileCallsites.TryGetValue(filePath, out var existing) ? existing + count : count;
            }
        }

        var totalCallsites = perFileCallsites.Values.Sum();
        if (totalCallsites == 0)
        {
            throw new InvalidOperationException(
                $"No call-sites of '{oldMethod}' were rewritten. The method may have no callers, or every reference resolved to a declaration / nameof / cref rather than an invocation.");
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description =
            $"Replace {totalCallsites} call-site(s) of '{oldMethodSymbol.ToDisplayString()}' with '{newMethodSymbol.ToDisplayString()}' " +
            $"(argument reorder: [{string.Join(", ", indexMap)}])";

        var callsiteUpdates = perFileCallsites
            .Select(kvp => new CallsiteUpdateDto(kvp.Key, kvp.Value))
            .OrderBy(u => u.FilePath, StringComparer.Ordinal)
            .ToList();

        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description, changes);

        return new RefactoringPreviewDto(
            token,
            description,
            changes,
            Warnings: null,
            CallsiteUpdates: callsiteUpdates.Count == 0 ? null : callsiteUpdates);
    }

    private static async Task<(Solution Solution, int CallsiteCount, string? FilePath)> RewriteInvocationsInDocumentAsync(
        Solution solution,
        DocumentId docId,
        IEnumerable<ReferenceLocation> locations,
        IMethodSymbol oldMethodSymbol,
        IMethodSymbol newMethodSymbol,
        IReadOnlyList<int> indexMap,
        CancellationToken ct)
    {
        var doc = solution.GetDocument(docId);
        if (doc is null) return (solution, 0, null);

        var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return (solution, 0, null);

        // Collect invocation rewrites keyed by the invocation node. Each ReferenceLocation
        // points at the member-access identifier (e.g. "Build" in "helper.Build(a, b, c)");
        // walk up to the containing InvocationExpressionSyntax to reach both the method
        // name and the argument list. Skip reference locations that are not part of an
        // invocation (cref, nameof, method-group conversion) — the preview only rewrites
        // call sites.
        var rewrites = new Dictionary<InvocationExpressionSyntax, InvocationExpressionSyntax>();
        var newMethodSimpleName = newMethodSymbol.Name;

        foreach (var refLocation in locations)
        {
            ct.ThrowIfCancellationRequested();
            var node = root.FindNode(refLocation.Location.SourceSpan, getInnermostNodeForTie: true);
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation is null) continue;

            // Defensively confirm this invocation actually targets the old method name. A
            // ReferenceLocation inside an argument expression to a different method would
            // walk up to the enclosing invocation and rewrite the wrong call. The name on
            // the invocation's expression must match the old method's simple name.
            var invokedName = GetInvokedMethodSimpleName(invocation);
            if (!string.Equals(invokedName, oldMethodSymbol.Name, StringComparison.Ordinal)) continue;

            var newInvocation = RewriteInvocation(invocation, newMethodSimpleName, oldMethodSymbol, newMethodSymbol, indexMap);
            rewrites[invocation] = newInvocation;
        }

        if (rewrites.Count == 0) return (solution, 0, doc.FilePath);

        root = root.ReplaceNodes(rewrites.Keys, (original, _) =>
            rewrites.TryGetValue(original, out var replacement) ? replacement : original);

        var updatedSolution = solution.WithDocumentSyntaxRoot(docId, root);
        return (updatedSolution, rewrites.Count, doc.FilePath);
    }

    private static string? GetInvokedMethodSimpleName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            GenericNameSyntax gn => gn.Identifier.ValueText,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax mb => mb.Name.Identifier.ValueText,
            _ => null,
        };
    }

    private static InvocationExpressionSyntax RewriteInvocation(
        InvocationExpressionSyntax invocation,
        string newMethodSimpleName,
        IMethodSymbol oldMethodSymbol,
        IMethodSymbol newMethodSymbol,
        IReadOnlyList<int> indexMap)
    {
        // Rewrite the method-name token on the invocation expression. Preserve any type
        // arguments (GenericNameSyntax) and the receiver (MemberAccess / MemberBinding).
        var newExpression = RewriteInvokedExpression(invocation.Expression, newMethodSimpleName);

        // Reorder the arguments. The mapping is expressed as: the new parameter at position i
        // corresponds to the old parameter at position indexMap[i]. Two call-site shapes:
        //
        //   • Positional only — oldArgs[indexMap[i]] already gives the correct expression,
        //     just strip trivia and re-space.
        //   • Named (any arg has NameColon) — the caller's lexical order may differ from
        //     the semantic order, so first build a semantic-ordered array (oldSemantic[k]
        //     = the argument bound to the old method's parameter k) and then map through
        //     indexMap. Name-token preservation rewrites each argument's NameColon to the
        //     new method's parameter name at its new slot so the rewritten call lexically
        //     matches the new signature.
        var oldArgs = invocation.ArgumentList.Arguments;
        var hasNamedArgs = oldArgs.Any(a => a.NameColon is not null);

        SeparatedSyntaxList<ArgumentSyntax> newArgs;
        if (hasNamedArgs)
        {
            newArgs = ReorderWithNamedArguments(oldArgs, oldMethodSymbol, newMethodSymbol, indexMap);
        }
        else
        {
            newArgs = ReorderPositionalArguments(oldArgs, indexMap);
        }

        var newArgList = invocation.ArgumentList.WithArguments(newArgs);
        return invocation.WithExpression(newExpression).WithArgumentList(newArgList);
    }

    private static ExpressionSyntax RewriteInvokedExpression(ExpressionSyntax expression, string newMethodSimpleName)
    {
        return expression switch
        {
            IdentifierNameSyntax id =>
                SyntaxFactory.IdentifierName(newMethodSimpleName).WithTriviaFrom(id),
            GenericNameSyntax gn =>
                gn.WithIdentifier(SyntaxFactory.Identifier(newMethodSimpleName).WithTriviaFrom(gn.Identifier)),
            MemberAccessExpressionSyntax ma =>
                ma.WithName(SyntaxFactory.IdentifierName(newMethodSimpleName).WithTriviaFrom(ma.Name)),
            MemberBindingExpressionSyntax mb =>
                mb.WithName(SyntaxFactory.IdentifierName(newMethodSimpleName).WithTriviaFrom(mb.Name)),
            _ => expression,
        };
    }

    private static SeparatedSyntaxList<ArgumentSyntax> ReorderPositionalArguments(
        SeparatedSyntaxList<ArgumentSyntax> oldArgs,
        IReadOnlyList<int> indexMap)
    {
        // Purely positional callers: map each new-slot argument to the original argument at
        // the old index indexMap[i]. Preserve the first argument's leading trivia (usually
        // empty) so the rewritten list fits seamlessly into the parentheses.
        var reordered = new List<ArgumentSyntax>(indexMap.Count);
        for (var i = 0; i < indexMap.Count; i++)
        {
            var oldIndex = indexMap[i];
            if (oldIndex < 0 || oldIndex >= oldArgs.Count)
            {
                // Caller provided fewer positional args than the old method's parameter
                // count (e.g. default values used). Skip this slot — the new method's
                // corresponding parameter is whatever the old default expansion would have
                // been. Roslyn will surface a missing-arg error downstream if the new
                // method has no default for that slot.
                continue;
            }

            var arg = oldArgs[oldIndex].WithoutTrivia();
            // First argument has no leading space; subsequent ones need ", " separators.
            // The SeparatedSyntaxList will insert commas between nodes; we only need to
            // ensure the argument expression itself has the right leading trivia.
            if (i == 0)
            {
                reordered.Add(arg);
            }
            else
            {
                reordered.Add(arg.WithLeadingTrivia(SyntaxFactory.Space));
            }
        }

        return SyntaxFactory.SeparatedList(reordered);
    }

    private static SeparatedSyntaxList<ArgumentSyntax> ReorderWithNamedArguments(
        SeparatedSyntaxList<ArgumentSyntax> oldArgs,
        IMethodSymbol oldMethodSymbol,
        IMethodSymbol newMethodSymbol,
        IReadOnlyList<int> indexMap)
    {
        // Mixed or fully-named callers: build a SEMANTIC array from the lexical oldArgs.
        // oldSemantic[k] is the argument bound to the old method's parameter k — derived
        // by reading NameColon for named args and by lexical position for positional args
        // in the prefix (C# rule: named args must follow positional).
        //
        // Positional prefix fills slots 0..positionalCount-1; named args fill slots whose
        // parameter name matches their NameColon.Name. Missing slots (default values used
        // at the call-site) are left as null and skipped in the output.
        var oldSemantic = new ArgumentSyntax?[oldMethodSymbol.Parameters.Length];
        var positionalIndex = 0;
        foreach (var arg in oldArgs)
        {
            if (arg.NameColon is null)
            {
                if (positionalIndex < oldSemantic.Length)
                {
                    oldSemantic[positionalIndex] = arg;
                }
                positionalIndex++;
            }
            else
            {
                var name = arg.NameColon.Name.Identifier.ValueText;
                for (var k = 0; k < oldMethodSymbol.Parameters.Length; k++)
                {
                    if (string.Equals(oldMethodSymbol.Parameters[k].Name, name, StringComparison.Ordinal))
                    {
                        oldSemantic[k] = arg;
                        break;
                    }
                }
            }
        }

        // For each new-slot i, fetch the semantic-slot oldSemantic[indexMap[i]] and rewrite
        // the NameColon's identifier to the new method's parameter name at new-slot i. The
        // expression and refKind survive unchanged; only the name token and trivia are
        // rebuilt so the emitted call reads "arg2: \"x\", arg3: true, arg1: 1".
        var reordered = new List<ArgumentSyntax>(indexMap.Count);
        for (var i = 0; i < indexMap.Count; i++)
        {
            var oldSemanticIndex = indexMap[i];
            if (oldSemanticIndex < 0 || oldSemanticIndex >= oldSemantic.Length) continue;

            var sourceArg = oldSemantic[oldSemanticIndex];
            if (sourceArg is null) continue;

            var newParamName = newMethodSymbol.Parameters[i].Name;
            // NameColon built directly from the factory emits "name:" with no trailing space,
            // which produces "arg2:\"x\"" instead of the idiomatic "arg2: \"x\"". Add a trailing
            // space to the colon token so the rewritten source matches the Roslyn formatter's
            // canonical spacing for named arguments.
            var rebuiltColonToken = SyntaxFactory.Token(SyntaxKind.ColonToken)
                .WithTrailingTrivia(SyntaxFactory.Space);
            var rebuiltName = SyntaxFactory.NameColon(
                SyntaxFactory.IdentifierName(newParamName),
                rebuiltColonToken);
            var rebuilt = SyntaxFactory.Argument(
                rebuiltName,
                sourceArg.RefKindKeyword,
                sourceArg.Expression.WithoutTrivia());

            reordered.Add(i == 0 ? rebuilt : rebuilt.WithLeadingTrivia(SyntaxFactory.Space));
        }

        return SyntaxFactory.SeparatedList(reordered);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    // Signature parsing + resolution
    // ═══════════════════════════════════════════════════════════════════════════════════

    private readonly record struct MethodSignature(string FullyQualifiedName, IReadOnlyList<string> ParameterTypes);

    private static MethodSignature ParseMethodSignature(string signature, string paramName)
    {
        // Expected shape: "Namespace.Type.Method(P1, P2, P3)" or "Method()".
        // Whitespace around commas and inside parens is permitted and trimmed.
        var openParen = signature.IndexOf('(');
        var closeParen = signature.LastIndexOf(')');
        if (openParen < 0 || closeParen < 0 || closeParen < openParen)
            throw new ArgumentException(
                $"Invalid method signature '{signature}'. Expected form: 'Namespace.Type.Method(ParamType1, ParamType2)'.", paramName);

        var name = signature[..openParen].Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                $"Invalid method signature '{signature}'. Method name segment is empty.", paramName);

        var paramList = signature[(openParen + 1)..closeParen].Trim();
        var paramTypes = string.IsNullOrWhiteSpace(paramList)
            ? []
            : (IReadOnlyList<string>)paramList
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return new MethodSignature(name, paramTypes);
    }

    private static async Task<IMethodSymbol?> ResolveMethodBySignatureAsync(
        Solution solution, MethodSignature sig, CancellationToken ct)
    {
        // Split FQ name into containing-type + method-name at the last dot.
        var lastDot = sig.FullyQualifiedName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == sig.FullyQualifiedName.Length - 1)
        {
            // No namespace/type qualifier — fall back to solution-wide simple-name search.
            return await ResolveMethodByBareNameAsync(solution, sig, ct).ConfigureAwait(false);
        }

        var containingTypeName = sig.FullyQualifiedName[..lastDot];
        var methodName = sig.FullyQualifiedName[(lastDot + 1)..];

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var containingType = compilation.GetTypeByMetadataName(containingTypeName);
            if (containingType is null) continue;

            var candidates = containingType.GetMembers(methodName).OfType<IMethodSymbol>().ToList();
            var match = MatchOverload(candidates, sig.ParameterTypes);
            if (match is not null) return match;
        }

        return null;
    }

    private static async Task<IMethodSymbol?> ResolveMethodByBareNameAsync(
        Solution solution, MethodSignature sig, CancellationToken ct)
    {
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var candidates = new List<IMethodSymbol>();
            foreach (var symbol in compilation.GetSymbolsWithName(sig.FullyQualifiedName, SymbolFilter.Member, ct))
            {
                if (symbol is IMethodSymbol method) candidates.Add(method);
            }

            var match = MatchOverload(candidates, sig.ParameterTypes);
            if (match is not null) return match;
        }

        return null;
    }

    private static IMethodSymbol? MatchOverload(IReadOnlyList<IMethodSymbol> candidates, IReadOnlyList<string> paramTypeLiterals)
    {
        // First prefer an exact arity match with normalized-type match on every parameter.
        // Match is lenient on short-vs-fully-qualified type names: compare the caller's
        // literal against both the parameter symbol's short name (Name) and its
        // minimally-qualified display string (ToDisplayString with TypeQualificationStyle =
        // NameOnly) and its full ToDisplayString.
        IMethodSymbol? singleArityMatch = null;
        var arityMatches = 0;

        foreach (var candidate in candidates)
        {
            if (candidate.Parameters.Length != paramTypeLiterals.Count) continue;

            arityMatches++;
            singleArityMatch ??= candidate;

            var allMatch = true;
            for (var i = 0; i < paramTypeLiterals.Count; i++)
            {
                var literal = paramTypeLiterals[i];
                var paramType = candidate.Parameters[i].Type;

                if (!ParameterTypeMatchesLiteral(paramType, literal))
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch) return candidate;
        }

        // If only one candidate has the right arity and no candidate strictly matched
        // the type-list, return that single arity candidate. This lets callers pass the
        // shortest-disambiguating literal (e.g. just the parameter names in the current
        // docs' informal "Method(a, b, c)" shorthand) without requiring exact type text.
        return arityMatches == 1 ? singleArityMatch : null;
    }

    private static bool ParameterTypeMatchesLiteral(ITypeSymbol paramType, string literal)
    {
        if (string.IsNullOrWhiteSpace(literal)) return false;

        if (string.Equals(paramType.Name, literal, StringComparison.Ordinal)) return true;

        var displayFull = paramType.ToDisplayString();
        if (string.Equals(displayFull, literal, StringComparison.Ordinal)) return true;

        var displayMinimal = paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (string.Equals(displayMinimal, literal, StringComparison.Ordinal)) return true;

        // Tolerate primitive aliases (int ↔ System.Int32, string ↔ System.String, etc.).
        var specialAlias = GetSpecialTypeAlias(paramType);
        if (specialAlias is not null && string.Equals(specialAlias, literal, StringComparison.Ordinal)) return true;

        return false;
    }

    private static string? GetSpecialTypeAlias(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Boolean => "bool",
        SpecialType.System_Byte => "byte",
        SpecialType.System_SByte => "sbyte",
        SpecialType.System_Int16 => "short",
        SpecialType.System_UInt16 => "ushort",
        SpecialType.System_Int32 => "int",
        SpecialType.System_UInt32 => "uint",
        SpecialType.System_Int64 => "long",
        SpecialType.System_UInt64 => "ulong",
        SpecialType.System_Char => "char",
        SpecialType.System_Single => "float",
        SpecialType.System_Double => "double",
        SpecialType.System_Decimal => "decimal",
        SpecialType.System_String => "string",
        SpecialType.System_Object => "object",
        _ => null,
    };

    private static IReadOnlyList<int> BuildArgumentIndexMap(IMethodSymbol oldMethod, IMethodSymbol newMethod)
    {
        // For each new-method parameter at index i, find the old-method parameter whose
        // name matches. That old-index is indexMap[i]. If the new method declares any
        // parameter whose name is absent from the old method's parameter list, the
        // mapping is ambiguous and the preview must refuse.
        //
        // Why NAME-match rather than TYPE-match: a reordering rewrite where the new
        // method's parameter list is a permutation of the old method's often has
        // identical types across parameters (e.g. all int), so type-match alone cannot
        // disambiguate. Parameter name equality is the stable contract the caller
        // declared in the signature text.
        var map = new int[newMethod.Parameters.Length];
        for (var i = 0; i < newMethod.Parameters.Length; i++)
        {
            var newParamName = newMethod.Parameters[i].Name;
            var oldIndex = -1;
            for (var j = 0; j < oldMethod.Parameters.Length; j++)
            {
                if (string.Equals(oldMethod.Parameters[j].Name, newParamName, StringComparison.Ordinal))
                {
                    oldIndex = j;
                    break;
                }
            }

            if (oldIndex < 0)
            {
                throw new InvalidOperationException(
                    $"newMethod parameter '{newParamName}' at position {i} does not correspond to any parameter of oldMethod " +
                    $"'{oldMethod.ToDisplayString()}'. The new method's parameter names must all be drawn from the old method " +
                    "so the reorder can be derived unambiguously.");
            }

            map[i] = oldIndex;
        }

        return map;
    }
}
