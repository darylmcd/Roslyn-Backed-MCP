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
        // Throw on unresolved symbol so callers see a structured NotFound envelope
        // (matches find_references contract).
        var symbol = await SymbolResolver.ResolveOrThrowAsync(solution, locator, ct).ConfigureAwait(false);

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
        // Throw on unresolved symbol so callers see a structured NotFound envelope
        // (matches find_references contract).
        var symbol = await SymbolResolver.ResolveOrThrowAsync(solution, locator, ct).ConfigureAwait(false);

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var refLocations = references.SelectMany(r => r.Locations).ToList();
        var materialized = await ReferenceLocationMaterializer.MaterializeAsync(refLocations, ct).ConfigureAwait(false);

        var results = new List<TypeUsageDto>(materialized.Count);
        foreach (var item in materialized)
        {
            if (item.SyntaxRoot is null) continue;

            // find-type-usages-cref-classification: descend into structured trivia so that
            // references inside <see cref="X"/> / <seealso cref="X"/> / <exception cref="X"/>
            // doc-comment attributes resolve to the cref node, not the declaration the doc
            // comment is attached to. Without descendIntoTrivia:true the classifier defaults
            // to Other for every doc-comment cref reference.
            var refNode = item.SyntaxRoot.FindNode(item.Source.Location.SourceSpan, findInsideTrivia: true);
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

        var compilation = await ResolveContainingCompilationAsync(solution, namedType, ct).ConfigureAwait(false);
        var candidates = GetMutationCandidates(namedType, compilation);

        if (candidates.Length == 0)
        {
            return CreateTypeMutationDto(namedType, solution, []);
        }

        var mutatingMembers = await BuildMutatingMembersAsync(solution, namedType, candidates, ct).ConfigureAwait(false);
        return CreateTypeMutationDto(namedType, solution, mutatingMembers);
    }

    private async Task<Compilation?> ResolveContainingCompilationAsync(
        Solution solution,
        INamedTypeSymbol namedType,
        CancellationToken ct)
    {
        // Get the compilation that defines the type so the side-effect classifier can read
        // semantic models against the same compilation as the symbol.
        foreach (var project in solution.Projects)
        {
            var projectCompilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (projectCompilation is null)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(projectCompilation.Assembly, namedType.ContainingAssembly))
            {
                return projectCompilation;
            }
        }

        return null;
    }

    private static MutationCandidate[] GetMutationCandidates(INamedTypeSymbol namedType, Compilation? compilation)
    {
        // Filter to mutating members up front so we can fan out the expensive
        // SymbolFinder.FindReferencesAsync calls in parallel while preserving declaration order.
        // Each candidate carries its computed scope so we don't recompute it inside the parallel
        // member tasks.
        return namedType.GetMembers()
            .Select(member => new MutationCandidate(member, ClassifyMutationScope(member, namedType, compilation)))
            .Where(candidate => candidate.Scope is not null)
            .Select(candidate => candidate with { Scope = candidate.Scope! })
            .ToArray();
    }

    private async Task<MutatingMemberDto[]> BuildMutatingMembersAsync(
        Solution solution,
        INamedTypeSymbol namedType,
        IReadOnlyList<MutationCandidate> candidates,
        CancellationToken ct)
    {
        // Concurrent caches shared across the parallel member tasks. Roslyn's per-document
        // caches mean these are mostly redundant after warmup, but the dictionary still saves
        // the async-state-machine cost on hot documents.
        var rootCache = new ConcurrentDictionary<DocumentId, SyntaxNode?>();
        var modelCache = new ConcurrentDictionary<DocumentId, SemanticModel?>();
        var parallelism = Math.Clamp(Environment.ProcessorCount, 4, 16);
        using var semaphore = new SemaphoreSlim(parallelism, parallelism);

        var memberTasks = new Task<MutatingMemberDto>[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            memberTasks[i] = BuildMutatingMemberAsync(
                solution,
                namedType,
                candidates[i],
                rootCache,
                modelCache,
                semaphore,
                ct);
        }

        return await Task.WhenAll(memberTasks).ConfigureAwait(false);
    }

    private async Task<MutatingMemberDto> BuildMutatingMemberAsync(
        Solution solution,
        INamedTypeSymbol namedType,
        MutationCandidate candidate,
        ConcurrentDictionary<DocumentId, SyntaxNode?> rootCache,
        ConcurrentDictionary<DocumentId, SemanticModel?> modelCache,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var callers = new List<MutationCallerDto>();
            var references = await SymbolFinder.FindReferencesAsync(candidate.Member, solution, ct).ConfigureAwait(false);

            foreach (var refSymbol in references)
            {
                foreach (var refLocation in refSymbol.Locations)
                {
                    var caller = await BuildMutationCallerAsync(
                        refLocation,
                        namedType,
                        rootCache,
                        modelCache,
                        ct).ConfigureAwait(false);
                    if (caller is not null)
                    {
                        callers.Add(caller);
                    }
                }
            }

            var memberLoc = candidate.Member.Locations.FirstOrDefault(location => location.IsInSource);
            return new MutatingMemberDto(
                Name: candidate.Member.Name,
                FullyQualifiedName: candidate.Member.ToDisplayString(),
                Kind: candidate.Member.Kind.ToString(),
                FilePath: memberLoc?.GetLineSpan().Path,
                Line: memberLoc?.GetLineSpan().StartLinePosition.Line + 1,
                ExternalCallers: callers,
                MutationScope: candidate.Scope!);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<MutationCallerDto?> BuildMutationCallerAsync(
        ReferenceLocation refLocation,
        INamedTypeSymbol namedType,
        ConcurrentDictionary<DocumentId, SyntaxNode?> rootCache,
        ConcurrentDictionary<DocumentId, SemanticModel?> modelCache,
        CancellationToken ct)
    {
        var doc = refLocation.Document;
        var root = await GetCachedSyntaxRootAsync(doc, rootCache, ct).ConfigureAwait(false);
        var model = await GetCachedSemanticModelAsync(doc, modelCache, ct).ConfigureAwait(false);

        // For interface-implemented members (e.g. Dispose), filter to only calls
        // where the receiver type matches the target type.
        if (root is not null && model is not null && !IsReceiverOfTargetType(root, model, refLocation.Location, namedType, ct))
        {
            return null;
        }

        var containingSymbol = root is not null && model is not null
            ? SymbolServiceHelpers.GetContainingSymbolFromRoot(root, model, refLocation.Location, ct)
            : null;

        // Skip calls from within the type itself (internal mutators calling each other).
        if (containingSymbol is not null &&
            SymbolEqualityComparer.Default.Equals(containingSymbol.ContainingType, namedType))
        {
            return null;
        }

        var preview = await SymbolResolver.GetPreviewTextAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
        var phase = ClassifyCallerPhase(root, model, refLocation.Location, namedType);
        var lineSpan = refLocation.Location.GetLineSpan();

        return new MutationCallerDto(
            FilePath: lineSpan.Path,
            StartLine: lineSpan.StartLinePosition.Line + 1,
            StartColumn: lineSpan.StartLinePosition.Character + 1,
            ContainingMember: containingSymbol?.ToDisplayString(),
            PreviewText: preview,
            CallerPhase: phase);
    }

    private static async Task<SyntaxNode?> GetCachedSyntaxRootAsync(
        Document document,
        ConcurrentDictionary<DocumentId, SyntaxNode?> rootCache,
        CancellationToken ct)
    {
        // ConcurrentDictionary has no async value factory, so we check-then-add.
        // The race is benign: at worst two members fetch the same document's root concurrently
        // and one TryAdd loses; the result is identical.
        if (rootCache.TryGetValue(document.Id, out var root))
        {
            return root;
        }

        root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        rootCache.TryAdd(document.Id, root);
        return root;
    }

    private static async Task<SemanticModel?> GetCachedSemanticModelAsync(
        Document document,
        ConcurrentDictionary<DocumentId, SemanticModel?> modelCache,
        CancellationToken ct)
    {
        if (modelCache.TryGetValue(document.Id, out var model))
        {
            return model;
        }

        model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        modelCache.TryAdd(document.Id, model);
        return model;
    }

    private static TypeMutationDto CreateTypeMutationDto(
        INamedTypeSymbol namedType,
        Solution solution,
        IReadOnlyList<MutatingMemberDto> mutatingMembers)
    {
        var summary = $"Type '{namedType.Name}' has {mutatingMembers.Count} mutating member(s) " +
                      $"with {mutatingMembers.Sum(member => member.ExternalCallers.Count)} external caller(s).";

        return new TypeMutationDto(
            Type: SymbolMapper.ToDto(namedType, solution),
            MutatingMembers: mutatingMembers,
            Summary: summary);
    }

    private sealed record MethodMutationAnalysisContext(SyntaxTree SourceTree, SyntaxNode MethodNode);

    private sealed record MutationCandidate(ISymbol Member, string? Scope);

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
        // find-type-usages-cref-classification: doc-comment references (`<see cref="X"/>`,
        // `<seealso cref="X"/>`, `<exception cref="X"/>`) live inside a CrefSyntax or
        // XmlCrefAttributeSyntax ancestor. Detect that first so the reference is bucketed as
        // Documentation instead of falling through to the code-context classifier and
        // landing in Other.
        if (IsInsideDocCommentCref(refNode))
        {
            return TypeUsageClassification.Documentation;
        }

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

    private static bool IsInsideDocCommentCref(SyntaxNode refNode)
    {
        foreach (var ancestor in refNode.AncestorsAndSelf())
        {
            // `CrefSyntax` is the common base for all cref variants (NameMemberCref, TypeCref,
            // QualifiedCref, ConversionOperatorMemberCref, IndexerMemberCref, OperatorMemberCref).
            if (ancestor is CrefSyntax) return true;
            if (ancestor is XmlCrefAttributeSyntax) return true;
            // Stop walking once we leave the doc comment trivia boundary.
            if (ancestor is DocumentationCommentTriviaSyntax) return true;
        }
        return false;
    }

    /// <summary>
    /// Classifies a type-usage syntactic context into a mutation category after the ancestor walk.
    /// </summary>
    /// <remarks>
    /// WS2 session 2.10 disposition (2026-04-21): considered-and-rejected for extraction.
    /// cc=21, MI=50 — dense switch-over-closed-kind-set pattern; the high-cc/high-MI shape
    /// signals the structure is already clear. See inline note above for rationale.
    /// </remarks>
    // WS2 session 2.10 — rejected for extraction (2026-04-21). cc=21, MI=50. Same
    // dense-switch-over-closed-kind-set pattern as SideEffectClassifier.ClassifyMethod —
    // the switch arms classify a single syntactic usage into a mutation category. Each
    // arm is a short match; extraction would raise helper count without reader benefit.
    // See the closed backlog row top10-complexity-hotspots-not-yet-extracted for
    // session-level disposition rationale.
    private static TypeUsageClassification ClassifyTypeUsageAfterWalk(SyntaxNode typeNode, SyntaxNode parent, ISymbol referencedSymbol)
    {
        // Static class usage at call sites: TypeName.Member (expression is the type identifier).
        if (referencedSymbol is INamedTypeSymbol { IsStatic: true } &&
            parent is MemberAccessExpressionSyntax ma &&
            ma.Expression.Equals(typeNode))
        {
            return TypeUsageClassification.StaticMemberAccess;
        }

        return parent switch
        {
            TypeArgumentListSyntax => TypeUsageClassification.GenericArgument,
            MethodDeclarationSyntax methodDecl when methodDecl.ReturnType == typeNode => TypeUsageClassification.MethodReturnType,
            LocalFunctionStatementSyntax localFunc when localFunc.ReturnType == typeNode => TypeUsageClassification.MethodReturnType,
            ParameterSyntax param when param.Type == typeNode => TypeUsageClassification.MethodParameter,
            PropertyDeclarationSyntax propDecl when propDecl.Type == typeNode => TypeUsageClassification.PropertyType,
            VariableDeclarationSyntax varDecl when varDecl.Type == typeNode => varDecl.Parent is FieldDeclarationSyntax
                ? TypeUsageClassification.FieldType
                : TypeUsageClassification.LocalVariable,
            SimpleBaseTypeSyntax or BaseListSyntax => TypeUsageClassification.BaseType,
            CastExpressionSyntax castExpr when castExpr.Type == typeNode => TypeUsageClassification.Cast,
            BinaryExpressionSyntax binaryAs when binaryAs.IsKind(SyntaxKind.AsExpression) && binaryAs.Right == typeNode => TypeUsageClassification.Cast,
            IsPatternExpressionSyntax => TypeUsageClassification.TypeCheck,
            BinaryExpressionSyntax binaryIs when binaryIs.IsKind(SyntaxKind.IsExpression) => TypeUsageClassification.TypeCheck,
            TypePatternSyntax or DeclarationPatternSyntax => TypeUsageClassification.TypeCheck,
            ObjectCreationExpressionSyntax objCreate when objCreate.Type == typeNode => TypeUsageClassification.ObjectCreation,
            ImplicitObjectCreationExpressionSyntax => TypeUsageClassification.ObjectCreation,
            _ => TypeUsageClassification.Other,
        };
    }

    /// <summary>
    /// Classifies a member as mutating and returns the highest-severity scope of the mutation.
    /// Returns <see langword="null"/> if the member does not mutate at all. The scope follows
    /// <see cref="SideEffectClassifier.Scopes"/>: FieldWrite (settable property or instance-field
    /// reassignment), CollectionWrite (Add/Remove/Clear-style call), IO/Network/Process/Database
    /// (calls into the catalogued side-effect APIs).
    ///
    /// Side-effect detection requires a <see cref="SemanticModel"/> for the method body. When
    /// none is available (e.g. method has no source location), only the field-write and
    /// collection-write heuristics run.
    /// </summary>
    private static string? ClassifyMutationScope(ISymbol member, INamedTypeSymbol containingType, Compilation? compilation)
    {
        if (member.IsStatic) return null;
        if (member.DeclaredAccessibility == Accessibility.Private) return null;

        if (TryClassifyPropertyMutationScope(member, out var propertyScope))
        {
            return propertyScope;
        }

        if (member is not IMethodSymbol method ||
            method.MethodKind is not (MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation))
        {
            return null;
        }

        return ClassifyMethodMutationScope(method, containingType, compilation);
    }

    private static bool TryClassifyPropertyMutationScope(ISymbol member, out string? scope)
    {
        scope = null;
        if (member is not IPropertySymbol property ||
            property.SetMethod is null ||
            property.SetMethod.IsInitOnly ||
            property.SetMethod.DeclaredAccessibility == Accessibility.Private)
        {
            return false;
        }

        scope = SideEffectClassifier.Scopes.FieldWrite;
        return true;
    }

    private static string? ClassifyMethodMutationScope(
        IMethodSymbol method,
        INamedTypeSymbol containingType,
        Compilation? compilation)
    {
        // Naming convention is checked first because it doesn't require a syntax tree.
        var byName = IsMutatingByName(method);
        var analysisContext = TryCreateMethodMutationAnalysisContext(method);
        if (analysisContext is null)
        {
            return byName ? SideEffectClassifier.Scopes.FieldWrite : null;
        }

        // Side-effect detection: highest severity wins. Requires the right SemanticModel
        // for the syntax tree containing the method body — fall back to the field/collection
        // heuristics when one is not available.
        var sideEffectScope = TryClassifyMethodSideEffects(compilation, analysisContext.SourceTree, analysisContext.MethodNode);
        if (sideEffectScope is not null)
        {
            return sideEffectScope;
        }

        if (HasInstanceFieldAssignment(analysisContext.MethodNode, containingType))
        {
            return SideEffectClassifier.Scopes.FieldWrite;
        }

        if (HasMutatingCollectionCall(analysisContext.MethodNode))
        {
            return SideEffectClassifier.Scopes.CollectionWrite;
        }

        return byName ? SideEffectClassifier.Scopes.FieldWrite : null;
    }

    private static MethodMutationAnalysisContext? TryCreateMethodMutationAnalysisContext(IMethodSymbol method)
    {
        var location = method.Locations.FirstOrDefault(l => l.IsInSource);
        if (location?.SourceTree is null)
        {
            return null;
        }

        var methodNode = location.SourceTree.GetRoot().FindNode(location.SourceSpan);
        return new MethodMutationAnalysisContext(location.SourceTree, methodNode);
    }

    private static string? TryClassifyMethodSideEffects(
        Compilation? compilation,
        SyntaxTree sourceTree,
        SyntaxNode methodNode)
    {
        if (compilation is null)
        {
            return null;
        }

        var model = compilation.GetSemanticModel(sourceTree);
        return SideEffectClassifier.ClassifyMethodSideEffects(methodNode, model, CancellationToken.None);
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
