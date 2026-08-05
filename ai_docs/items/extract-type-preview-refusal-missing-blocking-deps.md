# extract-type-preview-refusal-missing-blocking-deps — surface structured blocking-dependency data on extract_type_preview refusals

**row:** `extract-type-preview-refusal-missing-blocking-deps` · **pri:** `High` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:56-64` (compile-safety refusal — `CollectExtractTypeDanglingReferenceWarnings` result flattened into a prose `InvalidOperationException` message)
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:146` (member-not-found refusal — same flattening pattern)
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs` (exception → structured envelope mapping; needs a field to carry the new structured data)
- tests under `tests/RoslynMcp.Tests/Services/TypeExtractionServiceTests.cs` (or equivalent)

## Acceptance

- [ ] `CollectExtractTypeDanglingReferenceWarnings`'s per-member warning data (member name + the specific referenced state that blocks it) is captured as a structured list, not just joined into `summary` prose
- [ ] The refusal envelope (both the compile-safety and member-not-found shapes) carries a `blockingDependencies: [{member, reason}]`-shaped field a caller can act on programmatically
- [ ] Existing prose message is preserved for human readability; the structured field is additive
- [ ] Regression test asserts the structured field is populated on both refusal paths

## Evidence

- `ai_docs/reports/20260805T210025Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md` §2a `extract_type_preview` rows + §3 pattern 1 + §2b extract/move-type gap row. 6 sessions, 2 repos (DotNet-Firewall-Analyzer, BioFileTransfer), near-identical refusal wording in 5. Every occurrence ended in tool abandonment — no session ever retried successfully.

## Context

Both refusal shapes are correct/safe guardrails — they're preventing generation of code that wouldn't compile or that references a nonexistent member. The gap is purely in what the caller can *do* with the refusal: today it's a prose `InvalidOperationException` naming the blocking member(s) but giving no machine-actionable next step, so every session in the retro sample abandoned the tool for a hand-written split rather than retrying with a corrected/expanded `memberNames` list. `CollectExtractTypeDanglingReferenceWarnings` already computes the exact blocking-member data internally (per-warning strings) — the fix is exposing that structured data in the response instead of only flattening it to `summary` prose.

## Notes

An auto-expand-dependencies mode (pull in the minimal member closure automatically) was also proposed in the source report but is a larger design decision (does the caller want auto-expansion, or does it change extraction semantics silently?) — out of scope for this row; the structured refusal data is the minimal fix that unblocks programmatic retry either way.
