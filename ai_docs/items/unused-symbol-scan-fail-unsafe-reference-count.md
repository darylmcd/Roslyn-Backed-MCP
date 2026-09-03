# unused-symbol-scan-fail-unsafe-reference-count — unused-symbol-scan-fail-unsafe-reference-count

**row:** `unused-symbol-scan-fail-unsafe-reference-count` · **pri:** `High` · **size:** `M`

# `unused-symbol-scan-fail-unsafe-reference-count` — the unused-symbol reference scan cannot distinguish "no references" from "the scan under-counted"

## Anchors

- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:162`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:200`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:903`
- `src/RoslynMcp.Roslyn/Services/DeadCodeService.cs:37`
- `tests/RoslynMcp.Tests/DeadFieldDetectorTests.cs:1`

## Acceptance

- [ ] A symbol with a real in-assembly reference is never reported as unused — pinned by a fixture covering a `private static` method invoked earlier in its own file, and a `private readonly` field of a nested `struct` read in the same file
- [ ] An incomplete or failed reference scan is **distinguishable** from a genuine zero-reference result, and never surfaces as a confident "unused" claim
- [ ] `remove_dead_code_preview` does not rely solely on the same predicate that produced the candidate — or, if it must, that is documented as a known non-independence
- [ ] `find_unused_symbols` is either made accurate or demoted from **stable** back to experimental, with the false-positive rate stated in its description

## Evidence

- 8 reported-dead symbols, **8/8 confirmed live** by direct source read of an external consumer repo (`C:/Code-Repo/DotNet-Network-Documentation` @ `b5f38479`) during a `/refactor-audit` run, 2026-09-02, re-verified 2026-09-03. Report: `.../ai_docs/audits/20260902-1443/report.md`
- Cold refutation verdict: **upheld** (2026-09-03). The refuter verified the ground truth independently 8/8 and could not break the finding; its attempt to break the *severity* argument backfired and strengthened it (see Context).

## Context

### What actually goes wrong (corrected root cause)

The original framing of this finding — "the tool mis-classifies **private members**" — was **wrong**, and a remediator following it would find nothing to fix. Accessibility enters `UnusedCodeAnalyzer` only via `IsPublicFiltered` / `ShouldSkipFieldForDeadFieldAnalysis`, which *exclude* symbols; there is no classification path that could mis-label a private member.

The real defect is that the reference scan is **fail-unsafe**. A symbol is reported unused on exactly one criterion:

- `UnusedCodeAnalyzer.cs:200-201` — `FindReferencesAsync(...)` then `refs.Sum(r => r.Locations.Count()) == 0`
- `:903` — the same shape for dead fields

`ScanCandidatesAndAppendResultsAsync` (`:162-176`) fans that call out across `Math.Clamp(Environment.ProcessorCount, 4, 16)` concurrent tasks behind a `SemaphoreSlim`, awaits `Task.WhenAll`, and has **no detection of an incomplete or failed scan**. An under-count is therefore indistinguishable from genuine deadness and surfaces as a confident "unused" claim.

### The removal guard provides no independent protection

`DeadCodeService.PreviewRemoveDeadCodeAsync` (`DeadCodeService.cs:37-38`) guards removal with the *identical* predicate — `FindReferencesAsync` then `Sum(...Locations.Count()) > 0`. So the preview re-asks the question the detector already answered wrongly and gets the same wrong answer.

The chain is real and by design, though not as originally described: `remove_dead_code_apply` does **not** consume `find_unused_symbols` output directly — it takes only a `previewToken` minted by `remove_dead_code_preview(symbolHandles)` under a route-provenance guard. But `UnusedSymbolDto` carries `SymbolHandleSerializer.CreateHandle(symbol)` (`UnusedCodeAnalyzer.cs:229`), which is exactly what the preview consumes. A chained removal of these 8 symbols would therefore **proceed, not refuse**.

### Why High

`find_unused_symbols` is **stable**, not experimental — CHANGELOG catalog `2026.04` promoted six read-only advanced-analysis tools to stable and names it. Only `find_dead_fields` still carries `"experimental"` in its `McpToolMetadata`. So an "it's experimental" disclaimer covers at most half of this and cannot rescue the stable tool. Combined with a removal path whose only safety check is the same failing predicate, the worst case is deleted working code.

### The 8 confirmed-live symbols

| Reported | Ground truth (verified 2026-09-03) |
|---|---|
| `DiffEngine.DiffDevice` | `private static`, invoked `Core/Diff/DiffEngine.cs:60`, declared `:156` |
| `DeviceClassifierService.ClassifyByHostname` | `private static`, invoked `:115`, declared `:256` |
| `DependencyTelemetry.Operation._logger` | `private readonly` on a nested **`struct Operation`**, declared `:65`, read `:134,:144,:152` |
| `DependencyTelemetry.Operation._startTicks` | `private readonly`, declared `:71`, read `:128` via `Stopwatch.GetElapsedTime` |
| `AppConfig._instance` | live; all three sites (`:20,43,54`) are `_instance ??= new AppConfig()` **compound read-writes**; `:20` returns the value so it is unambiguously read |
| `ConfigLoader.PlainStringMappings` | declared `:85`, read `:46` in a deconstructing `foreach` |
| `ChangesSheetBuilder` | live (`Utils/InventoryProjectionService.cs:45`) — an **`internal static class`**, not a private member |
| `ClientLogRateLimiter` | live (`Web/ClientLogEndpoints.cs:13`) — an implicitly-**internal** `static class`, not a private member |

The last two show the failure is not confined to private members; it spans private, internal and nested-struct-field shapes across three projects.

## Notes

**Reproduction caveat — this row is NOT yet reproducible in this repo, and that is a gap to close first.** The tool-output half of the evidence was produced on 2026-09-02 while the `roslyn` MCP server was connected; the server disconnected before the row was written, so the raw output was never captured. Missing and required before remediation: the **full result-set size**, the **exact parameters used** (`includePublic`, `projectName`, `limit`, `excludeConventionInvoked`), and the **workspace load transcript**. Reproduce by loading `NetworkDocumentation.sln` (9 projects / 706 documents) and re-running both tools against the Core project.

**Unexplained evidence gap — do not skip this.** `ConfigLoader.ValidatedStringMappings` and `BoolMappings` (`ConfigLoader.cs:51,58`) are `private static readonly` tuple arrays read by *identical* deconstructing `foreach`es, and were apparently **not** reported. Equally, a wholesale reference-resolution failure would have flagged far more than 8 symbols. So neither a classifier bug nor a total scan failure explains *exactly these 8* — something selects them. Identifying that selector is the first diagnostic step; the concurrency/fail-unsafe hypothesis above is the leading candidate, not a settled conclusion.

**Counterargument.** Both tools sit behind a documented triage procedure in the consumer repo telling agents to confirm every hit with `find_references` before acting, so a careful operator is protected. This fails on three counts: that doc's four false-positive classes are all "referenced only from *outside* the compilation" (tests, reflection/JSON, SPA/OpenAPI, intentional public surface) and none stretches to a `private static` method invoked 96 lines above its own declaration in the same file; its own step 1 tells you to confirm with `find_references`, which here would have to return the very references the tool claims do not exist; and `find_unused_symbols` is stable, so the experimental disclaimer does not apply to it.

**Blast radius.** `UnusedCodeAnalyzer` and `DeadCodeService`, plus any prompt or skill recommending the tools (`roslyn-mcp:dead-code`). The mutation tools themselves need no change if the predicate is fixed at the source.
