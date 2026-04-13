namespace RoslynMcp.Core.Models;

public sealed record RefactoringSuggestionDto(
    string Severity,
    string Category,
    string Description,
    string TargetSymbol,
    string FilePath,
    int Line,
    IReadOnlyList<string> RecommendedTools);
