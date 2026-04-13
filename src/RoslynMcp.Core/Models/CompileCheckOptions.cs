namespace RoslynMcp.Core.Models;

/// <summary>
/// Options for in-memory compilation checking, consolidating the parameter list
/// of <see cref="Services.ICompileCheckService.CheckAsync"/>.
/// </summary>
public sealed record CompileCheckOptions(
    string? ProjectFilter = null,
    bool EmitValidation = false,
    string? SeverityFilter = null,
    string? FileFilter = null,
    int Offset = 0,
    int Limit = 50);
