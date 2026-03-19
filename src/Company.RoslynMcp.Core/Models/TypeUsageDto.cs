namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Classifies how a type is used at a given source location.
/// </summary>
public enum TypeUsageClassification
{
    MethodReturnType,
    MethodParameter,
    PropertyType,
    LocalVariable,
    FieldType,
    GenericArgument,
    BaseType,
    Cast,
    TypeCheck,
    ObjectCreation,
    Other
}

/// <summary>
/// Represents a source usage of a type.
/// </summary>
public sealed record TypeUsageDto(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string? ContainingMember,
    string? PreviewText,
    TypeUsageClassification Classification);
