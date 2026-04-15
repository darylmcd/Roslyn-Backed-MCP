namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Configuration options for preview store entry limits.
/// </summary>
public sealed record PreviewStoreOptions
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

    /// <summary>
    /// Item 6 (v1.18, <c>preview-token-cross-process-handoff</c>): when set, composite preview
    /// tokens are persisted to disk under this directory so a separate <c>roslynmcp</c> process
    /// (e.g. an apply-phase sub-agent) can redeem them. <see langword="null"/> = in-memory only
    /// (default, preserves pre-v1.18 behavior). Set via <c>ROSLYNMCP_PREVIEW_PERSIST_DIR</c>.
    /// <para>
    /// Currently applies to <see cref="CompositePreviewStore"/> only — full Roslyn-Solution
    /// previews stored in <see cref="PreviewStore"/> are not portable across processes.
    /// </para>
    /// </summary>
    public string? PersistDirectory { get; init; }
}
