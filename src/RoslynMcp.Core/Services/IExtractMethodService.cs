using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface IExtractMethodService
{
    Task<RefactoringPreviewDto> PreviewExtractMethodAsync(
        string workspaceId, string filePath,
        int startLine, int startColumn, int endLine, int endColumn,
        string methodName, CancellationToken ct);

    /// <summary>
    /// Preview extracting a shared sub-expression (at an example span) into a new private
    /// static helper and rewriting every structurally-identical call site in the scope.
    /// Complements <see cref="PreviewExtractMethodAsync"/> (statement-block, single-function)
    /// by handling the "this sub-expression appears in N functions; extract to a shared helper"
    /// shape.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="exampleFilePath">Absolute path to the source file containing the example.</param>
    /// <param name="exampleStartLine">1-based start line of the example expression.</param>
    /// <param name="exampleStartColumn">1-based start column of the example expression.</param>
    /// <param name="exampleEndLine">1-based end line of the example expression.</param>
    /// <param name="exampleEndColumn">1-based end column of the example expression.</param>
    /// <param name="helperName">Name of the synthesized helper method.</param>
    /// <param name="helperAccessibility">Accessibility modifier (private, internal, or public).
    /// Defaults to <c>private</c>.</param>
    /// <param name="allowCrossFile">When true, scans the entire project for structurally-
    /// identical matches. When false (default), the scan is restricted to the example
    /// expression's containing type.</param>
    /// <remarks>
    /// Guards: (a) refuses when the example span does not resolve to a single
    /// <see cref="Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax" />, (b) refuses when
    /// any candidate site references a variable whose semantic type differs from the
    /// corresponding variable at the example site, (c) refuses when <paramref name="allowCrossFile"/>
    /// is false and a hit resides in a different containing type.
    /// </remarks>
    Task<RefactoringPreviewDto> PreviewExtractSharedExpressionToHelperAsync(
        string workspaceId,
        string exampleFilePath,
        int exampleStartLine, int exampleStartColumn,
        int exampleEndLine, int exampleEndColumn,
        string helperName,
        string helperAccessibility,
        bool allowCrossFile,
        CancellationToken ct);
}
