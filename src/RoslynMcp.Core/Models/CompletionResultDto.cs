namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the completion items returned for a completion request.
/// </summary>
public sealed record CompletionResultDto(
    IReadOnlyList<CompletionItemDto> Items,
    bool IsIncomplete);

/// <summary>
/// Represents a single completion candidate.
/// </summary>
public sealed record CompletionItemDto(
    string DisplayText,
    string? FilterText,
    string? SortText,
    string? InlineDescription,
    string Kind,
    IReadOnlyList<string>? Tags);
