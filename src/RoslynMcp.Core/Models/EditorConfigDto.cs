namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the effective .editorconfig options for a source file.
/// </summary>
public sealed record EditorConfigOptionsDto(
    string FilePath,
    string? ApplicableEditorConfigPath,
    IReadOnlyList<EditorConfigEntryDto> Options);

/// <summary>
/// A single key-value pair from the effective .editorconfig options.
/// </summary>
public sealed record EditorConfigEntryDto(
    string Key,
    string Value,
    string? Source);
