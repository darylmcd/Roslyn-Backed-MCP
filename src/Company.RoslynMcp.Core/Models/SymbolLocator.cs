namespace Company.RoslynMcp.Core.Models;

public sealed record SymbolLocator(
    string? FilePath,
    int? Line,
    int? Column,
    string? SymbolHandle,
    string? MetadataName)
{
    public static SymbolLocator BySource(string filePath, int line, int column) =>
        new(filePath, line, column, null, null);

    public static SymbolLocator ByHandle(string symbolHandle) =>
        new(null, null, null, symbolHandle, null);

    public static SymbolLocator ByMetadataName(string metadataName) =>
        new(null, null, null, null, metadataName);

    public bool HasSourceLocation =>
        !string.IsNullOrWhiteSpace(FilePath) && Line.HasValue && Column.HasValue;

    public bool HasHandle => !string.IsNullOrWhiteSpace(SymbolHandle);

    public bool HasMetadataName => !string.IsNullOrWhiteSpace(MetadataName);

    public void Validate()
    {
        if (HasHandle || HasMetadataName || HasSourceLocation)
        {
            return;
        }

        throw new ArgumentException(
            "Provide either a file path with line/column, a symbol handle, or a metadata name.");
    }
}
