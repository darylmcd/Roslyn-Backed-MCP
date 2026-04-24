namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a preview of applying a code fix to all instances of a diagnostic across a scope.
/// </summary>
/// <remarks>
/// When the registered <c>FixAllProvider</c> throws while computing the fix (e.g. the well-known
/// <c>"Sequence contains no elements"</c> crash on <c>IDE0300</c>), the response sets
/// <see cref="Error"/> = <c>true</c>, <see cref="Category"/> = <c>"FixAllProviderCrash"</c>, and
/// <see cref="PerOccurrenceFallbackAvailable"/> = <c>true</c> so callers can distinguish a
/// provider-side defect from a missing provider, zero occurrences, or a provider that produced
/// no actions. <see cref="GuidanceMessage"/> carries the human-readable detail regardless of
/// whether the response is an error envelope or a benign empty result.
/// </remarks>
public sealed record FixAllPreviewDto(
    string PreviewToken,
    string DiagnosticId,
    string Scope,
    int FixedCount,
    IReadOnlyList<FileChangeDto> Changes,
    string? GuidanceMessage = null,
    bool Error = false,
    string? Category = null,
    bool? PerOccurrenceFallbackAvailable = null);
