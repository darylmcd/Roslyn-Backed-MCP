# interface-extraction-replacement-symbol-refusal — Refuse incomplete usage replacement

**row:** `interface-extraction-replacement-symbol-refusal` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:275-286`

## Acceptance

- [ ] When `replaceUsages` is requested, a null post-generation compilation or missing rebound concrete-type symbol refuses the preview instead of returning the partially rewritten solution.
- [ ] Cover both refusal paths through the public preview route, including the absence of a redeemable preview, and retain a successful replacement control.
- [ ] Check whether other symbol-rebinding guards in this workflow share the same silent-return mechanism; address all instances in the complete fix.

## Evidence

- Current `ReplaceConcreteUsagesAsync` returns `solution` at lines 281 and 286 when the forked compilation or similar symbol is unavailable. The caller has already added the generated interface and base-list reference at lines 98-119, so those returns can publish a preview without the requested usage replacement.
- Sighted during cold review of the 2026-11-09 closeout for PRs #2042 and #2046. The existing `replace-concrete-usages-non-identifier-spellings` row concerns unsupported syntax shapes after references are found; it does not cover these earlier unavailable-compilation and symbol-rebinding returns.
