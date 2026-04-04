namespace RoslynMcp.Core.Models;

/// <summary>
/// Result of writing a key/value pair into an .editorconfig file.
/// </summary>
public sealed record EditorConfigWriteResultDto(
    string EditorConfigPath,
    string Key,
    string Value,
    bool CreatedNewFile);
