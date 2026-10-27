# interface-extraction-conflict-check-fail-closed — Require verified interface conflict checks

**row:** `interface-extraction-conflict-check-fail-closed` · **pri:** `Medium` · **size:** `S`

## Anchors

- src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:339-364 (IO/access failures log then continue; null compilation also accepted)

## Acceptance

- [ ] Refuse before ordinary/composite publication when a required project conflict check cannot obtain a compilation, including null, IOException and UnauthorizedAccessException. Preserve cancellation and authored bounded public guidance.
- [ ] Observe old-code regression then repaired success through actual public extraction, retaining valid no-conflict and actual-conflict controls and unchanged solution/version/disk on refusal.

## Evidence

- Fresh parameter plan and independent cold source review both verified this different mechanism and found no existing canonical owner. No runtime proof or source repair claimed. Not selected for inline parameter or nullable work.
