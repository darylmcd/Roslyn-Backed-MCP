using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Item 7: single replacement directive for string-literal-to-constant migration.
/// </summary>
/// <param name="LiteralValue">The exact string literal value to match (without surrounding quotes).</param>
/// <param name="ReplacementExpression">The C# expression text to substitute (e.g., <c>"Constants.MyValue"</c>).</param>
/// <param name="UsingNamespace">Optional namespace to inject as a <c>using</c> directive in files where the replacement applied.</param>
public sealed record StringLiteralReplacementDto(
    string LiteralValue,
    string ReplacementExpression,
    string? UsingNamespace = null);

/// <summary>
/// Item 7: finds string literals in argument/initializer position and replaces them with a
/// named constant or identifier expression. Stays strict about syntactic position so XML doc
/// comments, interpolated string holes, and nameof() literals are not touched.
/// </summary>
public interface IStringLiteralReplaceService
{
    Task<RefactoringPreviewDto> PreviewReplaceAsync(
        string workspaceId,
        IReadOnlyList<StringLiteralReplacementDto> replacements,
        RestructureScope scope,
        CancellationToken ct);
}
