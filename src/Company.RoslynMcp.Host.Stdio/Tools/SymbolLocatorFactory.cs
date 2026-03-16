using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Host.Stdio.Tools;

internal static class SymbolLocatorFactory
{
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
