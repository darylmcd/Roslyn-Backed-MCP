using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>An occurrence that the shared-expression preview rewrote: its original document, syntax and position in the hit list.</summary>
internal readonly record struct ProjectedHit(Document Original, ExpressionSyntax OriginalExpression, int Index);

/// <summary>
/// The post-rewrite seam of <c>extract_shared_expression_to_helper_preview</c>: checks the projected (rewritten) solution
/// before a preview token is stored. Each step returns a refusal message; later steps (helper-body bindings, capture
/// timing) are added here without touching the preview's call site. Compiler-error and call-site checks need the
/// compiler's own binding of the projected code, which only exists after the rewrite.
/// </summary>
internal static class SharedExpressionProjectionVerifier
{
    public const string HitAnnotationKind = "RoslynMcp.SharedExpressionHit";

    /// <summary>Marks a generated helper invocation so the verifier can find it again after formatting.</summary>
    public static SyntaxAnnotation CreateHitAnnotation(int index) =>
        new(HitAnnotationKind, index.ToString(CultureInfo.InvariantCulture));

    /// <returns>A refusal message, or null when every check passed.</returns>
    public static async Task<string?> VerifyAsync(
        Solution baseline, Solution projected, ProjectId project, IReadOnlyList<ProjectedHit> hits, CancellationToken ct)
    {
        // Compile errors first: the call-site comparison assumes the projection binds.
        return await VerifyNoIntroducedErrorsAsync(baseline, projected, project, ct).ConfigureAwait(false)
            ?? await VerifyCallSitesAsync(projected, hits, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The rewritten project and every project depending on it must not gain compiler errors, e.g. a private helper
    /// called through the owner type from another type (CS0122) or a helper call in an expression-only position.
    /// </summary>
    private static async Task<string?> VerifyNoIntroducedErrorsAsync(
        Solution baseline, Solution projected, ProjectId project, CancellationToken ct)
    {
        var introduced = await PreviewCompilationDelta.IntroducedErrorsAsync(
            baseline, projected, [project], PreviewCompilationDelta.DocumentKey, ct).ConfigureAwait(false);
        if (introduced.Count == 0) return null;

        return "The shared helper rewrite would introduce compiler errors (" +
            InterfaceNullableProjection.FormatIds(introduced.Select(error => error.Diagnostic)) +
            "). Choose a helper accessibility every matching site can call, or narrow the extraction scope.";
    }

    /// <summary>
    /// Call-site binding: the operations that enclose a replaced occurrence must bind to the same targets and
    /// conversions afterwards. Only what an enclosing operation consumes is compared (invoked method or operator, member,
    /// conversion kind, type); constness is not, because every ancestor of a replaced constant stops being constant.
    /// Constant-only slots are refused earlier by the use-site classifier and by the compile-error check. A binding
    /// that cannot be fetched or is invalid on either side is a refusal, never an exception.
    /// </summary>
    private static async Task<string?> VerifyCallSitesAsync(
        Solution projected, IReadOnlyList<ProjectedHit> hits, CancellationToken ct)
    {
        var projectedNodes = new Dictionary<DocumentId, Dictionary<string, SyntaxNode>>();
        foreach (var hit in hits)
        {
            ct.ThrowIfCancellationRequested();
            var location = $"{hit.Original.Name}:{hit.OriginalExpression.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";
            var refusal =
                $"Replacing the expression at {location} with a helper call changes how the surrounding expression binds " +
                "(a constant conversion, overload or operator), so it cannot be extracted without changing behavior. " +
                "Select a different example or narrow the extraction scope.";

            var originalModel = await hit.Original.GetSemanticModelAsync(ct).ConfigureAwait(false);
            var projectedDocument = projected.GetDocument(hit.Original.Id);
            if (originalModel is null || projectedDocument is null) return refusal;

            if (!projectedNodes.TryGetValue(projectedDocument.Id, out var byIndex))
            {
                var root = await projectedDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                byIndex = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
                if (root is not null)
                {
                    foreach (var node in root.GetAnnotatedNodes(HitAnnotationKind))
                    {
                        foreach (var annotation in node.GetAnnotations(HitAnnotationKind))
                        {
                            if (annotation.Data is { } data) byIndex[data] = node;
                        }
                    }
                }

                projectedNodes[projectedDocument.Id] = byIndex;
            }

            if (!byIndex.TryGetValue(hit.Index.ToString(CultureInfo.InvariantCulture), out var projectedNode)) return refusal;
            var projectedModel = await projectedDocument.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (projectedModel is null) return refusal;

            var before = Describe(originalModel.GetOperation(hit.OriginalExpression, ct));
            var after = Describe(projectedModel.GetOperation(projectedNode, ct));
            if (before is null || after is null || !before.SequenceEqual(after)) return refusal;
        }

        return null;
    }

    private readonly record struct OperationShape(OperationKind Kind, bool IsImplicit, string? Type, string? Target);

    /// <summary>The shapes of every operation above <paramref name="start"/>, or null when it has no valid binding.</summary>
    private static List<OperationShape>? Describe(IOperation? start)
    {
        if (start is null || start.Kind == OperationKind.Invalid) return null;

        var shapes = new List<OperationShape>();
        var operation = start.Parent;
        // The helper returns the occurrence's converted type, so the implicit conversions directly above it
        // are part of the helper body afterwards.
        while (operation is IConversionOperation { IsImplicit: true }) operation = operation.Parent;
        for (; operation is not null; operation = operation.Parent)
        {
            shapes.Add(new OperationShape(
                operation.Kind,
                operation.IsImplicit,
                operation.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                operation switch
                {
                    IInvocationOperation invocation => Key(invocation.TargetMethod),
                    IBinaryOperation binary => binary.OperatorKind + "|" + Key(binary.OperatorMethod),
                    IUnaryOperation unary => unary.OperatorKind + "|" + Key(unary.OperatorMethod),
                    IConversionOperation conversion => conversion.Conversion + "|" + Key(conversion.OperatorMethod),
                    IPropertyReferenceOperation property => Key(property.Property),
                    IFieldReferenceOperation field => Key(field.Field),
                    IMethodReferenceOperation method => Key(method.Method),
                    IObjectCreationOperation creation => Key(creation.Constructor),
                    IArgumentOperation argument => Key(argument.Parameter),
                    _ => null,
                }));
        }

        return shapes;
    }

    /// <summary>A symbol identity that is stable across the original and the projected compilation.</summary>
    private static string Key(ISymbol? symbol) => symbol is null
        ? string.Empty
        : symbol.OriginalDefinition.GetDocumentationCommentId() + "|" + symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
