namespace RoslynMcp.Core.Models;

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
    Constructor,
    DIRegistration,
    StaticMemberAccess,
    /// <summary>
    /// The reference is inside an XML doc comment (<c>&lt;see cref="X"/&gt;</c>,
    /// <c>&lt;seealso cref="X"/&gt;</c>, <c>&lt;exception cref="X"/&gt;</c>). Previously
    /// buckets under <see cref="Other"/>; split out for <c>find-type-usages-cref-classification</c>
    /// so doc-comment references are visually distinguishable from real consumers.
    /// </summary>
    Documentation,
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
