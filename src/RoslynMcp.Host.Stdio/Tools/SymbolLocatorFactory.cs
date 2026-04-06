using RoslynMcp.Core.Models;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Creates a <see cref="SymbolLocator"/> from the optional tool parameters supplied by an MCP caller,
/// picking the most specific identification strategy available.
/// </summary>
internal static class SymbolLocatorFactory
{
    /// <summary>
    /// Builds a <see cref="SymbolLocator"/> from the provided parameters.
    /// </summary>
    /// <remarks>
    /// Priority order: <paramref name="symbolHandle"/> (most stable) &gt;
    /// <paramref name="metadataName"/> &gt; <paramref name="filePath"/>/<paramref name="line"/>/<paramref name="column"/>.
    /// The error message reflects only those identification strategies that the calling tool exposes;
    /// callers that omit <paramref name="metadataName"/> can pass <c>supportsMetadataName: false</c>
    /// to keep the message accurate (BUG-002).
    /// </remarks>
    /// <exception cref="System.ArgumentException">
    /// Thrown when none of the identification parameters are supplied, or when a partial source location
    /// is provided (file path / line / column without all three required values, BUG-001).
    /// </exception>
    public static SymbolLocator Create(
        string? filePath = null,
        int? line = null,
        int? column = null,
        string? symbolHandle = null,
        string? metadataName = null,
        bool supportsMetadataName = true)
    {
        if (!string.IsNullOrWhiteSpace(symbolHandle))
            return SymbolLocator.ByHandle(symbolHandle);

        if (!string.IsNullOrWhiteSpace(metadataName))
            return SymbolLocator.ByMetadataName(metadataName);

        var hasFilePath = !string.IsNullOrWhiteSpace(filePath);
        if (hasFilePath && line.HasValue && column.HasValue)
            return SymbolLocator.BySource(filePath!, line.Value, column.Value);

        // BUG-001: When the caller supplied a partial source location (e.g. filePath + line, but no
        // column), the previous behavior was to fall through to the generic "provide one of..." error,
        // which obscured the real problem. Surface the partial-location case explicitly so the caller
        // sees the missing field instead of a vague suggestion.
        if (hasFilePath || line.HasValue || column.HasValue)
        {
            var missing = new List<string>(3);
            if (!hasFilePath) missing.Add("filePath");
            if (!line.HasValue) missing.Add("line");
            if (!column.HasValue) missing.Add("column");
            throw new ArgumentException(
                $"Source location is incomplete. Provide all of filePath, line, and column. Missing: {string.Join(", ", missing)}. " +
                "Note: column must point at the symbol identifier token (e.g., the interface name), not the start of the line.");
        }

        // BUG-002: Tailor the error message to the strategies that this caller actually exposes.
        // Tools without a metadataName parameter should not have it suggested in their errors.
        throw new ArgumentException(
            supportsMetadataName
                ? "Provide one of: filePath with line and column, symbolHandle, or metadataName."
                : "Provide either filePath with line and column, or symbolHandle.");
    }
}
