namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a span within a source file. The path and start position are always present; the end
/// position is null when the producer knows where the span begins but not where it ends (for example a
/// build diagnostic that gives only <c>file(line,column)</c> and whose end could not be resolved).
/// <see cref="EndLine"/> and <see cref="EndColumn"/> are either both set or both null.
/// </summary>
public sealed record LocationDto(
    string FilePath,
    int StartLine,
    int StartColumn,
    int? EndLine,
    int? EndColumn,
    string? ContainingMember,
    string? PreviewText,
    string? Classification = null);
