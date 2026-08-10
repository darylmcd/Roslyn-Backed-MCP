# compilation-cache-analyzers-entry-guard — mirror the already-canceled entry guard onto GetCompilationWithAnalyzersAsync

**row:** `compilation-cache-analyzers-entry-guard` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompilationCache.cs:114-136` (`GetCompilationWithAnalyzersAsync`, unguarded cache-miss branch)
- `src/RoslynMcp.Roslyn/Services/CompilationCache.cs:85-87` (the guard already shipped for `GetCompilationAsync`)
- `src/RoslynMcp.Roslyn/Contracts/ICompilationCache.cs` (remarks paragraph carrying the asymmetry caveat)
- `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs`

## Acceptance

- [ ] `GetCompilationWithAnalyzersAsync`'s cache-miss branch short-circuits an already-canceled caller BEFORE `BuildCompilationWithAnalyzersAsync` starts, mirroring `CompilationCache.cs:85`. A test asserts that an already-canceled caller neither starts the analyzer-bound build nor installs an `_analyzerBound` entry.
- [ ] The `ICompilationCache` remarks paragraph loses its `GetCompilationWithAnalyzersAsync` does NOT yet carry that entry guard ... deliberately out of this change's scope` caveat and states ONE symmetric guarantee for both methods.

## Evidence

- Traced during the code-quality review of PR #1203 (`compilation-cache-cancellation-coverage-and-entry-guard`): `GetCompilationWithAnalyzersAsync` reads `ct` only via `ObserveWithCallerToken`; on a cache miss it unconditionally calls `BuildCompilationWithAnalyzersAsync` and `AddOrUpdate`s the entry before any token check, so an already-canceled caller still triggers the analyzer-bound build plus a nested uncancelable `GetCompilationAsync(..., CancellationToken.None)`.

## Context

PR #1203 fixed exactly this defect on the raw-compilation path (`GetCompilationAsync`) and deliberately scoped the analyzer-bound twin OUT — the plan stanza named `CompilationCache.cs:117` as explicit negative space, and the review-cycle-2 notes carried an `info` finding observing there was budget headroom to include it.

Two facts make this a real row rather than a nit:

1. `ObserveWithCallerToken`'s own `IsCancellationRequested` check (`CompilationCache.cs:147`) only short-circuits a caller's **await** of an existing entry. It does not prevent the miss branch from starting work.
2. The shipped `ICompilationCache` doc now states the asymmetry in prose, so the contract advertises a known gap. That caveat is the thing this row removes.

Filed as a NEW row rather than an amendment to `compilation-cache-adoption-read-side`: that row was closed by PR #1207 in sweep `20260810T175048Z`, so its `items/` detail file no longer exists.

## Notes

- Keep the method's existing `async` shape in mind: `GetCompilationAsync` is non-`async` and returns `Task.FromCanceled`, whereas `GetCompilationWithAnalyzersAsync` is `async`, so the guard there should `throw new OperationCanceledException(ct)` / `ct.ThrowIfCancellationRequested()` rather than returning a faulted task.
