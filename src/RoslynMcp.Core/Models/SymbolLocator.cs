namespace RoslynMcp.Core.Models;

/// <summary>
/// Identifies a symbol by source location, stable handle, or metadata name.
/// </summary>
public sealed record SymbolLocator(
    string? FilePath,
    int? Line,
    int? Column,
    string? SymbolHandle,
    string? MetadataName)
{
    /// <summary>
    /// Creates a symbol locator from a source file path and position.
    /// </summary>
    public static SymbolLocator BySource(string filePath, int line, int column) =>
        new(filePath, line, column, null, null);

    /// <summary>
    /// Creates a symbol locator from a serialized symbol handle.
    /// </summary>
    public static SymbolLocator ByHandle(string symbolHandle) =>
        new(null, null, null, symbolHandle, null);

    /// <summary>
    /// Creates a symbol locator from a metadata name.
    /// </summary>
    public static SymbolLocator ByMetadataName(string metadataName) =>
        new(null, null, null, null, metadataName);

    /// <summary>
    /// Gets a value indicating whether the locator identifies a symbol by source position.
    /// </summary>
    public bool HasSourceLocation =>
        !string.IsNullOrWhiteSpace(FilePath) && Line.HasValue && Column.HasValue;

    /// <summary>
    /// Gets a value indicating whether the locator identifies a symbol by serialized handle.
    /// </summary>
    public bool HasHandle => !string.IsNullOrWhiteSpace(SymbolHandle);

    /// <summary>
    /// Gets a value indicating whether the locator identifies a symbol by metadata name.
    /// </summary>
    public bool HasMetadataName => !string.IsNullOrWhiteSpace(MetadataName);

    /// <summary>
    /// Validates that at least one symbol identification strategy has been provided.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the locator does not contain a source location, handle, or metadata name.</exception>
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
