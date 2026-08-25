# observability-file-sink-full-stream — Add an opt-in JSON-lines file sink for the full ILogger stream

**row:** `observability-file-sink-full-stream` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Diagnostics/ServerObservability.cs:7`
- `src/RoslynMcp.Host.Stdio/Program.cs:43`
- `src/RoslynMcp.Host.Stdio/Diagnostics/HostProcessMetadataStore.cs:266`
- `tests/RoslynMcp.Host.Stdio.Tests/ServerObservabilitySinkTests.cs`

## Acceptance

- [ ] An env-gated file sink writes JSON-lines records (ts UTC ISO-8601 ms, level, category, eventId, message, correlationId when present) for the FULL ILogger stream — not just unexpected exceptions — under the HostProcessMetadataStore per-user root (`<root>/logs/`).
- [ ] Default remains off; enabling requires no rebuild; stderr behavior unchanged; stdout purity analyzer still green.
- [ ] A foreign-CWD launch writes nothing into the working directory (preserve current clean behavior — verified live 2026-08-25).

## Evidence

- Live audit: stderr-only prose stream, host-swallowed in real deployments; JSON sink exception-only + default `Disabled` — see `ai_docs/audits/20260825-1440/report.md` (A6/A1) + `run1-stderr.log`.

## Context

Extend `ServerObservabilitySinkKind` (`disabled|stderr|file`) or add `ROSLYNMCP_LOG_FILE`; register an `ILoggerProvider` targeting `<metadata-store-root>/logs/`, reusing `HostProcessMetadataStore` root resolution (per-user, XDG-aware, OneDrive-safe). Published + foreign-CWD deployment context: the sink path must be install-stable and CWD-independent.

## Notes

- PR #1253 / ADR 0003 deliberately retired MCP protocol logging (`notifications/message`) as unsafe — do NOT reintroduce it; stay on the operator-side sink path.
- Whether the unexpected-exception sink should default to `stderr` instead of `Disabled` is a separate product decision (audit Info observation).
- Bound file growth (size-capped rotation) but retention must outlast a debug session (A12).
