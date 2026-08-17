# atomic-file-cleanup-error-detail-redaction — Sanitize temp-file cleanup diagnostics

**row:** `atomic-file-cleanup-error-detail-redaction` · **pri:** `High` · **size:** `S` · **deps:** `public-exception-detail-policy,mcp-logging-stderr-otel-migration,composite-apply-error-detail-redaction`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs` — `AtomicFileWriter.TryDeleteTemp`.
- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`

## Acceptance

- [ ] Preserve best-effort temp cleanup and propagation of the original write failure; cleanup failure never replaces or masks the primary exception.
- [ ] Emit only a stable cleanup category, sanitized target identity, and correlation through the shared diagnostic projection; never log raw exception message, absolute temp/target path, or secret-bearing value.
- [ ] One locked-temp/path-sentinel logger regression proves the cleanup warning is observable and neither stderr nor configured sink contains the sentinel or absolute paths.

## Evidence

- `AtomicFileWriter.TryDeleteTemp` currently logs the caught exception plus full `TempPath` and `TargetPath` after cleanup fails.
- The public partial-apply row owns the mid-loop result contract; this row isolates cleanup-only observability as a separate regression shape.
