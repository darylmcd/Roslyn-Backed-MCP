# interface-extraction-catch-chain-dead-rethrow — drop the now-dead InvalidOperationException rethrow and cover the narrowed catch

**row:** `interface-extraction-catch-chain-dead-rethrow` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:415-427`
- `tests/RoslynMcp.Tests/ExtractInterfaceSemanticUsingsTests.cs`

## Acceptance

- [ ] Remove the now-dead `catch (InvalidOperationException) { throw; }` at `InterfaceExtractionService.cs:415` — verify the conflict-throw test still passes, proving the deliberate `InvalidOperationException` propagates past the narrowed filter unaided.
- [ ] Add a test that injects an `ICompilationCache` whose `GetCompilationAsync` throws a non-IO exception (e.g. `KeyNotFoundException`) and asserts `PreviewExtractInterfaceAsync` propagates it rather than skipping the conflict check, so the narrowing cannot silently regress to bare `Exception`.

## Evidence

Traced at the code level during code-quality review of `interface-extraction-conflict-check-hardening` (PR landing this row's parent fix): `InvalidOperationException` derives from `SystemException`, not `IOException`/`UnauthorizedAccessException`, so the `catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)` filter introduced by that PR can never match it — clause 1 (`catch (InvalidOperationException) { throw; }`) is unreachable-in-effect after that diff. Separately, `rg ICompilationCache tests/` shows the only conflict-check coverage added by that PR is the throw path (line 410); no test exercises the catch clause itself, and a substitutable-cache pattern already exists at `CompilationCacheAdoptionTests.cs:957` (`CountingAdhocCompilationCache`) to model the new test on.

## Context

Spin-off from the `interface-extraction-conflict-check-hardening` row's code-quality review (top-n-remediation run 20260810T233007Z).
