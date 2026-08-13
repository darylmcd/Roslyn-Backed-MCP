# sdk-2x-upgrade — ModelContextProtocol 1.4.1 → 2.x major upgrade

**row:** `sdk-2x-upgrade` · **pri:** `High` · **size:** `L` · **deps:** `mcp-logging-stderr-otel-migration`

## Anchors

- `Directory.Packages.props:6` (ModelContextProtocol 1.4.1 pin)
- `src/RoslynMcp.Host.Stdio/Program.cs` (registration + `WithRequestFilters` pipeline)
- `src/RoslynMcp.Host.Stdio/McpLoggingProvider.cs` (MCP9005 obsolescence target — see deps)
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs` (envelope vs SDK raw structured emission)
- `tests/RoslynMcp.Tests/` incl. `Catalog/Snapshots/catalog-v2.3.1.json` re-baseline

## Acceptance

- [ ] ModelContextProtocol ≥2.1.0; protocol 2026-07-28 negotiated with `server/discover` + legacy-initialize fallback verified over stdio
- [ ] csharp-sdk#844 binding-stage exception-propagation contract re-verified for the call-tool filter pipeline
- [ ] Structured-content behavior reconciled (SDK raw emission vs bespoke StructuredCallToolFilter envelope); JSON-RPC error-code assertions re-baselined (-32020..-32022 renumbering)
- [ ] Contract-care breaks bundled into this one major window: drop the deprecated logging capability, `apply_composite_preview` rename with alias (row `apply-composite-preview-destructive-misnomer`), any consolidation renames ready to ride along
- [ ] ADR + CHANGELOG migration note per public-repo breaking-change posture

## Evidence

- 1.4.1 vs 2.1.0 (2.0.0 → 2026-07-28, 2.1.0 → 2026-08-05); MCP spec two revisions stale; 6 of 10 conformance fails are SDK-blocked — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §4, §5

## Context

L umbrella — split into prep/upgrade/re-baseline children at plan time. Blocked on `mcp-logging-stderr-otel-migration` (MCP9005 turns the logging bridge into a build break under warnings-as-errors; migrate first, then upgrade). Unlocks `tasks-extension-slow-ops`, `caching-hints-tools-list`, MRTR elicitation, JSON Schema 2020-12. Risks are release-note-derived, not compile-verified.
