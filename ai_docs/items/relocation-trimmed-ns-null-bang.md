# relocation-trimmed-ns-null-bang — Redundant null-forgiving RemoveNode result in NamespaceRelocationService

**row:** `relocation-trimmed-ns-null-bang` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`

## Acceptance

- [ ] No null-forgiving operator on RemoveNode; the null case has a defined behavior with a test.

## Evidence

- NamespaceRelocationService.cs ~396.
