# test-reference-map-counts-synthesized-members — Exclude implicit/synthesized members from test_reference_map coverage

**row:** `test-reference-map-counts-synthesized-members` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestReferenceMapService.cs`

## Acceptance

- [ ] AnimalRecord.<Clone>$ / PrintMembers / implicit Cat() not in uncoveredSymbols

## Evidence

- test_reference_map(W_RW) → covered 6 / uncovered 148 (3.9%), uncovered includes AnimalRecord.<Clone>$(), PrintMembers, Cat.Cat(), IAnimal.Speak(). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
