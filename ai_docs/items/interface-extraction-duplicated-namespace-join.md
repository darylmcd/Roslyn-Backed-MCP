# interface-extraction-duplicated-namespace-join — Reuse NamespaceMemberClassifier.GetContainingNamespaceName in interface extraction

**row:** `interface-extraction-duplicated-namespace-join` · **pri:** `Low` · **size:** `S` · **verified:** `e71aace5065e4d5a7edbefb3068e02c0a2579312`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs`

## Acceptance

- [ ] The hand-rolled namespace join (~:233,:251) is replaced by the shared helper with no behaviour change (existing interface-extraction tests stay green; add an `@keyword` namespace row).

## Evidence

- Plan review of extract-type-nested-namespace-identity (sketch 1).
