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
    ServerUpdateInfoDto? Update,
    PathBoundaryDto? PathBoundary);

/// <summary>
/// sanctioned-roots-empty-boundary-fails-silently-until-first-call: filesystem-boundary state, so
/// an unconfigured host is diagnosable from <c>server_info</c> instead of only from the first
/// path call's rejection. Deliberately reports the root COUNT and never the root paths — the
/// boundary is a security control and its contents are not client business.
/// <para>
/// The field is null on unit-test paths that construct <c>ServerTools</c> without booting the
/// host (the runtime <c>SecurityOptionsSnapshot</c> is unset), matching
/// <see cref="SurfaceRegisteredCountsDto"/>'s convention. Null means "unknown", never
/// "unconfigured".
/// </para>
/// </summary>
/// <param name="ConfiguredRootCount">Number of entries parsed from <c>ROSLYNMCP_SANCTIONED_ROOTS</c>.</param>
/// <param name="FailOpen">Whether <c>ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN</c> is set. Only rescues the
/// zero-root case; it never bypasses a non-empty boundary.</param>
/// <param name="Enforcing">True when a non-empty boundary is actively constraining paths.</param>
/// <param name="Hint">Null when the configuration is coherent; otherwise an actionable one-liner
/// naming the variable to set. Non-null means path-taking tools are rejecting (or unbounded).</param>
public sealed record PathBoundaryDto(
    int ConfiguredRootCount,
    bool FailOpen,
    bool Enforcing,
    string? Hint);

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
/// <c>host.Build()</c>. <see cref="ParityOk"/> is true when SDK registrations match the selected
/// tier expectation and reflected declarations match the complete catalog for every surface.
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
/// Update-availability subtree. Present on <c>server_info</c> so callers can inspect the
/// latest-version check status even when no newer version is available. The
/// <see cref="Latest"/> field is non-null only when the registry version is STRICTLY GREATER
/// than the running build (server-info-update-latest-inverted contract).
/// <see cref="CheckStatus"/> and <see cref="LastCheckedAt"/> distinguish pending, failed,
/// timed-out, and completed checks even when <see cref="Latest"/> is null.
/// </summary>
public sealed record ServerUpdateInfoDto(
    string Current,
    string? Latest,
    bool UpdateAvailable,
    string? Command,
    string CheckStatus,
    string? LastCheckedAt);

/// <summary>
/// tool-output-schema-batch-1-server-info-workspace: typed shape for the
/// <c>server_heartbeat</c> tool response. Single field carrying the
/// <see cref="ConnectionStateDto"/>; the wrapper exists to give the response an object root
/// (per MCP 2025-06-18, <c>structuredContent</c> requires an object body).
/// </summary>
public sealed record ServerHeartbeatDto(ConnectionStateDto Connection);

/// <summary>
/// tool-output-schema-batch-1-server-info-workspace: typed shape for <c>workspace_list</c>'s
/// default (verbose=false) response. The advertised tool schema is a union of this shape and
/// <see cref="WorkspaceListVerboseDto"/> so both modes satisfy the public contract.
/// </summary>
public sealed record WorkspaceListDto(int Count, IReadOnlyList<WorkspaceStatusSummaryDto> Workspaces);

/// <summary>
/// Typed shape for <c>workspace_list(verbose=true)</c>. Keeping the verbose envelope explicit
/// prevents its structured content from drifting away from the union advertised by tools/list.
/// </summary>
public sealed record WorkspaceListVerboseDto(int Count, IReadOnlyList<WorkspaceStatusDto> Workspaces);
