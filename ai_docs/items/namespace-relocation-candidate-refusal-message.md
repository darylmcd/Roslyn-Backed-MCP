# namespace-relocation-candidate-refusal-message — Explain unusable namespace relocation candidates

**row:** `namespace-relocation-candidate-refusal-message` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:222-232`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] Give actionable safe guidance on missing or multiply matched relocation targets without disclosing caller text or absolute/generated source paths. Exercise the experimental MCP preview and assert no token is published on refusal.
- [ ] Retain successful unique-declaration previews. Coordinate with enum/delegate selection when that independent support/refusal row changes candidate discovery.

## Evidence

- At 9e7a9439 the candidate resolver throws plain InvalidOperationException for missing and ambiguous matches. The ambiguous path interpolates a list of source paths. The host shows the generic InvalidOperation guidance, leaving callers unable to distinguish a missing match from a multi-declaration refusal.
