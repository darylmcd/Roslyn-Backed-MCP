namespace Company.RoslynMcp.Core.Models;

public sealed record CompletionResultDto(
    IReadOnlyList<CompletionItemDto> Items,
    bool IsIncomplete);

public sealed record CompletionItemDto(
    string DisplayText,
    string? FilterText,
    string? SortText,
    string? InlineDescription,
    string Kind,
    IReadOnlyList<string>? Tags);
