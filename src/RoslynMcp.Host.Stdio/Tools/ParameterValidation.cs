namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Validates constrained tool parameters to improve LLM precision and provide
/// actionable error messages when invalid values are supplied.
/// </summary>
internal static class ParameterValidation
{
    private static readonly string[] SeverityValues = ["Error", "Warning", "Info", "Hidden"];
    private static readonly string[] TypeKindValues = ["class", "interface", "record", "enum"];
    private static readonly string[] BulkReplaceScopeValues = ["parameters", "fields", "all"];

    /// <summary>Validates severity filter if provided.</summary>
    public static void ValidateSeverity(string? severity)
    {
        if (severity is not null && !SeverityValues.Contains(severity, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Invalid severity '{severity}'. Must be one of: {string.Join(", ", SeverityValues)}");
    }

    /// <summary>Validates type kind for scaffolding.</summary>
    public static void ValidateTypeKind(string typeKind)
    {
        if (!TypeKindValues.Contains(typeKind, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Invalid type kind '{typeKind}'. Must be one of: {string.Join(", ", TypeKindValues)}");
    }

    /// <summary>Validates bulk replace scope if provided.</summary>
    public static void ValidateBulkReplaceScope(string? scope)
    {
        if (scope is not null && !BulkReplaceScopeValues.Contains(scope, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Invalid scope '{scope}'. Must be one of: {string.Join(", ", BulkReplaceScopeValues)}");
    }
}
