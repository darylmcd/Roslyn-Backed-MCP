namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// Structured surface metadata attached next to the <c>[McpServerTool]</c> attribute.
/// Used by the <c>SurfaceCatalogTests</c> metadata-consistency check to assert that every
/// catalog entry agrees with its declaring method's annotation — eliminates silent drift
/// between <see cref="ServerSurfaceCatalog.Tools"/> and the <c>[McpServerTool]</c> surface.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpToolMetadataAttribute : Attribute
{
    public McpToolMetadataAttribute(
        string category,
        string supportTier,
        bool readOnly,
        bool destructive,
        string summary)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
        SupportTier = supportTier ?? throw new ArgumentNullException(nameof(supportTier));
        ReadOnly = readOnly;
        Destructive = destructive;
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    public string Category { get; }
    public string SupportTier { get; }
    public bool ReadOnly { get; }
    public bool Destructive { get; }
    public string Summary { get; }
}
