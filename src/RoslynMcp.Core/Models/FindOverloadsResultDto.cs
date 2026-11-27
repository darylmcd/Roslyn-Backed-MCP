namespace RoslynMcp.Core.Models;

/// <summary>Method overloads and provenance for the selected type definition.</summary>
public sealed record FindOverloadsResultDto(
    IReadOnlyList<SignatureHelpDto> Items,
    string ResolvedProject,
    string? ResolvedTargetFramework,
    bool MemberFound);
