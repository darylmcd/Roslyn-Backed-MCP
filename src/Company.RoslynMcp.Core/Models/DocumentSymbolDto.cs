namespace Company.RoslynMcp.Core.Models;

public sealed record DocumentSymbolDto(
    string Name,
    string Kind,
    IReadOnlyList<string>? Modifiers,
    int StartLine,
    int EndLine,
    IReadOnlyList<DocumentSymbolDto>? Children);
