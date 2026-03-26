namespace RoslynMcp.Core.Models;

/// <summary>
/// Result of consumer/dependency analysis for a type or interface.
/// </summary>
public sealed record ConsumerAnalysisDto(
    SymbolDto TargetSymbol,
    IReadOnlyList<TypeConsumerDto> Consumers,
    string Summary);

/// <summary>
/// A type that depends on the analyzed symbol.
/// </summary>
public sealed record TypeConsumerDto(
    string TypeName,
    string FullyQualifiedName,
    string FilePath,
    int Line,
    string ProjectName,
    IReadOnlyList<string> DependencyKinds);
