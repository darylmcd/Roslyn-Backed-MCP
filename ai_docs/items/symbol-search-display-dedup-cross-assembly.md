# symbol-search-display-dedup-cross-assembly — Preserve distinct same-display search symbols

**row:** `symbol-search-display-dedup-cross-assembly` · **pri:** `Low` · **size:** `S` · **verified:** `84f385ad2949078de9a9e029d66f69f25883d8a9`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs:39-60` — shared result/deduplication state for both search passes.
- `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs:93-99` — source-declaration results enter the shared collector.
- `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs:116-131` — project enumeration for the qualified-name fallback.
- `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs:203-207` — display-only key drops later matches before DTO mapping.

## Acceptance

- [ ] Reproduce the current omission with two independent source assemblies exposing different symbols with the same display string. Return both matches with existing DTO shape/location data when the result cap is not reached.
- [ ] Retain single-result behavior when the same actual symbol is encountered through both search passes. Cover the shared identity rule without copying the collector.
- [ ] Preserve existing kind/project/namespace filters, result limits, cancellation behavior and response field meanings; qualify the correction under the current release policy.

## Evidence

| Field | Evidence |
|---|---|
| Trigger -> harm | A valid unfiltered solution-wide search finds same-display symbols in two source assemblies; the shared display-only set drops the second, hiding a legitimate result. |
| Reach / origin / impact / route | L2 / pre-existing / P2 / I5. Track for later remediation; no current done-check or existing test is being waived. |
| Source call path | SearchSymbolsAsync creates one HashSet<string> at line45 and shares it with both passes. Primary results at93-99 and per-project fallback at116 onward call TryAddSymbol; lines203-205 use only ToDisplayString before mapping at207. |
| Origin proof | Current main84f385ad2949078de9a9e029d66f69f25883d8a9; git blame attributes203-207 to ce192ff1 (2026-04-16), before this run. The namespace-filter correction is a separate mechanism. |
| Verification limit | Source/call-path finding only; no runtime reproduction or regression test was executed during filing. |
| Dedupe | Canonical list and SymbolSearchService hotspot rows were checked. Existing cancellation/logging rows and symbol-search-response-byte-budget concern different mechanisms. |

## Context

- Keep this one shared deduplication mechanism in scope. Distinct assembly identity must survive while repeated views of one symbol remain deduplicated. Do not prescribe a display-string suffix workaround or add public fields.
- Filed under Directive3/V4 during the current closeout. The operator stopped new row selection; this is tracking only and is not selected for remediation in this run.
