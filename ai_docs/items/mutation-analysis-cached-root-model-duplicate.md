# mutation-analysis-cached-root-model-duplicate — Share the cached root/model lookup in MutationAnalysisService

**row:** `mutation-analysis-cached-root-model-duplicate` · **pri:** `Low` · **size:** `S` · **verified:** `02fddfa9500d30848b8f5e34367322db1011b3df`

## Anchors

- `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:479`

## Acceptance

- [ ] One generic cached-lookup helper replaces both methods (lines 479 and 497); callers unchanged; existing tests pass.

## Evidence

- Surface audit 2026-11-20 (server 5.1.1), find_duplicated_methods: reported as a duplicate group and confirmed by reading both methods.
