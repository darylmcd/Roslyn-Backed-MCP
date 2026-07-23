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
    private static readonly string[] ReplaceInvocationScopeValues = ["all"];

    /// <summary>
    /// Upper bound for pagination page size. Guards against unbounded materialization
    /// when a caller supplies an excessively large limit (e.g. int.MaxValue). Chosen with
    /// headroom above every current tool default/clamp (highest observed is 500).
    /// </summary>
    private const int MaxLimit = 1000;

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

    /// <summary>
    /// Validates replace_invocation_preview scope if provided. Only "all" is accepted today —
    /// the parameter is reserved for future scope filters (per-project / changed-files).
    /// </summary>
    public static void ValidateReplaceInvocationScope(string? scope)
    {
        if (scope is not null && !ReplaceInvocationScopeValues.Contains(scope, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Invalid scope '{scope}'. Must be one of: {string.Join(", ", ReplaceInvocationScopeValues)}");
    }

    /// <summary>Validates pagination parameters.</summary>
    public static void ValidatePagination(int offset, int limit)
    {
        if (offset < 0)
            throw new ArgumentException($"Invalid offset '{offset}'. Offset must be greater than or equal to 0.");

        if (limit <= 0)
            throw new ArgumentException($"Invalid limit '{limit}'. Limit must be greater than 0.");

        if (limit > MaxLimit)
            throw new ArgumentException($"Invalid limit '{limit}'. Limit must not exceed {MaxLimit}.");
    }

    /// <summary>
    /// Validates a batch/array-size bound. Unlike <see cref="ValidatePagination"/> (an offset/limit
    /// pair), this guards the length of a caller-supplied collection against a per-tool maximum so a
    /// documented batch cap (e.g. "max 50") is actually enforced instead of silently accepted.
    /// </summary>
    public static void ValidateBulkSize(int count, int maxCount, string paramName)
    {
        if (count > maxCount)
            throw new ArgumentException(
                $"Too many items ({count}) for '{paramName}'. Must not exceed {maxCount}.", paramName);
    }
}
