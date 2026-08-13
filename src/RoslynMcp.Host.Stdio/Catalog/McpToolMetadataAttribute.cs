namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// Item 3: structured surface metadata attached next to the <c>[McpServerTool]</c> attribute.
/// Used by the <c>SurfaceCatalogTests</c> metadata-consistency check to assert that every
/// catalog entry agrees with its declaring method's annotation — eliminates silent drift
/// between <see cref="ServerSurfaceCatalog.Tools"/> and the <c>[McpServerTool]</c> surface.
/// </summary>
/// <remarks>
/// <para>
/// tool-output-schema-infrastructure: the optional <see cref="OutputSchemaTypeRef"/> parameter
/// names the DTO record whose JSON-Schema representation describes a tool's
/// <c>structuredContent</c> channel per MCP 2025-06-18 § Tools / Structured Content. Tools that
/// do not set this parameter remain text-only and unchanged; tools that do are emitted via
/// both the <c>content[].text</c> and <c>structuredContent</c> channels by
/// <c>StructuredCallToolFilter</c>. <see cref="SurfaceRegistrationPolicy"/> also projects each
/// opted-in schema into the SDK's advertised protocol-tool object.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpToolMetadataAttribute : Attribute
{
    public McpToolMetadataAttribute(
        string category,
        string supportTier,
        bool readOnly,
        bool destructive,
        string summary,
        Type? outputSchemaTypeRef = null)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
        SupportTier = supportTier ?? throw new ArgumentNullException(nameof(supportTier));
        ReadOnly = readOnly;
        Destructive = destructive;
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        OutputSchemaTypeRef = outputSchemaTypeRef;
    }

    public string Category { get; }
    public string SupportTier { get; }
    public bool ReadOnly { get; }
    public bool Destructive { get; }
    public string Summary { get; }

    /// <summary>
    /// tool-output-schema-infrastructure: optional CLR type whose JSON-Schema (generated via
    /// <see cref="System.Text.Json.Schema.JsonSchemaExporter"/>) describes the tool's
    /// <c>structuredContent</c> channel. <see langword="null"/> means the tool returns
    /// text-only content (legacy contract, unchanged behavior). When set, the dispatch
    /// pipeline emits both <c>content[].text</c> (serialized JSON of the same payload) and
    /// the <c>structuredContent</c> channel — the MCP spec requires both channels coexist
    /// when <c>structuredContent</c> is populated.
    /// </summary>
    public Type? OutputSchemaTypeRef { get; }
}
