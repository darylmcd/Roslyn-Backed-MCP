# composite-apply-orchestrator-extract-atomic-file-writer — Move AtomicFileWriter out of CompositeApplyOrchestrator.cs into its own file

**row:** `composite-apply-orchestrator-extract-atomic-file-writer` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:208`

## Acceptance

- [ ] AtomicFileWriter lives in its own file with unchanged behavior; existing composite-apply and atomic-writer tests pass without edits to assertions.

## Evidence

- Two unrelated types in one file; static finding.
