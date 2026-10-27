# extract-type-late-bound-member-identity — Detect reflection, dynamic and nameof uses of extracted members

**row:** `extract-type-late-bound-member-identity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:529-541`, `:996-997` — references found via `GetSymbolInfo(...).Symbol` only.

## Acceptance

- [ ] Red first: `typeof(F1).GetMethod("Compute", NonPublic | Static).Invoke(...)` after extracting `Compute` throws NullReferenceException.
- [ ] Red first: retained `dynamic self = this; self.Compute()` throws RuntimeBinderException.
- [ ] Red first: retained `nameof(Compute)` (method-group candidate, not rewritten) breaks compilation.
- [ ] Rewrite `nameof` candidates to the helper member.
- [ ] Refuse `Get{Method,Field,Property,Event,Member}` constant-name queries against the source type, and `dynamic` receivers of the source type; moved code using `dynamic` against private source members also refuses (it loses binder access).
- [ ] Unrelated reflection and dynamic uses stay eligible.

## Evidence

- 2026-10-27 runtime probe: reflection NullReferenceException, dynamic RuntimeBinderException. The deleted archive also recorded runtime-binder failures for moved dynamic code.

## Context

- Operator policy (2026-10-15 decision, `ai_docs/plans/20261007T211247Z_backlog-remediate/operator-decisions-20261015.md`; refined 2026-10-16 in the deleted extraction archive): refuse before any token, workspace or disk effect when supported semantic analysis proves loss, or cannot verify preservation, using a typed public `InvalidOperationException` subtype (the archive named it `ExtractionPreservationUnverifiedException`) distinct from proven-loss refusals. Keep the existing namespace; no blanket keyword bans; no automatic relocation. Adding that public refusal type is breaking under contract-care: record it in ADR 0032 (0029 is taken) plus a `Changed — BREAKING` migration note.
