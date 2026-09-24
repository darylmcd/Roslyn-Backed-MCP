# revert-by-sequence-already-reverted-indistinct — Report already-reverted sequences distinctly in revert_apply_by_sequence

**row:** `revert-by-sequence-already-reverted-indistinct` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/UndoService.cs:164`

## Acceptance

- [ ] revert_apply_by_sequence on a reverted seq returns reason=already-reverted

## Evidence

- revert_apply_by_sequence(60) twice → 2nd: reason=unknown-sequence "Either the sequence is from before this session, or the apply did not produce a revertable snapshot". — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
