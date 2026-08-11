# compilation-cache-cancellation-test-contract-drift — align CompilationCache cancellation tests with the documented OperationCanceledException contract and refresh the now-vacuous analyzer poisoning test

**row:** `compilation-cache-cancellation-test-contract-drift` · **pri:** `Medium` · **size:** `M` · **deps:** `compilation-cache-wire-group-c-consumer`

## Anchors

- `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs:71`
- `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs:86-110`
- `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs:133`
- `src/RoslynMcp.Roslyn/Services/CompilationCache.cs:124-136`
- `src/RoslynMcp.Roslyn/Contracts/ICompilationCache.cs:35`

## Acceptance

- [ ] The four `Assert.ThrowsExactlyAsync<TaskCanceledException>` assertions assert the contract's documented `OperationCanceledException` (assignable, not exact); `CompilationCache`'s two already-canceled guards then use `ct.ThrowIfCancellationRequested()` / a single `Task.FromCanceled` idiom without the exception-subtype justification comment.
- [ ] `GetCompilationWithAnalyzersAsync_OneCallersCancellation_DoesNotPoisonSharedEntry` either documents what it now actually covers, or becomes the analyzer-bound twin of `GetCompilationAsync_CallerCanceledMidFetch_DoesNotAffectOtherCaller` so the `_analyzerBound` path's `ObserveWithCallerToken`/`WaitAsync` decoupling branch is actually exercised (today no test reaches it: an already-canceled caller short-circuits at the guard and `CancellationToken.None` short-circuits at `!ct.CanBeCanceled`).
- [ ] (Optional, folds in 2 low findings) Fix the unresolvable `<see cref="GetCompilationAsync"/>` (qualify as `CompilationCache.GetCompilationAsync`) and replace the line-number-pinned XML doc comment with a symbol reference.

## Evidence

Traced during code-quality review of `compilation-cache-analyzers-entry-guard`: `ICompilationCache.cs:35` documents only `OperationCanceledException` while the tests pin the derived `TaskCanceledException` exactly via `Assert.ThrowsExactlyAsync`, and `CompilationCache.cs:127-132`'s own comment cites those over-exact tests as the reason for choosing `await Task.FromCanceled<T>(ct)` over the plainer `ct.ThrowIfCancellationRequested()` — the test assertion, not the documented contract, is dictating production code shape. Separately, with the new entry guard at `CompilationCache.cs:133` returning before the `AddOrUpdate` at `:143`, caller A now installs no `_analyzerBound` entry at all, so the shared-entry-poisoning scenario `GetCompilationWithAnalyzersAsync_OneCallersCancellation_DoesNotPoisonSharedEntry` documents (`:108`, "pre-fix this rethrew the first caller's OperationCanceledException") is unreachable by construction — the test is now a strict subset of the row's new regression test.

## Context

Spin-off from the `compilation-cache-analyzers-entry-guard` row's code-quality review (top-n-remediation run 20260810T233007Z). The anchored test file is also touched by open row `compilation-cache-wire-group-c-consumer` — different regions, but sequence via review if both land close together.
