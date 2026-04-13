namespace RoslynMcp.Core.Models;

/// <summary>
/// A syntax or lexer error detected when validating text after a proposed <c>apply_text_edit</c>.
/// </summary>
public sealed record TextEditSyntaxErrorDto(int Line, int Column, string Message);
