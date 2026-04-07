using System.Collections.Concurrent;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class MutationAnalysisService : IMutationAnalysisService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<MutationAnalysisService> _logger;

    public MutationAnalysisService(IWorkspaceManager workspace, ILogger<MutationAnalysisService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<ImpactAnalysisDto?> AnalyzeImpactAsync(
        string workspaceId,
        SymbolLocator locator,
        ImpactAnalysisPaging paging,
        CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return null;

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var refLocations = references.SelectMany(r => r.Locations).ToList();
        var materialized = await ReferenceLocationMaterializer.MaterializeAsync(refLocations, ct).ConfigureAwait(false);

        var directRefs = new List<LocationDto>(materialized.Count);
        var affectedDeclarations = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var affectedProjects = new HashSet<string>();

        foreach (var item in materialized)
        {
            directRefs.Add(item.Dto);
            if (item.ContainingSymbol is not null)
                affectedDeclarations.Add(item.ContainingSymbol);
            affectedProjects.Add(item.Source.Document.Project.Name);
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            var implementations = await SymbolFinder.FindImplementationsAsync(namedType, solution, cancellationToken: ct).ConfigureAwait(false);
            foreach (var impl in implementations)
                affectedDeclarations.Add(impl);

            var derived = await SymbolFinder.FindDerivedClassesAsync(namedType, solution, cancellationToken: ct).ConfigureAwait(false);
            foreach (var d in derived)
                affectedDeclarations.Add(d);
        }

        // FLAG-3D: Stable sort + paginate references and declarations server-side. The full
        // counts are kept on the DTO so callers can detect when more pages are available.
        var orderedRefs = directRefs
            .OrderBy(r => r.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(r => r.StartLine)
            .ThenBy(r => r.StartColumn)
            .ToList();
        var totalRefs = orderedRefs.Count;
        var refOffset = Math.Max(0, paging.ReferencesOffset);
        var refLimit = Math.Max(1, paging.ReferencesLimit);
        var pagedRefs = orderedRefs.Skip(refOffset).Take(refLimit).ToList();
        var hasMoreRefs = refOffset + pagedRefs.Count < totalRefs;

        var orderedDecls = affectedDeclarations
            .Select(s => SymbolMapper.ToDto(s, solution))
            .OrderBy(d => d.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();
        var totalDecls = orderedDecls.Count;
        var declLimit = Math.Max(1, paging.DeclarationsLimit);
        var pagedDecls = orderedDecls.Take(declLimit).ToList();
        var hasMoreDecls = pagedDecls.Count < totalDecls;

        var summary = $"Symbol '{symbol.Name}' has {totalRefs} reference(s) across {affectedProjects.Count} project(s), " +
                       $"affecting {totalDecls} declaration(s)." +
                       (hasMoreRefs || hasMoreDecls
                           ? $" Showing {pagedRefs.Count} reference(s) (offset {refOffset}, limit {refLimit}) " +
                             $"and {pagedDecls.Count} declaration(s) (limit {declLimit}). Pass referencesOffset/referencesLimit/declarationsLimit to page."
                           : string.Empty);

        return new ImpactAnalysisDto(
            SymbolMapper.ToDto(symbol, solution),
            pagedRefs,
            pagedDecls,
            affectedProjects.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            summary,
            TotalDirectReferences: totalRefs,
            TotalAffectedDeclarations: totalDecls,
            HasMoreReferences: hasMoreRefs,
            HasMoreDeclarations: hasMoreDecls,
            ReferencesOffset: refOffset,
            ReferencesLimit: refLimit);
    }

    public async Task<IReadOnlyList<PropertyWriteDto>> FindPropertyWritesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var (writes, _) = await FindPropertyWritesWithMetadataAsync(workspaceId, locator, ct).ConfigureAwait(false);
        return writes;
    }

    /// <summary>
    /// FLAG-3C: Same as <see cref="FindPropertyWritesAsync"/> but returns a tuple including
    /// the resolved symbol kind so the tool layer can disambiguate "zero writes to property"
    /// from "position resolved to a non-property symbol".
    /// </summary>
    public async Task<(IReadOnlyList<PropertyWriteDto> Writes, string? ResolvedSymbolKind)> FindPropertyWritesWithMetadataAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return ([], null);
        if (symbol is not IPropertySymbol property) return ([], symbol.Kind.ToString());

        var references = await SymbolFinder.FindReferencesAsync(property, solution, ct).ConfigureAwait(false);
        var refLocations = references.SelectMany(r => r.Locations).ToList();
        var materialized = await ReferenceLocationMaterializer.MaterializeAsync(refLocations, ct).ConfigureAwait(false);

        var results = new List<PropertyWriteDto>();
        foreach (var item in materialized)
        {
            if (item.SyntaxRoot is null) continue;

            var refNode = item.SyntaxRoot.FindNode(item.Source.Location.SourceSpan);

            // Determine if this reference is a write (left side of assignment, out/ref arg, or in an initializer)
            if (!IsWriteReference(refNode)) continue;

            var isObjectInitializer = refNode.Ancestors()
                .Any(static a => a is InitializerExpressionSyntax init &&
                    init.Kind() == SyntaxKind.ObjectInitializerExpression);

            var lineSpan = item.Source.Location.GetLineSpan();

            results.Add(new PropertyWriteDto(
                FilePath: lineSpan.Path,
                StartLine: lineSpan.StartLinePosition.Line + 1,
                StartColumn: lineSpan.StartLinePosition.Character + 1,
                EndLine: lineSpan.EndLinePosition.Line + 1,
                EndColumn: lineSpan.EndLinePosition.Character + 1,
                ContainingMember: item.ContainingSymbol?.ToDisplayString(),
                PreviewText: item.Dto.PreviewText,
                IsObjectInitializer: isObjectInitializer));
        }

        return (results, "Property");
    }

    public async Task<IReadOnlyList<TypeUsageDto>> FindTypeUsagesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var refLocations = references.SelectMany(r => r.Locations).ToList();
        var materialized = await ReferenceLocationMaterializer.MaterializeAsync(refLocations, ct).ConfigureAwait(false);

        var results = new List<TypeUsageDto>(materialized.Count);
        foreach (var item in materialized)
        {
            if (item.SyntaxRoot is null) continue;

            var refNode = item.SyntaxRoot.FindNode(item.Source.Location.SourceSpan);
            var classification = ClassifyTypeUsage(refNode, symbol);

            var lineSpan = item.Source.Location.GetLineSpan();

            results.Add(new TypeUsageDto(
                FilePath: lineSpan.Path,
                StartLine: lineSpan.StartLinePosition.Line + 1,
                StartColumn: lineSpan.StartLinePosition.Character + 1,
                EndLine: lineSpan.EndLinePosition.Line + 1,
                EndColumn: lineSpan.EndLinePosition.Character + 1,
                ContainingMember: item.ContainingSymbol?.ToDisplayString(),
                PreviewText: item.Dto.PreviewText,
                Classification: classification));
        }

        return results;
    }

    public async Task<TypeMutationDto?> FindTypeMutationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol namedType) return null;

        // Filter to mutating members up front so we can fan out the expensive
        // SymbolFinder.FindReferencesAsync calls in parallel while preserving declaration order.
        var candidates = namedType.GetMembers()
            .Where(m => IsMutatingMember(m, namedType))
            .ToArray();

        if (candidates.Length == 0)
        {
            return new TypeMutationDto(
                Type: SymbolMapper.ToDto(namedType, solution),
                MutatingMembers: [],
                Summary: $"Type '{namedType.Name}' has 0 mutating member(s) with 0 external caller(s).");
        }

        // Concurrent caches shared across the parallel member tasks. Roslyn's per-document
        // caches mean these are mostly redundant after warmup, but the dictionary still saves
        // the async-state-machine cost on hot documents.
        var rootCache = new ConcurrentDictionary<DocumentId, SyntaxNode?>();
        var modelCache = new ConcurrentDictionary<DocumentId, SemanticModel?>();

        var parallelism = Math.Clamp(Environment.ProcessorCount, 4, 16);
        using var semaphore = new SemaphoreSlim(parallelism, parallelism);

        async Task<MutatingMemberDto> ProcessMemberAsync(ISymbol member)
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var callers = new List<MutationCallerDto>();
                var references = await SymbolFinder.FindReferencesAsync(member, solution, ct).ConfigureAwait(false);

                foreach (var refSymbol in references)
                {
                    foreach (var refLocation in refSymbol.Locations)
                    {
                        var doc = refLocation.Document;

                        // ConcurrentDictionary has no async value factory, so we check-then-add.
                        // The race is benign: at worst two members fetch the same document's
                        // root/model concurrently and one TryAdd loses; the result is identical.
                        if (!rootCache.TryGetValue(doc.Id, out var root))
                        {
                            root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                            rootCache.TryAdd(doc.Id, root);
                        }
                        if (!modelCache.TryGetValue(doc.Id, out var model))
                        {
                            model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                            modelCache.TryAdd(doc.Id, model);
                        }

                        // For interface-implemented members (e.g. Dispose), filter to only calls
                        // where the receiver type matches the target type
                        if (root is not null && model is not null && !IsReceiverOfTargetType(root, model, refLocation.Location, namedType, ct))
                            continue;

                        var containingSymbol = root is not null && model is not null
                            ? SymbolServiceHelpers.GetContainingSymbolFromRoot(root, model, refLocation.Location, ct)
                            : null;

                        // Skip calls from within the type itself (internal mutators calling each other)
                        if (containingSymbol is not null &&
                            SymbolEqualityComparer.Default.Equals(containingSymbol.ContainingType, namedType))
                            continue;

                        var preview = await SymbolResolver.GetPreviewTextAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                        var phase = ClassifyCallerPhase(root, model, refLocation.Location, namedType);
                        var lineSpan = refLocation.Location.GetLineSpan();

                        callers.Add(new MutationCallerDto(
                            FilePath: lineSpan.Path,
                            StartLine: lineSpan.StartLinePosition.Line + 1,
                            StartColumn: lineSpan.StartLinePosition.Character + 1,
                            ContainingMember: containingSymbol?.ToDisplayString(),
                            PreviewText: preview,
                            CallerPhase: phase));
                    }
                }

                var memberLoc = member.Locations.FirstOrDefault(l => l.IsInSource);
                return new MutatingMemberDto(
                    Name: member.Name,
                    FullyQualifiedName: member.ToDisplayString(),
                    Kind: member.Kind.ToString(),
                    FilePath: memberLoc?.GetLineSpan().Path,
                    Line: memberLoc?.GetLineSpan().StartLinePosition.Line + 1,
                    ExternalCallers: callers);
            }
            finally
            {
                semaphore.Release();
            }
        }

        var memberTasks = new Task<MutatingMemberDto>[candidates.Length];
        for (var i = 0; i < candidates.Length; i++)
        {
            memberTasks[i] = ProcessMemberAsync(candidates[i]);
        }

        var mutatingMembers = await Task.WhenAll(memberTasks).ConfigureAwait(false);

        var summary = $"Type '{namedType.Name}' has {mutatingMembers.Length} mutating member(s) " +
                      $"with {mutatingMembers.Sum(m => m.ExternalCallers.Count)} external caller(s).";

        return new TypeMutationDto(
            Type: SymbolMapper.ToDto(namedType, solution),
            MutatingMembers: mutatingMembers,
            Summary: summary);
    }

    private static bool IsWriteReference(SyntaxNode refNode)
    {
        // Object initializer member assignment: { Prop = value }
        if (refNode.Parent is AssignmentExpressionSyntax assignInInit &&
            assignInInit.Left == refNode &&
            assignInInit.Parent is InitializerExpressionSyntax)
            return true;

        // Regular assignment: target = value
        if (refNode.Parent is AssignmentExpressionSyntax directAssign && directAssign.Left == refNode)
            return true;

        // MemberAccess on left of assignment: obj.Prop = value
        if (refNode.Parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Parent is AssignmentExpressionSyntax memberAssign &&
            memberAssign.Left == memberAccess)
            return true;

        // out or ref argument
        if (refNode.Parent is ArgumentSyntax arg &&
            (arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) || arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)))
            return true;

        return false;
    }

    private static TypeUsageClassification ClassifyTypeUsage(SyntaxNode refNode, ISymbol referencedSymbol)
    {
        var parent = refNode.Parent;
        if (parent is null) return TypeUsageClassification.Other;

        var typeNode = refNode;
        while (parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NullableTypeSyntax or ArrayTypeSyntax)
        {
            typeNode = parent;
            parent = parent.Parent;
            if (parent is null) return TypeUsageClassification.Other;
        }

        return ClassifyTypeUsageAfterWalk(typeNode, parent, referencedSymbol);
    }

    private static TypeUsageClassification ClassifyTypeUsageAfterWalk(SyntaxNode typeNode, SyntaxNode parent, ISymbol referencedSymbol)
    {
        // Static class usage at call sites: TypeName.Member (expression is the type identifier).
        if (referencedSymbol is INamedTypeSymbol { IsStatic: true } &&
            parent is MemberAccessExpressionSyntax ma &&
            ma.Expression.Equals(typeNode))
        {
            return TypeUsageClassification.StaticMemberAccess;
        }

        if (parent is TypeArgumentListSyntax)
            return TypeUsageClassification.GenericArgument;

        if (parent is MethodDeclarationSyntax methodDecl && methodDecl.ReturnType == typeNode)
            return TypeUsageClassification.MethodReturnType;

        if (parent is LocalFunctionStatementSyntax localFunc && localFunc.ReturnType == typeNode)
            return TypeUsageClassification.MethodReturnType;

        if (parent is ParameterSyntax param && param.Type == typeNode)
            return TypeUsageClassification.MethodParameter;

        if (parent is PropertyDeclarationSyntax propDecl && propDecl.Type == typeNode)
            return TypeUsageClassification.PropertyType;

        if (parent is VariableDeclarationSyntax varDecl && varDecl.Type == typeNode)
        {
            var grandparent = varDecl.Parent;
            return grandparent is FieldDeclarationSyntax
                ? TypeUsageClassification.FieldType
                : TypeUsageClassification.LocalVariable;
        }

        if (parent is SimpleBaseTypeSyntax || parent is BaseListSyntax)
            return TypeUsageClassification.BaseType;

        if (parent is CastExpressionSyntax castExpr && castExpr.Type == typeNode)
            return TypeUsageClassification.Cast;
        if (parent is BinaryExpressionSyntax binaryAs &&
            binaryAs.IsKind(SyntaxKind.AsExpression) &&
            binaryAs.Right == typeNode)
            return TypeUsageClassification.Cast;

        if (parent is IsPatternExpressionSyntax)
            return TypeUsageClassification.TypeCheck;
        if (parent is BinaryExpressionSyntax binaryIs &&
            binaryIs.IsKind(SyntaxKind.IsExpression))
            return TypeUsageClassification.TypeCheck;
        if (parent is TypePatternSyntax || parent is DeclarationPatternSyntax)
            return TypeUsageClassification.TypeCheck;

        if (parent is ObjectCreationExpressionSyntax objCreate && objCreate.Type == typeNode)
            return TypeUsageClassification.ObjectCreation;
        if (parent is ImplicitObjectCreationExpressionSyntax)
            return TypeUsageClassification.ObjectCreation;

        return TypeUsageClassification.Other;
    }

    private static bool IsMutatingMember(ISymbol member, INamedTypeSymbol containingType)
    {
        if (member.IsStatic) return false;
        if (member.DeclaredAccessibility == Accessibility.Private) return false;

        // Settable properties
        if (member is IPropertySymbol prop && prop.SetMethod is not null &&
            !prop.SetMethod.IsInitOnly && prop.SetMethod.DeclaredAccessibility != Accessibility.Private)
            return true;

        // Methods that write to instance fields/properties (simple heuristic: check method body)
        if (member is IMethodSymbol method &&
            method.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation)
        {
            if (IsMutatingByName(method)) return true;

            var location = method.Locations.FirstOrDefault(l => l.IsInSource);
            if (location?.SourceTree is null) return false;

            var methodNode = location.SourceTree.GetRoot().FindNode(location.SourceSpan);
            return HasInstanceFieldAssignment(methodNode, containingType)
                || HasMutatingCollectionCall(methodNode);
        }

        return false;
    }

    /// <summary>
    /// Public API surface often mutates via Add*/Set*/Remove*/Clear* without obvious field
    /// writes in the body. Matched by naming convention before falling back to AST inspection.
    /// </summary>
    private static bool IsMutatingByName(IMethodSymbol method)
    {
        if (method.DeclaredAccessibility != Accessibility.Public || !method.ReturnsVoid)
            return false;

        return method.Name.StartsWith("Add", StringComparison.Ordinal)
            || method.Name.StartsWith("Set", StringComparison.Ordinal)
            || method.Name.StartsWith("Remove", StringComparison.Ordinal)
            || method.Name.StartsWith("Clear", StringComparison.Ordinal)
            || method.Name.StartsWith("Update", StringComparison.Ordinal)
            || method.Name.StartsWith("Insert", StringComparison.Ordinal);
    }

    private static bool HasInstanceFieldAssignment(SyntaxNode methodNode, INamedTypeSymbol containingType)
    {
        return methodNode.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(a =>
            {
                var left = a.Left;
                if (left is MemberAccessExpressionSyntax memberAccess)
                {
                    return memberAccess.Expression is ThisExpressionSyntax ||
                           (memberAccess.Expression is IdentifierNameSyntax &&
                            IsInstanceMember(memberAccess.Name.Identifier.Text, containingType));
                }
                if (left is IdentifierNameSyntax ident)
                    return IsInstanceMember(ident.Identifier.Text, containingType);
                return false;
            });
    }

    private static bool HasMutatingCollectionCall(SyntaxNode methodNode)
    {
        return methodNode.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.Expression is MemberAccessExpressionSyntax access &&
                        access.Name.Identifier.Text is "Add" or "Remove" or "Clear" or "Insert"
                                                            or "RemoveAt" or "AddRange" or "RemoveAll");
    }

    /// <summary>
    /// Checks if the reference location's invocation receiver is of the target type.
    /// Prevents over-matching for interface-implemented members like Dispose().
    /// </summary>
    private static bool IsReceiverOfTargetType(
        SyntaxNode root, SemanticModel model, Location location, INamedTypeSymbol targetType, CancellationToken ct)
    {
        var node = root.FindNode(location.SourceSpan);

        // Walk up to find the invocation or member access
        var memberAccess = node.FirstAncestorOrSelf<MemberAccessExpressionSyntax>();
        if (memberAccess is not null)
        {
            var expr = memberAccess.Expression;
            while (expr is ParenthesizedExpressionSyntax paren)
                expr = paren.Expression;

            var receiverTypeInfo = model.GetTypeInfo(expr, ct);
            var receiverType = receiverTypeInfo.Type ?? receiverTypeInfo.ConvertedType;
            if (receiverType is INamedTypeSymbol receiverNamed)
                return ReceiverMatchesMutationTarget(receiverNamed, targetType);
        }

        // For well-known interface methods (Dispose, GetHashCode, Equals, ToString),
        // require an explicit receiver match to prevent over-matching across all IDisposable etc.
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation?.Expression is IdentifierNameSyntax identName)
        {
            var methodName = identName.Identifier.Text;
            if (methodName is "Dispose" or "GetHashCode" or "Equals" or "ToString" or "CompareTo")
            {
                // No explicit receiver — could be any type. Reject to avoid over-matching.
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the call's receiver is the same type as the analyzed type, or a derived class instance
    /// (so external calls on subtypes are kept). Does not treat unrelated types as matching via shared interfaces.
    /// </summary>
    private static bool ReceiverMatchesMutationTarget(INamedTypeSymbol receiver, INamedTypeSymbol targetType)
    {
        if (SymbolEqualityComparer.Default.Equals(receiver, targetType))
            return true;

        for (var b = receiver.BaseType; b is not null; b = b.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(b, targetType))
                return true;
        }

        return false;
    }

    private static bool IsInstanceMember(string name, INamedTypeSymbol type)
    {
        return type.GetMembers(name).Any(m => !m.IsStatic);
    }

    private static string ClassifyCallerPhase(SyntaxNode? root, SemanticModel? model, Location location, INamedTypeSymbol targetType)
    {
        if (root is null) return "Unknown";

        var node = root.FindNode(location.SourceSpan);

        // Check if inside an object initializer
        if (node.Ancestors().OfType<InitializerExpressionSyntax>()
            .Any(e => e.Kind() == SyntaxKind.ObjectInitializerExpression))
            return "Construction";

        // Check if inside a constructor
        if (node.Ancestors().OfType<ConstructorDeclarationSyntax>().Any())
            return "Construction";

        // Check if inside a method of the same type that returns void (builder-pattern heuristic)
        var enclosingMethod = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (enclosingMethod is not null && model is not null)
        {
            var returnType = enclosingMethod.ReturnType.ToString();
            if (returnType is "void" or "Void")
            {
                var methodSymbol = model.GetDeclaredSymbol(enclosingMethod);
                if (methodSymbol?.ContainingType is not null &&
                    SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType, targetType))
                    return "Construction";
            }
        }

        return "PostConstruction";
    }
}
