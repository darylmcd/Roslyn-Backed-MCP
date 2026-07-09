# security-options-fail-open-default — Default PathValidationFailOpen to fail-closed

**row:** `security-options-fail-open-default` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SecurityOptions.cs:14`

## Acceptance

- [ ] PathValidationFailOpen defaults to false (fail-closed) when unset.
- [ ] A test asserts that a roots-lookup failure blocks the write/edit rather than allowing it.

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S03c-roslyn-build-test-services::DG5-security-data
