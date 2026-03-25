namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a symbol declared in a document, with its location, kind, and nested children.
/// </summary>
/// <param name="Name">The declared name of the symbol.</param>
/// <param name="Kind">The symbol kind (e.g., class, method, property).</param>
/// <param name="Modifiers">Access and other modifiers, or <see langword="null"/> if none.</param>
/// <param name="StartLine">The 1-based start line of the symbol's span.</param>
/// <param name="EndLine">The 1-based end line of the symbol's span.</param>
/// <param name="Children">Nested child symbols, or <see langword="null"/> if the symbol has no children.</param>
public sealed record DocumentSymbolDto(
    string Name,
    string Kind,
    IReadOnlyList<string>? Modifiers,
    int StartLine,
    int EndLine,
    IReadOnlyList<DocumentSymbolDto>? Children);
