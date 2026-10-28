namespace RoslynMcp.Core.Models;

// locationdto-stage3-flat-field-deprecation (ADR 0001, stage 3): this file is the ONLY production code that
// reads or writes the deprecated flat location members. Partial spans are a live state (build output gives
// file + start with no end, so Location is null), which the nested value cannot represent; readers therefore
// prefer Location and fall back to the flat members. Delete this file with the flat members at the next major.
#pragma warning disable CS0618

/// <summary>
/// Single read/write seam over the deprecated flat location members of <see cref="SymbolDto"/>,
/// <see cref="DiagnosticDto"/> and <see cref="TypeUsageDto"/>.
/// </summary>
internal static class LocationDtoFlatAccess
{
    internal static string? ResolvedFilePath(this SymbolDto dto) => dto.Location?.FilePath ?? dto.FilePath;

    internal static int? ResolvedStartLine(this SymbolDto dto) => dto.Location?.StartLine ?? dto.StartLine;

    internal static int? ResolvedStartColumn(this SymbolDto dto) => dto.Location?.StartColumn ?? dto.StartColumn;

    internal static int? ResolvedEndLine(this SymbolDto dto) => dto.Location?.EndLine ?? dto.EndLine;

    internal static int? ResolvedEndColumn(this SymbolDto dto) => dto.Location?.EndColumn ?? dto.EndColumn;

    internal static string? ResolvedFilePath(this DiagnosticDto dto) => dto.Location?.FilePath ?? dto.FilePath;

    internal static int? ResolvedStartLine(this DiagnosticDto dto) => dto.Location?.StartLine ?? dto.StartLine;

    internal static int? ResolvedStartColumn(this DiagnosticDto dto) => dto.Location?.StartColumn ?? dto.StartColumn;

    internal static int? ResolvedEndLine(this DiagnosticDto dto) => dto.Location?.EndLine ?? dto.EndLine;

    internal static int? ResolvedEndColumn(this DiagnosticDto dto) => dto.Location?.EndColumn ?? dto.EndColumn;

    internal static string ResolvedFilePath(this TypeUsageDto dto) => dto.Location?.FilePath ?? dto.FilePath;

    /// <summary>The usage classification name; the nested value carries the same vocabulary as the enum.</summary>
    internal static string ResolvedClassification(this TypeUsageDto dto) =>
        dto.Location?.Classification ?? dto.Classification.ToString();

    /// <summary>Returns a copy with the end of the span set on both representations so they cannot drift.</summary>
    internal static DiagnosticDto WithResolvedEnd(
        this DiagnosticDto diagnostic, int endLine, int endColumn, LocationDto? location) =>
        diagnostic with { EndLine = endLine, EndColumn = endColumn, Location = location };
}
