# code-fix-provider-failure-silent-fallback — Surface provider failure before fallback

**row:** `code-fix-provider-failure-silent-fallback` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:811`
- (new) `tests/RoslynMcp.Tests/CodeFixProviderFailureTests.cs`

## Acceptance

- [ ] A code-fix provider registration failure remains distinct from successful registration with no action; report the unexpected failure through the existing diagnostics contract and prevent silent legacy fallback from masking it.
- [ ] Observe a regression fail against the old catch-and-null behavior and pass after the correct handling; retain genuine no-action fallback, cancellation propagation, deterministic action selection and secret-safe tool envelopes.

## Evidence

- Verified main `6cf842a8b7ea8ac56d7706e9c91c451d1852d9b4`: `CaptureFirstActionAsync` calls `await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);` then `catch (Exception ex) when (ex is not OperationCanceledException) { return null; }` at `RefactoringService.cs:811-817`.
- `rg -n CaptureFirstActionAsync ai_docs/items` found no existing tracking row. Re-derive caller/fallback behavior and all instances before implementation.

## Context

- Found during cold diagnosis of fix-all equivalence key behavior; this callback has a different failure mechanism from keyless FixAll registration and belongs in its own row.
- Use existing provider registration seams; do not add duplicate infrastructure or publish raw provider exception messages.
- backlog: sync ai_docs/backlog.md.
