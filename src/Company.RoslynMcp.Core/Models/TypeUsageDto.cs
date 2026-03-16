namespace Company.RoslynMcp.Core.Models;

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

public sealed record TypeUsageDto(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string? ContainingMember,
    string? PreviewText,
    TypeUsageClassification Classification);
