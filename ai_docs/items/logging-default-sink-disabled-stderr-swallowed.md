# logging-default-sink-disabled-stderr-swallowed — Enable the bounded file sink by default for installed users

**row:** `logging-default-sink-disabled-stderr-swallowed` · **pri:** `High` · **size:** `M` · **deps:** `logging-retention-destroys-repro-evidence`

## Anchors

- `src/RoslynMcp.Host.Stdio/Diagnostics/ServerObservability.cs`
- `.claude-plugin/mcp.json`
- `docs/setup.md`
- `docs/stdio-client-integration.md`

## Acceptance

- [ ] Default (variable unset) installs the file sink; `disabled` remains an explicit opt-out; `stderr` unchanged.
- [ ] Behaviour change recorded per contract-care: ADR in docs/decisions/ + CHANGELOG migration note; docs/setup.md table default updated.
- [ ] Startup line (or heartbeat) reports the resolved log file path so an agent can locate it without reading docs.
- [ ] Runs only after the retention row lands so default-on cannot grow unbounded.

## Evidence

- Logging audit 20260930-1340 (dimension A6); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

ServerObservabilityOptions.Parse maps null/empty to Disabled (ServerObservability.cs:21); Program.cs:45 only installs the file provider when the variable says `file`. The plugin launch config (.claude-plugin/mcp.json) sets only ROSLYNMCP_SANCTIONED_ROOTS. For an MCP server stderr is usually swallowed by the host, so in the shipped default configuration there is no agent-reachable sink: the first failure leaves no record and the agent must learn the env var, restart the server and reproduce. The file sink itself is sound (stable per-user path, V1/V2/V7 all succeeded once enabled).

Audit verdict "sufficient (agentic)" is conditional on exactly this fix.

**Approach:** Flip the Parse default to File; add the resolved path to the Startup record and server_info. Do not add a second default-on sink. Gate behind the retention row (deps).

**Counterargument:** Public published artifact: default-on disk writes change behaviour and put local paths on disk unasked. Mitigation: bounded size + retention row + documented opt-out; the records are already designed operator-safe (no exception text).
