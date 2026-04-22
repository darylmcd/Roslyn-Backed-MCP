namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of applying a previewed workspace mutation.
/// </summary>
/// <param name="Success">Whether the apply succeeded.</param>
/// <param name="AppliedFiles">Files whose on-disk content was changed.</param>
/// <param name="Error">Human-readable error description when <paramref name="Success"/> is false.</param>
/// <param name="MutatedSymbol">
/// When a rename apply changes a symbol's identity, carries a fresh <see cref="SymbolDto"/> for the
/// renamed symbol (new name, new symbol handle) so downstream tools can keep chaining without
/// re-resolving by the new name. Null for all other apply kinds (the existing handle still resolves
/// after move-type, change-signature, format, code-fix, etc.).
/// </param>
public sealed record ApplyResultDto(
    bool Success,
    IReadOnlyList<string> AppliedFiles,
    string? Error,
    SymbolDto? MutatedSymbol = null);
