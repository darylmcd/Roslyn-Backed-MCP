# test-related-private-member-no-container-fallback — Fall back to the containing type's tests in test_related for private/internal members

**row:** `test-related-private-member-no-container-fallback` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs:309`

## Acceptance

- [ ] test_related(ParameterObjectService.ClassifyValueTypeMutation) returns the type's related tests (45)

## Evidence

- test_related(ClassifyValueTypeMutation 498:28) → 0; test_related(ParameterObjectService) → 45. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
