using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// <c>parameter-object-preview-tool</c> implementation. Synthesizes a positional
/// <c>sealed record</c> DTO for a chosen subset of a method's parameters and rewrites
/// every call site to flow grouped argument values through the new record's primary
/// constructor. See <c>ai_docs/items/parameter-object-preview-design.md</c> for the
/// authoritative contract (refusal cases, cross-project policy, generated-DTO shape).
/// </summary>
public sealed class ParameterObjectService : IParameterObjectService
{
    private static readonly HashSet<string> ReservedGeneratedRecordMemberNames = new(StringComparer.Ordinal)
    {
        "Clone",
        "Deconstruct",
        "Equals",
        "EqualityContract",
        "GetHashCode",
        "GetType",
        "MemberwiseClone",
        "PrintMembers",
        "ReferenceEquals",
        "ToString",
    };

    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;

    public ParameterObjectService(IWorkspaceManager workspace, IPreviewStore previewStore)
    {
        _workspace = workspace;
        _previewStore = previewStore;
    }

    public async Task<RefactoringPreviewDto> PreviewParameterObjectAsync(
        string workspaceId,
        SymbolLocator target,
        ParameterObjectPreviewRequest request,
        CancellationToken ct)
    {
        target.Validate();
        ValidateRequest(request);
        IdentifierValidation.ThrowIfInvalidIdentifier(request.NewTypeName, "newTypeName");

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, target, ct).ConfigureAwait(false);
        if (symbol is not IMethodSymbol method)
            throw new InvalidOperationException(
                $"parameter_object_preview requires a method symbol; resolved {symbol?.Kind.ToString() ?? "null"} instead.");

        EnforceTargetMethodContractRefusals(method);

        var groupedParameters = ResolveGroupedParameters(method, request);
        ValidateGeneratedMemberNames(groupedParameters, request.NewTypeName);
        EnforceParameterShapeRefusals(method, groupedParameters);

        var newParameterName = request.ParameterName ?? CamelCase(request.NewTypeName);
        IdentifierValidation.ThrowIfInvalidIdentifier(newParameterName, "parameterName");
        await EnforceNewParameterNameIsAvailableAsync(
            solution, method, groupedParameters, newParameterName, ct).ConfigureAwait(false);
        await EnforceGroupedParametersAreReadOnlyAsync(
            solution, method, groupedParameters, ct).ConfigureAwait(false);

        var (dtoProject, dtoVisibilityIsPublic) = ResolveDtoProject(solution, method, request);

        var callSites = await CollectCallSiteBindingsAsync(solution, method, groupedParameters, ct).ConfigureAwait(false);
        var methodProject = solution.GetProject(method.ContainingAssembly, ct)!;
        EnforceCrossProjectReferences(solution, methodProject, dtoProject, callSites);
        await EnforceGroupedParameterTypesAreDtoCompatibleAsync(
            solution, methodProject, dtoProject, groupedParameters, dtoVisibilityIsPublic, ct).ConfigureAwait(false);

        var defaultValueWarnings = await CollectDefaultValueWarningsAsync(
            solution, groupedParameters, callSites, ct).ConfigureAwait(false);
        if (defaultValueWarnings.Count > 0)
        {
            throw new InvalidOperationException(
                "parameter_object_preview refuses: one or more call sites omit a grouped parameter and rely on its default value. " +
                "Either add the explicit argument at every site first, or remove the omitted parameter from parameterNames. " +
                "Affected sites: " + string.Join("; ", defaultValueWarnings));
        }

        var (dtoNamespace, dtoFolders) = ResolveDtoLocation(dtoProject, request, method);
        var dtoFilePath = ResolveDtoFilePath(dtoProject, dtoFolders, request.NewTypeName);
        await EnforceDtoDestinationIsFreeAsync(
            dtoProject, dtoFilePath, dtoNamespace, request.NewTypeName, ct).ConfigureAwait(false);
        var dtoSource = BuildDtoSource(dtoNamespace, request.NewTypeName, groupedParameters, dtoVisibilityIsPublic);

        var originalTexts = new Dictionary<DocumentId, string>();
        var perFileCallsites = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var accumulator = await RewriteMethodDeclarationAsync(
            solution, method, groupedParameters, dtoNamespace, request.NewTypeName, newParameterName, originalTexts, ct).ConfigureAwait(false);

        accumulator = await RewriteCallSitesAsync(
            accumulator, solution, method, groupedParameters, dtoNamespace, request.NewTypeName, callSites,
            originalTexts, perFileCallsites, ct).ConfigureAwait(false);

        // Add the new DTO document last so its diff is appended cleanly; we capture no
        // pre-existing text for it (it's an Added file).
        var dtoFolderArray = dtoFolders.Count == 0 ? null : (IEnumerable<string>)dtoFolders;
        var targetProject = accumulator.GetProject(dtoProject.Id)!;
        var newDoc = targetProject.AddDocument(
            $"{request.NewTypeName}.cs",
            dtoSource,
            folders: dtoFolderArray,
            filePath: dtoFilePath);
        accumulator = newDoc.Project.Solution;

        var changes = await BuildFileChangesAsync(accumulator, solution, originalTexts, newDoc.Id, ct).ConfigureAwait(false);
        if (changes.Count == 0)
            throw new InvalidOperationException(
                "parameter_object_preview produced no changes — verify the target method actually has parameters to group.");

        var description = $"Group {groupedParameters.Count} parameter(s) of {method.ToDisplayString()} into new record '{request.NewTypeName}'";
        var token = _previewStore.Store(workspaceId, accumulator, _workspace.GetCurrentVersion(workspaceId), description, changes);

        var callsiteUpdates = perFileCallsites
            .Select(kvp => new CallsiteUpdateDto(kvp.Key, kvp.Value))
            .OrderBy(u => u.FilePath, StringComparer.Ordinal)
            .ToList();

