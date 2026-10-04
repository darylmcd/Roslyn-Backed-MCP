using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Editing;

namespace RoslynMcp.Roslyn.Services;

public sealed class BulkRefactoringService : IBulkRefactoringService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ICompilationCache _compilationCache;

    public BulkRefactoringService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        ICompilationCache compilationCache)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _compilationCache = compilationCache;
    }

    public async Task<RefactoringPreviewDto> PreviewBulkReplaceTypeAsync(
        string workspaceId, string oldTypeName, string newTypeName, string? scope, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);

        // Resolve old type
        var oldTypeSymbol = await ResolveTypeByNameAsync(workspaceId, _compilationCache, solution, oldTypeName, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Type '{oldTypeName}' not found in the solution.");

        // Resolve new type (must exist)
        var newTypeSymbol = await ResolveTypeByNameAsync(workspaceId, _compilationCache, solution, newTypeName, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Replacement type '{newTypeName}' not found in the solution.");

        var normalizedScope = (scope ?? "all").ToLowerInvariant();
        var validScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "parameters", "fields", "all" };
        if (!validScopes.Contains(normalizedScope))
            throw new PublicArgumentException("scope must be one of: parameters, fields, all.", nameof(scope));

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
        // preview-token-apply-route-provenance: tag the producer family so bulk_replace_type_apply
        // can refuse a foreign token before mutating the workspace.
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description, changes, PreviewKind.BulkReplaceType);

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

    private static async Task<INamedTypeSymbol?> ResolveTypeByNameAsync(
        string workspaceId, ICompilationCache compilationCache, Solution solution, string typeName, CancellationToken ct)
    {
        // Try fully qualified name first
        foreach (var project in solution.Projects)
        {
            var compilation = await compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
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
    // by overload match, builds a parameter-name permutation,
    // and rewrites every InvocationExpressionSyntax of the old method through SymbolFinder.
    // Explicit arguments keep lexical evaluation order and receive target parameter names.
    // Implicit values are preserved instead of using replacement-method defaults.
    // ═══════════════════════════════════════════════════════════════════════════════════

    public async Task<RefactoringPreviewDto> PreviewReplaceInvocationAsync(
        string workspaceId, string oldMethod, string newMethod, string? scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(oldMethod))
            throw new PublicArgumentException("oldMethod must be a fully-qualified signature like 'Type.Method(P1,P2)'.", nameof(oldMethod));
        if (string.IsNullOrWhiteSpace(newMethod))
            throw new PublicArgumentException("newMethod must be a fully-qualified signature like 'Type.Method(P1,P2)'.", nameof(newMethod));

        // replace-invocation scope today is always 'all' — the parameter is reserved for
        // future file / project scoping. Reject unknown values eagerly so a stale caller
        // gets a clear error instead of silent expanded scope.
        var normalizedScope = (scope ?? "all").ToLowerInvariant();
        if (!string.Equals(normalizedScope, "all", StringComparison.Ordinal))
            throw new PublicArgumentException("scope must be 'all' for replace_invocation_preview.", nameof(scope));

        var oldSig = ParseMethodSignature(oldMethod, nameof(oldMethod));
        var newSig = ParseMethodSignature(newMethod, nameof(newMethod));

        var solution = _workspace.GetCurrentSolution(workspaceId);

        var oldMethodSymbol = await ResolveMethodBySignatureAsync(workspaceId, _compilationCache, solution, oldSig, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not resolve oldMethod '{oldMethod}'. Ensure the fully-qualified type name and parameter-type list match an existing method overload.");

        var newMethodSymbol = await ResolveMethodBySignatureAsync(workspaceId, _compilationCache, solution, newSig, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not resolve newMethod '{newMethod}'. Ensure the fully-qualified type name and parameter-type list match an existing method overload.");

        // Validate a parameter-name bijection and describe each new parameter's original
        // position in the preview. Call-site rewriting uses semantic argument bindings
        // and target parameter names to preserve the caller's lexical evaluation order.
        var indexMap = BuildArgumentIndexMap(oldMethodSymbol, newMethodSymbol);

        var references = await SymbolFinder.FindReferencesAsync(oldMethodSymbol, solution, ct).ConfigureAwait(false);

        // replace-invocation-rewrites-replacement-body: a replacement that delegates to the old
        // method (NewM(...) => OldM(...)) must keep its own body intact — rewriting that call
        // would make the replacement call itself forever. Exclude every reference that lies
        // inside one of the new method's declarations. Compare by file path + span rather than
        // SyntaxTree identity because newMethodSymbol may come from a cached compilation.
        var replacementDeclarations = newMethodSymbol.DeclaringSyntaxReferences
            .Where(r => !string.IsNullOrEmpty(r.SyntaxTree.FilePath))
            .Select(r => (FilePath: r.SyntaxTree.FilePath, Span: r.Span))
            .ToList();

        var refsByDocument = references
            .SelectMany(r => r.Locations)
            .Where(loc => loc.Location.IsInSource)
            .Where(loc => !IsInsideDeclaration(loc, replacementDeclarations))
            .GroupBy(loc => loc.Document.Id);

        var newSolution = solution;
        var perFileCallsites = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var docGroup in refsByDocument)
        {
            ct.ThrowIfCancellationRequested();
            var (updatedSolution, count, filePath) = await RewriteInvocationsInDocumentAsync(
                newSolution, docGroup.Key, docGroup, oldMethodSymbol, newMethodSymbol, ct)
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

        // preview-token-apply-route-provenance: replace_invocation_preview redeems through the
        // SHARED bulk_replace_type_apply route, so it mints the same kind by design.
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description, changes, PreviewKind.BulkReplaceType);

        return new RefactoringPreviewDto(
            token,
            description,
            changes,
            Warnings: null,
            CallsiteUpdates: callsiteUpdates.Count == 0 ? null : callsiteUpdates);
    }

    private static bool IsInsideDeclaration(
        ReferenceLocation location,
        IReadOnlyList<(string FilePath, Microsoft.CodeAnalysis.Text.TextSpan Span)> declarations)
    {
        var filePath = location.Location.SourceTree?.FilePath ?? location.Document.FilePath;
        if (string.IsNullOrEmpty(filePath)) return false;

        var span = location.Location.SourceSpan;
        foreach (var (declFilePath, declSpan) in declarations)
        {
            if (string.Equals(declFilePath, filePath, StringComparison.OrdinalIgnoreCase) && declSpan.Contains(span))
                return true;
        }

        return false;
    }

    private static async Task<(Solution Solution, int CallsiteCount, string? FilePath)> RewriteInvocationsInDocumentAsync(
        Solution solution,
        DocumentId docId,
        IEnumerable<ReferenceLocation> locations,
        IMethodSymbol oldMethodSymbol,
        IMethodSymbol newMethodSymbol,
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
        var model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new PublicInvalidOperationException("Invocation replacement requires a semantic model.");
        var annotation = new SyntaxAnnotation();
        var generator = SyntaxGenerator.GetGenerator(doc);
        var rewrites = new Dictionary<InvocationExpressionSyntax, IInvocationOperation>();
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

            var operation = model.GetOperation(invocation, ct) as IInvocationOperation
                ?? throw new PublicInvalidOperationException("Invocation replacement requires a successfully bound original call.");
            if (!SameMethodDefinition(operation.TargetMethod, oldMethodSymbol)) continue;
            rewrites[invocation] = operation;
        }

        if (rewrites.Count == 0) return (solution, 0, doc.FilePath);

        root = root.ReplaceNodes(rewrites.Keys, (original, rewritten) =>
            RewriteInvocation(original, rewritten, rewrites[original], model, generator, newMethodSimpleName, newMethodSymbol)
                .WithAdditionalAnnotations(annotation));


        var updatedSolution = solution.WithDocumentSyntaxRoot(docId, root);
        var updatedDocument = updatedSolution.GetDocument(docId)
            ?? throw new PublicInvalidOperationException("Invocation replacement requires an available source document.");
        var updatedRoot = await updatedDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false)
            ?? throw new PublicInvalidOperationException("Invocation replacement requires an available syntax tree.");
        var updatedModel = await updatedDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new PublicInvalidOperationException("Invocation replacement requires a semantic model.");
        foreach (var rewritten in updatedRoot.GetAnnotatedNodes(annotation).OfType<InvocationExpressionSyntax>())
        {
            if (updatedModel.GetOperation(rewritten, ct) is not IInvocationOperation rebound ||
                !SameMethodDefinition(rebound.TargetMethod, newMethodSymbol) ||
                updatedModel.GetDiagnostics(rewritten.Span, ct).Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new PublicInvalidOperationException("Invocation replacement cannot preserve a valid binding to the requested replacement method.");
        }
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


    private static bool SameMethodDefinition(IMethodSymbol actual, IMethodSymbol expected)
    {
        actual = (actual.ReducedFrom ?? actual).OriginalDefinition;
        expected = (expected.ReducedFrom ?? expected).OriginalDefinition;
        var actualId = actual.GetDocumentationCommentId();
        var expectedId = expected.GetDocumentationCommentId();
        return actualId is not null && expectedId is not null &&
            actual.ContainingAssembly.Identity.Equals(expected.ContainingAssembly.Identity) &&
            string.Equals(actualId, expectedId, StringComparison.Ordinal);
    }

    private static InvocationExpressionSyntax RewriteInvocation(
        InvocationExpressionSyntax originalInvocation,
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SemanticModel model,
        SyntaxGenerator generator,
        string newMethodSimpleName,
        IMethodSymbol newMethodSymbol)
    {
        var targetParameters = newMethodSymbol.Parameters.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var arguments = new List<SyntaxNodeOrToken>();
        void AppendArgument(ArgumentSyntax argument, SyntaxToken? separator = null)
        {
            if (arguments.Count != 0)
                arguments.Add(separator ?? SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space));
            arguments.Add(argument);
        }
        var expanded = operation.Arguments.SingleOrDefault(a =>
            a.ArgumentKind is ArgumentKind.ParamArray or ArgumentKind.ParamCollection && a.IsImplicit);
        var expandedElements = new List<SyntaxNodeOrToken>();
        SyntaxToken? expandedSeparator = null;
        for (var i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
        {
            var source = invocation.ArgumentList.Arguments[i];
            if (model.GetOperation(originalInvocation.ArgumentList.Arguments[i]) is not IArgumentOperation argument)
            {
                if (expanded is null)
                    throw new PublicInvalidOperationException("Invocation replacement cannot determine an argument's original parameter binding.");
                if (expandedElements.Count == 0)
                    expandedSeparator = i > 0 ? invocation.ArgumentList.Arguments.GetSeparator(i - 1) : null;
                else
                    expandedElements.Add(invocation.ArgumentList.Arguments.GetSeparator(i - 1));
                expandedElements.Add(source.Expression);
                continue;
            }

            var parameter = argument.Parameter
                ?? throw new PublicInvalidOperationException("Invocation replacement cannot determine an argument's original parameter binding.");
            var target = targetParameters[parameter.Name];
            if (parameter.RefKind != target.RefKind)
                throw new PublicInvalidOperationException("Invocation replacement cannot change a parameter's ref, in, or out passing mode.");
            AppendArgument(NameArgument(source, target.Name),
                i > 0 ? invocation.ArgumentList.Arguments.GetSeparator(i - 1) : null);
        }

        if (expanded is not null)
        {
            var parameter = expanded.Parameter
                ?? throw new PublicInvalidOperationException("Invocation replacement cannot determine an argument's original parameter binding.");
            var expandedType = expanded.Value.Type
                ?? throw new PublicInvalidOperationException("Invocation replacement cannot determine an expanded argument's original type.");
            var type = SyntaxFactory.ParseTypeName(expandedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            var expressions = SyntaxFactory.SeparatedList<ExpressionSyntax>(expandedElements);
            ExpressionSyntax expression = expanded.ArgumentKind == ArgumentKind.ParamArray
                ? SyntaxFactory.ArrayCreationExpression((ArrayTypeSyntax)type,
                    SyntaxFactory.InitializerExpression(SyntaxKind.ArrayInitializerExpression, expressions))
                    .WithNewKeyword(SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space))
                : SyntaxFactory.CastExpression(type, SyntaxFactory.CollectionExpression(
                    SyntaxFactory.SeparatedList<CollectionElementSyntax>(expressions.GetWithSeparators().Select(item =>
                        item.IsToken ? item : (SyntaxNodeOrToken)SyntaxFactory.ExpressionElement((ExpressionSyntax)(item.AsNode()
                            ?? throw new PublicInvalidOperationException("Invocation replacement requires a parameter expression.")))))));
            AppendArgument(NameArgument(SyntaxFactory.Argument(expression), parameter.Name), expandedSeparator);
        }

        // Materialize the original call's implicit values, including compiler-provided caller
        // information, instead of letting different target defaults change the call's meaning.
        foreach (var argument in operation.Arguments.Where(a => a.ArgumentKind == ArgumentKind.DefaultValue))
        {
            var parameter = argument.Parameter
                ?? throw new PublicInvalidOperationException("Invocation replacement cannot determine an argument's original parameter binding.");
            var type = SyntaxFactory.ParseTypeName(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            ExpressionSyntax value;
            if (argument.Value.ConstantValue is { HasValue: true, Value: null } &&
                parameter.Type.IsValueType && parameter.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
                value = SyntaxFactory.DefaultExpression(type);
            else if (argument.Value.ConstantValue.HasValue)
                value = SyntaxFactory.CastExpression(type,
                    (ExpressionSyntax)generator.LiteralExpression(argument.Value.ConstantValue.Value));
            else if (argument.Value is IDefaultValueOperation)
                value = SyntaxFactory.DefaultExpression(type);
            else
                throw new PublicInvalidOperationException("Invocation replacement cannot preserve an implicit argument value.");
            AppendArgument(NameArgument(SyntaxFactory.Argument(value), parameter.Name));
        }

        return invocation.WithExpression(RewriteInvokedExpression(invocation.Expression, newMethodSimpleName))
            .WithArgumentList(invocation.ArgumentList.WithArguments(SyntaxFactory.SeparatedList<ArgumentSyntax>(arguments)));
    }

    private static SyntaxToken NameIdentifier(string parameterName)
    {
        var identifier = SyntaxFacts.GetKeywordKind(parameterName) != SyntaxKind.None ||
            SyntaxFacts.GetContextualKeywordKind(parameterName) != SyntaxKind.None
            ? "@" + parameterName : parameterName;
        return SyntaxFactory.ParseToken(identifier);
    }

    private static ArgumentSyntax NameArgument(ArgumentSyntax argument, string parameterName)
    {
        var name = SyntaxFactory.IdentifierName(NameIdentifier(parameterName));
        var colon = SyntaxFactory.Token(SyntaxKind.ColonToken).WithTrailingTrivia(SyntaxFactory.Space);
        if (argument.NameColon is { } originalName)
        {
            name = name.WithTriviaFrom(originalName.Name);
            colon = originalName.ColonToken;
        }
        return argument.WithNameColon(SyntaxFactory.NameColon(name).WithColonToken(colon));
    }

    private static ExpressionSyntax RewriteInvokedExpression(ExpressionSyntax expression, string newMethodSimpleName)
    {
        return expression switch
        {
            IdentifierNameSyntax id =>
                SyntaxFactory.IdentifierName(NameIdentifier(newMethodSimpleName)).WithTriviaFrom(id),
            GenericNameSyntax gn =>
                gn.WithIdentifier(NameIdentifier(newMethodSimpleName).WithTriviaFrom(gn.Identifier)),
            MemberAccessExpressionSyntax ma =>
                ma.WithName(ma.Name is GenericNameSyntax generic
                    ? generic.WithIdentifier(NameIdentifier(newMethodSimpleName).WithTriviaFrom(generic.Identifier))
                    : SyntaxFactory.IdentifierName(NameIdentifier(newMethodSimpleName)).WithTriviaFrom(ma.Name)),
            MemberBindingExpressionSyntax mb =>
                mb.WithName(mb.Name is GenericNameSyntax generic
                    ? generic.WithIdentifier(NameIdentifier(newMethodSimpleName).WithTriviaFrom(generic.Identifier))
                    : SyntaxFactory.IdentifierName(NameIdentifier(newMethodSimpleName)).WithTriviaFrom(mb.Name)),
            _ => expression,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    // Signature parsing + resolution
    // ═══════════════════════════════════════════════════════════════════════════════════

    private readonly record struct MethodSignature(string FullyQualifiedName, IReadOnlyList<string> ParameterTypes);

    private static MethodSignature ParseMethodSignature(string signature, string paramName)
    {
        const string invalidSignature =
            "Method signature must have the form 'Namespace.Type.Method(ParamType1, ParamType2)' with complete, non-empty parameter types.";
        signature = signature.Trim();
        var openParen = signature.IndexOf('(');
        if (openParen < 0 || !signature.EndsWith(')'))
            throw new PublicArgumentException(invalidSignature, paramName);

        var name = signature[..openParen].Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new PublicArgumentException("Method signature must include a non-empty method name.", paramName);

        var parameterText = signature[(openParen + 1)..^1];
        var parameterTypes = new List<string>();
        var delimiters = new Stack<char>();
        var slotStart = 0;
        for (var i = 0; i <= parameterText.Length; i++)
        {
            if (i < parameterText.Length)
            {
                var character = parameterText[i];
                if (character is '<' or '(' or '[')
                    delimiters.Push(character);
                else if (character is '>' or ')' or ']')
                {
                    var expected = character switch { '>' => '<', ')' => '(', _ => '[' };
                    if (!delimiters.TryPop(out var opening) || opening != expected)
                        throw new PublicArgumentException(invalidSignature, paramName);
                }
                if (character != ',' || delimiters.Count != 0)
                    continue;
            }
            else if (delimiters.Count != 0)
                throw new PublicArgumentException(invalidSignature, paramName);

            var fragment = parameterText[slotStart..i].Trim();
            if (fragment.Length == 0)
            {
                if (i == parameterText.Length && slotStart == 0)
                    break; // An entirely blank list is the valid zero-argument form.
                throw new PublicArgumentException(invalidSignature, paramName);
            }

            var type = SyntaxFactory.ParseTypeName(fragment, consumeFullText: true);
            if (type.ContainsDiagnostics || type.DescendantTokens().Any(token => token.IsMissing) ||
                type.DescendantNodesAndSelf().OfType<OmittedTypeArgumentSyntax>().Any())
                throw new PublicArgumentException(invalidSignature, paramName);
            parameterTypes.Add(type.NormalizeWhitespace().ToString());
            slotStart = i + 1;
        }

        return new MethodSignature(name, parameterTypes);
    }

    private static async Task<IMethodSymbol?> ResolveMethodBySignatureAsync(
        string workspaceId, ICompilationCache compilationCache, Solution solution, MethodSignature sig, CancellationToken ct)
    {
        // Split FQ name into containing-type + method-name at the last dot.
        var lastDot = sig.FullyQualifiedName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == sig.FullyQualifiedName.Length - 1)
        {
            // No namespace/type qualifier — fall back to solution-wide simple-name search.
            return await ResolveMethodByBareNameAsync(workspaceId, compilationCache, solution, sig, ct).ConfigureAwait(false);
        }

        var containingTypeName = sig.FullyQualifiedName[..lastDot];
        var methodName = sig.FullyQualifiedName[(lastDot + 1)..];

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
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
        string workspaceId, ICompilationCache compilationCache, Solution solution, MethodSignature sig, CancellationToken ct)
    {
        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
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
        if (SyntaxFactory.AreEquivalent(SyntaxFactory.ParseTypeName(displayFull), SyntaxFactory.ParseTypeName(literal))) return true;

        var displayMinimal = paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (SyntaxFactory.AreEquivalent(SyntaxFactory.ParseTypeName(displayMinimal), SyntaxFactory.ParseTypeName(literal))) return true;

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
        if (oldMethod.Parameters.Length != newMethod.Parameters.Length)
            throw new PublicInvalidOperationException("The replacement method must have a permutation of the original parameter names.");

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
