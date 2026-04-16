namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a preview of pending refactoring changes before they are applied.
/// </summary>
/// <param name="CallsiteUpdates">
/// Optional compact per-file summary of touched callsites (file path + invocation
/// count). Populated by tools whose apply path may rewrite invocations across many
/// files — e.g., <c>change_signature_preview op=remove</c> on an interface method
/// that dispatches across N implementer callsites. Lets callers audit reach without
/// parsing every <see cref="Changes"/> diff. Null when the producing tool does not
/// emit per-callsite info.
/// </param>
public sealed record RefactoringPreviewDto(
    string PreviewToken,
    string Description,
    IReadOnlyList<FileChangeDto> Changes,
    IReadOnlyList<string>? Warnings,
    IReadOnlyList<CallsiteUpdateDto>? CallsiteUpdates = null);
