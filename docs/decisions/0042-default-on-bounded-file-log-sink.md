# ADR 0042: Default-on bounded file log sink

Status: Accepted, 2026-11-20 (operator decision; additive wire change plus a new default-on local disk write, shipped in a minor release).

## Context

`ROSLYNMCP_OBSERVABILITY_SINK` defaulted to `disabled`. An MCP host usually swallows a server's stderr, so in the shipped configuration (the plugin launch config sets only `ROSLYNMCP_SANCTIONED_ROOTS`) no sink was reachable by an agent: the first failure left no record, and the agent had to learn the variable, restart the server and reproduce. The file sink itself was sound (stable per-user path, bounded segments), and its retention gap closed when the dead-process startup sweep landed (`logging-retention-dead-pid-sweep`), which bounds disk use across processes.

The file sink writes the full `ILogger` stream. Information and Warning messages carry absolute local paths (a workspace path a tool resolved, a path the sanctioned-root boundary rejected), so a default-on sink puts local paths on disk unasked. The packed README had stated that the server wrote only `host-process.json`.

## Decision

| `ROSLYNMCP_OBSERVABILITY_SINK` | Behavior |
|---|---|
| unset, empty, whitespace | `file` (changed from `disabled`) |
| `file` | Unchanged: bounded per-process JSON-lines files under the per-user state directory |
| `stderr` | Unchanged |
| `disabled` | Explicit opt-out: no log files |
| anything else | Unchanged: startup fails without echoing the value |

- The bounded sink is the only default sink. There is no second, path-free default sink: one stream the agent can rely on beats two partial ones.
- The records stay local and are never transmitted. Exception messages and stack text are still never written; absolute paths in Information/Warning messages are accepted.
- `server_info` gains an additive `observability` object `{ sink, logFile, logDirectory }`. `logFile` and `logDirectory` are ABSOLUTE per-user paths, null unless the sink is `file`; the whole object is null on a host that was not booted, meaning unknown. This is the one filesystem path `server_info` reports (contrast `pathBoundary`, which reports a root count and never paths): the log is the agent's only reachable diagnostic channel, so the operator accepted exposing its location to the client.
- The host logs one `ObservabilitySinkConfigured` record (event id 1005; fields `sink`, `logFile`, `logDirectory`) at startup, to stderr and, with the file sink, to the file.
- The retention variables (`ROSLYNMCP_OBSERVABILITY_MAX_SEGMENT_BYTES`, `MAX_SEGMENTS`, `RETENTION_DAYS`, `MAX_TOTAL_BYTES`) stay sink-scoped and fail-fast, the repository convention for invalid environment values. They are parsed whenever the sink is `file`, which is now the default, and never when the sink is `disabled` or `stderr`.

## Consequences

- Behavior: every install that did not set the variable now writes bounded JSON lines under `%LOCALAPPDATA%/roslyn-mcp/logs/` (Windows) or `$XDG_STATE_HOME/roslyn-mcp/logs/` (elsewhere). Disk use is at most `(MAX_SEGMENTS + 1) x MAX_SEGMENT_BYTES` (20 MiB at the defaults) per running host; the startup sweep trims files of finished hosts by age (14 days) and to a 64 MiB total, never deleting a running host's files, so N concurrent hosts hold up to N x 20 MiB.
- Migration: set `ROSLYNMCP_OBSERVABILITY_SINK=disabled` to restore the previous behavior. A host that previously started with an invalid `ROSLYNMCP_OBSERVABILITY_MAX_*` or `RETENTION_DAYS` value (ignored while the sink was disabled) now fails startup naming that variable; fix or unset the value, or set the sink to `disabled`.
- Wire: `server_info.observability` is additive (`docs/release-policy.md`: consumers must ignore unknown fields). The `server_info` output schema gains one nullable property.
- A log directory that cannot be created or written never fails a launch: the provider reports one fixed stderr warning and the sweep isolates its own failures.
- Privacy documentation (`src/RoslynMcp.Host.Stdio/README.md`, `docs/stdio-client-integration.md`, `docs/setup.md`) states the default, the paths in records and the opt-out. ADR 0003 only tells clients to opt into the `stderr` sink for secret-safe events; that stays accurate, and the file sink is now on without opting in.
