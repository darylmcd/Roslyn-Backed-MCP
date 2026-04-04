namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Configuration options for preview store entry limits.
/// </summary>
public sealed class PreviewStoreOptions
{
    /// <summary>
    /// Gets the maximum number of preview entries allowed per store.
    /// Defaults to 20.
    /// </summary>
    public int MaxEntries { get; init; } = 20;

    /// <summary>
    /// Gets the TTL for preview entries in minutes. Defaults to 5.
    /// </summary>
    public int TtlMinutes { get; init; } = 5;
}
