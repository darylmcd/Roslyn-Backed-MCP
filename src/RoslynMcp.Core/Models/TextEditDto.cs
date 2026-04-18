namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a text edit over a source span.
/// </summary>
public sealed record TextEditDto(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string NewText);

/// <summary>
/// Represents the result of applying one or more text edits to a file.
/// </summary>
/// <param name="Verification">
/// When <c>verify=true</c> was requested on the apply call, carries the post-edit
/// compile-check outcome (including any new errors and whether an auto-revert
/// fired). <c>null</c> on the default (<c>verify=false</c>) path so existing
/// call-sites see the same shape they did before
/// <c>semantic-edit-with-compile-check-wrapper</c>.
/// </param>
public sealed record TextEditResultDto(
    bool Success,
    string FilePath,
    int EditsApplied,
    IReadOnlyList<FileChangeDto> Changes,
    IReadOnlyList<TextEditSyntaxErrorDto>? SyntaxErrors = null,
    VerifyOutcomeDto? Verification = null);
