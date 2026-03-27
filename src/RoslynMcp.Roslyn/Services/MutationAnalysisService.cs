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

    public async Task<ImpactAnalysisDto?> AnalyzeImpactAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return null;

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var directRefs = new List<LocationDto>();
        var affectedDeclarations = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var affectedProjects = new HashSet<string>();

        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var preview = await SymbolResolver.GetPreviewTextAsync(refLocation.Document, refLocation.Location, ct).ConfigureAwait(false);
                var containingSymbol = await SymbolServiceHelpers.GetContainingSymbolAsync(refLocation.Document, refLocation.Location, ct).ConfigureAwait(false);
                directRefs.Add(SymbolMapper.ToLocationDto(refLocation.Location, containingSymbol, preview));

                if (containingSymbol is not null)
                    affectedDeclarations.Add(containingSymbol);

                affectedProjects.Add(refLocation.Document.Project.Name);
            }
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

        var summary = $"Symbol '{symbol.Name}' has {directRefs.Count} reference(s) across {affectedProjects.Count} project(s), " +
                       $"affecting {affectedDeclarations.Count} declaration(s).";

        return new ImpactAnalysisDto(
            SymbolMapper.ToDto(symbol, solution),
            directRefs,
            affectedDeclarations.Select(s => SymbolMapper.ToDto(s, solution)).ToList(),
            affectedProjects.ToList(),
            summary);
    }

    public async Task<IReadOnlyList<PropertyWriteDto>> FindPropertyWritesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is not IPropertySymbol property) return [];

        var references = await SymbolFinder.FindReferencesAsync(property, solution, ct).ConfigureAwait(false);
        var results = new List<PropertyWriteDto>();

        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var doc = refLocation.Document;
                var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null) continue;

                var refNode = root.FindNode(refLocation.Location.SourceSpan);

                // Determine if this reference is a write (left side of assignment, out/ref arg, or in an initializer)
                var isWrite = IsWriteReference(refNode);
                if (!isWrite) continue;

                var isObjectInitializer = refNode.Ancestors()
                    .Any(static a => a is InitializerExpressionSyntax init &&
                        init.Kind() == SyntaxKind.ObjectInitializerExpression);

                var preview = await SymbolResolver.GetPreviewTextAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                var containingSymbol = await SymbolServiceHelpers.GetContainingSymbolAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                var lineSpan = refLocation.Location.GetLineSpan();

                results.Add(new PropertyWriteDto(
                    FilePath: lineSpan.Path,
                    StartLine: lineSpan.StartLinePosition.Line + 1,
                    StartColumn: lineSpan.StartLinePosition.Character + 1,
                    EndLine: lineSpan.EndLinePosition.Line + 1,
                    EndColumn: lineSpan.EndLinePosition.Character + 1,
                    ContainingMember: containingSymbol?.ToDisplayString(),
                    PreviewText: preview,
                    IsObjectInitializer: isObjectInitializer));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<TypeUsageDto>> FindTypeUsagesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var results = new List<TypeUsageDto>();

        foreach (var refSymbol in references)
        {
            foreach (var refLocation in refSymbol.Locations)
            {
                var doc = refLocation.Document;
                var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null) continue;

                var refNode = root.FindNode(refLocation.Location.SourceSpan);
                var classification = ClassifyTypeUsage(refNode);

                var preview = await SymbolResolver.GetPreviewTextAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                var containingSymbol = await SymbolServiceHelpers.GetContainingSymbolAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
                var lineSpan = refLocation.Location.GetLineSpan();

                results.Add(new TypeUsageDto(
                    FilePath: lineSpan.Path,
                    StartLine: lineSpan.StartLinePosition.Line + 1,
                    StartColumn: lineSpan.StartLinePosition.Character + 1,
                    EndLine: lineSpan.EndLinePosition.Line + 1,
                    EndColumn: lineSpan.EndLinePosition.Character + 1,
                    ContainingMember: containingSymbol?.ToDisplayString(),
                    PreviewText: preview,
                    Classification: classification));
            }
        }

        return results;
    }

    public async Task<TypeMutationDto?> FindTypeMutationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol namedType) return null;

        var mutatingMembers = new List<MutatingMemberDto>();

        // Cache document roots and semantic models to avoid redundant async lookups
        // across multiple members referencing the same documents.
        var rootCache = new Dictionary<DocumentId, SyntaxNode?>();
        var modelCache = new Dictionary<DocumentId, SemanticModel?>();

        foreach (var member in namedType.GetMembers())
        {
            if (!IsMutatingMember(member, namedType)) continue;

            var callers = new List<MutationCallerDto>();
            var references = await SymbolFinder.FindReferencesAsync(member, solution, ct).ConfigureAwait(false);

            foreach (var refSymbol in references)
            {
                foreach (var refLocation in refSymbol.Locations)
                {
                    var doc = refLocation.Document;
                    if (!rootCache.TryGetValue(doc.Id, out var root))
                    {
                        root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                        rootCache[doc.Id] = root;
                    }
                    if (!modelCache.TryGetValue(doc.Id, out var model))
                    {
                        model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                        modelCache[doc.Id] = model;
                    }

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
            mutatingMembers.Add(new MutatingMemberDto(
                Name: member.Name,
                FullyQualifiedName: member.ToDisplayString(),
                Kind: member.Kind.ToString(),
                FilePath: memberLoc?.GetLineSpan().Path,
                Line: memberLoc?.GetLineSpan().StartLinePosition.Line + 1,
                ExternalCallers: callers));
        }

        var summary = $"Type '{namedType.Name}' has {mutatingMembers.Count} mutating member(s) " +
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

    private static TypeUsageClassification ClassifyTypeUsage(SyntaxNode refNode)
    {
        var parent = refNode.Parent;
        if (parent is null) return TypeUsageClassification.Other;

        // Walk up past qualified names, generic names, etc.
        var typeNode = refNode;
        while (parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NullableTypeSyntax or ArrayTypeSyntax)
        {
            typeNode = parent;
            parent = parent.Parent;
            if (parent is null) return TypeUsageClassification.Other;
        }

        // Handle generic type arguments (e.g., List<T>)
        if (parent is TypeArgumentListSyntax)
            return TypeUsageClassification.GenericArgument;

        // Method return type
        if (parent is MethodDeclarationSyntax methodDecl && methodDecl.ReturnType == typeNode)
            return TypeUsageClassification.MethodReturnType;

        // Local function return type
        if (parent is LocalFunctionStatementSyntax localFunc && localFunc.ReturnType == typeNode)
            return TypeUsageClassification.MethodReturnType;

        // Method parameter
        if (parent is ParameterSyntax param)
        {
            if (param.Type == typeNode)
                return TypeUsageClassification.MethodParameter;
        }

        // Property type
        if (parent is PropertyDeclarationSyntax propDecl && propDecl.Type == typeNode)
            return TypeUsageClassification.PropertyType;

        // Variable declaration — field or local
        if (parent is VariableDeclarationSyntax varDecl && varDecl.Type == typeNode)
        {
            var grandparent = varDecl.Parent;
            return grandparent is FieldDeclarationSyntax
                ? TypeUsageClassification.FieldType
                : TypeUsageClassification.LocalVariable;
        }

        // Base list (base class or interface)
        if (parent is SimpleBaseTypeSyntax || parent is BaseListSyntax)
            return TypeUsageClassification.BaseType;

        // Cast expression: (T)expr or expr as T
        if (parent is CastExpressionSyntax castExpr && castExpr.Type == typeNode)
            return TypeUsageClassification.Cast;
        if (parent is BinaryExpressionSyntax binaryAs &&
            binaryAs.IsKind(SyntaxKind.AsExpression) &&
            binaryAs.Right == typeNode)
            return TypeUsageClassification.Cast;

        // Is-pattern / type check
        if (parent is IsPatternExpressionSyntax)
            return TypeUsageClassification.TypeCheck;
        if (parent is BinaryExpressionSyntax binaryIs &&
            binaryIs.IsKind(SyntaxKind.IsExpression))
            return TypeUsageClassification.TypeCheck;
        if (parent is TypePatternSyntax || parent is DeclarationPatternSyntax)
            return TypeUsageClassification.TypeCheck;

        // Object creation: new T()
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
            var location = method.Locations.FirstOrDefault(l => l.IsInSource);
            if (location?.SourceTree is null) return false;

            var root = location.SourceTree.GetRoot();
            var methodNode = root.FindNode(location.SourceSpan);

            var hasInstanceAssignment = methodNode.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Any(a =>
                {
                    var left = a.Left;
                    if (left is MemberAccessExpressionSyntax memberAccess)
                    {
                        return memberAccess.Expression is ThisExpressionSyntax ||
                               (memberAccess.Expression is IdentifierNameSyntax && IsInstanceMember(memberAccess.Name.Identifier.Text, containingType));
                    }
                    if (left is IdentifierNameSyntax ident)
                    {
                        return IsInstanceMember(ident.Identifier.Text, containingType);
                    }
                    return false;
                });

            if (hasInstanceAssignment) return true;

            // Methods that call mutating collection methods (.Add, .Remove, .Clear) on instance members
            var hasMutatingCollectionCall = methodNode.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv =>
                {
                    if (inv.Expression is MemberAccessExpressionSyntax access)
                    {
                        var methodName = access.Name.Identifier.Text;
                        return methodName is "Add" or "Remove" or "Clear" or "Insert" or "RemoveAt" or "AddRange" or "RemoveAll";
                    }
                    return false;
                });

            return hasMutatingCollectionCall;
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
