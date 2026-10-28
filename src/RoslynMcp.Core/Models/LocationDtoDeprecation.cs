namespace RoslynMcp.Core.Models;

/// <summary>
/// Shared <see cref="ObsoleteAttribute"/> message for the legacy flat location members of
/// <see cref="SymbolDto"/>, <see cref="DiagnosticDto"/> and <see cref="TypeUsageDto"/> (ADR 0001, stage 3).
/// </summary>
internal static class LocationDtoDeprecation
{
    internal const string FlatMemberMessage =
        "Use Location instead. The flat location members are removed in the next major release (ADR 0001).";
}
