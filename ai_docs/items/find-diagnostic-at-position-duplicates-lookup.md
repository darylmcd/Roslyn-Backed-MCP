# find-diagnostic-at-position-duplicates-lookup — FindDiagnosticAtPositionAsync duplicates DiagnosticDocumentLookup

**row:** `find-diagnostic-at-position-duplicates-lookup` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`

## Acceptance

- [ ] One lookup path; the ratchet-pinned InvalidOperationException stays in the same member or its baseline entry is relocated with an unchanged fingerprint.

## Evidence

- PR 1991 implementer sighting.
