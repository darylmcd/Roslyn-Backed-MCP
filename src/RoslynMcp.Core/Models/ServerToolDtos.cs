namespace RoslynMcp.Core.Models;

/// <summary>
/// tool-output-schema-batch-1-server-info-workspace: typed shape for the <c>server_info</c>
/// tool response. Mirrors the historical anonymous-object shape so existing clients see no
/// wire-format change; the typed declaration exists so
/// <see cref="System.Text.Json.Schema.JsonSchemaExporter"/> can publish a JSON-Schema for the
/// tool's <c>structuredContent</c> channel per MCP 2025-06-18 § Tools / Structured Content.
/// </summary>
/// <remarks>
/// Property names are PascalCase here; <see cref="System.Text.Json.JsonNamingPolicy.CamelCase"/>
/// in <c>JsonDefaults.Indented</c> rewrites them at serialization time so on-the-wire shape
/// stays camelCase (workspaceCount, productShape, etc.).
/// <para>
/// Nullable fields here intentionally do NOT carry <c>[JsonIgnore(WhenWritingNull)]</c> — the
/// historical anonymous-object shape emitted explicit <c>null</c> values, and existing tests
/// (<c>ServerInfoUpdateLatestTests</c>) assert that exact wire shape. The DTO preserves it.
/// </para>
/// </remarks>
public sealed record ServerInfoDto(
    string Server,
    string Version,
    string ProductShape,
    string Runtime,
    string Os,
    string RoslynVersion,
    int WorkspaceCount,
    string? WorkspaceCountHint,
    ConnectionStateDto Connection,
    string CatalogVersion,
    ServerSurfaceCountsDto Surface,
    ResourceServerNameHintsDto ResourceServerNames,
    IReadOnlyList<string> ProductBoundaries,
    ServerCapabilitiesDto Capabilities,
    ServerUpdateInfoDto? Update);

/// <summary>
/// Surface-count subtree on <see cref="ServerInfoDto"/>. <see cref="Registered"/> is null on
/// unit-test paths that construct <c>ServerTools</c> without booting the host (the runtime
/// <c>SurfaceRegistrationSnapshot</c> is unset).
/// </summary>
public sealed record ServerSurfaceCountsDto(
    SurfaceTierCountsDto Tools,
    SurfaceTierCountsDto Resources,
    SurfaceTierCountsDto Prompts,
    SurfaceRegisteredCountsDto? Registered);

/// <summary>
/// Stable/experimental count pair for one surface dimension (tools, resources, or prompts).
/// </summary>
public sealed record SurfaceTierCountsDto(int Stable, int Experimental);

/// <summary>
/// concurrent-mcp-instances-no-tools: runtime-observed registration counts, captured at
/// <c>host.Build()</c>. <see cref="ParityOk"/> is true when the catalog and the SDK's reflected
/// surface agree on counts.
/// </summary>
public sealed record SurfaceRegisteredCountsDto(int Tools, int Resources, int Prompts, bool ParityOk);

/// <summary>
/// Client-facing resource server-name guidance. MCP resource URIs stay <c>roslyn://...</c>,
/// but some hosts require a separate server handle when reading resources. This hint gives
/// agents the canonical handle and known aliases instead of forcing them to guess.
/// </summary>
public sealed record ResourceServerNameHintsDto(
    string Canonical,
    IReadOnlyList<string> Aliases,
    string ProbeGuidance);

/// <summary>
/// MCP capability flags advertised by the server's <c>initialize</c> response, echoed on
/// <c>server_info</c> for clients that prefer a single readiness call over splitting reads
/// across <c>initialize</c> + a tool invocation.
/// </summary>
public sealed record ServerCapabilitiesDto(bool Tools, bool Resources, bool Prompts, bool Logging, bool Progress);

/// <summary>
/// Update-availability subtree. Only present when the registry probe succeeded; the
/// <see cref="Latest"/> field is non-null only when the registry version is STRICTLY GREATER
/// than the running build (server-info-update-latest-inverted contract). Both nullable fields
/// emit explicit <c>null</c> on the wire (matching the legacy anonymous-object shape) so the
/// existing <c>ServerInfoUpdateLatestTests</c> contract holds.
/// </summary>
public sealed record ServerUpdateInfoDto(
    string Current,
    string? Latest,
    bool UpdateAvailable,
    string? Command);

/// <summary>
/// tool-output-schema-batch-1-server-info-workspace: typed shape for the
/// <c>server_heartbeat</c> tool response. Single field carrying the
/// <see cref="ConnectionStateDto"/>; the wrapper exists to give the response an object root
/// (per MCP 2025-06-18, <c>structuredContent</c> requires an object body).
/// </summary>
public sealed record ServerHeartbeatDto(ConnectionStateDto Connection);

/// <summary>
/// tool-output-schema-batch-1-server-info-workspace: typed shape for <c>workspace_list</c>'s
/// default (verbose=false) response. The verbose-mode payload (full
/// <see cref="WorkspaceStatusDto"/> per workspace) is NOT described by this schema —
/// verbose mode is an opt-in that surfaces the same per-workspace fields as
/// <c>workspace_status verbose=true</c> on each entry.
/// </summary>
public sealed record WorkspaceListDto(int Count, IReadOnlyList<WorkspaceStatusSummaryDto> Workspaces);
