namespace RoslynMcp.Core.Models;

/// <summary>
/// Groups a set of text edits to be applied to a single file.
/// </summary>
/// <param name="FilePath">The absolute path to the file being edited.</param>
/// <param name="Edits">The text edits to apply, in any order.</param>
public sealed record FileEditsDto(string FilePath, TextEditDto[] Edits);
