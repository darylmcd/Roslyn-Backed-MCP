namespace RoslynMcp.Core.Models;

public sealed record MsBuildPropertyEvaluationDto(
    string ProjectName,
    string ProjectPath,
    string PropertyName,
    string? EvaluatedValue);

public sealed record MsBuildItemEvaluationDto(
    string ProjectName,
    string ProjectPath,
    string ItemType,
    IReadOnlyList<MsBuildItemInstanceDto> Items);

public sealed record MsBuildItemInstanceDto(
    string Include,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record MsBuildPropertiesDumpDto(
    string ProjectName,
    string ProjectPath,
    IReadOnlyDictionary<string, string> Properties);
