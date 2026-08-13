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

        if (method.MethodKind == MethodKind.LocalFunction)
            throw new ArgumentException(
                $"parameter_object_preview does not support local functions ({method.ToDisplayString()}); v1 scope is intra-class methods only.",
                nameof(target));

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

        var callerLocations = await CollectCallerSpansAsync(solution, method, ct).ConfigureAwait(false);
        var methodProject = solution.GetProject(method.ContainingAssembly, ct)!;
        EnforceCrossProjectReferences(solution, methodProject, dtoProject, callerLocations);

        var defaultValueWarnings = await CollectDefaultValueWarningsAsync(
            solution, method, groupedParameters, callerLocations, ct).ConfigureAwait(false);
        if (defaultValueWarnings.Count > 0)
        {
            throw new InvalidOperationException(
                "parameter_object_preview refuses: one or more call sites omit a grouped parameter and rely on its default value. " +
                "Either add the explicit argument at every site first, or remove the omitted parameter from parameterNames. " +
                "Affected sites: " + string.Join("; ", defaultValueWarnings));
        }

        var (dtoNamespace, dtoFolders) = ResolveDtoLocation(dtoProject, request, method);
        var dtoFilePath = ResolveDtoFilePath(dtoProject, dtoFolders, request.NewTypeName);
        var dtoSource = BuildDtoSource(dtoNamespace, request.NewTypeName, groupedParameters, dtoVisibilityIsPublic);

        var originalTexts = new Dictionary<DocumentId, string>();
        var perFileCallsites = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var accumulator = await RewriteMethodDeclarationAsync(
            solution, method, groupedParameters, request.NewTypeName, newParameterName, originalTexts, ct).ConfigureAwait(false);

        accumulator = await RewriteCallSitesAsync(
            accumulator, solution, method, groupedParameters, request.NewTypeName, callerLocations,
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

                var useKind = ClassifyVariableRequiredUse(reference, semanticModel, ct);
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
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var semanticUseKind = ClassifySemanticVariableRequiredUse(reference, semanticModel, ct);
        if (semanticUseKind is not null) return semanticUseKind;

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

    private static async Task<Dictionary<DocumentId, List<TextSpan>>> CollectCallerSpansAsync(
        Solution solution, IMethodSymbol method, CancellationToken ct)
    {
        var locations = new Dictionary<DocumentId, List<TextSpan>>();
        var callers = await SymbolFinder.FindCallersAsync(method, solution, ct).ConfigureAwait(false);
        foreach (var caller in callers)
        {
            foreach (var location in caller.Locations)
            {
                ct.ThrowIfCancellationRequested();
                if (!location.IsInSource) continue;
                var doc = solution.GetDocument(location.SourceTree);
                if (doc is null) continue;
                if (!locations.TryGetValue(doc.Id, out var spans))
                {
                    spans = [];
                    locations[doc.Id] = spans;
                }
                if (!spans.Contains(location.SourceSpan))
                    spans.Add(location.SourceSpan);
            }
        }
        return locations;
    }

    private static void EnforceCrossProjectReferences(
        Solution solution,
        Project methodProject,
        Project dtoProject,
        Dictionary<DocumentId, List<TextSpan>> callerLocations)
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
        foreach (var docId in callerLocations.Keys)
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

    private static async Task<List<string>> CollectDefaultValueWarningsAsync(
        Solution solution,
        IMethodSymbol method,
        IReadOnlyList<IParameterSymbol> grouped,
        Dictionary<DocumentId, List<TextSpan>> callerLocations,
        CancellationToken ct)
    {
        var groupedSet = new HashSet<string>(grouped.Select(p => p.Name), StringComparer.Ordinal);
        var warnings = new List<string>();

        foreach (var (docId, spans) in callerLocations)
        {
            var doc = solution.GetDocument(docId);
            if (doc is null) continue;
            var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root is null) continue;
            foreach (var span in spans)
            {
                var node = root.FindNode(span);
                var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
                if (invocation is null) continue;

                var providedNames = CollectProvidedParameterNames(invocation, method);
                foreach (var groupedName in groupedSet)
                {
                    if (providedNames.Contains(groupedName)) continue;
                    var lineSpan = invocation.GetLocation().GetLineSpan();
                    var filePath = doc.FilePath ?? doc.Name;
                    warnings.Add($"{filePath}:{lineSpan.StartLinePosition.Line + 1} omits '{groupedName}'");
                }
            }
        }
        return warnings;
    }

    private static HashSet<string> CollectProvidedParameterNames(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        // Build the set of parameter names actually supplied at this call site, accounting
        // for both positional prefix and named-argument suffix. Anything missing relied on
        // a default value and triggers the refusal path.
        var args = invocation.ArgumentList.Arguments;
        var provided = new HashSet<string>(StringComparer.Ordinal);
        var positionalIndex = 0;
        foreach (var arg in args)
        {
            if (arg.NameColon is null)
            {
                if (positionalIndex < method.Parameters.Length)
                    provided.Add(method.Parameters[positionalIndex].Name);
                positionalIndex++;
            }
            else
            {
                provided.Add(arg.NameColon.Name.Identifier.ValueText);
            }
        }
        return provided;
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

        IReadOnlyList<string> folders;
        if (request.DtoFolders is { Count: > 0 })
        {
            folders = request.DtoFolders;
        }
        else
        {
            folders = ResolveFolderSegmentsForNamespace(ns!, dtoProject.Name);
        }
        return (ns!, folders);
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
        return Path.Combine([projectDir, .. folders, $"{typeName}.cs"]);
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
        string newTypeName,
        string newParameterName,
        CancellationToken ct)
    {
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
                    newParamTexts.Add($"{newTypeName} {newParameterName}");
                    insertedDtoParam = true;
                }
                continue;
            }
            newParamTexts.Add(parameter.ToString());
        }
        if (!insertedDtoParam)
            newParamTexts.Add($"{newTypeName} {newParameterName}");

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
        string newTypeName,
        Dictionary<DocumentId, List<TextSpan>> callerLocations,
        Dictionary<DocumentId, string> originalTexts,
        Dictionary<string, int> perFileCallsites,
        CancellationToken ct)
    {
        var groupedNames = new HashSet<string>(grouped.Select(p => p.Name), StringComparer.Ordinal);

        foreach (var (docId, spans) in callerLocations)
        {
            ct.ThrowIfCancellationRequested();
            await CaptureOriginalTextAsync(originalTexts, baselineSolution, docId, ct).ConfigureAwait(false);

            var baselineDocument = baselineSolution.GetDocument(docId);
            var baselineRoot = baselineDocument is null
                ? null
                : await baselineDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (baselineRoot is null) continue;

            // Declaration rewriting may change absolute positions and may replace nested
            // invocations such as nameof(parameter). A child-index syntax path remains
            // stable because these rewrites change expressions, not the containing member
            // or statement structure that locates a target call site.
            var callerPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var span in spans)
            {
                var invocation = baselineRoot.FindNode(span).FirstAncestorOrSelf<InvocationExpressionSyntax>();
                if (invocation is not null) callerPaths.Add(BuildSyntaxPath(baselineRoot, invocation));
            }

            var doc = accumulator.GetDocument(docId);
            if (doc is null) continue;
            var oldRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (oldRoot is null) continue;
            var targets = callerPaths
                .Select(path => ResolveSyntaxPath(oldRoot, path))
                .OfType<InvocationExpressionSyntax>()
                .ToArray();

            var rewrittenCount = 0;
            var newRoot = oldRoot.ReplaceNodes(targets, (_, rewrittenNode) =>
            {
                var invocation = (InvocationExpressionSyntax)rewrittenNode;
                var newArgList = BuildRewrittenArgumentList(
                    invocation.ArgumentList, method, grouped, groupedNames, newTypeName);
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
        HashSet<string> groupedNames,
        string newTypeName)
    {
        // Materialize the lexical args into a semantic slot array indexed by the original
        // method's parameter order. NameColon args land in the named slot; positional args
        // fill the prefix lexically. Missing slots indicate default-value omission — already
        // refused upstream for grouped parameters; for non-grouped ones we leave the
        // omission as-is (the rewritten call still relies on the same default).
        var args = argList.Arguments;
        var semanticArgs = new ArgumentSyntax?[method.Parameters.Length];
        var positionalIndex = 0;
        foreach (var arg in args)
        {
            if (arg.NameColon is null)
            {
                if (positionalIndex < semanticArgs.Length) semanticArgs[positionalIndex] = arg;
                positionalIndex++;
            }
            else
            {
                var name = arg.NameColon.Name.Identifier.ValueText;
                for (var k = 0; k < method.Parameters.Length; k++)
                {
                    if (string.Equals(method.Parameters[k].Name, name, StringComparison.Ordinal))
                    {
                        semanticArgs[k] = arg;
                        break;
                    }
                }
            }
        }

        // Build the DTO constructor args in `parameterNames` order, stripping NameColon
        // (positional-record primary ctor takes positional args).
        var dtoArgs = new List<ArgumentSyntax>(grouped.Count);
        for (var i = 0; i < grouped.Count; i++)
        {
            // grouped[i] is the i-th parameterName. Find its index on the original method.
            var paramName = grouped[i].Name;
            var paramIndex = -1;
            for (var k = 0; k < method.Parameters.Length; k++)
            {
                if (string.Equals(method.Parameters[k].Name, paramName, StringComparison.Ordinal))
                {
                    paramIndex = k;
                    break;
                }
            }
            if (paramIndex < 0) return null;
            var src = semanticArgs[paramIndex];
            if (src is null) return null; // would have been caught upstream as default-value refusal
            var stripped = SyntaxFactory.Argument(src.Expression.WithoutTrivia());
            dtoArgs.Add(i == 0 ? stripped : stripped.WithLeadingTrivia(SyntaxFactory.Space));
        }

        var dtoCreation = SyntaxFactory.ObjectCreationExpression(
            SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.IdentifierName(newTypeName),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(dtoArgs)),
            initializer: null);

        // Build the rewritten outer call's argument list. Walk the original method's
        // parameters in order; emit a single Argument(dtoCreation) at the first grouped
        // index, skip the rest of the grouped params, and emit non-grouped args
        // unchanged (preserving their original NameColon shape if any).
        var outerArgs = new List<ArgumentSyntax>();
        var emittedDto = false;
        for (var k = 0; k < method.Parameters.Length; k++)
        {
            var paramName = method.Parameters[k].Name;
            if (groupedNames.Contains(paramName))
            {
                if (!emittedDto)
                {
                    outerArgs.Add(SyntaxFactory.Argument(dtoCreation));
                    emittedDto = true;
                }
                continue;
            }
            var src = semanticArgs[k];
            if (src is null) continue; // omitted, relies on original default — fine for non-grouped
            outerArgs.Add(SyntaxFactory.Argument(src.NameColon, src.RefKindKeyword, src.Expression.WithoutTrivia()));
        }
        if (!emittedDto)
            outerArgs.Add(SyntaxFactory.Argument(dtoCreation));

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
