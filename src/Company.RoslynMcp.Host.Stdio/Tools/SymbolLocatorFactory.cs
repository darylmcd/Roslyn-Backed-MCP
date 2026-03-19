using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Host.Stdio.Tools;

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
    /// </remarks>
    /// <exception cref="System.ArgumentException">Thrown when none of the identification parameters are supplied.</exception>
    public static SymbolLocator Create(
        string? filePath = null,
        int? line = null,
        int? column = null,
        string? symbolHandle = null,
        string? metadataName = null)
    {
        if (!string.IsNullOrWhiteSpace(symbolHandle))
            return SymbolLocator.ByHandle(symbolHandle);

        if (!string.IsNullOrWhiteSpace(metadataName))
            return SymbolLocator.ByMetadataName(metadataName);

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
            return SymbolLocator.BySource(filePath, line.Value, column.Value);

        throw new ArgumentException("Provide either filePath/line/column, symbolHandle, or metadataName.");
    }
}
