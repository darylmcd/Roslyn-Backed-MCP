namespace Company.RoslynMcp.Core.Models;

public sealed record FileEditsDto(string FilePath, TextEditDto[] Edits);
