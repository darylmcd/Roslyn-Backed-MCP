# extract-type-protected-member-external-consumers — Refuse or rebind extraction of protected members used by derived classes in other files/projects

**row:** `extract-type-protected-member-external-consumers` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs` (CollectExternalConsumerWarningsAsync)
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Extracting a protected member referenced from a derived class outside the source file is refused with a structured blocking dependency or its references are rebound; red-first test.

## Evidence

- extract-type-override-modifier-stripped cold review: external-consumer scan skips private/protected members.
