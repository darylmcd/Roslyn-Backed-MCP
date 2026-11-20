# logging-sink-env-parsing-and-event-name-style — Settle log event-name style and pid-reuse handling for the file sink

**row:** `logging-sink-env-parsing-and-event-name-style` · **pri:** `Low` · **size:** `M` · **verified:** `96629cae1d46b5daac93e90539ff5bc624f57daa`

## Anchors

- `src/RoslynMcp.Core/Services/LogEvents.cs:44`
- `src/RoslynMcp.Host.Stdio/Diagnostics/JsonLinesSegmentWriter.cs:9`

## Acceptance

- [ ] Normalise event-name style in `LogEvents.cs`: `TransportDisconnectedName` (`:47`), `RetentionSweepCompletedName` (`:49`) and `RetentionSweepFailedName` (`:51`) carry a `Log` prefix while `UnexpectedFailureName` (`:45`) and the tool/workspace names do not; either rename (event names are published, check the wire/doc contract first) or document the rule beside the constants.
- [ ] Decide whether to embed the process start time in the file names `roslyn-mcp-{pid}.jsonl` (`JsonLinesSegmentWriter.cs:9`) so a reused pid is detectable; today a reused pid keeps old files (documented over-retention at `docs/stdio-client-integration.md:98`). Record the decision (ADR line or doc note) and implement or close it.
- [ ] Env parsing: `ROSLYNMCP_OBSERVABILITY_RETENTION_DAYS` / `_MAX_TOTAL_BYTES` are parsed in `JsonLinesRetentionOptions.Parse` (`src/RoslynMcp.Host.Stdio/Diagnostics/JsonLinesRetentionOptions.cs:71`), not inline in `Program.cs`; `Program.cs:50` only passes `ReadEnv`. Confirm nothing else parses `ROSLYNMCP_OBSERVABILITY_*` inline, then drop this bullet.

## Evidence

- Event ids 1003 and 1004 were added by PR 2169 and kept the mixed style of 1002.
- Re-derived at 96629cae: the "parsed inline in Program.cs" premise did not hold, hence the third bullet.