        return new RefactoringPreviewDto(
            token,
            description,
            changes,
            Warnings: null,
            CallsiteUpdates: callsiteUpdates.Count == 0 ? null : callsiteUpdates);
    }

    private static void ValidateRequest(ParameterObjectPreviewRequest request)
    {
        if (request.ParameterNames is null || request.ParameterNames.Count < 2)
            throw new ArgumentException(
                "parameter_object_preview requires at least two parameter names to group.",
                nameof(request));
        if (string.IsNullOrWhiteSpace(request.NewTypeName))
            throw new ArgumentException("parameter_object_preview requires newTypeName.", nameof(request));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in request.ParameterNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(
                    "parameterNames entries must be non-empty.", nameof(request));
            if (!seen.Add(name))
                throw new ArgumentException(
                    $"parameterNames contains a duplicate entry: '{name}'.", nameof(request));
        }
    }

    private static IReadOnlyList<IParameterSymbol> ResolveGroupedParameters(
        IMethodSymbol method, ParameterObjectPreviewRequest request)
    {
        var byName = method.Parameters.ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);
        var grouped = new List<IParameterSymbol>(request.ParameterNames.Count);
        foreach (var name in request.ParameterNames)
        {
            if (!byName.TryGetValue(name, out var p))
                throw new ArgumentException(
                    $"Parameter '{name}' not found on {method.ToDisplayString()}. Existing parameters: " +
                    string.Join(", ", method.Parameters.Select(q => q.Name)),
                    nameof(request));
            grouped.Add(p);
        }
        return grouped;
    }

    private static void ValidateGeneratedMemberNames(
        IReadOnlyList<IParameterSymbol> grouped,
        string newTypeName)
    {
        var duplicateCollisions = grouped
            .GroupBy(parameter => Capitalize(parameter.Name), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}' from [{string.Join(", ", group.Select(parameter => $"'{parameter.Name}'"))}]")
            .ToArray();
        if (duplicateCollisions.Length > 0)
        {
            throw new ArgumentException(
                "parameter_object_preview refuses: grouped parameter names would generate duplicate positional-record members after capitalization. " +
                "Rename or omit one parameter from each collision. Collisions: " + string.Join("; ", duplicateCollisions),
                nameof(grouped));
        }

        var reservedCollisions = grouped
            .Select(parameter => (SourceName: parameter.Name, GeneratedName: Capitalize(parameter.Name)))
            .Where(candidate => ReservedGeneratedRecordMemberNames.Contains(candidate.GeneratedName))
            .Select(candidate => $"source parameter '{candidate.SourceName}' generates reserved member '{candidate.GeneratedName}'")
            .ToArray();
        if (reservedCollisions.Length > 0)
        {
            throw new ArgumentException(
                "parameter_object_preview refuses: one or more generated positional-record members collide with reserved, synthesized, or inherited record/object members. " +
                "Rename or omit each source parameter. Collisions: " + string.Join("; ", reservedCollisions),
                nameof(grouped));
        }

        var typeIdentifier = newTypeName[0] == '@' ? newTypeName[1..] : newTypeName;
        var enclosingTypeCollision = grouped
            .Select(parameter => (SourceName: parameter.Name, GeneratedName: Capitalize(parameter.Name)))
            .FirstOrDefault(candidate => string.Equals(candidate.GeneratedName, typeIdentifier, StringComparison.Ordinal));
        if (enclosingTypeCollision != default)
        {
            throw new ArgumentException(
                "parameter_object_preview refuses: a generated positional-record member cannot have the same name as its enclosing record type. " +
                $"Source parameter '{enclosingTypeCollision.SourceName}' generates member '{enclosingTypeCollision.GeneratedName}', which collides with newTypeName '{newTypeName}'. " +
                "Rename the type or omit the source parameter.",
                nameof(grouped));
        }
    }

    private static async Task EnforceNewParameterNameIsAvailableAsync(
        Solution solution,
        IMethodSymbol method,
        IReadOnlyList<IParameterSymbol> grouped,
        string newParameterName,
        CancellationToken ct)
    {
        var identifierValue = newParameterName[0] == '@' ? newParameterName[1..] : newParameterName;
        var groupedOrdinals = grouped.Select(parameter => parameter.Ordinal).ToHashSet();
        var retainedCollision = method.Parameters.FirstOrDefault(parameter =>
            !groupedOrdinals.Contains(parameter.Ordinal)
            && string.Equals(parameter.Name, identifierValue, StringComparison.Ordinal));
        if (retainedCollision is not null)
        {
            throw new ArgumentException(
                $"parameter_object_preview refuses: new parameter name '{newParameterName}' collides with retained parameter '{retainedCollision.Name}'. " +
                "Choose a distinct parameterName.",
                nameof(newParameterName));
        }

        var typeParameterCollision = method.TypeParameters.FirstOrDefault(typeParameter =>
            string.Equals(typeParameter.Name, identifierValue, StringComparison.Ordinal));
        if (typeParameterCollision is not null)
        {
            throw new ArgumentException(
                $"parameter_object_preview refuses: new parameter name '{newParameterName}' collides with method type parameter '{typeParameterCollision.Name}'. " +
                "Choose a distinct parameterName.",
                nameof(newParameterName));
        }

        var declarationCollisions = new List<string>();
        var memberReferenceCollisions = new List<string>();
        foreach (var declarationReference in method.DeclaringSyntaxReferences)
        {
            var declaration = await declarationReference.GetSyntaxAsync(ct).ConfigureAwait(false);
            if (declaration is not BaseMethodDeclarationSyntax methodDeclaration) continue;
            var document = solution.GetDocument(declaration.SyntaxTree);
            if (document is null) continue;
            var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (semanticModel is null) continue;

            foreach (var node in GetBodyNodes(methodDeclaration))
            {
                var declaredSymbol = semanticModel.GetDeclaredSymbol(node, ct);
                if (!IsValueDeclaration(declaredSymbol)
                    || !string.Equals(declaredSymbol!.Name, identifierValue, StringComparison.Ordinal))
                {
                    continue;
                }

                var lineSpan = node.GetLocation().GetLineSpan();
                var filePath = document.FilePath ?? document.Name;
                declarationCollisions.Add(
                    $"{declaredSymbol.Kind} '{declaredSymbol.Name}' at {filePath}:{lineSpan.StartLinePosition.Line + 1}");
            }

            foreach (var reference in GetBodyNodes(methodDeclaration)
                .OfType<IdentifierNameSyntax>()
                .Where(reference => string.Equals(reference.Identifier.ValueText, identifierValue, StringComparison.Ordinal)))
            {
                if (!IsUnqualifiedReference(reference)) continue;

                var referencedSymbol = semanticModel.GetSymbolInfo(reference, ct).Symbol;
                if (!IsCapturableMember(referencedSymbol)) continue;

                var lineSpan = reference.GetLocation().GetLineSpan();
                var filePath = document.FilePath ?? document.Name;
                memberReferenceCollisions.Add(
                    $"{referencedSymbol!.Kind} '{referencedSymbol.Name}' at {filePath}:{lineSpan.StartLinePosition.Line + 1}");
            }
        }

        if (declarationCollisions.Count > 0)
        {
            throw new ArgumentException(
                $"parameter_object_preview refuses: new parameter name '{newParameterName}' collides with a declaration inside the target method and would capture or break rewritten references. " +
                "Choose a distinct parameterName. Collisions: " +
                string.Join("; ", declarationCollisions.Distinct(StringComparer.Ordinal)),
                nameof(newParameterName));
        }

        if (memberReferenceCollisions.Count > 0)
        {
            throw new ArgumentException(
                $"parameter_object_preview refuses: new parameter name '{newParameterName}' would capture an existing unqualified member reference inside the target method. " +
                "Choose a distinct parameterName or qualify the affected member reference. Collisions: " +
                string.Join("; ", memberReferenceCollisions.Distinct(StringComparer.Ordinal)),
                nameof(newParameterName));
        }
    }

    private static bool IsValueDeclaration(ISymbol? symbol) => symbol switch
    {
        ILocalSymbol => true,
        IParameterSymbol => true,
        IRangeVariableSymbol => true,
        IMethodSymbol { MethodKind: MethodKind.LocalFunction } => true,
        _ => false,
    };

    private static bool IsCapturableMember(ISymbol? symbol) => symbol switch
    {
        IFieldSymbol => true,
        IPropertySymbol => true,
        IEventSymbol => true,
        IMethodSymbol { MethodKind: not MethodKind.LocalFunction and not MethodKind.AnonymousFunction } => true,
        _ => false,
    };

    private static bool IsUnqualifiedReference(IdentifierNameSyntax reference) => reference.Parent switch
    {
        MemberAccessExpressionSyntax memberAccess when memberAccess.Name == reference => false,
        MemberBindingExpressionSyntax => false,
        QualifiedNameSyntax qualifiedName when qualifiedName.Right == reference => false,
        AliasQualifiedNameSyntax aliasQualifiedName when aliasQualifiedName.Name == reference => false,
        NameColonSyntax => false,
        NameEqualsSyntax => false,
        _ => true,
    };

    private static IEnumerable<SyntaxNode> GetBodyNodes(BaseMethodDeclarationSyntax declaration)
    {
        if (declaration.Body is not null)
        {
            foreach (var node in declaration.Body.DescendantNodesAndSelf())
                yield return node;
        }

        if (declaration.ExpressionBody is not null)
        {
            foreach (var node in declaration.ExpressionBody.DescendantNodesAndSelf())
                yield return node;
        }
    }

    private static async Task EnforceGroupedParametersAreReadOnlyAsync(
        Solution solution,
        IMethodSymbol method,
        IReadOnlyList<IParameterSymbol> grouped,
        CancellationToken ct)
    {
        var groupedMembersByOrdinal = grouped.ToDictionary(parameter => parameter.Ordinal, parameter => parameter.Name);
        var unsupportedUses = new List<string>();

        foreach (var declarationReference in method.DeclaringSyntaxReferences)
        {
            var declaration = await declarationReference.GetSyntaxAsync(ct).ConfigureAwait(false);
            if (declaration is not BaseMethodDeclarationSyntax methodDeclaration) continue;
            var document = solution.GetDocument(declaration.SyntaxTree);
            if (document is null) continue;
            var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (semanticModel?.GetDeclaredSymbol(methodDeclaration, ct) is not IMethodSymbol declaredMethod) continue;

            var groupedSymbols = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
            foreach (var (ordinal, parameterName) in groupedMembersByOrdinal)
            {
                if (ordinal < declaredMethod.Parameters.Length)
                    groupedSymbols[declaredMethod.Parameters[ordinal]] = parameterName;
            }

            foreach (var reference in GetBodyNodes(methodDeclaration).OfType<IdentifierNameSyntax>())
            {
                var symbol = semanticModel.GetSymbolInfo(reference, ct).Symbol;
                if (symbol is not IParameterSymbol parameter
                    || !groupedSymbols.TryGetValue(parameter, out var parameterName))
                {
                    continue;
                }

                var useKind = ClassifyVariableRequiredUse(reference, parameter, semanticModel, ct);
                if (useKind is null) continue;

                var lineSpan = reference.GetLocation().GetLineSpan();
                var filePath = document.FilePath ?? document.Name;
                unsupportedUses.Add(
                    $"'{parameterName}' at {filePath}:{lineSpan.StartLinePosition.Line + 1} ({useKind})");
            }
        }

        if (unsupportedUses.Count > 0)
        {
            throw new InvalidOperationException(
                "parameter_object_preview refuses: grouped parameters are written or aliased inside the target method. " +
                "Generated positional-record properties cannot preserve variable-required semantics. " +
                "Make these uses read-only or omit the affected parameters. Affected uses: " +
                string.Join("; ", unsupportedUses.Distinct(StringComparer.Ordinal)));
        }
    }

    private static string? ClassifyVariableRequiredUse(
        IdentifierNameSyntax reference,
        IParameterSymbol parameter,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var semanticUseKind = ClassifySemanticVariableRequiredUse(reference, semanticModel, ct);
        if (semanticUseKind is not null) return semanticUseKind;

        var valueTypeMutationKind = ClassifyValueTypeMutation(reference, parameter, semanticModel, ct);
        if (valueTypeMutationKind is not null) return valueTypeMutationKind;

        var assignment = reference.Ancestors().OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(candidate => IsDirectAssignmentTarget(candidate.Left, reference));
        if (assignment is not null)
            return assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ? "assignment" : "compound assignment";

        var effectiveNode = UnwrapParentheses(reference);
        if (effectiveNode.Parent is PrefixUnaryExpressionSyntax prefix
            && (prefix.IsKind(SyntaxKind.PreIncrementExpression)
                || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
        {
            return "increment/decrement";
        }
        if (effectiveNode.Parent is PostfixUnaryExpressionSyntax postfix
            && (postfix.IsKind(SyntaxKind.PostIncrementExpression)
                || postfix.IsKind(SyntaxKind.PostDecrementExpression)))
        {
            return "increment/decrement";
        }
        if (effectiveNode.Parent is PrefixUnaryExpressionSyntax addressOf
            && addressOf.IsKind(SyntaxKind.AddressOfExpression))
        {
            return "address-of expression";
        }
        if (effectiveNode.Parent is MakeRefExpressionSyntax)
            return "typed-reference alias";
        if (effectiveNode.Parent is RefExpressionSyntax)
            return "ref expression";

        if (effectiveNode.Parent is ArgumentSyntax argument && argument.Expression == effectiveNode)
        {
            if (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)) return "ref argument";
            if (argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) return "out argument";
            if (argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword)) return "in argument";
        }

        return null;
    }

    private static string? ClassifySemanticVariableRequiredUse(
        IdentifierNameSyntax reference,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var operation = semanticModel.GetOperation(reference, ct);
        if (operation is null) return null;

        operation = UnwrapTransparentOperation(operation);
        if (operation.Parent is IArgumentOperation argument && ReferenceEquals(argument.Value, operation))
        {
            if (argument.Parameter?.RefKind == RefKind.Ref
                && (argument.Parameter.IsThis || argument.Syntax is not ArgumentSyntax))
            {
                return "ref extension receiver";
            }
            if (argument.Parameter?.RefKind == RefKind.Ref) return "ref argument";
            if (argument.Parameter?.RefKind == RefKind.Out) return "out argument";
        }

        if (operation.Parent is IInvocationOperation invocation && ReferenceEquals(invocation.Instance, operation))
        {
            var receiver = invocation.TargetMethod.ReducedFrom?.Parameters.FirstOrDefault();
            if (receiver?.RefKind == RefKind.Ref)
                return "ref extension receiver";
        }

        return null;
    }

    /// <summary>
    /// Detects mutations that flow THROUGH a mutable value-type parameter — a non-readonly
    /// instance member call or a nested member/element write — which the positional-record
    /// rewrite would silently redirect onto a temporary struct copy (the mutation would
    /// compile cleanly and be discarded). Reference-type parameters and readonly structs
    /// short-circuit to null: member mutation through a reference lands on the shared heap
    /// object either way, so those uses stay eligible.
    /// </summary>
    private static string? ClassifyValueTypeMutation(
        IdentifierNameSyntax reference,
        IParameterSymbol parameter,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        if (!parameter.Type.IsValueType) return null;
        if (parameter.Type is INamedTypeSymbol { IsReadOnly: true }) return null;

        var operation = semanticModel.GetOperation(reference, ct);
        if (operation is not null)
        {
            var unwrapped = UnwrapTransparentOperation(operation);
            if (unwrapped.Parent is IInvocationOperation invocation
                && ReferenceEquals(invocation.Instance, unwrapped)
                && invocation.TargetMethod.ContainingType?.IsValueType == true
                && !invocation.TargetMethod.IsReadOnly)
            {
                return "mutable value-type member call";
            }
        }

        // Walk the receiver chain rooted at this reference (param.Field, param[i], and
        // nested combinations). A write through the chain mutates the parameter only when
        // every intermediate receiver is itself value-typed — the first reference-typed
        // segment re-anchors the write onto a shared heap object, keeping it eligible.
        var chainRoot = UnwrapParentheses(reference);
        ExpressionSyntax current = chainRoot;
        while (true)
        {
            ExpressionSyntax? next = current.Parent switch
            {
                MemberAccessExpressionSyntax member when member.Expression == current => member,
                ElementAccessExpressionSyntax element when element.Expression == current => element,
                _ => null,
            };
            if (next is null) break;

            if (current != chainRoot
                && semanticModel.GetTypeInfo(current, ct).Type?.IsValueType != true)
            {
                return null;
            }

            current = UnwrapParentheses(next);
        }

        if (current == chainRoot) return null;

        if (current.Parent is AssignmentExpressionSyntax assignment && assignment.Left == current)
            return "value-type member assignment";
        if (current.Parent is PrefixUnaryExpressionSyntax prefix
            && (prefix.IsKind(SyntaxKind.PreIncrementExpression)
                || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
        {
            return "value-type member assignment";
        }
        if (current.Parent is PostfixUnaryExpressionSyntax postfix
            && (postfix.IsKind(SyntaxKind.PostIncrementExpression)
                || postfix.IsKind(SyntaxKind.PostDecrementExpression)))
        {
            return "value-type member assignment";
        }

        return null;
    }

    private static IOperation UnwrapTransparentOperation(IOperation operation)
    {
        while (true)
        {
            switch (operation.Parent)
            {
                case IConversionOperation conversion when ReferenceEquals(conversion.Operand, operation):
                    operation = conversion;
                    continue;
                case IParenthesizedOperation parenthesized when ReferenceEquals(parenthesized.Operand, operation):
                    operation = parenthesized;
                    continue;
                default:
                    return operation;
            }
        }
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        var current = expression;
        while (current.Parent is ParenthesizedExpressionSyntax parenthesized
            && parenthesized.Expression == current)
        {
            current = parenthesized;
        }
        return current;
    }

    private static bool IsDirectAssignmentTarget(ExpressionSyntax target, IdentifierNameSyntax reference)
    {
        target = target is ParenthesizedExpressionSyntax parenthesized
            ? UnwrapParenthesizedExpression(parenthesized)
            : target;
        if (target.Span == reference.Span) return true;
        return target is TupleExpressionSyntax tuple
            && tuple.Arguments.Any(argument => IsDirectAssignmentTarget(argument.Expression, reference));
    }

    private static ExpressionSyntax UnwrapParenthesizedExpression(ParenthesizedExpressionSyntax expression)
    {
        ExpressionSyntax current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesized)
            current = parenthesized.Expression;
        return current;
    }

    /// <summary>
    /// Refuses target methods whose kind or dispatch contract the single-declaration
    /// rewriter cannot rewrite atomically. Must run before any caller collection or
    /// preview construction so no compile-breaking partial rewrite is ever stored as a
    /// redeemable preview token.
    /// </summary>
    private static void EnforceTargetMethodContractRefusals(IMethodSymbol target)
    {
        if (target.MethodKind == MethodKind.LocalFunction)
            throw new ArgumentException(
                $"parameter_object_preview does not support local functions ({target.ToDisplayString()}); v1 scope is intra-class methods only.",
                nameof(target));

        if (target.MethodKind == MethodKind.ExplicitInterfaceImplementation || !target.ExplicitInterfaceImplementations.IsEmpty)
            throw new ArgumentException(
                $"parameter_object_preview refuses: '{target.ToDisplayString()}' explicitly implements an interface member. " +
                "The interface declaration would keep the old signature, breaking the implementation relationship; " +
                "change the interface first, then re-run against a free-standing method.",
                nameof(target));

        if (target.MethodKind is not (MethodKind.Ordinary or MethodKind.ReducedExtension))
            throw new ArgumentException(
                $"parameter_object_preview refuses: '{target.ToDisplayString()}' has method kind '{target.MethodKind}'. " +
                "Constructors, destructors, accessors, operators, conversions, and delegate/anonymous functions " +
                "cannot have their declaration and call sites rewritten atomically; only ordinary methods " +
                "(including extension methods) are supported.",
                nameof(target));

        if (target.IsOverride)
            throw new ArgumentException(
                $"parameter_object_preview refuses: '{target.ToDisplayString()}' is an 'override'. " +
                "Rewriting it in isolation would leave the base declaration and sibling overrides at the old arity, " +
                "breaking the override relationship; restructure the whole hierarchy manually instead.",
                nameof(target));

        if (target.IsVirtual || target.IsAbstract)
            throw new ArgumentException(
                $"parameter_object_preview refuses: '{target.ToDisplayString()}' is a " +
                $"{(target.IsAbstract ? "'abstract'" : "'virtual'")} dispatch root. " +
                "Overrides in derived types would keep the old signature; restructure the whole hierarchy manually instead.",
                nameof(target));

        if (ImplementsInterfaceMember(target))
            throw new ArgumentException(
                $"parameter_object_preview refuses: '{target.ToDisplayString()}' implements an interface member. " +
                "The interface declaration would keep the old signature, breaking the implementation relationship; " +
                "change the interface first, then re-run against a free-standing method.",
                nameof(target));

        if (target.IsExtern || target.GetAttributes().Any(a =>
                a.AttributeClass?.Name is "DllImportAttribute" or "LibraryImportAttribute"))
            throw new ArgumentException(
                $"parameter_object_preview refuses: '{target.ToDisplayString()}' is an extern/PInvoke declaration. " +
                "Its signature is bound to a native entry point and cannot be regrouped.",
                nameof(target));

        if (target.PartialDefinitionPart is not null || target.PartialImplementationPart is not null)
            throw new ArgumentException(
                $"parameter_object_preview refuses: '{target.ToDisplayString()}' is a partial method with paired " +
                "definition/implementation declarations, which may bind different parameter names. " +
                "Merge the partial declarations first, then re-run.",
                nameof(target));
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol target)
    {
        var containingType = target.ContainingType;
        if (containingType is null) return false;
        return containingType.AllInterfaces
            .SelectMany(i => i.GetMembers().OfType<IMethodSymbol>())
            .Any(m => SymbolEqualityComparer.Default.Equals(
                containingType.FindImplementationForInterfaceMember(m), target));
    }

    private static void EnforceParameterShapeRefusals(IMethodSymbol method, IReadOnlyList<IParameterSymbol> grouped)
    {
        foreach (var p in grouped)
        {
            if (p.RefKind != RefKind.None)
                throw new ArgumentException(
                    $"parameter_object_preview refuses: parameter '{p.Name}' has by-ref kind '{p.RefKind}'. " +
                    "Positional records cannot carry ref/out/in semantics; remove it from parameterNames.",
                    nameof(grouped));
            if (p.IsParams)
                throw new ArgumentException(
                    $"parameter_object_preview refuses: parameter '{p.Name}' is a 'params' array. " +
                    "Variadic call shape does not survive grouping; remove it from parameterNames.",
                    nameof(grouped));
            if (p.IsThis)
                throw new ArgumentException(
                    $"parameter_object_preview refuses: parameter '{p.Name}' is the extension-method 'this' receiver. " +
                    "Drop it from parameterNames; other parameters of the extension method remain eligible.",
                    nameof(grouped));
        }
    }

    private static (Project DtoProject, bool VisibilityIsPublic) ResolveDtoProject(
        Solution solution, IMethodSymbol method, ParameterObjectPreviewRequest request)
    {
        var methodProject = solution.GetProject(method.ContainingAssembly)
            ?? throw new InvalidOperationException(
                $"Could not resolve a project for the method's containing assembly '{method.ContainingAssembly?.Name}'.");

        if (string.IsNullOrWhiteSpace(request.DtoProjectName))
        {
            var sameProjectVisibility = method.ContainingType.DeclaredAccessibility == Accessibility.Public;
            return (methodProject, sameProjectVisibility);
        }

        var dtoProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, request.DtoProjectName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"dtoProjectName '{request.DtoProjectName}' was not found in the loaded solution.",
                nameof(request));

        var crossProject = dtoProject.Id != methodProject.Id;
        // Cross-project: force public so the method's signature can reference it from another assembly.
        var visibility = crossProject || method.ContainingType.DeclaredAccessibility == Accessibility.Public;
        return (dtoProject, visibility);
    }

    /// <summary>
    /// Semantic argument binding for one caller invocation, captured against the baseline
    /// solution: a child-index syntax path locating the invocation, the invocation span,
    /// one argument-list index per target parameter ordinal (null = omitted or supplied by
    /// the extension receiver), and the expanded params-tail argument indices in source order.
    /// </summary>
    private sealed record CallSiteBinding(
        string SyntaxPath,
        TextSpan Span,
        int?[] SlotArgIndices,
        IReadOnlyList<int> VariadicArgIndices);

    /// <summary>
    /// Collects every reference to the target method and binds each one semantically to a
    /// concrete invocation shape via <see cref="IInvocationOperation"/>. Any reference that
    /// is not a direct invocation of the target (method group, delegate conversion, nameof,
    /// doc-comment reference) — or whose arguments cannot be mapped completely onto the
    /// target's parameters — refuses the whole preview here, before any token is stored,
    /// so a redeemed preview always covers every reference atomically.
    /// </summary>
    private static async Task<Dictionary<DocumentId, List<CallSiteBinding>>> CollectCallSiteBindingsAsync(
        Solution solution,
        IMethodSymbol method,
        IReadOnlyList<IParameterSymbol> grouped,
        CancellationToken ct)
    {
        var spansByDoc = new Dictionary<DocumentId, List<TextSpan>>();
        var callers = await SymbolFinder.FindCallersAsync(method, solution, ct).ConfigureAwait(false);
        foreach (var caller in callers)
        {
            foreach (var location in caller.Locations)
            {
                ct.ThrowIfCancellationRequested();
                if (!location.IsInSource) continue;
                var doc = solution.GetDocument(location.SourceTree);
                if (doc is null) continue;
                if (!spansByDoc.TryGetValue(doc.Id, out var spans))
                {
                    spans = [];
                    spansByDoc[doc.Id] = spans;
                }
                if (!spans.Contains(location.SourceSpan))
                    spans.Add(location.SourceSpan);
            }
        }

        var result = new Dictionary<DocumentId, List<CallSiteBinding>>();
        foreach (var (docId, spans) in spansByDoc)
        {
            var doc = solution.GetDocument(docId);
            if (doc is null) continue;
            var filePath = doc.FilePath ?? doc.Name;
            var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (root is null || semanticModel is null)
                throw new InvalidOperationException(
                    $"parameter_object_preview refuses: caller document '{filePath}' has no syntax root or semantic model, " +
                    "so its references cannot be verified for an atomic rewrite.");

            var bindings = new List<CallSiteBinding>();
            var seenInvocationSpans = new HashSet<TextSpan>();
            foreach (var span in spans)
            {
                var node = root.FindNode(span);
                var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
                if (invocation is null || !invocation.Expression.Span.Contains(span))
                {
                    var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    throw new InvalidOperationException(
                        $"parameter_object_preview refuses: '{method.ToDisplayString()}' is referenced without a direct invocation " +
                        $"(method group, delegate conversion, nameof, or doc-comment reference) at {filePath}:{line}. " +
                        "The rewrite is atomic-or-refused; convert or remove this reference first.");
                }
                if (!seenInvocationSpans.Add(invocation.Span)) continue;
                bindings.Add(BindCallSite(semanticModel, root, invocation, method, grouped, filePath, ct));
            }
            if (bindings.Count > 0) result[docId] = bindings;
        }
        return result;
    }

    /// <summary>
    /// Derives every argument-to-parameter association for one invocation from the
    /// compiler's <see cref="IInvocationOperation"/> bindings instead of lexical argument
    /// position, so in-position named arguments, reduced extension-method receivers, and
    /// expanded params tails all map correctly. Refuses any shape it cannot map completely.
    /// </summary>
    private static CallSiteBinding BindCallSite(
        SemanticModel semanticModel,
        SyntaxNode root,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        IReadOnlyList<IParameterSymbol> grouped,
        string filePath,
        CancellationToken ct)
    {
        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var site = $"{filePath}:{line}";
        static InvalidOperationException Unmappable(string site) => new(
            $"parameter_object_preview refuses: the call at {site} has an argument shape that cannot be mapped " +
            "completely onto the target's parameters (unsupported params/collection expansion or synthesized argument).");

        if (semanticModel.GetOperation(invocation, ct) is not IInvocationOperation operation)
            throw new InvalidOperationException(
                $"parameter_object_preview refuses: the call at {site} cannot be bound to an invocation operation, " +
                "so its arguments cannot be mapped onto the target's parameters.");

        var normalizedTarget = (method.ReducedFrom ?? method).OriginalDefinition;
        var normalizedInvoked = (operation.TargetMethod.ReducedFrom ?? operation.TargetMethod).OriginalDefinition;
        if (!SymbolEqualityComparer.Default.Equals(normalizedInvoked, normalizedTarget))
            throw new InvalidOperationException(
                $"parameter_object_preview refuses: the reference at {site} does not resolve to a direct invocation of " +
                $"'{method.ToDisplayString()}'.");

        var args = invocation.ArgumentList.Arguments;
        var slots = new int?[method.Parameters.Length];
        var variadic = new List<int>();
        // IOperation represents extension invocations in unreduced form (receiver as
        // argument 0); align its parameter ordinals with the resolved method symbol's.
        var ordinalOffset = method.Parameters.Length - operation.TargetMethod.Parameters.Length;

        foreach (var argument in operation.Arguments)
        {
            if (argument.Parameter is null) throw Unmappable(site);
            var ordinal = argument.Parameter.Ordinal + ordinalOffset;
            switch (argument.ArgumentKind)
            {
                case ArgumentKind.DefaultValue:
                    continue; // omitted at the call site; grouped omissions refuse downstream
                case ArgumentKind.Explicit when argument.Syntax is ArgumentSyntax argumentSyntax:
                    var index = args.IndexOf(argumentSyntax);
                    if (index < 0 || ordinal < 0 || ordinal >= slots.Length) throw Unmappable(site);
                    slots[ordinal] = index;
                    continue;
                case ArgumentKind.Explicit when argument.Parameter.Ordinal == 0
                    && operation.TargetMethod.IsExtensionMethod
                    && operation.Instance is null:
                    // Reduced extension receiver: IOperation surfaces it as argument 0 of the
                    // unreduced form with the receiver expression (not an ArgumentSyntax) as
                    // its syntax. It stays the receiver, so it takes no argument slot.
                    continue;
                case ArgumentKind.ParamArray:
                    if (argument.Value is not IArrayCreationOperation { Initializer: { } initializer })
                        throw Unmappable(site);
                    foreach (var element in initializer.ElementValues)
                    {
                        var elementIndex = IndexOfArgumentContaining(args, element.Syntax.Span);
                        if (elementIndex < 0) throw Unmappable(site);
                        variadic.Add(elementIndex);
                    }
                    continue;
                default:
                    throw Unmappable(site);
            }
        }
        variadic.Sort();

        EnforceEvaluationOrderPreserved(semanticModel, args, slots, variadic, grouped, site, ct);

        return new CallSiteBinding(BuildSyntaxPath(root, invocation), invocation.Span, slots, variadic);
    }

    private static int IndexOfArgumentContaining(SeparatedSyntaxList<ArgumentSyntax> args, TextSpan span)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Expression.Span.Contains(span)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Refuses any call site whose rewrite would evaluate a non-constant argument
    /// expression in a different left-to-right order than the source call. Compile-time
    /// constants have no observable evaluation, so they may reorder freely; everything
    /// else must keep its source-relative order or the preview is refused.
    /// </summary>
    private static void EnforceEvaluationOrderPreserved(
        SemanticModel semanticModel,
        SeparatedSyntaxList<ArgumentSyntax> args,
        int?[] slots,
        IReadOnlyList<int> variadic,
        IReadOnlyList<IParameterSymbol> grouped,
        string site,
        CancellationToken ct)
    {
        var groupedOrdinals = new HashSet<int>(grouped.Select(p => p.Ordinal));
        var firstGroupedOrdinal = grouped.Min(p => p.Ordinal);
        var emission = new List<int>(args.Count);
        for (var ordinal = 0; ordinal < slots.Length; ordinal++)
        {
            if (ordinal == firstGroupedOrdinal)
            {
                foreach (var parameter in grouped)
                {
                    if (slots[parameter.Ordinal] is int groupedIndex) emission.Add(groupedIndex);
                }
                continue;
            }
            if (groupedOrdinals.Contains(ordinal)) continue;
            if (slots[ordinal] is int retainedIndex) emission.Add(retainedIndex);
        }
        emission.AddRange(variadic);

        var previousNonConstantIndex = -1;
        foreach (var index in emission)
        {
            if (semanticModel.GetConstantValue(args[index].Expression, ct).HasValue) continue;
            if (index < previousNonConstantIndex)
                throw new InvalidOperationException(
                    $"parameter_object_preview refuses: rewriting the call at {site} would evaluate a non-constant argument " +
                    "expression in a different order than the source call (named arguments out of declaration order, or grouped " +
                    "parameters that interleave with retained ones). Reorder the arguments at the call site first.");
            previousNonConstantIndex = index;
        }
    }

    private static void EnforceCrossProjectReferences(
        Solution solution,
        Project methodProject,
        Project dtoProject,
        Dictionary<DocumentId, List<CallSiteBinding>> callSites)
    {
        var missing = new List<string>();
        var seenProjects = new HashSet<ProjectId>();

        // The declaring project must reference the DTO project so the rewritten signature
        // (which now mentions the new record type) compiles. Same-project case is the no-op.
        void Check(Project p)
        {
            if (p.Id == dtoProject.Id) return;
            if (!seenProjects.Add(p.Id)) return;
            var hasReference = p.AllProjectReferences.Any(r => r.ProjectId == dtoProject.Id);
            if (!hasReference) missing.Add($"{p.Name} -> {dtoProject.Name}");
        }

        Check(methodProject);
        foreach (var docId in callSites.Keys)
        {
            var callerProject = solution.GetProject(docId.ProjectId);
            if (callerProject is null) continue;
            Check(callerProject);
        }

        if (missing.Count > 0)
            throw new ArgumentException(
                "parameter_object_preview refuses: one or more caller projects do not reference the DTO project. " +
                "Add a project reference (use add_project_reference_preview) for each entry, then retry. " +
                "Missing references: " + string.Join("; ", missing));
    }

    /// <summary>
    /// Validates that every grouped parameter type can legally appear as a positional member
    /// of the generated record. Three refusal classes, each thrown before any preview token is
    /// stored: (1) the type depends on a method or containing-type type parameter, which a
    /// non-generic top-level record cannot bind; (2) some constituent type is less accessible
    /// than the record visibility chosen by <see cref="ResolveDtoProject"/>, which would emit
    /// an inconsistent-accessibility (CS0051-style) DTO; (3) for a cross-project DTO, some
    /// source-declared constituent type lives in an assembly the DTO project cannot see, so
    /// the emitted type name would not resolve (CS0246).
    /// </summary>
    private static async Task EnforceGroupedParameterTypesAreDtoCompatibleAsync(
        Solution solution,
        Project methodProject,
        Project dtoProject,
        IReadOnlyList<IParameterSymbol> grouped,
        bool dtoVisibilityIsPublic,
        CancellationToken ct)
    {
        var crossProject = dtoProject.Id != methodProject.Id;
        var dtoCompilation = crossProject
            ? await dtoProject.GetCompilationAsync(ct).ConfigureAwait(false)
            : null;
        var neededRank = dtoVisibilityIsPublic ? PublicAccessibilityRank : InternalAccessibilityRank;
        var recordVisibility = dtoVisibilityIsPublic ? "public" : "internal";

        foreach (var parameter in grouped)
        {
            foreach (var constituent in EnumerateConstituentTypes(parameter.Type))
            {
                if (constituent is ITypeParameterSymbol typeParameter)
                {
                    var declarer = typeParameter.DeclaringMethod is { } declaringMethod
                        ? $"method '{declaringMethod.ToDisplayString()}'"
                        : $"type '{typeParameter.DeclaringType?.ToDisplayString()}'";
                    throw new ArgumentException(
                        $"parameter_object_preview refuses: parameter '{parameter.Name}' has type '{parameter.Type.ToDisplayString()}' " +
                        $"which depends on type parameter '{typeParameter.Name}' declared by {declarer}. " +
                        "The generated record is non-generic and its new top-level file cannot bind that type parameter; " +
                        "remove the parameter from parameterNames.",
                        nameof(grouped));
                }

                if (constituent is not INamedTypeSymbol named || named.TypeKind == TypeKind.Error)
                    continue;

                if (AccessibilityRank(named.DeclaredAccessibility) < neededRank)
                    throw new ArgumentException(
                        $"parameter_object_preview refuses: parameter '{parameter.Name}' has type '{parameter.Type.ToDisplayString()}' " +
                        $"involving '{named.ToDisplayString()}', which is '{named.DeclaredAccessibility.ToString().ToLowerInvariant()}' — " +
                        $"less accessible than the generated '{recordVisibility}' record. " +
                        "Widen the type's accessibility, or remove the parameter from parameterNames.",
                        nameof(grouped));

                if (crossProject
                    && named.Locations.Any(l => l.IsInSource)
                    && !IsAssemblyReachable(dtoCompilation!, named.ContainingAssembly))
                {
                    var typeProjectName = solution.GetProject(named.ContainingAssembly, ct)?.Name
                        ?? named.ContainingAssembly.Name;
                    throw new ArgumentException(
                        $"parameter_object_preview refuses: parameter '{parameter.Name}' has type '{parameter.Type.ToDisplayString()}' " +
                        $"involving '{named.ToDisplayString()}', which is declared in '{typeProjectName}' — not referenced by DTO project '{dtoProject.Name}'. " +
                        "Add a project reference (use add_project_reference_preview) for each entry, then retry. " +
                        $"Missing references: {dtoProject.Name} -> {typeProjectName}",
                        nameof(grouped));
                }
            }
        }
    }

    private const int PublicAccessibilityRank = 4;
    private const int InternalAccessibilityRank = 2;

    /// <summary>
    /// Rank order for "accessible from a new top-level file": protected/private-family
    /// accessibility never qualifies, because the generated record is never nested inside
    /// the declaring type.
    /// </summary>
    private static int AccessibilityRank(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => PublicAccessibilityRank,
        Accessibility.ProtectedOrInternal => 3,
        Accessibility.Internal => InternalAccessibilityRank,
        _ => 1,
    };

    /// <summary>
    /// Yields <paramref name="type"/> and every type it structurally depends on: array
    /// element types, pointer/function-pointer constituents, generic type arguments, and
    /// containing types of nested types.
    /// </summary>
    private static IEnumerable<ITypeSymbol> EnumerateConstituentTypes(ITypeSymbol type)
    {
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<ITypeSymbol>();
        pending.Push(type);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            switch (current)
            {
                case IArrayTypeSymbol array:
                    pending.Push(array.ElementType);
                    break;
                case IPointerTypeSymbol pointer:
                    pending.Push(pointer.PointedAtType);
                    break;
                case IFunctionPointerTypeSymbol functionPointer:
                    pending.Push(functionPointer.Signature.ReturnType);
                    foreach (var p in functionPointer.Signature.Parameters) pending.Push(p.Type);
                    break;
                case INamedTypeSymbol named:
                    foreach (var argument in named.TypeArguments) pending.Push(argument);
                    if (named.ContainingType is { } outer) pending.Push(outer);
                    break;
            }
        }
    }

    /// <summary>
    /// True when <paramref name="assembly"/>'s types resolve inside <paramref name="dtoCompilation"/>:
    /// it is the DTO assembly itself or one of its referenced assemblies (compared by identity,
    /// because the symbols originate from different compilations).
    /// </summary>
    private static bool IsAssemblyReachable(Compilation dtoCompilation, IAssemblySymbol assembly)
    {
        if (dtoCompilation.Assembly.Identity.Equals(assembly.Identity)) return true;
        return dtoCompilation.SourceModule.ReferencedAssemblySymbols
            .Any(referenced => referenced.Identity.Equals(assembly.Identity));
    }

    private static async Task<List<string>> CollectDefaultValueWarningsAsync(
        Solution solution,
        IReadOnlyList<IParameterSymbol> grouped,
        Dictionary<DocumentId, List<CallSiteBinding>> callSites,
        CancellationToken ct)
    {
        // A grouped parameter with no bound argument slot relied on its default value at
        // that call site — the same semantic binding the rewriter consumes, so this gate
        // and the rewrite agree by construction.
        var warnings = new List<string>();
        foreach (var (docId, bindings) in callSites)
        {
            var doc = solution.GetDocument(docId);
            if (doc is null) continue;
            var text = await doc.GetTextAsync(ct).ConfigureAwait(false);
            var filePath = doc.FilePath ?? doc.Name;
            foreach (var binding in bindings)
            {
                foreach (var parameter in grouped)
                {
                    if (binding.SlotArgIndices[parameter.Ordinal] is not null) continue;
                    var line = text.Lines.GetLinePosition(binding.Span.Start).Line + 1;
                    warnings.Add($"{filePath}:{line} omits '{parameter.Name}'");
                }
            }
        }
        return warnings;
    }

    private static (string Namespace, IReadOnlyList<string> Folders) ResolveDtoLocation(
        Project dtoProject, ParameterObjectPreviewRequest request, IMethodSymbol method)
    {
        var ns = request.DtoNamespace;
        if (string.IsNullOrWhiteSpace(ns))
        {
            ns = method.ContainingNamespace?.IsGlobalNamespace == false
                ? method.ContainingNamespace.ToDisplayString()
                : dtoProject.Name;
        }
        else
        {
            // Caller-supplied namespace is untrusted boundary input: it is emitted into the
            // DTO source AND (when dtoFolders is omitted) split into folder segments that
            // feed Path.Combine, so every dot-separated part must be a legal identifier.
            foreach (var part in ns.Split('.'))
            {
                if (part.Length == 0)
                    throw new InvalidOperationException(
                        $"dtoNamespace '{ns}' contains an empty namespace part " +
                        "(check for doubled, leading, or trailing dots).");
                IdentifierValidation.ThrowIfInvalidIdentifier(part, $"dtoNamespace part '{part}'");
            }
        }

        IReadOnlyList<string> folders;
        if (request.DtoFolders is { Count: > 0 })
        {
            folders = SplitCallerSuppliedFolders(request.DtoFolders);
        }
        else
        {
            folders = ResolveFolderSegmentsForNamespace(ns!, dtoProject.Name);
        }
        ProjectRelativePathValidation.ValidateFolderSegments(folders, "dtoFolders");
        return (ns!, folders);
    }

    /// <summary>
    /// Normalizes caller-supplied <c>dtoFolders</c> entries for validation: a nested-path
    /// entry like <c>"Models/Requests"</c> is common ergonomics, so each entry is refused
    /// only when rooted (which <see cref="Path.Combine(string[])"/> would honor by
    /// discarding the project directory) and otherwise split on both separators into
    /// per-directory segments for <see cref="ProjectRelativePathValidation.ValidateFolderSegments"/>.
    /// Empty split products (doubled/trailing separators) are preserved so the validator
    /// refuses them with an actionable message rather than silently dropping them.
    /// </summary>
    private static IReadOnlyList<string> SplitCallerSuppliedFolders(IReadOnlyList<string> dtoFolders)
    {
        var segments = new List<string>();
        foreach (var entry in dtoFolders)
        {
            if (string.IsNullOrWhiteSpace(entry))
                throw new InvalidOperationException(
                    "dtoFolders contains an empty or whitespace entry; " +
                    "each entry must name a directory inside the target project.");
            if (Path.IsPathRooted(entry))
                throw new InvalidOperationException(
                    $"dtoFolders entry '{entry}' is a rooted path; " +
                    "entries must be relative directory paths inside the target project.");
            segments.AddRange(entry.Split(['/', '\\']));
        }
        return segments;
    }

    private static IReadOnlyList<string> ResolveFolderSegmentsForNamespace(string typeNamespace, string projectName)
    {
        // Mirrors ScaffoldingService.ResolveFolderSegmentsForNamespace — kept private to
        // avoid widening that helper's visibility for a single new caller.
        if (string.IsNullOrWhiteSpace(typeNamespace) || string.Equals(typeNamespace, projectName, StringComparison.Ordinal))
            return Array.Empty<string>();
        var working = typeNamespace.StartsWith(projectName + ".", StringComparison.Ordinal)
            ? typeNamespace[(projectName.Length + 1)..]
            : typeNamespace;
        return working.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ResolveDtoFilePath(Project dtoProject, IReadOnlyList<string> folders, string typeName)
    {
        var projectDir = Path.GetDirectoryName(dtoProject.FilePath)
            ?? throw new InvalidOperationException(
                $"DTO project '{dtoProject.Name}' has no resolvable directory (FilePath='{dtoProject.FilePath}').");
        var combined = Path.Combine([projectDir, .. folders, $"{typeName}.cs"]);
        // Belt-and-braces: even with validated segments, require the canonical result to
        // sit strictly under the canonical project directory before it leaves this method.
        return ProjectRelativePathValidation.EnsureDescendantOfRoot(projectDir, combined, "dtoFolders");
    }

    /// <summary>
    /// Refuses the preview — before any document is added or a token is minted — when the
    /// resolved DTO destination is already occupied: an existing document at the same
    /// canonical path, an existing file on disk (an Added document has no pre-write
    /// snapshot, so redeeming the token would overwrite it silently and irreversibly), or
    /// an existing type <c>{dtoNamespace}.{newTypeName}</c> declared in the DTO project.
    /// </summary>
    private static async Task EnforceDtoDestinationIsFreeAsync(
        Project dtoProject,
        string dtoFilePath,
        string dtoNamespace,
        string newTypeName,
        CancellationToken ct)
    {
        var canonicalTarget = Path.GetFullPath(dtoFilePath);
        var occupied = dtoProject.Documents.Any(d =>
            d.FilePath is not null &&
            string.Equals(Path.GetFullPath(d.FilePath), canonicalTarget, StringComparison.OrdinalIgnoreCase));
        if (occupied)
            throw new InvalidOperationException(
                $"parameter_object_preview refuses: project '{dtoProject.Name}' already contains a document at " +
                $"'{canonicalTarget}'. Choose a different newTypeName or dtoFolders.");

        if (File.Exists(canonicalTarget))
            throw new InvalidOperationException(
                $"parameter_object_preview refuses: a file already exists on disk at '{canonicalTarget}' and " +
                "applying the preview would overwrite it with no rollback snapshot. " +
                "Choose a different newTypeName or dtoFolders, or remove the file first.");

        var compilation = await dtoProject.GetCompilationAsync(ct).ConfigureAwait(false);
        var metadataName = $"{dtoNamespace}.{newTypeName}";
        if (compilation?.Assembly.GetTypeByMetadataName(metadataName) is not null)
            throw new InvalidOperationException(
                $"parameter_object_preview refuses: type '{metadataName}' already exists in project " +
                $"'{dtoProject.Name}'. Choose a different newTypeName or dtoNamespace.");
    }

    /// <summary>
    /// Returns the type reference to emit for the generated DTO from the document position of
    /// <paramref name="contextNode"/>: the bare <paramref name="typeName"/> when the DTO
    /// namespace is already in scope there (the enclosing namespace chain equals or is nested
    /// under it, or a plain using directive visible to the node imports it), otherwise
    /// <c>global::{dtoNamespace}.{typeName}</c> so the emitted reference binds regardless of
    /// the document's local imports. Kept private to the service, mirroring the conditional
    /// qualification pattern in <c>ExtractMethodService</c>. Note: a <c>global using</c>
    /// declared in a different file of the same project is invisible to this syntax-only scan,
    /// so such callers receive a technically-unnecessary but still-correct qualification.
    /// </summary>
    private static string ResolveDtoTypeReference(SyntaxNode contextNode, string dtoNamespace, string typeName)
    {
        if (string.IsNullOrWhiteSpace(dtoNamespace))
            return typeName;

        // Enclosing namespace chain, outermost-first (handles nested block namespaces).
        var namespaceParts = new List<string>();
        foreach (var ns in contextNode.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>())
            namespaceParts.Insert(0, ns.Name.ToString());
        var contextNamespace = string.Join('.', namespaceParts);

        // Name lookup searches each enclosing namespace outward, so the bare name binds when
        // the context namespace equals the DTO namespace or is nested anywhere under it.
        if (string.Equals(contextNamespace, dtoNamespace, StringComparison.Ordinal)
            || contextNamespace.StartsWith(dtoNamespace + ".", StringComparison.Ordinal))
        {
            return typeName;
        }

        // Plain (non-alias, non-static) using directives on the compilation unit or any
        // enclosing namespace declaration — including same-file `global using` — also bring
        // the DTO namespace into scope.
        foreach (var node in contextNode.AncestorsAndSelf())
        {
            var usings = node switch
            {
                CompilationUnitSyntax cu => cu.Usings,
                BaseNamespaceDeclarationSyntax nsDecl => nsDecl.Usings,
                _ => default,
            };
            foreach (var directive in usings)
            {
                if (directive.Alias is null
                    && directive.StaticKeyword.IsKind(SyntaxKind.None)
                    && string.Equals(directive.Name?.ToString(), dtoNamespace, StringComparison.Ordinal))
                {
                    return typeName;
                }
            }
        }

        return $"global::{dtoNamespace}.{typeName}";
    }

    private static string BuildDtoSource(
        string ns, string typeName, IReadOnlyList<IParameterSymbol> grouped, bool isPublic)
    {
        var visibility = isPublic ? "public" : "internal";
        var sb = new StringBuilder();
        sb.Append("namespace ").Append(ns).AppendLine(";");
        sb.AppendLine();
        sb.Append(visibility).Append(" sealed record ").Append(typeName).Append('(');
        for (var i = 0; i < grouped.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(grouped[i].Type.ToDisplayString()).Append(' ').Append(Capitalize(grouped[i].Name));
        }
        sb.AppendLine(");");
        return sb.ToString();
    }

    private static string Capitalize(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private static string CamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static async Task<Solution> RewriteMethodDeclarationAsync(
        Solution solution,
        IMethodSymbol method,
        IReadOnlyList<IParameterSymbol> grouped,
        string dtoNamespace,
        string newTypeName,
        string newParameterName,
        Dictionary<DocumentId, string> originalTexts,
        CancellationToken ct)
    {
        var accumulator = solution;
        var groupedNames = new HashSet<string>(grouped.Select(p => p.Name), StringComparer.Ordinal);
        var groupedMembersByOrdinal = grouped.ToDictionary(p => p.Ordinal, p => Capitalize(p.Name));
        var declarations = new List<(DocumentId DocumentId, BaseMethodDeclarationSyntax Declaration)>();

        foreach (var declRef in method.DeclaringSyntaxReferences)
        {
            var node = await declRef.GetSyntaxAsync(ct).ConfigureAwait(false);
            if (node is not BaseMethodDeclarationSyntax mds) continue;
            var doc = solution.GetDocument(node.SyntaxTree);
            if (doc is not null) declarations.Add((doc.Id, mds));
        }

        foreach (var documentGroup in declarations.GroupBy(d => d.DocumentId))
        {
            var baselineDocument = solution.GetDocument(documentGroup.Key);
            if (baselineDocument is null) continue;
            var semanticModel = await baselineDocument.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (semanticModel is null) continue;

            await CaptureOriginalTextAsync(originalTexts, solution, documentGroup.Key, ct).ConfigureAwait(false);

            // Work from the bottom of a document upward so replacing one partial-method
            // declaration cannot shift the baseline spans of declarations above it.
            foreach (var (_, declaration) in documentGroup.OrderByDescending(d => d.Declaration.SpanStart))
            {
                var doc = accumulator.GetDocument(documentGroup.Key);
                if (doc is null) break;
                var oldRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (oldRoot is null) break;

                var currentMds = oldRoot.FindNode(declaration.Span).FirstAncestorOrSelf<BaseMethodDeclarationSyntax>();
                if (currentMds is null) continue;

                var rewrittenMds = RewriteMethodDeclaration(
                    declaration,
                    semanticModel,
                    groupedNames,
                    groupedMembersByOrdinal,
                    dtoNamespace,
                    newTypeName,
                    newParameterName,
                    ct);

                var newRoot = oldRoot.ReplaceNode(currentMds, rewrittenMds);
                accumulator = accumulator.WithDocumentText(doc.Id, SourceText.From(newRoot.ToFullString()));
            }
        }
        return accumulator;
    }

    private static BaseMethodDeclarationSyntax RewriteMethodDeclaration(
        BaseMethodDeclarationSyntax declaration,
        SemanticModel semanticModel,
        HashSet<string> groupedNames,
        IReadOnlyDictionary<int, string> groupedMembersByOrdinal,
        string dtoNamespace,
        string newTypeName,
        string newParameterName,
        CancellationToken ct)
    {
        var dtoTypeReference = ResolveDtoTypeReference(declaration, dtoNamespace, newTypeName);
        var declaredMethod = semanticModel.GetDeclaredSymbol(declaration, ct) as IMethodSymbol;
        var parameterMembers = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
        if (declaredMethod is not null)
        {
            foreach (var (ordinal, memberName) in groupedMembersByOrdinal)
            {
                if (ordinal < declaredMethod.Parameters.Length)
                    parameterMembers[declaredMethod.Parameters[ordinal]] = memberName;
            }
        }

        var rewritten = declaration;
        var referenceRewriter = new GroupedParameterReferenceRewriter(
            semanticModel,
            parameterMembers,
            newParameterName,
            ct);
        if (declaration.Body is { } body)
            rewritten = rewritten.WithBody((BlockSyntax)referenceRewriter.Visit(body)!);
        if (declaration.ExpressionBody is { } expressionBody)
            rewritten = rewritten.WithExpressionBody((ArrowExpressionClauseSyntax)referenceRewriter.Visit(expressionBody)!);

        var existingList = declaration.ParameterList;
        var newParamTexts = new List<string>(existingList.Parameters.Count);
        var insertedDtoParam = false;
        foreach (var parameter in existingList.Parameters)
        {
            if (groupedNames.Contains(parameter.Identifier.ValueText))
            {
                if (!insertedDtoParam)
                {
                    newParamTexts.Add($"{dtoTypeReference} {newParameterName}");
                    insertedDtoParam = true;
                }
                continue;
            }
            newParamTexts.Add(parameter.ToString());
        }
        if (!insertedDtoParam)
            newParamTexts.Add($"{dtoTypeReference} {newParameterName}");

        var newListText = "(" + string.Join(", ", newParamTexts) + ")";
        var newList = SyntaxFactory.ParseParameterList(newListText).WithTriviaFrom(existingList);
        return rewritten.WithParameterList(newList);
    }

    private sealed class GroupedParameterReferenceRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly IReadOnlyDictionary<IParameterSymbol, string> _parameterMembers;
        private readonly string _newParameterName;
        private readonly CancellationToken _ct;

        public GroupedParameterReferenceRewriter(
            SemanticModel semanticModel,
            IReadOnlyDictionary<IParameterSymbol, string> parameterMembers,
            string newParameterName,
            CancellationToken ct)
        {
            _semanticModel = semanticModel;
            _parameterMembers = parameterMembers;
            _newParameterName = newParameterName;
            _ct = ct;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            _ct.ThrowIfCancellationRequested();
            var symbol = _semanticModel.GetSymbolInfo(node, _ct).Symbol;
            if (symbol is not IParameterSymbol parameter
                || !_parameterMembers.TryGetValue(parameter, out var memberName))
            {
                return base.VisitIdentifierName(node);
            }

            return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(_newParameterName),
                    SyntaxFactory.IdentifierName(memberName))
                .WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (node.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }
                && node.ArgumentList.Arguments is [{ Expression: IdentifierNameSyntax argument }])
            {
                var symbol = _semanticModel.GetSymbolInfo(argument, _ct).Symbol;
                if (symbol is IParameterSymbol parameter
                    && _parameterMembers.ContainsKey(parameter))
                {
                    // nameof(parameter) is a compile-time string equal to the source
                    // symbol name. Replace the entire invocation: rewriting only its
                    // argument would emit the invalid nameof("name") shape, while
                    // nameof(dto.Member) would change the observable string.
                    return SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(parameter.Name))
                        .WithTriviaFrom(node);
                }
            }

            return base.VisitInvocationExpression(node);
        }
    }

    private static async Task<Solution> RewriteCallSitesAsync(
        Solution accumulator,
        Solution baselineSolution,
        IMethodSymbol method,
        IReadOnlyList<IParameterSymbol> grouped,
        string dtoNamespace,
        string newTypeName,
        Dictionary<DocumentId, List<CallSiteBinding>> callSites,
        Dictionary<DocumentId, string> originalTexts,
        Dictionary<string, int> perFileCallsites,
        CancellationToken ct)
    {
        foreach (var (docId, bindings) in callSites)
        {
            ct.ThrowIfCancellationRequested();
            await CaptureOriginalTextAsync(originalTexts, baselineSolution, docId, ct).ConfigureAwait(false);

            var doc = accumulator.GetDocument(docId);
            if (doc is null) continue;
            var oldRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (oldRoot is null) continue;

            // Declaration rewriting may change absolute positions and may replace nested
            // invocations such as nameof(parameter). The child-index syntax path captured
            // at binding time against the baseline root remains stable because those
            // rewrites change expressions, not the containing member or statement
            // structure that locates a target call site.
            var targets = new Dictionary<SyntaxNode, CallSiteBinding>();
            foreach (var binding in bindings)
            {
                if (ResolveSyntaxPath(oldRoot, binding.SyntaxPath) is InvocationExpressionSyntax target)
                    targets[target] = binding;
            }
            if (targets.Count == 0) continue;

            var rewrittenCount = 0;
            var newRoot = oldRoot.ReplaceNodes(targets.Keys, (original, rewrittenNode) =>
            {
                var invocation = (InvocationExpressionSyntax)rewrittenNode;
                // Resolve scope against `original` — it is still attached to oldRoot, so
                // its namespace/using context is visible; `rewrittenNode` may be detached.
                var dtoTypeReference = ResolveDtoTypeReference(original, dtoNamespace, newTypeName);
                var newArgList = BuildRewrittenArgumentList(
                    invocation.ArgumentList, method, grouped, dtoTypeReference, targets[original]);
                if (newArgList is null) return invocation;
                rewrittenCount++;
                return invocation.WithArgumentList(newArgList);
            });
            if (rewrittenCount == 0) continue;

            accumulator = accumulator.WithDocumentText(docId, SourceText.From(newRoot.ToFullString()));
            var filePath = doc.FilePath ?? doc.Name;
            perFileCallsites[filePath] = perFileCallsites.TryGetValue(filePath, out var count)
                ? count + rewrittenCount
                : rewrittenCount;
        }
        return accumulator;
    }

    private static string BuildSyntaxPath(SyntaxNode root, SyntaxNode target)
    {
        var indices = new Stack<int>();
        var current = target;
        while (current != root)
        {
            var parent = current.Parent
                ?? throw new InvalidOperationException("Cannot build a syntax path for a detached caller node.");
            indices.Push(parent.ChildNodes().TakeWhile(child => child != current).Count());
            current = parent;
        }
        return string.Join('.', indices);
    }

    private static SyntaxNode? ResolveSyntaxPath(SyntaxNode root, string path)
    {
        var current = root;
        foreach (var component in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(component, out var index)) return null;
            var children = current.ChildNodes().ToArray();
            if (index < 0 || index >= children.Length) return null;
            current = children[index];
        }
        return current;
    }

    private static ArgumentListSyntax? BuildRewrittenArgumentList(
        ArgumentListSyntax argList,
        IMethodSymbol method,
        IReadOnlyList<IParameterSymbol> grouped,
        string dtoTypeReference,
        CallSiteBinding binding)
    {
        // Argument-to-parameter association was computed semantically at binding time
        // (IInvocationOperation); apply it here by argument-list index so nested rewrites
        // (e.g. nameof replacement inside a caller argument) carry through untouched.
        var args = argList.Arguments;
        ArgumentSyntax? ArgumentAt(int? index) =>
            index is int i && i >= 0 && i < args.Count ? args[i] : null;

        // Build the DTO constructor args in `parameterNames` order, stripping NameColon
        // (positional-record primary ctor takes positional args).
        var dtoArgs = new List<ArgumentSyntax>(grouped.Count);
        for (var i = 0; i < grouped.Count; i++)
        {
            var src = ArgumentAt(binding.SlotArgIndices[grouped[i].Ordinal]);
            if (src is null) return null; // would have been caught upstream as default-value refusal
            var stripped = SyntaxFactory.Argument(src.Expression.WithoutTrivia());
            dtoArgs.Add(i == 0 ? stripped : stripped.WithLeadingTrivia(SyntaxFactory.Space));
        }

        var dtoCreation = SyntaxFactory.ObjectCreationExpression(
            SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.ParseTypeName(dtoTypeReference),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(dtoArgs)),
            initializer: null);

        // Build the rewritten outer call's argument list. Walk the original method's
        // parameters in order; emit a single Argument(dtoCreation) at the first grouped
        // ordinal, skip the rest of the grouped params, emit non-grouped args unchanged
        // (preserving their original NameColon shape if any), and append the full
        // expanded params tail last so no variadic argument is dropped.
        var groupedOrdinals = new HashSet<int>(grouped.Select(p => p.Ordinal));
        var outerArgs = new List<ArgumentSyntax>();
        var emittedDto = false;
        for (var ordinal = 0; ordinal < method.Parameters.Length; ordinal++)
        {
            if (groupedOrdinals.Contains(ordinal))
            {
                if (!emittedDto)
                {
                    outerArgs.Add(SyntaxFactory.Argument(dtoCreation));
                    emittedDto = true;
                }
                continue;
            }
            var src = ArgumentAt(binding.SlotArgIndices[ordinal]);
            if (src is null) continue; // omitted (relies on default) or supplied by the receiver
            outerArgs.Add(SyntaxFactory.Argument(src.NameColon, src.RefKindKeyword, src.Expression.WithoutTrivia()));
        }
        if (!emittedDto)
            outerArgs.Add(SyntaxFactory.Argument(dtoCreation));
        foreach (var variadicIndex in binding.VariadicArgIndices)
        {
            var src = ArgumentAt(variadicIndex);
            if (src is null) return null;
            outerArgs.Add(SyntaxFactory.Argument(src.Expression.WithoutTrivia()));
        }

        // Add canonical inter-arg leading-space trivia.
        for (var i = 1; i < outerArgs.Count; i++)
            outerArgs[i] = outerArgs[i].WithLeadingTrivia(SyntaxFactory.Space);

        return SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(outerArgs))
            .WithTriviaFrom(argList);
    }

    private static async Task<string> CaptureOriginalTextAsync(
        Dictionary<DocumentId, string> originalTexts,
        Solution solution,
        DocumentId docId,
        CancellationToken ct)
    {
        if (originalTexts.TryGetValue(docId, out var cached)) return cached;
        var doc = solution.GetDocument(docId);
        if (doc is null) return string.Empty;
        var text = (await doc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
        originalTexts[docId] = text;
        return text;
    }

    private static async Task<List<FileChangeDto>> BuildFileChangesAsync(
        Solution accumulator,
        Solution baseline,
        Dictionary<DocumentId, string> originalTexts,
        DocumentId addedDocId,
        CancellationToken ct)
    {
        var changes = new List<FileChangeDto>();
        foreach (var (docId, originalText) in originalTexts)
        {
            var finalDoc = accumulator.GetDocument(docId);
            if (finalDoc is null) continue;
            var finalText = (await finalDoc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            if (string.Equals(finalText, originalText, StringComparison.Ordinal)) continue;
            var filePath = finalDoc.FilePath ?? finalDoc.Name;
            changes.Add(new FileChangeDto(filePath, DiffGenerator.GenerateUnifiedDiff(originalText, finalText, filePath)));
        }

        var addedDoc = accumulator.GetDocument(addedDocId);
        if (addedDoc is not null)
        {
            var addedText = (await addedDoc.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            var addedPath = addedDoc.FilePath ?? addedDoc.Name;
            changes.Add(new FileChangeDto(addedPath, DiffGenerator.GenerateUnifiedDiff(string.Empty, addedText, addedPath)));
        }
        return changes;
    }
}
