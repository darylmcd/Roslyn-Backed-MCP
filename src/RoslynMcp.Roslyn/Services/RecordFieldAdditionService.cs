using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Implements <see cref="IRecordFieldAdditionService"/>. Walks the cross-solution references for the
/// target record and classifies each reference site into one of five buckets:
/// <list type="number">
///   <item><description>Positional-construction sites (<c>new R(a, b)</c>) whose arity matches the existing primary ctor.</description></item>
///   <item><description>Deconstruction sites (<c>var (a, b) = r</c>, plus <c>switch</c> / <c>is</c> positional patterns).</description></item>
///   <item><description>Property-pattern sites (<c>r is { Foo: x, Bar: y }</c>) — flagged as a missed correlation when the pattern is in-spirit-exhaustive over the existing positional fields.</description></item>
///   <item><description><c>with</c>-expression sites (<c>existing with { ... }</c>).</description></item>
///   <item><description>Test-file paths that mention the record (deduped, sorted).</description></item>
/// </list>
/// Construction-site rewrites splice the new field into the argument list; deconstruction-site
/// rewrites splice <c>_</c> as the new positional discard. The compiler natively catches the
/// construction case (CS7036), but every other bucket compiles silently, which is what makes the
/// pre-flight audit useful.
/// </summary>
public sealed class RecordFieldAdditionService : IRecordFieldAdditionService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<RecordFieldAdditionService> _logger;

    public RecordFieldAdditionService(IWorkspaceManager workspace, ILogger<RecordFieldAdditionService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<RecordFieldAdditionImpactDto> PreviewAdditionAsync(
        string workspaceId,
        string recordMetadataName,
        string newFieldName,
        string newFieldType,
        string? defaultValueExpression,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordMetadataName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFieldType);

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveByMetadataNameAsync(solution, recordMetadataName, ct).ConfigureAwait(false);
        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            throw new KeyNotFoundException($"No type resolved for metadata name '{recordMetadataName}'.");
        }
        if (!typeSymbol.IsRecord)
        {
            throw new ArgumentException(
                $"Type '{recordMetadataName}' is not a record. preview_record_field_addition only " +
                "supports record class / record struct types.",
                nameof(recordMetadataName));
        }

        var newField = new NewRecordFieldDto(newFieldName, newFieldType, defaultValueExpression);
        var existingPositionalParameters = ExtractPositionalParameters(typeSymbol);
        var isPositional = existingPositionalParameters.Count > 0;

        // Cross-solution reference walk. SymbolFinder collapses partial declarations, generic
        // instantiations, and projects into one stream. We use it to (a) drive the per-location
        // classification for type-name references (ObjectCreation, RecursivePattern) AND (b)
        // identify the set of documents that mention the record, so we can do a second
        // SyntaxWalker pass over those documents to catch sites that don't directly reference
        // the type symbol — most notably `var (a, b) = r;` (references the local only) and
        // `x with { ... }` (references the local whose type is the target). Without the
        // document-level walk those sites would be silently missed.
        var references = await SymbolFinder.FindReferencesAsync(typeSymbol, solution, ct).ConfigureAwait(false);
        var refLocations = references
            .SelectMany(r => r.Locations)
            .Where(l => !l.IsImplicit)
            .ToList();

        var materialized = await ReferenceLocationMaterializer.MaterializeAsync(refLocations, ct).ConfigureAwait(false);

        var constructionSites = new List<RecordPositionalConstructionSiteDto>();
        var deconstructionSites = new List<RecordDeconstructionSiteDto>();
        var propertyPatternSites = new List<RecordPropertyPatternSiteDto>();
        var withExpressionSites = new List<RecordWithExpressionSiteDto>();
        var testFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dedupeSpans = new HashSet<(string Path, int Line, int Col, string Kind)>();

        // Pass A: per-reference-location classification (catches type-token uses like
        // ObjectCreation, RecursivePattern-with-type).
        foreach (var rich in materialized)
        {
            if (ct.IsCancellationRequested) break;
            if (rich.SyntaxRoot is null || rich.SemanticModel is null) continue;

            var sourceSpan = rich.Source.Location.SourceSpan;
            var node = rich.SyntaxRoot.FindNode(sourceSpan, getInnermostNodeForTie: true);
            if (node is null) continue;

            if (IsTestDocument(rich.Source.Document))
            {
                var path = rich.Source.Document.FilePath;
                if (!string.IsNullOrEmpty(path)) testFiles.Add(path);
            }

            var ctx = new RecordFieldClassificationContext(
                SemanticModel: rich.SemanticModel,
                TargetType: typeSymbol,
                ExistingParameters: existingPositionalParameters,
                NewField: newField,
                ConstructionSites: constructionSites,
                DeconstructionSites: deconstructionSites,
                PropertyPatternSites: propertyPatternSites,
                WithExpressionSites: withExpressionSites,
                DedupeSpans: dedupeSpans);

            ClassifySite(node, rich.Dto, ctx, ct);
        }

        // Pass B: document-level walk — catches deconstruction & with-expressions on variables
        // whose inferred type is the target (these don't show up as type-name references). We
        // only walk documents that already mentioned the type in pass A, so this is bounded by
        // the same reference-density constant as pass A.
        var documentsToWalk = materialized
            .Where(m => m.Source.Document is not null)
            .Select(m => m.Source.Document)
            .Distinct()
            .ToList();

        foreach (var doc in documentsToWalk)
        {
            if (ct.IsCancellationRequested) break;
            var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (root is null || semanticModel is null) continue;

            foreach (var node in root.DescendantNodes())
            {
                if (ct.IsCancellationRequested) break;
                switch (node)
                {
                    case WithExpressionSyntax we when ResolvesToTargetExpression(we.Expression, semanticModel, typeSymbol, ct):
                        AddUniqueWithSite(we, doc, withExpressionSites, dedupeSpans);
                        break;
                    case DeclarationExpressionSyntax decl when decl.Designation is ParenthesizedVariableDesignationSyntax pvd
                                                                && InferredDeconstructionTargetMatches(decl, semanticModel, typeSymbol, ct):
                        AddUniqueDeconstructionFromDesignation(decl, pvd, doc, existingPositionalParameters, newField, deconstructionSites, dedupeSpans);
                        break;
                    case AssignmentExpressionSyntax asn when asn.Left is TupleExpressionSyntax tuple
                                                              && AssignmentTargetMatches(asn, semanticModel, typeSymbol, ct):
                        AddUniqueDeconstructionFromTuple(asn, tuple, doc, existingPositionalParameters, newField, deconstructionSites, dedupeSpans);
                        break;
                    case RecursivePatternSyntax rp when PatternMatchesTarget(rp, semanticModel, typeSymbol, ct):
                        AddUniqueRecursivePattern(rp, doc, existingPositionalParameters, newField, deconstructionSites, propertyPatternSites, dedupeSpans);
                        break;
                }
            }
        }

        // Stable order: by file path then start line. Helps reviewers diff output across runs.
        constructionSites.Sort(CompareByLocation);
        deconstructionSites.Sort(CompareByLocation);
        propertyPatternSites.Sort(CompareByLocation);
        withExpressionSites.Sort(CompareByLocation);
        var testFileList = testFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

        var suggestedTasks = BuildSuggestedTasks(
            isPositional,
            constructionSites.Count,
            deconstructionSites.Count,
            propertyPatternSites.Count,
            withExpressionSites.Count,
            testFileList.Count);

        return new RecordFieldAdditionImpactDto(
            TargetRecordDisplay: typeSymbol.ToDisplayString(),
            IsPositionalRecord: isPositional,
            NewField: newField,
            ExistingPositionalParameters: existingPositionalParameters,
            PositionalConstructionSites: constructionSites,
            DeconstructionSites: deconstructionSites,
            PropertyPatternSites: propertyPatternSites,
            WithExpressionSites: withExpressionSites,
            TestFilesConstructing: testFileList,
            SuggestedTasks: suggestedTasks);
    }

    /// <summary>
    /// Returns the primary-constructor parameter list for a positional record, or an empty list
    /// when the record is not positional. Positional records always have a non-null
    /// <see cref="IMethodSymbol.IsImplicitlyDeclared"/> = <c>false</c> primary constructor with at
    /// least one parameter; non-positional record classes have only the implicit parameterless ctor.
    /// </summary>
    private static IReadOnlyList<ExistingPositionalParameterDto> ExtractPositionalParameters(INamedTypeSymbol typeSymbol)
    {
        // Find the constructor matching the primary record constructor signature: it is the only
        // ctor whose syntax is a RecordDeclarationSyntax with a non-null ParameterList.
        foreach (var ctor in typeSymbol.InstanceConstructors)
        {
            foreach (var declRef in ctor.DeclaringSyntaxReferences)
            {
                if (declRef.GetSyntax() is RecordDeclarationSyntax recordDecl && recordDecl.ParameterList is not null)
                {
                    var parameters = ctor.Parameters
                        .Select(p => new ExistingPositionalParameterDto(p.Name, p.Type.ToDisplayString()))
                        .ToList();
                    return parameters;
                }
            }
        }
        return Array.Empty<ExistingPositionalParameterDto>();
    }

    private static void ClassifySite(
        SyntaxNode node,
        LocationDto baseDto,
        RecordFieldClassificationContext ctx,
        CancellationToken ct)
    {
        // Walk up the node ancestry looking for the syntactic shape that classifies the site.
        // We bound the walk because most matches are within 3-4 hops of the IdentifierNameSyntax
        // ref, and a runaway walk on a deeply-nested expression would do useless work. Each
        // TryClassify* helper returns true when it handled the site, which terminates the walk.
        var current = node;
        var depth = 0;
        const int maxDepth = 8;

        while (current is not null && depth < maxDepth)
        {
            ct.ThrowIfCancellationRequested();
            depth++;

            if (current switch
            {
                ObjectCreationExpressionSyntax oc => TryClassifyObjectCreationSite(oc, baseDto, ctx, ct),
                ImplicitObjectCreationExpressionSyntax ioc => TryClassifyImplicitObjectCreationSite(ioc, baseDto, ctx, ct),
                WithExpressionSyntax we => TryClassifyWithExpressionSite(we, baseDto, ctx, ct),
                RecursivePatternSyntax rp => TryClassifyRecursivePatternSite(rp, baseDto, ctx, ct),
                DeclarationExpressionSyntax decl => TryClassifyDeclarationExpressionSite(decl, baseDto, ctx, ct),
                AssignmentExpressionSyntax asn => TryClassifyAssignmentExpressionSite(asn, baseDto, ctx, ct),
                _ => false,
            })
            {
                return;
            }

            current = current.Parent;
        }
    }

    private static bool TryClassifyObjectCreationSite(
        ObjectCreationExpressionSyntax oc,
        LocationDto baseDto,
        RecordFieldClassificationContext ctx,
        CancellationToken ct)
    {
        if (!ResolvesToTarget(oc.Type, ctx.SemanticModel, ctx.TargetType, ct)) return false;
        var argList = oc.ArgumentList;
        // Only flag when arity matches the existing positional-ctor count. A `new R()`
        // with zero args isn't an arity-match for a positional record with parameters
        // (so the compiler would already catch it). We focus on the "looks valid today,
        // breaks tomorrow" cases.
        if (argList is null || ctx.ExistingParameters.Count == 0 ||
            argList.Arguments.Count != ctx.ExistingParameters.Count)
        {
            return true;
        }
        var loc = UpdateDtoSpan(baseDto, oc);
        if (ctx.DedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, nameof(ObjectCreationExpressionSyntax))))
        {
            var original = argList.ToString();
            var suggested = BuildSuggestedArgumentList(argList, ctx.NewField);
            ctx.ConstructionSites.Add(new RecordPositionalConstructionSiteDto(loc, original, suggested));
        }
        return true;
    }

    private static bool TryClassifyImplicitObjectCreationSite(
        ImplicitObjectCreationExpressionSyntax ioc,
        LocationDto baseDto,
        RecordFieldClassificationContext ctx,
        CancellationToken ct)
    {
        if (!InferredImplicitTargetMatches(ioc, ctx.SemanticModel, ctx.TargetType, ct)) return false;
        var argList = ioc.ArgumentList;
        if (ctx.ExistingParameters.Count == 0 || argList.Arguments.Count != ctx.ExistingParameters.Count)
        {
            return true;
        }
        var loc = UpdateDtoSpan(baseDto, ioc);
        if (ctx.DedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, nameof(ImplicitObjectCreationExpressionSyntax))))
        {
            var original = argList.ToString();
            var suggested = BuildSuggestedArgumentList(argList, ctx.NewField);
            ctx.ConstructionSites.Add(new RecordPositionalConstructionSiteDto(loc, original, suggested));
        }
        return true;
    }

    private static bool TryClassifyWithExpressionSite(
        WithExpressionSyntax we,
        LocationDto baseDto,
        RecordFieldClassificationContext ctx,
        CancellationToken ct)
    {
        if (!ResolvesToTargetExpression(we.Expression, ctx.SemanticModel, ctx.TargetType, ct)) return false;
        var loc = UpdateDtoSpan(baseDto, we);
        if (ctx.DedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, nameof(WithExpressionSyntax))))
        {
            var initializer = we.Initializer.ToString();
            ctx.WithExpressionSites.Add(new RecordWithExpressionSiteDto(loc, initializer));
        }
        return true;
    }

    private static bool TryClassifyRecursivePatternSite(
        RecursivePatternSyntax rp,
        LocationDto baseDto,
        RecordFieldClassificationContext ctx,
        CancellationToken ct)
    {
        if (!PatternMatchesTarget(rp, ctx.SemanticModel, ctx.TargetType, ct)) return false;
        var loc = UpdateDtoSpan(baseDto, rp);
        // A recursive pattern can carry both a positional sub-pattern AND a property
        // sub-pattern. We classify each independently (a single ref can yield two
        // entries) so callers see the full impact.
        if (rp.PositionalPatternClause is { } positional && ctx.ExistingParameters.Count > 0 &&
            positional.Subpatterns.Count == ctx.ExistingParameters.Count &&
            ctx.DedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, "Deconstruct:" + nameof(RecursivePatternSyntax))))
        {
            var original = positional.ToString();
            var suggested = BuildSuggestedDeconstructionPattern(positional, ctx.NewField);
            ctx.DeconstructionSites.Add(new RecordDeconstructionSiteDto(loc, original, suggested));
        }
        if (rp.PropertyPatternClause is { } property &&
            ctx.DedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, "Property:" + nameof(RecursivePatternSyntax))))
        {
            var original = property.ToString();
            var missed = IsExhaustiveInSpirit(property, ctx.ExistingParameters, ctx.NewField);
            ctx.PropertyPatternSites.Add(new RecordPropertyPatternSiteDto(loc, original, missed));
        }
        return true;
    }

    private static bool TryClassifyDeclarationExpressionSite(
        DeclarationExpressionSyntax decl,
        LocationDto baseDto,
        RecordFieldClassificationContext ctx,
        CancellationToken ct)
    {
        if (decl.Designation is not ParenthesizedVariableDesignationSyntax pvd) return false;
        if (!InferredDeconstructionTargetMatches(decl, ctx.SemanticModel, ctx.TargetType, ct)) return false;
        AddUniqueDeconstructionFromDesignation(decl, pvd, baseDto, ctx.ExistingParameters, ctx.NewField, ctx.DeconstructionSites, ctx.DedupeSpans);
        return true;
    }

    private static bool TryClassifyAssignmentExpressionSite(
        AssignmentExpressionSyntax asn,
        LocationDto baseDto,
        RecordFieldClassificationContext ctx,
        CancellationToken ct)
    {
        if (asn.Left is not TupleExpressionSyntax tuple) return false;
        if (!AssignmentTargetMatches(asn, ctx.SemanticModel, ctx.TargetType, ct)) return false;
        AddUniqueDeconstructionFromTuple(asn, tuple, baseDto, ctx.ExistingParameters, ctx.NewField, ctx.DeconstructionSites, ctx.DedupeSpans);
        return true;
    }

    /// <summary>
    /// Builds a minimal <see cref="LocationDto"/> directly from a <see cref="SyntaxNode"/> —
    /// used by the pass-B document walker where we don't have a preceding
    /// <see cref="ReferenceLocation"/> to start from.
    /// </summary>
    private static LocationDto BuildLocationDto(SyntaxNode node, Document document)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        return new LocationDto(
            FilePath: document.FilePath ?? string.Empty,
            StartLine: lineSpan.StartLinePosition.Line + 1,
            StartColumn: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1,
            ContainingMember: null,
            PreviewText: null);
    }

    private static void AddUniqueWithSite(
        WithExpressionSyntax we,
        Document document,
        List<RecordWithExpressionSiteDto> withExpressionSites,
        HashSet<(string Path, int Line, int Col, string Kind)> dedupeSpans)
    {
        var loc = BuildLocationDto(we, document);
        if (dedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, nameof(WithExpressionSyntax))))
        {
            withExpressionSites.Add(new RecordWithExpressionSiteDto(loc, we.Initializer.ToString()));
        }
    }

    private static void AddUniqueDeconstructionFromDesignation(
        DeclarationExpressionSyntax decl,
        ParenthesizedVariableDesignationSyntax pvd,
        LocationDto baseDto,
        IReadOnlyList<ExistingPositionalParameterDto> existingParameters,
        NewRecordFieldDto newField,
        List<RecordDeconstructionSiteDto> deconstructionSites,
        HashSet<(string Path, int Line, int Col, string Kind)> dedupeSpans)
    {
        if (existingParameters.Count == 0 || pvd.Variables.Count != existingParameters.Count) return;
        var loc = UpdateDtoSpan(baseDto, decl);
        if (dedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, "Designation:" + nameof(DeclarationExpressionSyntax))))
        {
            var original = pvd.ToString();
            var suggested = BuildSuggestedDesignationPattern(pvd, newField);
            deconstructionSites.Add(new RecordDeconstructionSiteDto(loc, original, suggested));
        }
    }

    private static void AddUniqueDeconstructionFromDesignation(
        DeclarationExpressionSyntax decl,
        ParenthesizedVariableDesignationSyntax pvd,
        Document document,
        IReadOnlyList<ExistingPositionalParameterDto> existingParameters,
        NewRecordFieldDto newField,
        List<RecordDeconstructionSiteDto> deconstructionSites,
        HashSet<(string Path, int Line, int Col, string Kind)> dedupeSpans)
    {
        if (existingParameters.Count == 0 || pvd.Variables.Count != existingParameters.Count) return;
        var loc = BuildLocationDto(decl, document);
        if (dedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, "Designation:" + nameof(DeclarationExpressionSyntax))))
        {
            var original = pvd.ToString();
            var suggested = BuildSuggestedDesignationPattern(pvd, newField);
            deconstructionSites.Add(new RecordDeconstructionSiteDto(loc, original, suggested));
        }
    }

    private static void AddUniqueDeconstructionFromTuple(
        AssignmentExpressionSyntax asn,
        TupleExpressionSyntax tuple,
        LocationDto baseDto,
        IReadOnlyList<ExistingPositionalParameterDto> existingParameters,
        NewRecordFieldDto newField,
        List<RecordDeconstructionSiteDto> deconstructionSites,
        HashSet<(string Path, int Line, int Col, string Kind)> dedupeSpans)
    {
        if (existingParameters.Count == 0 || tuple.Arguments.Count != existingParameters.Count) return;
        var loc = UpdateDtoSpan(baseDto, asn);
        if (dedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, "Tuple:" + nameof(AssignmentExpressionSyntax))))
        {
            var original = tuple.ToString();
            var suggested = BuildSuggestedTuplePattern(tuple, newField);
            deconstructionSites.Add(new RecordDeconstructionSiteDto(loc, original, suggested));
        }
    }

    private static void AddUniqueDeconstructionFromTuple(
        AssignmentExpressionSyntax asn,
        TupleExpressionSyntax tuple,
        Document document,
        IReadOnlyList<ExistingPositionalParameterDto> existingParameters,
        NewRecordFieldDto newField,
        List<RecordDeconstructionSiteDto> deconstructionSites,
        HashSet<(string Path, int Line, int Col, string Kind)> dedupeSpans)
    {
        if (existingParameters.Count == 0 || tuple.Arguments.Count != existingParameters.Count) return;
        var loc = BuildLocationDto(asn, document);
        if (dedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, "Tuple:" + nameof(AssignmentExpressionSyntax))))
        {
            var original = tuple.ToString();
            var suggested = BuildSuggestedTuplePattern(tuple, newField);
            deconstructionSites.Add(new RecordDeconstructionSiteDto(loc, original, suggested));
        }
    }

    private static void AddUniqueRecursivePattern(
        RecursivePatternSyntax rp,
        Document document,
        IReadOnlyList<ExistingPositionalParameterDto> existingParameters,
        NewRecordFieldDto newField,
        List<RecordDeconstructionSiteDto> deconstructionSites,
        List<RecordPropertyPatternSiteDto> propertyPatternSites,
        HashSet<(string Path, int Line, int Col, string Kind)> dedupeSpans)
    {
        var loc = BuildLocationDto(rp, document);

        if (rp.PositionalPatternClause is { } positional && existingParameters.Count > 0 &&
            positional.Subpatterns.Count == existingParameters.Count)
        {
            if (dedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, "Deconstruct:" + nameof(RecursivePatternSyntax))))
            {
                var original = positional.ToString();
                var suggested = BuildSuggestedDeconstructionPattern(positional, newField);
                deconstructionSites.Add(new RecordDeconstructionSiteDto(loc, original, suggested));
            }
        }
        if (rp.PropertyPatternClause is { } property)
        {
            if (dedupeSpans.Add((loc.FilePath, loc.StartLine, loc.StartColumn, "Property:" + nameof(RecursivePatternSyntax))))
            {
                var original = property.ToString();
                var missed = IsExhaustiveInSpirit(property, existingParameters, newField);
                propertyPatternSites.Add(new RecordPropertyPatternSiteDto(loc, original, missed));
            }
        }
    }

    private static bool ResolvesToTarget(TypeSyntax typeSyntax, SemanticModel model, INamedTypeSymbol target, CancellationToken ct)
    {
        var info = model.GetSymbolInfo(typeSyntax, ct);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        return SymbolEqualityComparer.Default.Equals(UnwrapNamedType(symbol), target);
    }

    private static bool ResolvesToTargetExpression(ExpressionSyntax expr, SemanticModel model, INamedTypeSymbol target, CancellationToken ct)
    {
        var typeInfo = model.GetTypeInfo(expr, ct);
        var t = typeInfo.Type ?? typeInfo.ConvertedType;
        return SymbolEqualityComparer.Default.Equals(UnwrapNamedType(t), target);
    }

    private static bool InferredImplicitTargetMatches(ImplicitObjectCreationExpressionSyntax ioc, SemanticModel model, INamedTypeSymbol target, CancellationToken ct)
    {
        var typeInfo = model.GetTypeInfo(ioc, ct);
        var t = typeInfo.Type ?? typeInfo.ConvertedType;
        return SymbolEqualityComparer.Default.Equals(UnwrapNamedType(t), target);
    }

    private static bool PatternMatchesTarget(RecursivePatternSyntax rp, SemanticModel model, INamedTypeSymbol target, CancellationToken ct)
    {
        // The pattern's "target type" comes either from the explicit type token (`record is Foo {...}`)
        // or from the inferred type of the expression being matched. The semantic model exposes both
        // via GetTypeInfo on the pattern node itself.
        if (rp.Type is { } typeRef && ResolvesToTarget(typeRef, model, target, ct))
        {
            return true;
        }
        // Walk up to find the IsPatternExpression / SwitchExpressionArm to get the input type.
        SyntaxNode? n = rp.Parent;
        while (n is not null)
        {
            if (n is IsPatternExpressionSyntax ipe)
            {
                return ResolvesToTargetExpression(ipe.Expression, model, target, ct);
            }
            if (n is SwitchExpressionSyntax se)
            {
                return ResolvesToTargetExpression(se.GoverningExpression, model, target, ct);
            }
            if (n is SwitchStatementSyntax ss)
            {
                return ResolvesToTargetExpression(ss.Expression, model, target, ct);
            }
            n = n.Parent;
        }
        return false;
    }

    private static bool InferredDeconstructionTargetMatches(DeclarationExpressionSyntax decl, SemanticModel model, INamedTypeSymbol target, CancellationToken ct)
    {
        // The right-hand side of the assignment containing this declaration is the deconstruction
        // source. Find the enclosing assignment and check its right-hand type.
        SyntaxNode? n = decl.Parent;
        while (n is not null)
        {
            if (n is AssignmentExpressionSyntax asn && asn.Left == decl)
            {
                return ResolvesToTargetExpression(asn.Right, model, target, ct);
            }
            // foreach (var (a, b) in src) — ForEachVariableStatement is a separate shape.
            if (n is ForEachVariableStatementSyntax fevs)
            {
                var typeInfo = model.GetTypeInfo(fevs.Expression, ct);
                var elem = (typeInfo.Type ?? typeInfo.ConvertedType) is INamedTypeSymbol nt
                    ? nt.TypeArguments.FirstOrDefault() as INamedTypeSymbol
                    : null;
                return SymbolEqualityComparer.Default.Equals(elem, target);
            }
            n = n.Parent;
        }
        return false;
    }

    private static bool AssignmentTargetMatches(AssignmentExpressionSyntax asn, SemanticModel model, INamedTypeSymbol target, CancellationToken ct)
        => ResolvesToTargetExpression(asn.Right, model, target, ct);

    private static INamedTypeSymbol? UnwrapNamedType(ISymbol? symbol) => symbol switch
    {
        INamedTypeSymbol named when named.IsGenericType => named.OriginalDefinition,
        INamedTypeSymbol named => named,
        _ => null,
    };

    private static INamedTypeSymbol? UnwrapNamedType(ITypeSymbol? type) => type switch
    {
        INamedTypeSymbol named when named.IsGenericType => named.OriginalDefinition,
        INamedTypeSymbol named => named,
        _ => null,
    };

    private static bool IsTestDocument(Document document)
    {
        // Heuristic: same one CouplingAnalysis / ImpactSweep use — IsTestProject metadata flag
        // wins, otherwise file name heuristic. The metadata check requires reading the project's
        // XML so we cache via IsTestProject(Project).
        if (ProjectMetadataParser.IsTestProject(document.Project)) return true;
        var name = Path.GetFileNameWithoutExtension(document.FilePath);
        if (string.IsNullOrEmpty(name)) return false;
        return name.EndsWith("Tests", StringComparison.Ordinal)
            || name.EndsWith("Test", StringComparison.Ordinal)
            || name.EndsWith("Spec", StringComparison.Ordinal);
    }

    /// <summary>
    /// "Exhaustive in spirit" = the property pattern explicitly names every existing positional
    /// parameter of the record but does NOT name the new field. These are the patterns most likely
    /// to need an explicit update — the author has shown intent to be thorough about every field.
    /// A pattern that names only one field is a deliberate partial match and not flagged.
    /// </summary>
    private static bool IsExhaustiveInSpirit(
        PropertyPatternClauseSyntax property,
        IReadOnlyList<ExistingPositionalParameterDto> existingParameters,
        NewRecordFieldDto newField)
    {
        if (existingParameters.Count == 0) return false;

        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sub in property.Subpatterns)
        {
            if (sub.NameColon?.Name.Identifier.ValueText is { } n1) named.Add(n1);
            else if (sub.ExpressionColon?.Expression is IdentifierNameSyntax ins) named.Add(ins.Identifier.ValueText);
        }

        // Every existing parameter named, AND the new field is NOT named.
        var allExistingNamed = existingParameters.All(p => named.Contains(p.Name));
        var newFieldNotNamed = !named.Contains(newField.Name);
        return allExistingNamed && newFieldNotNamed;
    }

    private static string BuildSuggestedArgumentList(ArgumentListSyntax original, NewRecordFieldDto newField)
    {
        var args = original.Arguments.Select(a => a.ToString()).ToList();
        args.Add(newField.DefaultValueExpression ?? $"/* TODO: {newField.Name} ({newField.Type}) */");
        return "(" + string.Join(", ", args) + ")";
    }

    private static string BuildSuggestedDeconstructionPattern(PositionalPatternClauseSyntax original, NewRecordFieldDto newField)
    {
        var parts = original.Subpatterns.Select(s => s.ToString()).ToList();
        parts.Add("_");
        return "(" + string.Join(", ", parts) + ")";
    }

    private static string BuildSuggestedDesignationPattern(ParenthesizedVariableDesignationSyntax original, NewRecordFieldDto newField)
    {
        var parts = original.Variables.Select(v => v.ToString()).ToList();
        parts.Add("_");
        return "(" + string.Join(", ", parts) + ")";
    }

    private static string BuildSuggestedTuplePattern(TupleExpressionSyntax original, NewRecordFieldDto newField)
    {
        var parts = original.Arguments.Select(a => a.ToString()).ToList();
        parts.Add("_");
        return "(" + string.Join(", ", parts) + ")";
    }

    /// <summary>
    /// Promotes the base reference DTO span to cover the wider classified expression
    /// (e.g. the entire <c>new Foo(a, b)</c>, not just the type identifier inside it).
    /// </summary>
    private static LocationDto UpdateDtoSpan(LocationDto baseDto, SyntaxNode wider)
    {
        var lineSpan = wider.GetLocation().GetLineSpan();
        return baseDto with
        {
            StartLine = lineSpan.StartLinePosition.Line + 1,
            StartColumn = lineSpan.StartLinePosition.Character + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            EndColumn = lineSpan.EndLinePosition.Character + 1,
        };
    }

    private static int CompareByLocation<T>(T a, T b) where T : class
    {
        var la = GetLocation(a);
        var lb = GetLocation(b);
        var fileCmp = string.Compare(la.FilePath, lb.FilePath, StringComparison.OrdinalIgnoreCase);
        return fileCmp != 0 ? fileCmp : la.StartLine.CompareTo(lb.StartLine);
    }

    private static LocationDto GetLocation(object site) => site switch
    {
        RecordPositionalConstructionSiteDto c => c.Location,
        RecordDeconstructionSiteDto d => d.Location,
        RecordPropertyPatternSiteDto p => p.Location,
        RecordWithExpressionSiteDto w => w.Location,
        _ => throw new InvalidOperationException($"Unexpected site type {site.GetType().Name}."),
    };

    private static IReadOnlyList<string> BuildSuggestedTasks(
        bool isPositional, int construction, int deconstruction, int propertyPattern, int with, int testFiles)
    {
        var tasks = new List<string>();
        if (!isPositional)
        {
            tasks.Add("Target is NOT a positional record — only `with { ... }` consumers and explicit property writes are affected. Construction / deconstruction / positional-pattern lists will be empty.");
        }
        if (construction > 0)
            tasks.Add($"Update {construction} positional construction site(s) to pass the new field as the trailing argument.");
        if (deconstruction > 0)
            tasks.Add($"Update {deconstruction} deconstruction site(s) — add a trailing `_` (or named variable) for the new positional element.");
        if (propertyPattern > 0)
            tasks.Add($"Audit {propertyPattern} property-pattern site(s) — sites flagged with `MissedCorrelation: true` named every prior field and likely need the new field added.");
        if (with > 0)
            tasks.Add($"Audit {with} `with`-expression site(s) — confirm the new field's default is correct or add an explicit `{{ NewField = ... }}` assignment.");
        if (testFiles > 0)
            tasks.Add($"Sweep {testFiles} test file(s) that mention the record — fixture builders are typically the densest cluster of construction sites.");
        if (tasks.Count == 0)
            tasks.Add("No impact detected. The record is unreferenced beyond its declaration.");
        return tasks;
    }

    /// <summary>
    /// Bundles the 9 cross-cutting inputs and accumulators that the pass-A classification helpers
    /// all need, keeping their individual signatures narrow (3-4 parameters each). The lists are
    /// reference-shared with <see cref="PreviewAdditionAsync"/> — each helper mutates them in place.
    /// </summary>
    private sealed record RecordFieldClassificationContext(
        SemanticModel SemanticModel,
        INamedTypeSymbol TargetType,
        IReadOnlyList<ExistingPositionalParameterDto> ExistingParameters,
        NewRecordFieldDto NewField,
        List<RecordPositionalConstructionSiteDto> ConstructionSites,
        List<RecordDeconstructionSiteDto> DeconstructionSites,
        List<RecordPropertyPatternSiteDto> PropertyPatternSites,
        List<RecordWithExpressionSiteDto> WithExpressionSites,
        HashSet<(string Path, int Line, int Col, string Kind)> DedupeSpans);
}
