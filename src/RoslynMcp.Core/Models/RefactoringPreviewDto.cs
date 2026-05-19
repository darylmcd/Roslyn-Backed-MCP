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
/// <param name="GuidanceMessage">
/// Optional caller-facing guidance describing why the preview is empty (e.g.
/// <c>code_fix_preview</c> when no <c>CodeFixProvider</c> is registered for the
/// requested diagnostic id). When set, <see cref="PreviewToken"/> is empty and
/// <see cref="Changes"/> is empty — the caller is expected to act on the guidance
/// rather than apply the (empty) preview. Mirrors <c>FixAllPreviewDto.GuidanceMessage</c>
/// so <c>code_fix_preview</c> and <c>fix_all_preview</c> share a consistent
/// empty-envelope shape (code-fix-preview-vs-fix-all-preview-shape-inconsistency).
/// </param>
public sealed record RefactoringPreviewDto(
    string PreviewToken,
    string Description,
    IReadOnlyList<FileChangeDto> Changes,
    IReadOnlyList<string>? Warnings,
    IReadOnlyList<CallsiteUpdateDto>? CallsiteUpdates = null,
    string? GuidanceMessage = null);
