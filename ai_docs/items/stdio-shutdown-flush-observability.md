# stdio-shutdown-flush-observability — Make shutdown flush failures observable

**row:** `stdio-shutdown-flush-observability` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs` (process-exit and terminal stdout flush catches)
- `tests/RoslynMcp.Tests/StdioFlushOnExitTests.cs`

## Acceptance

- [ ] Preserve the no-throw shutdown guarantee without silently discarding every flush exception.
- [ ] Emit a bounded stderr or injected diagnostic signal that cannot corrupt the MCP stdout transport.
- [ ] Cover both successful flush and transport-gone failure paths.

## Evidence

- The 2026-07-26 code-quality review found three blanket stdout flush catches that intentionally suppress failures but provide no secondary observability.
